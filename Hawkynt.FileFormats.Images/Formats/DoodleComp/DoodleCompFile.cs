using System;
using FileFormat.Core;

namespace FileFormat.DoodleComp;

/// <summary>In-memory representation of a Commodore 64 Doodle Compressed hires image.</summary>
public readonly record struct DoodleCompFile
  : IImageFormatReader<DoodleCompFile>, IImageToRawImage<DoodleCompFile>,
    IImageFromRawImage<DoodleCompFile>, IImageFormatWriter<DoodleCompFile> {

  static string IImageFormatMetadata<DoodleCompFile>.PrimaryExtension => ".jj";
  static string[] IImageFormatMetadata<DoodleCompFile>.FileExtensions => [".jj"];
  static DoodleCompFile IImageFormatReader<DoodleCompFile>.FromSpan(ReadOnlySpan<byte> data) => DoodleCompReader.FromSpan(data);
  static byte[] IImageFormatWriter<DoodleCompFile>.ToBytes(DoodleCompFile file) => DoodleCompWriter.ToBytes(file);

  /// <summary>The fixed width of a Doodle Compressed image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Doodle Compressed image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Total decompressed data size (bitmap + screen).</summary>
  /// <summary>The kilobyte the screen sits in, of which it uses a thousand bytes.</summary>
  internal const int ScreenRamPaddedSize = 1024;

  internal const int DecompressedDataSize = ScreenRamPaddedSize + BitmapDataSize;

  /// <summary>Minimum file size: load address (2) + at least 1 byte of compressed data.</summary>
  internal const int MinimumFileSize = 3;

  /// <summary>The RLE escape byte used in Doodle compression.</summary>
  internal const byte RleEscapeByte = 0xFE;

  /// <summary>Default load address: Doodle loads at $5C00 and its bitmap lands at $6000.</summary>
  internal const ushort DefaultLoadAddress = 0x5C00;

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

  /// <summary>Converts this Doodle Compressed image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(DoodleCompFile file)
    => Commodore64Graphics.DecodeHires(file.BitmapData, file.ScreenRam, FixedWidth, FixedHeight);


  /// <summary>Encodes a picture as a compressed Doodle, scaling it to 320x200 first.</summary>
  public static DoodleCompFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight).PixelData;
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenRamSize];
    Commodore64Graphics.EncodeHires(rgb, FixedWidth, FixedHeight, bitmap, screen);

    return new() { LoadAddress = DefaultLoadAddress, BitmapData = bitmap, ScreenRam = screen };
  }

}
