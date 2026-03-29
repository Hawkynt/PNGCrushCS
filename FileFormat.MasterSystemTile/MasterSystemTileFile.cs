using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MasterSystemTile;

/// <summary>In-memory representation of Sega Master System / Game Gear 4bpp planar tile data (8x8 tiles, 32 bytes/tile, 16 tiles per row).</summary>
public sealed class MasterSystemTileFile : IImageFileFormat<MasterSystemTileFile> {

  /// <summary>Number of pixels per tile row/column.</summary>
  internal const int TileSize = 8;

  /// <summary>Number of bitplanes per pixel.</summary>
  internal const int PlanesPerPixel = 4;

  /// <summary>Number of bytes per tile (8 rows x 4 planes).</summary>
  internal const int BytesPerTile = TileSize * PlanesPerPixel;

  /// <summary>Number of tiles arranged horizontally in the output image.</summary>
  internal const int TilesPerRow = 16;

  /// <summary>Fixed image width: 16 tiles x 8 pixels = 128.</summary>
  internal const int FixedWidth = TilesPerRow * TileSize;

  /// <summary>Maximum palette entries for 4bpp.</summary>
  internal const int MaxPaletteEntries = 16;

  /// <summary>Default 16-entry grayscale palette (RGB triplets).</summary>
  private static readonly byte[] _DefaultPalette = _BuildGrayscalePalette();

  private static byte[] _BuildGrayscalePalette() {
    var palette = new byte[MaxPaletteEntries * 3];
    for (var i = 0; i < MaxPaletteEntries; ++i) {
      var gray = (byte)(i * 255 / (MaxPaletteEntries - 1));
      palette[i * 3] = gray;
      palette[i * 3 + 1] = gray;
      palette[i * 3 + 2] = gray;
    }

    return palette;
  }

  static string IImageFileFormat<MasterSystemTileFile>.PrimaryExtension => ".sms";
  static string[] IImageFileFormat<MasterSystemTileFile>.FileExtensions => [".sms", ".gg"];
  static FormatCapability IImageFileFormat<MasterSystemTileFile>.Capabilities => FormatCapability.IndexedOnly;
  static MasterSystemTileFile IImageFileFormat<MasterSystemTileFile>.FromFile(FileInfo file) => MasterSystemTileReader.FromFile(file);
  static MasterSystemTileFile IImageFileFormat<MasterSystemTileFile>.FromBytes(byte[] data) => MasterSystemTileReader.FromBytes(data);
  static MasterSystemTileFile IImageFileFormat<MasterSystemTileFile>.FromStream(Stream stream) => MasterSystemTileReader.FromStream(stream);
  static byte[] IImageFileFormat<MasterSystemTileFile>.ToBytes(MasterSystemTileFile file) => MasterSystemTileWriter.ToBytes(file);

  /// <summary>Image width in pixels (always 128).</summary>
  public int Width { get; init; } = FixedWidth;

  /// <summary>Image height in pixels (multiple of 8).</summary>
  public int Height { get; init; }

  /// <summary>Indexed pixel data (values 0-15, one byte per pixel, row-major).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>16-entry RGB palette (48 bytes: 16 colors x 3 bytes each).</summary>
  public byte[] Palette { get; init; } = _DefaultPalette[..];

  /// <summary>Converts this file to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(MasterSystemTileFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = MaxPaletteEntries,
    };
  }

  /// <summary>Creates from a platform-independent <see cref="RawImage"/>. Must be Indexed8 with at most 16 palette entries and width 128.</summary>
  public static MasterSystemTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Master System tile data requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth)
      throw new ArgumentException($"Master System tile data requires width {FixedWidth}, got {image.Width}.", nameof(image));
    if (image.PaletteCount > MaxPaletteEntries)
      throw new ArgumentException($"Master System tile data supports at most {MaxPaletteEntries} palette entries, got {image.PaletteCount}.", nameof(image));

    var palette = image.Palette != null ? image.Palette[..] : _DefaultPalette[..];

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = palette,
    };
  }
}
