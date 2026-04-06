using System;
using FileFormat.Core;

namespace FileFormat.AtariGfb;

/// <summary>In-memory representation of an Atari 8-bit GFB screen dump (320x192, 1bpp monochrome).</summary>
public readonly record struct AtariGfbFile : IImageFormatReader<AtariGfbFile>, IImageToRawImage<AtariGfbFile>, IImageFromRawImage<AtariGfbFile>, IImageFormatWriter<AtariGfbFile> {

  /// <summary>Fixed width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Fixed height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline row (320 / 8 = 40).</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  /// <summary>Exact file size in bytes (40 x 192 = 7680).</summary>
  internal const int FileSize = BytesPerRow * PixelHeight;

  static string IImageFormatMetadata<AtariGfbFile>.PrimaryExtension => ".gfb";
  static string[] IImageFormatMetadata<AtariGfbFile>.FileExtensions => [".gfb"];
  static AtariGfbFile IImageFormatReader<AtariGfbFile>.FromSpan(ReadOnlySpan<byte> data) => AtariGfbReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<AtariGfbFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<AtariGfbFile>.ToBytes(AtariGfbFile file) => AtariGfbWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw screen data (7680 bytes). 1bpp MSB-first, 40 bytes per row.</summary>
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this GFB screen dump to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(AtariGfbFile file) {

    var pixelData = new byte[BytesPerRow * PixelHeight];
    var srcLen = Math.Min(file.RawData.Length, FileSize);
    file.RawData.AsSpan(0, srcLen).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a GFB screen dump from an Indexed1 raw image (320x192).</summary>
  public static AtariGfbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rawData = new byte[FileSize];
    var srcLen = Math.Min(image.PixelData.Length, FileSize);
    image.PixelData.AsSpan(0, srcLen).CopyTo(rawData);

    return new() { RawData = rawData };
  }
}
