using System;
using System.IO;

namespace FileFormat.NesChr;

/// <summary>Reads NES CHR tile data from bytes, streams, or file paths.</summary>
public static class NesChrReader {

  public static NesChrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CHR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NesChrFile FromStream(Stream stream) {
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

  public static NesChrFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static NesChrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NesChrFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid NES CHR file. Minimum {NesChrFile.BytesPerTile} bytes, got {data.Length}.");
    if (data.Length % NesChrFile.BytesPerTile != 0)
      throw new InvalidDataException($"NES CHR data must be a multiple of {NesChrFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / NesChrFile.BytesPerTile;
    var tileRows = (tileCount + NesChrFile.TilesPerRow - 1) / NesChrFile.TilesPerRow;
    var width = NesChrFile.FixedWidth;
    var height = tileRows * NesChrFile.TileSize;
    var pixels = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileX = t % NesChrFile.TilesPerRow;
      var tileY = t / NesChrFile.TilesPerRow;
      var tileOffset = t * NesChrFile.BytesPerTile;

      for (var row = 0; row < NesChrFile.TileSize; ++row) {
        var plane0 = data[tileOffset + row];
        var plane1 = data[tileOffset + NesChrFile.TileSize + row];

        for (var bit = 0; bit < NesChrFile.TileSize; ++bit) {
          var lo = (plane0 >> (7 - bit)) & 1;
          var hi = (plane1 >> (7 - bit)) & 1;
          var pixelValue = (byte)((hi << 1) | lo);

          var px = tileX * NesChrFile.TileSize + bit;
          var py = tileY * NesChrFile.TileSize + row;
          pixels[py * width + px] = pixelValue;
        }
      }
    }

    return new NesChrFile {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }
}
