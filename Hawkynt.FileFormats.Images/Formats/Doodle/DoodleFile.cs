using System;
using FileFormat.Core;

namespace FileFormat.Doodle;

/// <summary>In-memory representation of a Commodore 64 Doodle hires image.</summary>
public readonly record struct DoodleFile : IImageFormatReader<DoodleFile>, IImageToRawImage<DoodleFile>, IImageFormatWriter<DoodleFile> {

  static string IImageFormatMetadata<DoodleFile>.PrimaryExtension => ".dd";
  static string[] IImageFormatMetadata<DoodleFile>.FileExtensions => [".dd"];
  static DoodleFile IImageFormatReader<DoodleFile>.FromSpan(ReadOnlySpan<byte> data) => DoodleReader.FromSpan(data);
  static byte[] IImageFormatWriter<DoodleFile>.ToBytes(DoodleFile file) => DoodleWriter.ToBytes(file);

  /// <summary>The fixed width of a Doodle image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Doodle image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 216).</summary>
  public const int ExpectedFileSize = 9218;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the padding section in bytes.</summary>
  internal const int PaddingSize = 216;

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

  /// <summary>Converts this Doodle image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(DoodleFile file)
    => Commodore64Graphics.DecodeHires(file.BitmapData, file.ScreenRam, FixedWidth, FixedHeight);

}
