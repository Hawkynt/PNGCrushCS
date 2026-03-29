using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pagefox;

/// <summary>In-memory representation of a Pagefox hires image (640x200, 1bpp monochrome, 16384 bytes).</summary>
public sealed class PagefoxFile : IImageFileFormat<PagefoxFile> {

  /// <summary>Expected file size in bytes (640 * 200 / 8 = 16000, padded to 16384).</summary>
  public const int ExpectedFileSize = 16384;

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 640;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  /// <summary>Bytes per row (640 / 8 = 80).</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  /// <summary>Active bitmap data size (80 * 200 = 16000).</summary>
  internal const int ActiveDataSize = BytesPerRow * PixelHeight;

  /// <summary>Black and white palette for indexed output (2 entries, 3 bytes each).</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<PagefoxFile>.PrimaryExtension => ".pfx";
  static string[] IImageFileFormat<PagefoxFile>.FileExtensions => [".pfx"];
  static PagefoxFile IImageFileFormat<PagefoxFile>.FromFile(FileInfo file) => PagefoxReader.FromFile(file);
  static PagefoxFile IImageFileFormat<PagefoxFile>.FromBytes(byte[] data) => PagefoxReader.FromBytes(data);
  static PagefoxFile IImageFileFormat<PagefoxFile>.FromStream(Stream stream) => PagefoxReader.FromStream(stream);
  static RawImage IImageFileFormat<PagefoxFile>.ToRawImage(PagefoxFile file) => ToRawImage(file);
  static PagefoxFile IImageFileFormat<PagefoxFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<PagefoxFile>.ToBytes(PagefoxFile file) => PagefoxWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw bitmap data (16384 bytes; first 16000 bytes are active pixel data, remainder is padding).</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>
  /// Converts this Pagefox image to a platform-independent <see cref="RawImage"/> in Indexed1 format.
  /// 1bpp MSB-first, 80 bytes per row, 200 rows. Output 640x200 with B&amp;W palette.
  /// </summary>
  public static RawImage ToRawImage(PagefoxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowStride = BytesPerRow; // 80 bytes per row
    var pixelData = new byte[rowStride * PixelHeight];

    // Copy active bitmap area (first 16000 bytes)
    var copyLen = Math.Min(file.RawData.Length, ActiveDataSize);
    file.RawData.AsSpan(0, copyLen).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Pagefox image from an Indexed1 640x200 raw image.</summary>
  public static PagefoxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rawData = new byte[ExpectedFileSize];
    var copyLen = Math.Min(image.PixelData.Length, ActiveDataSize);
    image.PixelData.AsSpan(0, copyLen).CopyTo(rawData.AsSpan(0));

    return new() { RawData = rawData };
  }
}
