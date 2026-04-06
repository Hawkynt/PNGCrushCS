using System;
using System.IO;

namespace FileFormat.Atari2600;

/// <summary>Parses atari 2600 tia playfield graphics from raw bytes.</summary>
public static class Atari2600Reader {

  public static Atari2600File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Atari2600File FromStream(Stream stream) {
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

  public static Atari2600File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Atari2600File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Atari2600File.BytesPerTile)
      throw new InvalidDataException($"Data too small: {data.Length} bytes.");
    if (data.Length % Atari2600File.BytesPerTile != 0)
      throw new InvalidDataException($"Data size {data.Length} is not a multiple of 8.");

    var tileCount = data.Length / Atari2600File.BytesPerTile;
    var tileCols = Atari2600File.TilesPerRow;
    var tileRows = (tileCount + tileCols - 1) / tileCols;
    var width = tileCols * Atari2600File.TileSize;
    var height = tileRows * Atari2600File.TileSize;
    var pixelData = new byte[width * height];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        if (t >= tileCount)
          break;
        var tileOffset = t * Atari2600File.BytesPerTile;

        for (var row = 0; row < Atari2600File.TileSize; ++row) {
          var b = data[tileOffset + row];
          for (var bit = 0; bit < Atari2600File.TileSize; ++bit) {
            var px = tileCol * Atari2600File.TileSize + bit;
            var py = tileRow * Atari2600File.TileSize + row;
            pixelData[py * width + px] = (byte)((b >> (7 - bit)) & 1);
          }
        }
      }

    return new Atari2600File { Width = width, Height = height, PixelData = pixelData };
  }
}
