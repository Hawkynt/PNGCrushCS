using System;
using FileFormat.Core;

namespace FileFormat.FliDesigner;

/// <summary>In-memory representation of a FLI Designer multicolour picture for the Commodore 64.</summary>
/// <remarks>
/// This used to keep the file as one undifferentiated payload laid out bitmap first with the eight
/// video matrices packed a thousand bytes apart, which is not the format: it is what you get by
/// writing down the parts in the order they are named rather than the order they are addressed. The
/// matrices sit a page apart because that is where the VIC-II looks for them, colour memory comes
/// first, and the bitmap is last. Reader and writer shared the mistake, so every round trip passed
/// and no other program could open a single file.
/// <para/>
/// The picture is 296 across rather than 160: FLI cannot colour the first three cells of a row, and
/// each multicolour pixel is drawn two wide, so what is left is 148 stored pixels shown as 296.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct FliDesignerFile
  : IImageFormatReader<FliDesignerFile>, IImageToRawImage<FliDesignerFile>,
    IImageFromRawImage<FliDesignerFile>, IImageFormatWriter<FliDesignerFile> {

  static string IImageFormatMetadata<FliDesignerFile>.PrimaryExtension => ".fd2";
  static string[] IImageFormatMetadata<FliDesignerFile>.FileExtensions => [".fd2"];
  static FliDesignerFile IImageFormatReader<FliDesignerFile>.FromSpan(ReadOnlySpan<byte> data) => FliDesignerReader.FromSpan(data);
  static byte[] IImageFormatWriter<FliDesignerFile>.ToBytes(FliDesignerFile file) => FliDesignerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FliDesignerFile>.VideoModes => [
    new("FLI Designer", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int FixedWidth = Commodore64Fli.VisibleWidth;

  /// <summary>Rows.</summary>
  public const int FixedHeight = Commodore64Fli.ScreenHeight;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where colour memory starts: straight after the load address.</summary>
  internal const int ColorRamOffset = LoadAddressSize;

  /// <summary>Where the video matrices start, colour memory having been given a whole page.</summary>
  internal const int MatricesOffset = ColorRamOffset + Commodore64Fli.MatrixStride;

  /// <summary>Where the bitmap starts: after all eight matrices.</summary>
  internal const int BitmapOffset = MatricesOffset + Commodore64Fli.MatrixAreaSize;

  /// <summary>The length of a whole FLI Designer picture.</summary>
  public const int FileSize = BitmapOffset + Commodore64Fli.BitmapSize;

  /// <summary>The same picture saved to the end of its sixteen-kilobyte bank.</summary>
  public const int PaddedFileSize = 17409;

  /// <summary>Default load address, which puts the matrices at the foot of the bank at $4000.</summary>
  internal const ushort DefaultLoadAddress = 0x3C00;

  /// <summary>Image width, always 296.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Colour memory, one entry a cell, which pattern 11 takes.</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>The eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] Matrices { get; init; }

  /// <summary>The bitmap, eight thousand bytes, a cell at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Whether the file ran on to the end of its bank, which is how it is written back.</summary>
  public bool Padded { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// The format keeps no background register, so pattern 00 is black — which is what the reference
  /// decoder shows for it and the only answer the file supports.
  /// </remarks>
  public static RawImage ToRawImage(FliDesignerFile file)
    => Commodore64Fli.DecodeMulticolor(
      file.BitmapData ?? [], file.Matrices ?? [], Commodore64Fli.MatrixStride,
      file.ColorRam ?? [], [], FixedHeight);

  /// <summary>Encodes a picture as FLI Designer, scaling it to 296x200 first.</summary>
  public static FliDesignerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bitmap = new byte[Commodore64Fli.BitmapSize];
    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    var colorRam = new byte[Commodore64Fli.ColorRamSize];
    Commodore64Fli.EncodeMulticolor(
      image, FixedHeight, 0, 0, bitmap, matrices, Commodore64Fli.MatrixStride, colorRam);

    return new() {
      LoadAddress = DefaultLoadAddress,
      ColorRam = colorRam,
      Matrices = matrices,
      BitmapData = bitmap,
    };
  }
}
