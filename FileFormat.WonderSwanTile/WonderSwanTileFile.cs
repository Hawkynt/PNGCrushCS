using System;
using FileFormat.Core;

namespace FileFormat.WonderSwanTile;

/// <summary>In-memory representation of a WonderSwan 2bpp tile data image.</summary>
public readonly record struct WonderSwanTileFile : IImageFormatReader<WonderSwanTileFile>, IImageToRawImage<WonderSwanTileFile>, IImageFromRawImage<WonderSwanTileFile>, IImageFormatWriter<WonderSwanTileFile> {

  internal const int BytesPerTile = 16;
  internal const int TileSize = 8;
  internal const int TilesPerRow = 16;
  internal const int BitsPerPixel = 2;
  internal const int PaletteColors = 4;

  private static readonly byte[] _DefaultPalette = [224, 248, 208, 136, 192, 112, 52, 104, 86, 8, 24, 32];

  static string IImageFormatMetadata<WonderSwanTileFile>.PrimaryExtension => ".wst";
  static string[] IImageFormatMetadata<WonderSwanTileFile>.FileExtensions => [".wst", ".ws"];
  static WonderSwanTileFile IImageFormatReader<WonderSwanTileFile>.FromSpan(ReadOnlySpan<byte> data) => WonderSwanTileReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<WonderSwanTileFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<WonderSwanTileFile>.ToBytes(WonderSwanTileFile file) => WonderSwanTileWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(WonderSwanTileFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteColors,
    };
  }

  public static WonderSwanTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"WonderSwanTile tile data requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != TilesPerRow * TileSize)
      throw new ArgumentException($"WonderSwanTile tile data requires width {TilesPerRow * TileSize}, got {image.Width}.", nameof(image));

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
