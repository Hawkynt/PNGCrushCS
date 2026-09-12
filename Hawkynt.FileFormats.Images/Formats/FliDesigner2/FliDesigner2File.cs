using System;
using FileFormat.Core;

namespace FileFormat.FliDesigner2;

/// <summary>In-memory representation of a FLI Designer 2 multicolour picture for the Commodore 64.</summary>
/// <remarks>
/// The same file its predecessor wrote, saved to the end of the sixteen-kilobyte bank it lives in
/// rather than stopping at the last byte of the bitmap: colour memory, a page apiece for the eight
/// video matrices, the bitmap, and then whatever the rest of the bank held. That is 17409 bytes, the
/// longer of the two lengths the reference decoder takes for the extension.
/// <para/>
/// What was written before was 17474 bytes holding a bitmap, eight thousand bytes of video matrix
/// addressed a raster line at a time, and colour memory — a layout no C64 has, since the VIC-II
/// reads its matrix through a pointer with 1024-byte granularity and cannot address a per-line
/// table at all. The reader made the same assumption, so the two agreed with each other and with
/// nothing else.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct FliDesigner2File
  : IImageFormatReader<FliDesigner2File>, IImageToRawImage<FliDesigner2File>,
    IImageFromRawImage<FliDesigner2File>, IImageFormatWriter<FliDesigner2File> {

  static string IImageFormatMetadata<FliDesigner2File>.PrimaryExtension => ".fd2";
  static string[] IImageFormatMetadata<FliDesigner2File>.FileExtensions => [".fd2"];
  static FliDesigner2File IImageFormatReader<FliDesigner2File>.FromSpan(ReadOnlySpan<byte> data) => FliDesigner2Reader.FromSpan(data);
  static byte[] IImageFormatWriter<FliDesigner2File>.ToBytes(FliDesigner2File file) => FliDesigner2Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FliDesigner2File>.VideoModes => [
    new("FLI Designer 2", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
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

  /// <summary>The length of a whole file: the picture, then the rest of the bank it was saved from.</summary>
  public const int FileSize = 17409;

  /// <summary>The last byte of the picture proper; everything after it is the rest of the bank.</summary>
  internal const int PictureSize = BitmapOffset + Commodore64Fli.BitmapSize;

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

  /// <summary>Whatever the rest of the bank held when the picture was saved.</summary>
  public byte[] Trailer { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(FliDesigner2File file)
    => Commodore64Fli.DecodeMulticolor(
      file.BitmapData ?? [], file.Matrices ?? [], Commodore64Fli.MatrixStride,
      file.ColorRam ?? [], [], FixedHeight);

  /// <summary>Encodes a picture as FLI Designer 2, scaling it to 296x200 first.</summary>
  public static FliDesigner2File FromRawImage(RawImage image) {
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
      Trailer = new byte[FileSize - PictureSize],
    };
  }
}
