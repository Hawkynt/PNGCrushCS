using System;
using System.IO;

namespace FileFormat.NeoGeoSprite;

/// <summary>Reads Neo Geo 4bpp sprite tile data data from bytes, streams, or file paths.</summary>
public static class NeoGeoSpriteReader {

  public static NeoGeoSpriteFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NeoGeoSprite file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NeoGeoSpriteFile FromStream(Stream stream) {
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

  public static NeoGeoSpriteFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < NeoGeoSpriteFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid NeoGeoSprite file (minimum {NeoGeoSpriteFile.BytesPerTile} bytes).");
    if (data.Length % NeoGeoSpriteFile.BytesPerTile != 0)
      throw new InvalidDataException($"NeoGeoSprite tile data must be a multiple of {NeoGeoSpriteFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / NeoGeoSpriteFile.BytesPerTile;
    var tileRows = (tileCount + NeoGeoSpriteFile.TilesPerRow - 1) / NeoGeoSpriteFile.TilesPerRow;
    var width = NeoGeoSpriteFile.TilesPerRow * NeoGeoSpriteFile.TileSize;
    var height = tileRows * NeoGeoSpriteFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % NeoGeoSpriteFile.TilesPerRow;
      var tileRow = t / NeoGeoSpriteFile.TilesPerRow;
      var tileOffset = t * NeoGeoSpriteFile.BytesPerTile;

      for (var row = 0; row < NeoGeoSpriteFile.TileSize; ++row) {
        var b0 = data[tileOffset + row * 2];
        var b1 = data[tileOffset + row * 2 + 1];
        var b2 = data[tileOffset + row * 2 + 16];
        var b3 = data[tileOffset + row * 2 + 17];
        for (var bit = 0; bit < 8; ++bit) {
          var shift = 7 - bit;
          var colorIndex = (byte)(
            ((b0 >> shift) & 1) |
            (((b1 >> shift) & 1) << 1) |
            (((b2 >> shift) & 1) << 2) |
            (((b3 >> shift) & 1) << 3));
          var px = tileCol * NeoGeoSpriteFile.TileSize + bit;
          var py = tileRow * NeoGeoSpriteFile.TileSize + row;
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

  public static NeoGeoSpriteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NeoGeoSpriteFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid NeoGeoSprite file (minimum {NeoGeoSpriteFile.BytesPerTile} bytes).");
    if (data.Length % NeoGeoSpriteFile.BytesPerTile != 0)
      throw new InvalidDataException($"NeoGeoSprite tile data must be a multiple of {NeoGeoSpriteFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / NeoGeoSpriteFile.BytesPerTile;
    var tileRows = (tileCount + NeoGeoSpriteFile.TilesPerRow - 1) / NeoGeoSpriteFile.TilesPerRow;
    var width = NeoGeoSpriteFile.TilesPerRow * NeoGeoSpriteFile.TileSize;
    var height = tileRows * NeoGeoSpriteFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % NeoGeoSpriteFile.TilesPerRow;
      var tileRow = t / NeoGeoSpriteFile.TilesPerRow;
      var tileOffset = t * NeoGeoSpriteFile.BytesPerTile;

      for (var row = 0; row < NeoGeoSpriteFile.TileSize; ++row) {
        var b0 = data[tileOffset + row * 2];
        var b1 = data[tileOffset + row * 2 + 1];
        var b2 = data[tileOffset + row * 2 + 16];
        var b3 = data[tileOffset + row * 2 + 17];
        for (var bit = 0; bit < 8; ++bit) {
          var shift = 7 - bit;
          var colorIndex = (byte)(
            ((b0 >> shift) & 1) |
            (((b1 >> shift) & 1) << 1) |
            (((b2 >> shift) & 1) << 2) |
            (((b3 >> shift) & 1) << 3));
          var px = tileCol * NeoGeoSpriteFile.TileSize + bit;
          var py = tileRow * NeoGeoSpriteFile.TileSize + row;
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
