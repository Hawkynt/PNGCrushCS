using System;

namespace FileFormat.NesChr;

/// <summary>Assembles NES CHR tile data bytes from pixel data.</summary>
public static class NesChrWriter {

  public static byte[] ToBytes(NesChrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var tilesX = file.Width / NesChrFile.TileSize;
    var tilesY = file.Height / NesChrFile.TileSize;
    var tileCount = tilesX * tilesY;
    var result = new byte[tileCount * NesChrFile.BytesPerTile];

    for (var ty = 0; ty < tilesY; ++ty)
      for (var tx = 0; tx < tilesX; ++tx) {
        var tileIndex = ty * tilesX + tx;
        var tileOffset = tileIndex * NesChrFile.BytesPerTile;

        for (var row = 0; row < NesChrFile.TileSize; ++row) {
          byte plane0 = 0;
          byte plane1 = 0;

          for (var bit = 0; bit < NesChrFile.TileSize; ++bit) {
            var px = tx * NesChrFile.TileSize + bit;
            var py = ty * NesChrFile.TileSize + row;
            var pixelValue = px < file.Width && py < file.Height
              ? file.PixelData[py * file.Width + px] & 0x03
              : 0;

            if ((pixelValue & 1) != 0)
              plane0 |= (byte)(1 << (7 - bit));
            if ((pixelValue & 2) != 0)
              plane1 |= (byte)(1 << (7 - bit));
          }

          result[tileOffset + row] = plane0;
          result[tileOffset + NesChrFile.TileSize + row] = plane1;
        }
      }

    return result;
  }
}
