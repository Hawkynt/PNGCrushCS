using System;

namespace FileFormat.NdsTexture;

/// <summary>Assembles Nintendo DS 4bpp tile texture bytes from pixel data.</summary>
public static class NdsTextureWriter {

  public static byte[] ToBytes(NdsTextureFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var tileCols = width / NdsTextureFile.TileSize;
    var tileRows = height / NdsTextureFile.TileSize;
    var tileCount = tileRows * tileCols;
    var result = new byte[tileCount * NdsTextureFile.BytesPerTile];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        var tileOffset = t * NdsTextureFile.BytesPerTile;

        for (var row = 0; row < NdsTextureFile.TileSize; ++row) {
          byte b0 = 0, b1 = 0, b2 = 0, b3 = 0;

          for (var bit = 0; bit < NdsTextureFile.TileSize; ++bit) {
            var px = tileCol * NdsTextureFile.TileSize + bit;
            var py = tileRow * NdsTextureFile.TileSize + row;
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
