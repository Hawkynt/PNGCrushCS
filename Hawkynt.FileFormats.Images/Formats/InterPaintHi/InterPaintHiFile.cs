using System;
using FileFormat.Core;

namespace FileFormat.InterPaintHi;

/// <summary>In-memory representation of a Commodore 64 InterPaint Hires image.</summary>
public readonly record struct InterPaintHiFile : IImageFormatReader<InterPaintHiFile>, IImageToRawImage<InterPaintHiFile>, IImageFormatWriter<InterPaintHiFile> {

  static string IImageFormatMetadata<InterPaintHiFile>.PrimaryExtension => ".iph";
  static string[] IImageFormatMetadata<InterPaintHiFile>.FileExtensions => [".iph", ".hre"];
  static InterPaintHiFile IImageFormatReader<InterPaintHiFile>.FromSpan(ReadOnlySpan<byte> data) => InterPaintHiReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterPaintHiFile>.ToBytes(InterPaintHiFile file) => InterPaintHiWriter.ToBytes(file);

  /// <summary>The fixed width of an InterPaint Hires image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of an InterPaint Hires image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000).</summary>
  public const int ExpectedFileSize = 9002;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Hires bitmap data (8000 bytes, 1 bit per pixel within 8x8 cells).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper nybble = foreground color, lower nybble = background color per cell).</summary>
  public byte[] ScreenRam { get; init; }

  /// <summary>Converts this InterPaint Hires image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(InterPaintHiFile file)
    => Commodore64Graphics.DecodeHires(file.BitmapData, file.ScreenRam, FixedWidth, FixedHeight);

}
