using System;

namespace FileFormat.GbaTile;

/// <summary>Assembles Game Boy Advance 4bpp tile data bytes from pixel data.</summary>
public static class GbaTileWriter {

  public static byte[] ToBytes(GbaTileFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var tileCols = width / GbaTileFile.TileSize;
    var tileRows = height / GbaTileFile.TileSize;
    var tileCount = tileRows * tileCols;
    var result = new byte[tileCount * GbaTileFile.BytesPerTile];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        var tileOffset = t * GbaTileFile.BytesPerTile;

        for (var row = 0; row < GbaTileFile.TileSize; ++row) {
          byte b0 = 0, b1 = 0, b2 = 0, b3 = 0;

          for (var bit = 0; bit < GbaTileFile.TileSize; ++bit) {
            var px = tileCol * GbaTileFile.TileSize + bit;
            var py = tileRow * GbaTileFile.TileSize + row;
            var colorIndex = py < height && px < width ? pixelData[py * width + px] & 0x0F : 0;
            var shift = 7 - bit;
            b0 |= (byte)((colorIndex & 1) << shift);
            b1 |= (byte)(((colorIndex >> 1) & 1) << shift);
            b2 |= (byte)(((colorIndex >> 2) & 1) << shift);
            b3 |= (byte)(((colorIndex >> 3) & 1) << shift);
          }

          result[tileOffset + row * 2] = b0;
          result[tileOffset + row * 2 + 1] = b1;
          result[tileOffset + row * 2 + 16] = b2;
          result[tileOffset + row * 2 + 17] = b3;
        }
      }

    return result;
  }
}
