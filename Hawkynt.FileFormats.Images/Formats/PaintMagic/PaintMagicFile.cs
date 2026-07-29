using System;
using FileFormat.Core;

namespace FileFormat.PaintMagic;

/// <summary>In-memory representation of a Paint Magic C64 multicolor image (Koala layout).</summary>
public readonly record struct PaintMagicFile : IImageFormatReader<PaintMagicFile>, IImageToRawImage<PaintMagicFile>, IImageFormatWriter<PaintMagicFile> {

  static string IImageFormatMetadata<PaintMagicFile>.PrimaryExtension => ".pmg";
  static string[] IImageFormatMetadata<PaintMagicFile>.FileExtensions => [".pmg"];
  static PaintMagicFile IImageFormatReader<PaintMagicFile>.FromSpan(ReadOnlySpan<byte> data) => PaintMagicReader.FromSpan(data);
  static byte[] IImageFormatWriter<PaintMagicFile>.ToBytes(PaintMagicFile file) => PaintMagicWriter.ToBytes(file);

  /// <summary>The fixed width of a Paint Magic image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Paint Magic image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size: loadAddress(2) + bitmapData(8000) + videoMatrix(1000) + colorRam(1000) + backgroundColor(1) = 10003.</summary>
  public const int ExpectedFileSize = 10003;

  internal const int BitmapDataSize = 8000;
  internal const int VideoMatrixSize = 1000;
  internal const int ColorRamSize = 1000;
  internal const int LoadAddressSize = 2;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Paint Magic image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(PaintMagicFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

}
