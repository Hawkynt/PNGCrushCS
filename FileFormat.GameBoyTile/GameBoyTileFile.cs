using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GameBoyTile;

/// <summary>In-memory representation of a Game Boy 2bpp tile data image.</summary>
public sealed class GameBoyTileFile : IImageFileFormat<GameBoyTileFile> {

  /// <summary>Bytes per tile (8 rows x 2 bytes per row).</summary>
  internal const int BytesPerTile = 16;

  /// <summary>Tile dimension in pixels.</summary>
  internal const int TileSize = 8;

  /// <summary>Number of tile columns in the layout (tiles arranged 16 per row).</summary>
  internal const int TilesPerRow = 16;

  /// <summary>The default Game Boy green-shade palette as RGB triplets (4 entries x 3 bytes).</summary>
  private static readonly byte[] _DefaultPalette = [
    224, 248, 208,
    136, 192, 112,
    52, 104, 86,
    8, 24, 32
  ];

  static string IImageFileFormat<GameBoyTileFile>.PrimaryExtension => ".2bpp";
  static string[] IImageFileFormat<GameBoyTileFile>.FileExtensions => [".2bpp", ".cgb"];
  static FormatCapability IImageFileFormat<GameBoyTileFile>.Capabilities => FormatCapability.IndexedOnly;
  static GameBoyTileFile IImageFileFormat<GameBoyTileFile>.FromFile(FileInfo file) => GameBoyTileReader.FromFile(file);
  static GameBoyTileFile IImageFileFormat<GameBoyTileFile>.FromBytes(byte[] data) => GameBoyTileReader.FromBytes(data);
  static GameBoyTileFile IImageFileFormat<GameBoyTileFile>.FromStream(Stream stream) => GameBoyTileReader.FromStream(stream);
  static byte[] IImageFileFormat<GameBoyTileFile>.ToBytes(GameBoyTileFile file) => GameBoyTileWriter.ToBytes(file);

  /// <summary>Image width in pixels (always 128 = 16 tiles x 8 pixels).</summary>
  public int Width { get; init; } = TilesPerRow * TileSize;

  /// <summary>Image height in pixels (ceil(tileCount / 16) * 8).</summary>
  public int Height { get; init; }

  /// <summary>Indexed pixel data (1 byte per pixel, values 0-3).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Palette as RGB triplets (4 entries x 3 bytes = 12 bytes).</summary>
  public byte[] Palette { get; init; } = _DefaultPalette[..];

  /// <summary>Converts a Game Boy tile file to a <see cref="RawImage"/> with Indexed8 format and the 4-color palette.</summary>
  public static RawImage ToRawImage(GameBoyTileFile file) {
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

  /// <summary>Creates a Game Boy tile file from a <see cref="RawImage"/>. Must be Indexed8 with width 128.</summary>
  public static GameBoyTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Game Boy tile data requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != TilesPerRow * TileSize)
      throw new ArgumentException($"Game Boy tile data requires width {TilesPerRow * TileSize}, got {image.Width}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette != null && image.Palette.Length >= 12 ? image.Palette[..] : _DefaultPalette[..],
    };
  }
}
