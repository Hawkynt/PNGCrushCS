using System;
using FileFormat.Core;

namespace FileFormat.NeoGeoSprite;

/// <summary>In-memory representation of a Neo Geo 4bpp sprite tile data image.</summary>
public readonly record struct NeoGeoSpriteFile : IImageFormatReader<NeoGeoSpriteFile>, IImageToRawImage<NeoGeoSpriteFile>, IImageFromRawImage<NeoGeoSpriteFile>, IImageFormatWriter<NeoGeoSpriteFile> {

  internal const int BytesPerTile = 32;
  internal const int TileSize = 8;
  internal const int TilesPerRow = 16;
  internal const int BitsPerPixel = 4;
  internal const int PaletteColors = 16;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFormatMetadata<NeoGeoSpriteFile>.PrimaryExtension => ".neo";
  static string[] IImageFormatMetadata<NeoGeoSpriteFile>.FileExtensions => [".neo", ".spr"];
  static NeoGeoSpriteFile IImageFormatReader<NeoGeoSpriteFile>.FromSpan(ReadOnlySpan<byte> data) => NeoGeoSpriteReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<NeoGeoSpriteFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<NeoGeoSpriteFile>.ToBytes(NeoGeoSpriteFile file) => NeoGeoSpriteWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(NeoGeoSpriteFile file) {
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
