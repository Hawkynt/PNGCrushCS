using System;

namespace FileFormat.WonderSwanTile;

/// <summary>Assembles WonderSwan 2bpp tile data bytes from pixel data.</summary>
public static class WonderSwanTileWriter {

  public static byte[] ToBytes(WonderSwanTileFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var tileCols = width / WonderSwanTileFile.TileSize;
    var tileRows = height / WonderSwanTileFile.TileSize;
    var tileCount = tileRows * tileCols;
    var result = new byte[tileCount * WonderSwanTileFile.BytesPerTile];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        var tileOffset = t * WonderSwanTileFile.BytesPerTile;

        for (var row = 0; row < WonderSwanTileFile.TileSize; ++row) {
          byte plane0 = 0;
          byte plane1 = 0;

          for (var bit = 0; bit < WonderSwanTileFile.TileSize; ++bit) {
            var px = tileCol * WonderSwanTileFile.TileSize + bit;
            var py = tileRow * WonderSwanTileFile.TileSize + row;
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
