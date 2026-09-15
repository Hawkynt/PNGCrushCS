using System;
using FileFormat.Core;

namespace FileFormat.FliEditor;

/// <summary>In-memory representation of a FLI Editor multicolour picture for the Commodore 64.</summary>
/// <remarks>
/// Laid out the way the machine addresses it: a table of background colours one to a raster line,
/// colour memory a page in, the eight video matrices a page apart because that is the granularity of
/// the VIC-II's matrix pointer, and the bitmap last. Loaded at $3B00 that puts colour memory at
/// $3C00, the matrices across $4000 to $5FFF and the bitmap at $6000, which is the arrangement every
/// FLI display routine expects.
/// <para/>
/// What was written before was the bitmap first with the matrices packed a thousand bytes apart, and
/// no background table at all — 17000 bytes that no C64 program can load and that only our own
/// reader, which made the same assumption, could open.
/// <para/>
/// The background register changing down the screen is what the table buys: pattern 00 can be a
/// different colour on every raster line rather than one for the whole picture.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct FliEditorFile
  : IImageFormatReader<FliEditorFile>, IImageToRawImage<FliEditorFile>,
    IImageFromRawImage<FliEditorFile>, IImageFormatWriter<FliEditorFile> {

  static string IImageFormatMetadata<FliEditorFile>.PrimaryExtension => ".fed";
  static string[] IImageFormatMetadata<FliEditorFile>.FileExtensions => [".fed"];
  static FliEditorFile IImageFormatReader<FliEditorFile>.FromSpan(ReadOnlySpan<byte> data) => FliEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<FliEditorFile>.ToBytes(FliEditorFile file) => FliEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FliEditorFile>.VideoModes => [
    new("FLI Editor", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int FixedWidth = Commodore64Fli.VisibleWidth;

  /// <summary>Rows.</summary>
  public const int FixedHeight = Commodore64Fli.ScreenHeight;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where the table of one background colour per raster line starts.</summary>
  internal const int BackgroundsOffset = 8;

  /// <summary>Where colour memory starts.</summary>
  internal const int ColorRamOffset = 258;

  /// <summary>Where the video matrices start.</summary>
  internal const int MatricesOffset = 1282;

  /// <summary>Where the bitmap starts: after all eight matrices.</summary>
  internal const int BitmapOffset = MatricesOffset + Commodore64Fli.MatrixAreaSize;

  /// <summary>The length of a whole FLI Editor picture, trailer included.</summary>
  public const int FileSize = 17665;

  /// <summary>The last byte of the picture proper; the rest is whatever the bank held.</summary>
  internal const int PictureSize = BitmapOffset + Commodore64Fli.BitmapSize;

  /// <summary>Default load address, which puts the matrices at the foot of the bank at $4000.</summary>
  internal const ushort DefaultLoadAddress = 0x3B00;

  /// <summary>Image width, always 296.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>What pattern 00 shows, one entry for each raster line.</summary>
  public byte[] Backgrounds { get; init; }

  /// <summary>Colour memory, one entry a cell, which pattern 11 takes.</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>The eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] Matrices { get; init; }

  /// <summary>The bitmap, eight thousand bytes, a cell at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(FliEditorFile file)
    => Commodore64Fli.DecodeMulticolor(
      file.BitmapData ?? [], file.Matrices ?? [], Commodore64Fli.MatrixStride,
      file.ColorRam ?? [], file.Backgrounds ?? [], FixedHeight);

  /// <summary>Encodes a picture as FLI Editor, scaling it to 296x200 first.</summary>
  /// <remarks>
  /// One background for the whole picture rather than one a line: choosing a different one per
  /// raster line is what the format allows and not what the shared encoder decides, and a table of
  /// black is a correct picture where a table of guesses would not be.
  /// </remarks>
  public static FliEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bitmap = new byte[Commodore64Fli.BitmapSize];
    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    var colorRam = new byte[Commodore64Fli.ColorRamSize];
    Commodore64Fli.EncodeMulticolor(
      image, FixedHeight, 0, 0, bitmap, matrices, Commodore64Fli.MatrixStride, colorRam);

    return new() {
      LoadAddress = DefaultLoadAddress,
      Backgrounds = new byte[FixedHeight],
      ColorRam = colorRam,
      Matrices = matrices,
      BitmapData = bitmap,
    };
  }
}
