using System;
using FileFormat.Core;

namespace FileFormat.SaracenPaint;

/// <summary>In-memory representation of a Saracen Paint C64 hires image (Art Studio hires layout).</summary>
public readonly record struct SaracenPaintFile : IImageFormatReader<SaracenPaintFile>, IImageToRawImage<SaracenPaintFile>, IImageFormatWriter<SaracenPaintFile> {

  static string IImageFormatMetadata<SaracenPaintFile>.PrimaryExtension => ".sar";
  static string[] IImageFormatMetadata<SaracenPaintFile>.FileExtensions => [".sar"];
  static SaracenPaintFile IImageFormatReader<SaracenPaintFile>.FromSpan(ReadOnlySpan<byte> data) => SaracenPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<SaracenPaintFile>.ToBytes(SaracenPaintFile file) => SaracenPaintWriter.ToBytes(file);

  /// <summary>The fixed width of a Saracen Paint image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Saracen Paint image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size: loadAddress(2) + screenRam(1000) + bitmapData(8000) + padding(7) = 9009.</summary>
  public const int ExpectedFileSize = 9009;

  internal const int ScreenRamSize = 1000;
  internal const int BitmapDataSize = 8000;
  internal const int LoadAddressSize = 2;
  internal const int PaddingSize = 7;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper/lower nybble = fg/bg color per 8x8 cell).</summary>
  public byte[] ScreenRam { get; init; }

  /// <summary>Hires bitmap data (8000 bytes, 1 bit per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Converts this Saracen Paint image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(SaracenPaintFile file)
    => Commodore64Graphics.DecodeHires(file.BitmapData, file.ScreenRam, FixedWidth, FixedHeight);

}
