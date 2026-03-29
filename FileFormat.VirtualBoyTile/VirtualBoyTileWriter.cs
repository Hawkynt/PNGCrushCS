using System;

namespace FileFormat.VirtualBoyTile;

/// <summary>Assembles Virtual Boy 2bpp red tile data bytes from pixel data.</summary>
public static class VirtualBoyTileWriter {

  public static byte[] ToBytes(VirtualBoyTileFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var tileCols = width / VirtualBoyTileFile.TileSize;
    var tileRows = height / VirtualBoyTileFile.TileSize;
    var tileCount = tileRows * tileCols;
    var result = new byte[tileCount * VirtualBoyTileFile.BytesPerTile];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        var tileOffset = t * VirtualBoyTileFile.BytesPerTile;

        for (var row = 0; row < VirtualBoyTileFile.TileSize; ++row) {
          byte plane0 = 0;
          byte plane1 = 0;

          for (var bit = 0; bit < VirtualBoyTileFile.TileSize; ++bit) {
            var px = tileCol * VirtualBoyTileFile.TileSize + bit;
            var py = tileRow * VirtualBoyTileFile.TileSize + row;
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
