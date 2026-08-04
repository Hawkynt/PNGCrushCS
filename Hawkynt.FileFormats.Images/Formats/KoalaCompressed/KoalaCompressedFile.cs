using System;
using FileFormat.Core;

namespace FileFormat.KoalaCompressed;

/// <summary>In-memory representation of a Commodore 64 compressed Koala multicolor image.</summary>
public readonly record struct KoalaCompressedFile : IImageFormatReader<KoalaCompressedFile>, IImageToRawImage<KoalaCompressedFile>, IImageFromRawImage<KoalaCompressedFile>, IImageFormatWriter<KoalaCompressedFile> {

  static string IImageFormatMetadata<KoalaCompressedFile>.PrimaryExtension => ".gg";
  static string[] IImageFormatMetadata<KoalaCompressedFile>.FileExtensions => [".gg"];
  static KoalaCompressedFile IImageFormatReader<KoalaCompressedFile>.FromSpan(ReadOnlySpan<byte> data) => KoalaCompressedReader.FromSpan(data);
  static byte[] IImageFormatWriter<KoalaCompressedFile>.ToBytes(KoalaCompressedFile file) => KoalaCompressedWriter.ToBytes(file);

  /// <summary>The fixed width of a Koala image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Koala image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The minimum file size in bytes (2 load address + at least 2 bytes RLE data).</summary>
  public const int MinFileSize = 4;

  /// <summary>The expected decompressed data size in bytes (8000 + 1000 + 1000 + 1).</summary>
  internal const int DecompressedDataSize = 10001;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>The RLE escape byte.</summary>
  internal const byte RleEscapeByte = 0xFE;

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

  /// <summary>Converts this compressed Koala image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(KoalaCompressedFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Reduces a picture to a Koala screen, which the writer then packs.</summary>
  /// <remarks>
  /// The same screen a plain Koala holds; only what surrounds it differs, so the reduction is the
  /// same one and the packing is the writer's business rather than this one's.
  /// </remarks>
  public static KoalaCompressedFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var videoMatrix = new byte[VideoMatrixSize];
    var colorRam = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, FixedWidth, FixedHeight, bitmap, videoMatrix, colorRam);

    return new() {
      LoadAddress = 0x6000,
      BitmapData = bitmap,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = background,
    };
  }

}
