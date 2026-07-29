using System;
using FileFormat.Core;

namespace FileFormat.ImageSysC64;

/// <summary>In-memory representation of a Commodore 64 Image System C64 image.</summary>
public readonly record struct ImageSysC64File : IImageFormatReader<ImageSysC64File>, IImageToRawImage<ImageSysC64File>, IImageFormatWriter<ImageSysC64File> {

  static string IImageFormatMetadata<ImageSysC64File>.PrimaryExtension => ".isc";
  static string[] IImageFormatMetadata<ImageSysC64File>.FileExtensions => [".isc"];
  static ImageSysC64File IImageFormatReader<ImageSysC64File>.FromSpan(ReadOnlySpan<byte> data) => ImageSysC64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<ImageSysC64File>.ToBytes(ImageSysC64File file) => ImageSysC64Writer.ToBytes(file);

  /// <summary>The fixed width of an Image System C64 image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of an Image System C64 image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1 + 1 + 14).</summary>
  public const int ExpectedFileSize = 10018;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the padding section in bytes.</summary>
  internal const int PaddingSize = 14;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address, typically 0x2000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Trailing padding bytes (14 bytes).</summary>
  public byte[] Padding { get; init; }

  /// <summary>Converts this Image System C64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ImageSysC64File file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

}
