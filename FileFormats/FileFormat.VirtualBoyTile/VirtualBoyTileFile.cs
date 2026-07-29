using System;
using FileFormat.Core;

namespace FileFormat.VirtualBoyTile;

/// <summary>In-memory representation of a Virtual Boy 2bpp red tile data image.</summary>
public readonly record struct VirtualBoyTileFile : IImageFormatReader<VirtualBoyTileFile>, IImageToRawImage<VirtualBoyTileFile>, IImageFromRawImage<VirtualBoyTileFile>, IImageFormatWriter<VirtualBoyTileFile> {

  internal const int BytesPerTile = 16;
  internal const int TileSize = 8;
  internal const int TilesPerRow = 16;
  internal const int BitsPerPixel = 2;
  internal const int PaletteColors = 4;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 85, 0, 0, 170, 0, 0, 255, 0, 0];

  static string IImageFormatMetadata<VirtualBoyTileFile>.PrimaryExtension => ".vbt";
  static string[] IImageFormatMetadata<VirtualBoyTileFile>.FileExtensions => [".vbt", ".vb", ".vboy"];
  static VirtualBoyTileFile IImageFormatReader<VirtualBoyTileFile>.FromSpan(ReadOnlySpan<byte> data) => VirtualBoyTileReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<VirtualBoyTileFile>.VideoModes => [new("Default", [(TilesPerRow * TileSize, new IntegerRange(TileSize, 4096, TileSize))], [new IntegerRange(2, 4)])];
  static byte[] IImageFormatWriter<VirtualBoyTileFile>.ToBytes(VirtualBoyTileFile file) => VirtualBoyTileWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(VirtualBoyTileFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = (file.PixelData ?? Array.Empty<byte>())[..],
      Palette = (file.Palette ?? Array.Empty<byte>())[..],
      PaletteCount = PaletteColors,
    };
  }

  public static VirtualBoyTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureIndexed(PixelFormat.Indexed8, _DefaultPalette);
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
