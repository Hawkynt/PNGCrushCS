using System;

namespace FileFormat.NeoGeoSprite;

/// <summary>Assembles Neo Geo 4bpp sprite tile data bytes from pixel data.</summary>
public static class NeoGeoSpriteWriter {

  public static byte[] ToBytes(NeoGeoSpriteFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var tileCols = width / NeoGeoSpriteFile.TileSize;
    var tileRows = height / NeoGeoSpriteFile.TileSize;
    var tileCount = tileRows * tileCols;
    var result = new byte[tileCount * NeoGeoSpriteFile.BytesPerTile];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        var tileOffset = t * NeoGeoSpriteFile.BytesPerTile;

        for (var row = 0; row < NeoGeoSpriteFile.TileSize; ++row) {
          byte b0 = 0, b1 = 0, b2 = 0, b3 = 0;

          for (var bit = 0; bit < NeoGeoSpriteFile.TileSize; ++bit) {
            var px = tileCol * NeoGeoSpriteFile.TileSize + bit;
            var py = tileRow * NeoGeoSpriteFile.TileSize + row;
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
