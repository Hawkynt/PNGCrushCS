using System;

namespace FileFormat.MasterSystemTile;

/// <summary>Assembles Sega Master System / Game Gear 4bpp planar tile data bytes from pixel data.</summary>
public static class MasterSystemTileWriter {

  public static byte[] ToBytes(MasterSystemTileFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var tileCols = file.Width / MasterSystemTileFile.TileSize;
    var tileRows = file.Height / MasterSystemTileFile.TileSize;
    var tileCount = tileRows * tileCols;
    var result = new byte[tileCount * MasterSystemTileFile.BytesPerTile];

    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
      for (var tileCol = 0; tileCol < tileCols; ++tileCol) {
        var t = tileRow * tileCols + tileCol;
        var tileOffset = t * MasterSystemTileFile.BytesPerTile;

        for (var row = 0; row < MasterSystemTileFile.TileSize; ++row) {
          byte plane0 = 0;
          byte plane1 = 0;
          byte plane2 = 0;
          byte plane3 = 0;

          for (var bit = 0; bit < MasterSystemTileFile.TileSize; ++bit) {
            var px = tileCol * MasterSystemTileFile.TileSize + bit;
            var py = tileRow * MasterSystemTileFile.TileSize + row;
            var colorIndex = py < file.Height && px < file.Width
              ? file.PixelData[py * file.Width + px] & 0x0F
              : 0;
            var shift = 7 - bit;

            plane0 |= (byte)(((colorIndex >> 0) & 1) << shift);
            plane1 |= (byte)(((colorIndex >> 1) & 1) << shift);
            plane2 |= (byte)(((colorIndex >> 2) & 1) << shift);
            plane3 |= (byte)(((colorIndex >> 3) & 1) << shift);
          }

          var rowOffset = tileOffset + row * MasterSystemTileFile.PlanesPerPixel;
          result[rowOffset] = plane0;
          result[rowOffset + 1] = plane1;
          result[rowOffset + 2] = plane2;
          result[rowOffset + 3] = plane3;
        }
      }

    return result;
  }
}
