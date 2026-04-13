using System;
using System.IO;

namespace FileFormat.GameBoyTile;

/// <summary>Reads Game Boy 2bpp tile data from bytes, streams, or file paths.</summary>
public static class GameBoyTileReader {

  public static GameBoyTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Game Boy tile file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GameBoyTileFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static GameBoyTileFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < GameBoyTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid Game Boy tile file (minimum {GameBoyTileFile.BytesPerTile} bytes).");
    if (data.Length % GameBoyTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"Game Boy tile data must be a multiple of {GameBoyTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / GameBoyTileFile.BytesPerTile;
    var tileRows = (tileCount + GameBoyTileFile.TilesPerRow - 1) / GameBoyTileFile.TilesPerRow;
    var width = GameBoyTileFile.TilesPerRow * GameBoyTileFile.TileSize;
    var height = tileRows * GameBoyTileFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % GameBoyTileFile.TilesPerRow;
      var tileRow = t / GameBoyTileFile.TilesPerRow;
      var tileOffset = t * GameBoyTileFile.BytesPerTile;

      for (var row = 0; row < GameBoyTileFile.TileSize; ++row) {
        var plane0 = data[tileOffset + row * 2];
        var plane1 = data[tileOffset + row * 2 + 1];

        for (var bit = 0; bit < GameBoyTileFile.TileSize; ++bit) {
          var shift = 7 - bit;
          var colorIndex = (byte)(((plane1 >> shift) & 1) << 1 | ((plane0 >> shift) & 1));
          var px = tileCol * GameBoyTileFile.TileSize + bit;
          var py = tileRow * GameBoyTileFile.TileSize + row;
          pixelData[py * width + px] = colorIndex;
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static GameBoyTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < GameBoyTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid Game Boy tile file (minimum {GameBoyTileFile.BytesPerTile} bytes).");
    if (data.Length % GameBoyTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"Game Boy tile data must be a multiple of {GameBoyTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / GameBoyTileFile.BytesPerTile;
    var tileRows = (tileCount + GameBoyTileFile.TilesPerRow - 1) / GameBoyTileFile.TilesPerRow;
    var width = GameBoyTileFile.TilesPerRow * GameBoyTileFile.TileSize;
    var height = tileRows * GameBoyTileFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % GameBoyTileFile.TilesPerRow;
      var tileRow = t / GameBoyTileFile.TilesPerRow;
      var tileOffset = t * GameBoyTileFile.BytesPerTile;

      for (var row = 0; row < GameBoyTileFile.TileSize; ++row) {
        var plane0 = data[tileOffset + row * 2];
        var plane1 = data[tileOffset + row * 2 + 1];

        for (var bit = 0; bit < GameBoyTileFile.TileSize; ++bit) {
          var shift = 7 - bit;
          var colorIndex = (byte)(((plane1 >> shift) & 1) << 1 | ((plane0 >> shift) & 1));
          var px = tileCol * GameBoyTileFile.TileSize + bit;
          var py = tileRow * GameBoyTileFile.TileSize + row;
          pixelData[py * width + px] = colorIndex;
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
