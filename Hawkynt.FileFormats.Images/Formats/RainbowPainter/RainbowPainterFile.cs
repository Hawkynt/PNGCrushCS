using System;
using FileFormat.Core;

namespace FileFormat.RainbowPainter;

/// <summary>In-memory representation of a Commodore 64 Rainbow Painter multicolor image.</summary>
public readonly record struct RainbowPainterFile : IImageFormatReader<RainbowPainterFile>, IImageToRawImage<RainbowPainterFile>, IImageFormatWriter<RainbowPainterFile> {

  static string IImageFormatMetadata<RainbowPainterFile>.PrimaryExtension => ".rp";
  static string[] IImageFormatMetadata<RainbowPainterFile>.FileExtensions => [".rp"];
  static RainbowPainterFile IImageFormatReader<RainbowPainterFile>.FromSpan(ReadOnlySpan<byte> data) => RainbowPainterReader.FromSpan(data);
  static byte[] IImageFormatWriter<RainbowPainterFile>.ToBytes(RainbowPainterFile file) => RainbowPainterWriter.ToBytes(file);

  /// <summary>The fixed width of a Rainbow Painter image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Rainbow Painter image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1).</summary>
  public const int ExpectedFileSize = 10003;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Rainbow Painter image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(RainbowPainterFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

}
