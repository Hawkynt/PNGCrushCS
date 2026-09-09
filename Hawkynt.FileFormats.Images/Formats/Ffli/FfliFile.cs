using System;
using FileFormat.Core;

namespace FileFormat.Ffli;

/// <summary>In-memory representation of a Flash FLI (.ffl) picture for the Commodore 64.</summary>
/// <remarks>
/// Two FLI fields shown one after the other fast enough for the eye to average them, which is how
/// the machine's sixteen colours become a hundred or so. What differs between the fields is only the
/// colour: they share one bitmap and one colour memory, and each has its own eight video matrices
/// and its own table of background colours, one entry to a raster line.
/// <para/>
/// Laid out the way the machine addresses it, and the third byte is a lower-case f — that letter is
/// what a decoder checks before believing the length. Colour memory takes a page from 259, the first
/// field's matrices a page apiece from 1283, the shared bitmap 9475, the second field's matrices
/// 17667 and its background table 25859.
/// <para/>
/// What was written before was 17002 bytes holding one field, with the matrices packed a thousand
/// apart and no signature at all.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct FfliFile
  : IImageFormatReader<FfliFile>, IImageToRawImage<FfliFile>,
    IImageFromRawImage<FfliFile>, IImageFormatWriter<FfliFile> {

  static string IImageFormatMetadata<FfliFile>.PrimaryExtension => ".ffli";
  static string[] IImageFormatMetadata<FfliFile>.FileExtensions => [".ffli", ".ffl"];
  static FfliFile IImageFormatReader<FfliFile>.FromSpan(ReadOnlySpan<byte> data) => FfliReader.FromSpan(data);
  static byte[] IImageFormatWriter<FfliFile>.ToBytes(FfliFile file) => FfliWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FfliFile>.VideoModes => [
    new("Flash FLI", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int FixedWidth = Commodore64Fli.VisibleWidth;

  /// <summary>Rows.</summary>
  public const int FixedHeight = Commodore64Fli.ScreenHeight;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where the letter that says this is a Flash FLI sits.</summary>
  internal const int SignatureOffset = 2;

  /// <summary>The letter itself.</summary>
  internal const byte Signature = (byte)'f';

  /// <summary>Where the first field's background table starts, one entry a raster line.</summary>
  internal const int FirstBackgroundsOffset = 3;

  /// <summary>Where colour memory starts, which both fields share.</summary>
  internal const int ColorRamOffset = 259;

  /// <summary>Where the first field's video matrices start.</summary>
  internal const int FirstMatricesOffset = 1283;

  /// <summary>Where the bitmap starts, which both fields share.</summary>
  internal const int BitmapOffset = FirstMatricesOffset + Commodore64Fli.MatrixAreaSize;

  /// <summary>Where the second field's video matrices start.</summary>
  internal const int SecondMatricesOffset = 17667;

  /// <summary>Where the second field's background table starts.</summary>
  internal const int SecondBackgroundsOffset = SecondMatricesOffset + Commodore64Fli.MatrixAreaSize;

  /// <summary>The length of a whole Flash FLI picture, which is also part of what identifies it.</summary>
  public const int FileSize = 26115;

  /// <summary>Default load address, which puts the first field's matrices at the foot of a bank.</summary>
  internal const ushort DefaultLoadAddress = 0x3B00;

  /// <summary>Image width, always 296.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>What pattern 00 shows in the first field, one entry for each raster line.</summary>
  public byte[] FirstBackgrounds { get; init; }

  /// <summary>What pattern 00 shows in the second field, one entry for each raster line.</summary>
  public byte[] SecondBackgrounds { get; init; }

  /// <summary>Colour memory, which both fields share.</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>The bitmap, which both fields share.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The first field's eight video matrices, a whole page apiece.</summary>
  public byte[] FirstMatrices { get; init; }

  /// <summary>The second field's eight video matrices, a whole page apiece.</summary>
  public byte[] SecondMatrices { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// The two fields are averaged, which is what a display alternating between them looks like and
  /// what makes the format worth more than the sixteen colours one field can hold.
  /// </remarks>
  public static RawImage ToRawImage(FfliFile file) {
    var bitmap = file.BitmapData ?? [];
    var colorRam = file.ColorRam ?? [];

    var first = _Field(bitmap, file.FirstMatrices ?? [], colorRam, file.FirstBackgrounds ?? []);
    var second = _Field(bitmap, file.SecondMatrices ?? [], colorRam, file.SecondBackgrounds ?? []);

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _Field(
    ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> matrices, ReadOnlySpan<byte> colorRam, ReadOnlySpan<byte> backgrounds)
    => PixelConverter.Convert(
      Commodore64Fli.DecodeMulticolor(
        bitmap, matrices, Commodore64Fli.MatrixStride, colorRam, backgrounds, FixedHeight),
      PixelFormat.Rgb24).PixelData;

  /// <summary>Encodes a picture as a Flash FLI, scaling it to 296x200 first.</summary>
  /// <remarks>
  /// Both fields are written the same, so the average of the two is the field itself and the picture
  /// comes back as it went in. Solving for two fields whose average is nearer the original is what
  /// the format is for and is a different problem from encoding one — one this does not attempt
  /// rather than attempts badly.
  /// </remarks>
  public static FfliFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bitmap = new byte[Commodore64Fli.BitmapSize];
    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    var colorRam = new byte[Commodore64Fli.ColorRamSize];
    Commodore64Fli.EncodeMulticolor(
      image, FixedHeight, 0, 0, bitmap, matrices, Commodore64Fli.MatrixStride, colorRam);

    return new() {
      LoadAddress = DefaultLoadAddress,
      FirstBackgrounds = new byte[FixedHeight],
      SecondBackgrounds = new byte[FixedHeight],
      ColorRam = colorRam,
      BitmapData = bitmap,
      FirstMatrices = matrices,
      SecondMatrices = (byte[])matrices.Clone(),
    };
  }
}
