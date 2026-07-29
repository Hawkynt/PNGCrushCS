using System;
using FileFormat.Core;

namespace FileFormat.Artist64;

/// <summary>In-memory representation of a Commodore 64 Artist 64 multicolor image.</summary>
public readonly record struct Artist64File : IImageFormatReader<Artist64File>, IImageToRawImage<Artist64File>, IImageFormatWriter<Artist64File> {

  static string IImageFormatMetadata<Artist64File>.PrimaryExtension => ".a64";
  static string[] IImageFormatMetadata<Artist64File>.FileExtensions => [".a64"];
  static Artist64File IImageFormatReader<Artist64File>.FromSpan(ReadOnlySpan<byte> data) => Artist64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Artist64File>.ToBytes(Artist64File file) => Artist64Writer.ToBytes(file);

  /// <summary>The fixed width of an Artist 64 image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of an Artist 64 image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 240).</summary>
  public const int ExpectedFileSize = 10242;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the trailing padding in bytes.</summary>
  internal const int PaddingSize = 240;

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

  /// <summary>Converts this Artist 64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Artist64File file)
    => Commodore64Graphics.DecodeMulticolor(
      // Pattern 00 is always black here: neither format stores a background register.
      file.BitmapData, file.VideoMatrix, file.ColorRam, 0, FixedWidth, FixedHeight);

}
