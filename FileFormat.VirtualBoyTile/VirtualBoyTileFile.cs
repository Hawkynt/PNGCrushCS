using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.VirtualBoyTile;

/// <summary>In-memory representation of a Virtual Boy 2bpp red tile data image.</summary>
public sealed class VirtualBoyTileFile : IImageFileFormat<VirtualBoyTileFile> {

  internal const int BytesPerTile = 16;
  internal const int TileSize = 8;
  internal const int TilesPerRow = 16;
  internal const int BitsPerPixel = 2;
  internal const int PaletteColors = 4;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 85, 0, 0, 170, 0, 0, 255, 0, 0];

  static string IImageFileFormat<VirtualBoyTileFile>.PrimaryExtension => ".vbt";
  static string[] IImageFileFormat<VirtualBoyTileFile>.FileExtensions => [".vbt", ".vb", ".vboy"];
  static FormatCapability IImageFileFormat<VirtualBoyTileFile>.Capabilities => FormatCapability.IndexedOnly;
  static VirtualBoyTileFile IImageFileFormat<VirtualBoyTileFile>.FromFile(FileInfo file) => VirtualBoyTileReader.FromFile(file);
  static VirtualBoyTileFile IImageFileFormat<VirtualBoyTileFile>.FromBytes(byte[] data) => VirtualBoyTileReader.FromBytes(data);
  static VirtualBoyTileFile IImageFileFormat<VirtualBoyTileFile>.FromStream(Stream stream) => VirtualBoyTileReader.FromStream(stream);
  static byte[] IImageFileFormat<VirtualBoyTileFile>.ToBytes(VirtualBoyTileFile file) => VirtualBoyTileWriter.ToBytes(file);

  public int Width { get; init; } = TilesPerRow * TileSize;
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = _DefaultPalette[..];

  public static RawImage ToRawImage(VirtualBoyTileFile file) {
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

  public static VirtualBoyTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"VirtualBoyTile tile data requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != TilesPerRow * TileSize)
      throw new ArgumentException($"VirtualBoyTile tile data requires width {TilesPerRow * TileSize}, got {image.Width}.", nameof(image));

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
