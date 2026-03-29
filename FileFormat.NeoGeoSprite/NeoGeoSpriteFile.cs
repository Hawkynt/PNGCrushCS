using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NeoGeoSprite;

/// <summary>In-memory representation of a Neo Geo 4bpp sprite tile data image.</summary>
public sealed class NeoGeoSpriteFile : IImageFileFormat<NeoGeoSpriteFile> {

  internal const int BytesPerTile = 32;
  internal const int TileSize = 8;
  internal const int TilesPerRow = 16;
  internal const int BitsPerPixel = 4;
  internal const int PaletteColors = 16;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFileFormat<NeoGeoSpriteFile>.PrimaryExtension => ".neo";
  static string[] IImageFileFormat<NeoGeoSpriteFile>.FileExtensions => [".neo", ".spr"];
  static FormatCapability IImageFileFormat<NeoGeoSpriteFile>.Capabilities => FormatCapability.IndexedOnly;
  static NeoGeoSpriteFile IImageFileFormat<NeoGeoSpriteFile>.FromFile(FileInfo file) => NeoGeoSpriteReader.FromFile(file);
  static NeoGeoSpriteFile IImageFileFormat<NeoGeoSpriteFile>.FromBytes(byte[] data) => NeoGeoSpriteReader.FromBytes(data);
  static NeoGeoSpriteFile IImageFileFormat<NeoGeoSpriteFile>.FromStream(Stream stream) => NeoGeoSpriteReader.FromStream(stream);
  static byte[] IImageFileFormat<NeoGeoSpriteFile>.ToBytes(NeoGeoSpriteFile file) => NeoGeoSpriteWriter.ToBytes(file);

  public int Width { get; init; } = TilesPerRow * TileSize;
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = _DefaultPalette[..];

  public static RawImage ToRawImage(NeoGeoSpriteFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteColors,
    };
  }

  public static NeoGeoSpriteFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"NeoGeoSprite tile data requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != TilesPerRow * TileSize)
      throw new ArgumentException($"NeoGeoSprite tile data requires width {TilesPerRow * TileSize}, got {image.Width}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette != null && image.Palette.Length >= PaletteColors * 3
        ? image.Palette[..]
        : _DefaultPalette[..],
    };
  }
}
