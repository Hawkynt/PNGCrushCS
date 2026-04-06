using System;
using FileFormat.Core;

namespace FileFormat.AtariHr;

/// <summary>In-memory representation of an Atari 8-bit HR hires screen dump (320x192, 1bpp monochrome).</summary>
public readonly record struct AtariHrFile : IImageFormatReader<AtariHrFile>, IImageToRawImage<AtariHrFile>, IImageFromRawImage<AtariHrFile>, IImageFormatWriter<AtariHrFile> {

  /// <summary>Fixed width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Fixed height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline row (320 / 8 = 40).</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  /// <summary>Exact file size in bytes (40 x 192 = 7680).</summary>
  internal const int FileSize = BytesPerRow * PixelHeight;

  static string IImageFormatMetadata<AtariHrFile>.PrimaryExtension => ".hr";
  static string[] IImageFormatMetadata<AtariHrFile>.FileExtensions => [".hr"];
  static AtariHrFile IImageFormatReader<AtariHrFile>.FromSpan(ReadOnlySpan<byte> data) => AtariHrReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<AtariHrFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<AtariHrFile>.ToBytes(AtariHrFile file) => AtariHrWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw screen data (7680 bytes). 1bpp MSB-first, 40 bytes per row.</summary>
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this HR screen dump to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(AtariHrFile file) {

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

  /// <summary>Creates an HR screen dump from an Indexed1 raw image (320x192).</summary>
  public static AtariHrFile FromRawImage(RawImage image) {
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
