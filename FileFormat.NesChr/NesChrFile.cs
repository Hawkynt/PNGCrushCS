using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NesChr;

/// <summary>In-memory representation of NES CHR tile data (2bpp planar, 8x8 tiles, 16 tiles per row).</summary>
public sealed class NesChrFile : IImageFileFormat<NesChrFile> {

  /// <summary>Number of pixels per tile row/column.</summary>
  internal const int TileSize = 8;

  /// <summary>Number of bytes per tile (two planes of 8 bytes each).</summary>
  internal const int BytesPerTile = 16;

  /// <summary>Number of tiles arranged horizontally in the output image.</summary>
  internal const int TilesPerRow = 16;

  /// <summary>Fixed image width: 16 tiles x 8 pixels = 128.</summary>
  internal const int FixedWidth = TilesPerRow * TileSize;

  /// <summary>Default 4-entry grayscale palette (RGB triplets): black, dark gray, light gray, white.</summary>
  private static readonly byte[] _DefaultPalette = [0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255];

  static string IImageFileFormat<NesChrFile>.PrimaryExtension => ".chr";
  static string[] IImageFileFormat<NesChrFile>.FileExtensions => [".chr"];
  static FormatCapability IImageFileFormat<NesChrFile>.Capabilities => FormatCapability.IndexedOnly;
  static NesChrFile IImageFileFormat<NesChrFile>.FromFile(FileInfo file) => NesChrReader.FromFile(file);
  static NesChrFile IImageFileFormat<NesChrFile>.FromBytes(byte[] data) => NesChrReader.FromBytes(data);
  static NesChrFile IImageFileFormat<NesChrFile>.FromStream(Stream stream) => NesChrReader.FromStream(stream);
  static byte[] IImageFileFormat<NesChrFile>.ToBytes(NesChrFile file) => NesChrWriter.ToBytes(file);

  /// <summary>Image width in pixels (always 128).</summary>
  public int Width { get; init; } = FixedWidth;

  /// <summary>Image height in pixels (multiple of 8).</summary>
  public int Height { get; init; }

  /// <summary>Indexed pixel data (values 0-3, one byte per pixel, row-major).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>4-entry RGB palette (12 bytes: 4 colors x 3 bytes each).</summary>
  public byte[] Palette { get; init; } = _DefaultPalette[..];

  /// <summary>Converts this NES CHR file to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(NesChrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = 4,
    };
  }

  /// <summary>Creates a NES CHR file from a platform-independent <see cref="RawImage"/>. Must be Indexed8 with at most 4 palette entries and width 128.</summary>
  public static NesChrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"NES CHR requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth)
      throw new ArgumentException($"NES CHR requires width {FixedWidth}, got {image.Width}.", nameof(image));
    if (image.PaletteCount > 4)
      throw new ArgumentException($"NES CHR supports at most 4 palette entries, got {image.PaletteCount}.", nameof(image));

    var palette = image.Palette != null ? image.Palette[..] : _DefaultPalette[..];

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = palette,
    };
  }
}
