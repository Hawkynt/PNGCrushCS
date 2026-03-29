using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.WonderSwanTile;

/// <summary>In-memory representation of a WonderSwan 2bpp tile data image.</summary>
public sealed class WonderSwanTileFile : IImageFileFormat<WonderSwanTileFile> {

  internal const int BytesPerTile = 16;
  internal const int TileSize = 8;
  internal const int TilesPerRow = 16;
  internal const int BitsPerPixel = 2;
  internal const int PaletteColors = 4;

  private static readonly byte[] _DefaultPalette = [224, 248, 208, 136, 192, 112, 52, 104, 86, 8, 24, 32];

  static string IImageFileFormat<WonderSwanTileFile>.PrimaryExtension => ".wst";
  static string[] IImageFileFormat<WonderSwanTileFile>.FileExtensions => [".wst", ".ws"];
  static FormatCapability IImageFileFormat<WonderSwanTileFile>.Capabilities => FormatCapability.IndexedOnly;
  static WonderSwanTileFile IImageFileFormat<WonderSwanTileFile>.FromFile(FileInfo file) => WonderSwanTileReader.FromFile(file);
  static WonderSwanTileFile IImageFileFormat<WonderSwanTileFile>.FromBytes(byte[] data) => WonderSwanTileReader.FromBytes(data);
  static WonderSwanTileFile IImageFileFormat<WonderSwanTileFile>.FromStream(Stream stream) => WonderSwanTileReader.FromStream(stream);
  static byte[] IImageFileFormat<WonderSwanTileFile>.ToBytes(WonderSwanTileFile file) => WonderSwanTileWriter.ToBytes(file);

  public int Width { get; init; } = TilesPerRow * TileSize;
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = _DefaultPalette[..];

  public static RawImage ToRawImage(WonderSwanTileFile file) {
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
