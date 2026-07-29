using System;
using FileFormat.Core;

namespace FileFormat.Pixel64;

/// <summary>In-memory representation of a Commodore 64 Pixel Perfect paint image.</summary>
public readonly record struct Pixel64File : IImageFormatReader<Pixel64File>, IImageToRawImage<Pixel64File>, IImageFormatWriter<Pixel64File> {

  static string IImageFormatMetadata<Pixel64File>.PrimaryExtension => ".px64";
  static string[] IImageFormatMetadata<Pixel64File>.FileExtensions => [".px64", ".px"];
  static Pixel64File IImageFormatReader<Pixel64File>.FromSpan(ReadOnlySpan<byte> data) => Pixel64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Pixel64File>.ToBytes(Pixel64File file) => Pixel64Writer.ToBytes(file);

  /// <summary>The fixed width of a Pixel64 image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Pixel64 image in pixels.</summary>
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

  /// <summary>Converts this Pixel64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Pixel64File file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

}
