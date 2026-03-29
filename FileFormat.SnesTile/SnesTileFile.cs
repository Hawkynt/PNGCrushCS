using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SnesTile;

/// <summary>In-memory representation of SNES 4BPP planar tile data (8x8 tiles, 16 tiles per row).</summary>
public sealed class SnesTileFile : IImageFileFormat<SnesTileFile> {

  /// <summary>Number of pixels per tile row/column.</summary>
  internal const int TileSize = 8;

  /// <summary>Number of bytes per tile (4 planes: planes 0+1 interleaved 16 bytes, planes 2+3 interleaved 16 bytes).</summary>
  internal const int BytesPerTile = 32;

  /// <summary>Number of tiles arranged horizontally in the output image.</summary>
  internal const int TilesPerRow = 16;

  /// <summary>Fixed image width: 16 tiles x 8 pixels = 128.</summary>
  internal const int FixedWidth = TilesPerRow * TileSize;

  /// <summary>Default 16-entry grayscale palette (RGB triplets).</summary>
  private static readonly byte[] _DefaultPalette = _BuildGrayscalePalette(16);

  static string IImageFileFormat<SnesTileFile>.PrimaryExtension => ".sfc";
  static string[] IImageFileFormat<SnesTileFile>.FileExtensions => [".sfc", ".snes"];
  static FormatCapability IImageFileFormat<SnesTileFile>.Capabilities => FormatCapability.IndexedOnly;
  static SnesTileFile IImageFileFormat<SnesTileFile>.FromFile(FileInfo file) => SnesTileReader.FromFile(file);
  static SnesTileFile IImageFileFormat<SnesTileFile>.FromBytes(byte[] data) => SnesTileReader.FromBytes(data);
  static SnesTileFile IImageFileFormat<SnesTileFile>.FromStream(Stream stream) => SnesTileReader.FromStream(stream);
  static byte[] IImageFileFormat<SnesTileFile>.ToBytes(SnesTileFile file) => SnesTileWriter.ToBytes(file);

  /// <summary>Image width in pixels (always 128).</summary>
  public int Width { get; init; } = FixedWidth;

  /// <summary>Image height in pixels (multiple of 8).</summary>
  public int Height { get; init; }

  /// <summary>Indexed pixel data (values 0-15, one byte per pixel, row-major).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>16-entry RGB palette (48 bytes: 16 colors x 3 bytes each).</summary>
  public byte[] Palette { get; init; } = _DefaultPalette[..];

  public static RawImage ToRawImage(SnesTileFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = 16,
    };
  }

  public static SnesTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"SNES tile requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth)
      throw new ArgumentException($"SNES tile requires width {FixedWidth}, got {image.Width}.", nameof(image));
    if (image.PaletteCount > 16)
      throw new ArgumentException($"SNES tile supports at most 16 palette entries, got {image.PaletteCount}.", nameof(image));

    var palette = image.Palette != null ? image.Palette[..] : _DefaultPalette[..];
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = palette,
    };
  }

  private static byte[] _BuildGrayscalePalette(int count) {
    var palette = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      var v = (byte)(i * 255 / (count - 1));
      palette[i * 3] = v;
      palette[i * 3 + 1] = v;
      palette[i * 3 + 2] = v;
    }
    return palette;
  }
}
