using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GeoPaint;

/// <summary>In-memory representation of a C64 GEOS GeoPaint image (640 pixels wide, 1bpp monochrome, RLE-compressed scanlines).</summary>
public sealed class GeoPaintFile : IImageFileFormat<GeoPaintFile> {

  /// <summary>Fixed width of a GeoPaint image in pixels.</summary>
  public const int FixedWidth = 640;

  /// <summary>Maximum height of a GeoPaint image in scanlines.</summary>
  public const int MaxHeight = 720;

  /// <summary>Bytes per uncompressed scanline (640 / 8).</summary>
  public const int BytesPerRow = 80;

  static string IImageFileFormat<GeoPaintFile>.PrimaryExtension => ".geo";
  static string[] IImageFileFormat<GeoPaintFile>.FileExtensions => [".geo"];
  static FormatCapability IImageFileFormat<GeoPaintFile>.Capabilities => FormatCapability.MonochromeOnly;
  static GeoPaintFile IImageFileFormat<GeoPaintFile>.FromFile(FileInfo file) => GeoPaintReader.FromFile(file);
  static GeoPaintFile IImageFileFormat<GeoPaintFile>.FromBytes(byte[] data) => GeoPaintReader.FromBytes(data);
  static GeoPaintFile IImageFileFormat<GeoPaintFile>.FromStream(Stream stream) => GeoPaintReader.FromStream(stream);
  static RawImage IImageFileFormat<GeoPaintFile>.ToRawImage(GeoPaintFile file) => file.ToRawImage();
  static GeoPaintFile IImageFileFormat<GeoPaintFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<GeoPaintFile>.ToBytes(GeoPaintFile file) => GeoPaintWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => FixedWidth;

  /// <summary>Number of scanlines (1..720).</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB-first, 80 bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public RawImage ToRawImage() => new() {
    Width = FixedWidth,
    Height = this.Height,
    Format = PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static GeoPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != FixedWidth)
      throw new ArgumentException($"Expected width {FixedWidth} but got {image.Width}.", nameof(image));
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    if (image.Height < 1 || image.Height > MaxHeight)
      throw new ArgumentException($"Height must be 1..{MaxHeight} but got {image.Height}.", nameof(image));

    return new() {
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
