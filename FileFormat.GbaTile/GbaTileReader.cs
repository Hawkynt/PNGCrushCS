using System;
using System.IO;

namespace FileFormat.GbaTile;

/// <summary>Reads Game Boy Advance 4bpp tile data data from bytes, streams, or file paths.</summary>
public static class GbaTileReader {

  public static GbaTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GbaTile file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GbaTileFile FromStream(Stream stream) {
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

  public static GbaTileFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static GbaTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < GbaTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid GbaTile file (minimum {GbaTileFile.BytesPerTile} bytes).");
    if (data.Length % GbaTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"GbaTile tile data must be a multiple of {GbaTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / GbaTileFile.BytesPerTile;
    var tileRows = (tileCount + GbaTileFile.TilesPerRow - 1) / GbaTileFile.TilesPerRow;
    var width = GbaTileFile.TilesPerRow * GbaTileFile.TileSize;
    var height = tileRows * GbaTileFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % GbaTileFile.TilesPerRow;
      var tileRow = t / GbaTileFile.TilesPerRow;
      var tileOffset = t * GbaTileFile.BytesPerTile;

      for (var row = 0; row < GbaTileFile.TileSize; ++row) {
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
          var px = tileCol * GbaTileFile.TileSize + bit;
          var py = tileRow * GbaTileFile.TileSize + row;
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
