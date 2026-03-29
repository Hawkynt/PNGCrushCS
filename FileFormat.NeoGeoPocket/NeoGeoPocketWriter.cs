using System;

namespace FileFormat.NeoGeoPocket;

/// <summary>Assembles neo geo pocket color 2bpp tile data bytes from pixel data.</summary>
public static class NeoGeoPocketWriter {

  public static byte[] ToBytes(NeoGeoPocketFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var tileCols = width / NeoGeoPocketFile.TileSize;
    var tileRows = height / NeoGeoPocketFile.TileSize;
    var tileCount = tileRows * tileCols;
    var result = new byte[tileCount * NeoGeoPocketFile.BytesPerTile];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        var tileOffset = t * NeoGeoPocketFile.BytesPerTile;

        for (var row = 0; row < NeoGeoPocketFile.TileSize; ++row) {
          byte plane0 = 0;
          byte plane1 = 0;

          for (var bit = 0; bit < NeoGeoPocketFile.TileSize; ++bit) {
            var px = tileCol * NeoGeoPocketFile.TileSize + bit;
            var py = tileRow * NeoGeoPocketFile.TileSize + row;
            var colorIndex = py < height && px < width ? pixelData[py * width + px] & 0x03 : 0;
            var shift = 7 - bit;
            plane0 |= (byte)((colorIndex & 1) << shift);
            plane1 |= (byte)(((colorIndex >> 1) & 1) << shift);
          }

          result[tileOffset + row * 2] = plane0;
          result[tileOffset + row * 2 + 1] = plane1;
        }
      }

    return result;
  }
}
