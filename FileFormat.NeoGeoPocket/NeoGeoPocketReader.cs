using System;
using System.IO;

namespace FileFormat.NeoGeoPocket;

/// <summary>Parses neo geo pocket color 2bpp tile data from raw bytes.</summary>
public static class NeoGeoPocketReader {

  public static NeoGeoPocketFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NeoGeoPocketFile FromStream(Stream stream) {
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

  public static NeoGeoPocketFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NeoGeoPocketFile.BytesPerTile)
      throw new InvalidDataException($"Data too small: {data.Length} bytes.");
    if (data.Length % NeoGeoPocketFile.BytesPerTile != 0)
      throw new InvalidDataException($"Data size {data.Length} is not a multiple of 16.");

    var tileCount = data.Length / NeoGeoPocketFile.BytesPerTile;
    var tileCols = NeoGeoPocketFile.TilesPerRow;
    var tileRows = (tileCount + tileCols - 1) / tileCols;
    var width = tileCols * NeoGeoPocketFile.TileSize;
    var height = tileRows * NeoGeoPocketFile.TileSize;
    var pixelData = new byte[width * height];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        if (t >= tileCount)
          break;
        var tileOffset = t * NeoGeoPocketFile.BytesPerTile;

        for (var row = 0; row < NeoGeoPocketFile.TileSize; ++row) {
          var plane0 = data[tileOffset + row * 2];
          var plane1 = data[tileOffset + row * 2 + 1];

          for (var bit = 0; bit < NeoGeoPocketFile.TileSize; ++bit) {
            var px = tileCol * NeoGeoPocketFile.TileSize + bit;
            var py = tileRow * NeoGeoPocketFile.TileSize + row;
            var shift = 7 - bit;
            var colorIndex = ((plane0 >> shift) & 1) | (((plane1 >> shift) & 1) << 1);
            pixelData[py * width + px] = (byte)colorIndex;
          }
        }
      }

    return new NeoGeoPocketFile { Width = width, Height = height, PixelData = pixelData };
  }
}
