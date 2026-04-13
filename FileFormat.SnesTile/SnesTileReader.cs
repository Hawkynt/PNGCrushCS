using System;
using System.IO;

namespace FileFormat.SnesTile;

/// <summary>Reads SNES 4BPP planar tile data from bytes, streams, or file paths.</summary>
public static class SnesTileReader {

  public static SnesTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SNES tile file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SnesTileFile FromStream(Stream stream) {
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

  public static SnesTileFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < SnesTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid SNES tile file. Minimum {SnesTileFile.BytesPerTile} bytes, got {data.Length}.");
    if (data.Length % SnesTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"SNES tile data must be a multiple of {SnesTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / SnesTileFile.BytesPerTile;
    var tileRows = (tileCount + SnesTileFile.TilesPerRow - 1) / SnesTileFile.TilesPerRow;
    var width = SnesTileFile.FixedWidth;
    var height = tileRows * SnesTileFile.TileSize;
    var pixels = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileX = t % SnesTileFile.TilesPerRow;
      var tileY = t / SnesTileFile.TilesPerRow;
      var tileOffset = t * SnesTileFile.BytesPerTile;

      for (var row = 0; row < SnesTileFile.TileSize; ++row) {
        // Planes 0+1 interleaved in first 16 bytes
        var plane0 = data[tileOffset + row * 2];
        var plane1 = data[tileOffset + row * 2 + 1];
        // Planes 2+3 interleaved in next 16 bytes
        var plane2 = data[tileOffset + 16 + row * 2];
        var plane3 = data[tileOffset + 16 + row * 2 + 1];

        for (var bit = 0; bit < SnesTileFile.TileSize; ++bit) {
          var shift = 7 - bit;
          var b0 = (plane0 >> shift) & 1;
          var b1 = (plane1 >> shift) & 1;
          var b2 = (plane2 >> shift) & 1;
          var b3 = (plane3 >> shift) & 1;
          var pixelValue = (byte)(b0 | (b1 << 1) | (b2 << 2) | (b3 << 3));

          var px = tileX * SnesTileFile.TileSize + bit;
          var py = tileY * SnesTileFile.TileSize + row;
          pixels[py * width + px] = pixelValue;
        }
      }
    }

    return new SnesTileFile {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
    }

  public static SnesTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SnesTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid SNES tile file. Minimum {SnesTileFile.BytesPerTile} bytes, got {data.Length}.");
    if (data.Length % SnesTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"SNES tile data must be a multiple of {SnesTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / SnesTileFile.BytesPerTile;
    var tileRows = (tileCount + SnesTileFile.TilesPerRow - 1) / SnesTileFile.TilesPerRow;
    var width = SnesTileFile.FixedWidth;
    var height = tileRows * SnesTileFile.TileSize;
    var pixels = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileX = t % SnesTileFile.TilesPerRow;
      var tileY = t / SnesTileFile.TilesPerRow;
      var tileOffset = t * SnesTileFile.BytesPerTile;

      for (var row = 0; row < SnesTileFile.TileSize; ++row) {
        // Planes 0+1 interleaved in first 16 bytes
        var plane0 = data[tileOffset + row * 2];
        var plane1 = data[tileOffset + row * 2 + 1];
        // Planes 2+3 interleaved in next 16 bytes
        var plane2 = data[tileOffset + 16 + row * 2];
        var plane3 = data[tileOffset + 16 + row * 2 + 1];

        for (var bit = 0; bit < SnesTileFile.TileSize; ++bit) {
          var shift = 7 - bit;
          var b0 = (plane0 >> shift) & 1;
          var b1 = (plane1 >> shift) & 1;
          var b2 = (plane2 >> shift) & 1;
          var b3 = (plane3 >> shift) & 1;
          var pixelValue = (byte)(b0 | (b1 << 1) | (b2 << 2) | (b3 << 3));

          var px = tileX * SnesTileFile.TileSize + bit;
          var py = tileY * SnesTileFile.TileSize + row;
          pixels[py * width + px] = pixelValue;
        }
      }
    }

    return new SnesTileFile {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }
}
