using System;
using System.IO;

namespace FileFormat.NdsTexture;

/// <summary>Reads Nintendo DS 4bpp tile texture data from bytes, streams, or file paths.</summary>
public static class NdsTextureReader {

  public static NdsTextureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NdsTexture file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NdsTextureFile FromStream(Stream stream) {
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

  public static NdsTextureFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < NdsTextureFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid NdsTexture file (minimum {NdsTextureFile.BytesPerTile} bytes).");
    if (data.Length % NdsTextureFile.BytesPerTile != 0)
      throw new InvalidDataException($"NdsTexture tile data must be a multiple of {NdsTextureFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / NdsTextureFile.BytesPerTile;
    var tileRows = (tileCount + NdsTextureFile.TilesPerRow - 1) / NdsTextureFile.TilesPerRow;
    var width = NdsTextureFile.TilesPerRow * NdsTextureFile.TileSize;
    var height = tileRows * NdsTextureFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % NdsTextureFile.TilesPerRow;
      var tileRow = t / NdsTextureFile.TilesPerRow;
      var tileOffset = t * NdsTextureFile.BytesPerTile;

      for (var row = 0; row < NdsTextureFile.TileSize; ++row) {
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
          var px = tileCol * NdsTextureFile.TileSize + bit;
          var py = tileRow * NdsTextureFile.TileSize + row;
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

  public static NdsTextureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NdsTextureFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid NdsTexture file (minimum {NdsTextureFile.BytesPerTile} bytes).");
    if (data.Length % NdsTextureFile.BytesPerTile != 0)
      throw new InvalidDataException($"NdsTexture tile data must be a multiple of {NdsTextureFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / NdsTextureFile.BytesPerTile;
    var tileRows = (tileCount + NdsTextureFile.TilesPerRow - 1) / NdsTextureFile.TilesPerRow;
    var width = NdsTextureFile.TilesPerRow * NdsTextureFile.TileSize;
    var height = tileRows * NdsTextureFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % NdsTextureFile.TilesPerRow;
      var tileRow = t / NdsTextureFile.TilesPerRow;
      var tileOffset = t * NdsTextureFile.BytesPerTile;

      for (var row = 0; row < NdsTextureFile.TileSize; ++row) {
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
          var px = tileCol * NdsTextureFile.TileSize + bit;
          var py = tileRow * NdsTextureFile.TileSize + row;
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
