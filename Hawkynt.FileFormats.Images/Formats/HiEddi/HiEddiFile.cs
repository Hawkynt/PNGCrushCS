using System;
using FileFormat.Core;

namespace FileFormat.HiEddi;

/// <summary>In-memory representation of a HiEddi C64 hires image (Doodle layout).</summary>
public readonly record struct HiEddiFile : IImageFormatReader<HiEddiFile>, IImageToRawImage<HiEddiFile>, IImageFormatWriter<HiEddiFile> {

  static string IImageFormatMetadata<HiEddiFile>.PrimaryExtension => ".hed";
  static string[] IImageFormatMetadata<HiEddiFile>.FileExtensions => [".hed"];
  static HiEddiFile IImageFormatReader<HiEddiFile>.FromSpan(ReadOnlySpan<byte> data) => HiEddiReader.FromSpan(data);
  static byte[] IImageFormatWriter<HiEddiFile>.ToBytes(HiEddiFile file) => HiEddiWriter.ToBytes(file);

  /// <summary>The fixed width of a HiEddi image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a HiEddi image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size: loadAddress(2) + bitmapData(8000) + screenRam(1000) + padding(216) = 9218.</summary>
  public const int ExpectedFileSize = 9218;

  internal const int BitmapDataSize = 8000;
  internal const int ScreenRamSize = 1000;
  internal const int LoadAddressSize = 2;
  internal const int PaddingSize = 216;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Hires bitmap data (8000 bytes, 1 bit per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper/lower nybble = fg/bg color per 8x8 cell).</summary>
  public byte[] ScreenRam { get; init; }

  /// <summary>Converts this HiEddi image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiEddiFile file)
    => Commodore64Graphics.DecodeHires(file.BitmapData, file.ScreenRam, FixedWidth, FixedHeight);

}
