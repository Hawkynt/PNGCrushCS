using System;

namespace FileFormat.Atari2600;

/// <summary>Assembles atari 2600 tia playfield graphics bytes from pixel data.</summary>
public static class Atari2600Writer {

  public static byte[] ToBytes(Atari2600File file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var tileCols = width / Atari2600File.TileSize;
    var tileRows = height / Atari2600File.TileSize;
    var tileCount = tileRows * tileCols;
    var result = new byte[tileCount * Atari2600File.BytesPerTile];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        var tileOffset = t * Atari2600File.BytesPerTile;

        for (var row = 0; row < Atari2600File.TileSize; ++row) {
          byte packed = 0;

          for (var bit = 0; bit < Atari2600File.TileSize; ++bit) {
            var px = tileCol * Atari2600File.TileSize + bit;
            var py = tileRow * Atari2600File.TileSize + row;
            var colorIndex = py < height && px < width ? pixelData[py * width + px] & 0x01 : 0;
            if (colorIndex != 0)
              packed |= (byte)(0x80 >> bit);
          }

          result[tileOffset + row] = packed;
        }
      }

    return result;
  }
}
