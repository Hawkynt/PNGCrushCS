using System;

namespace FileFormat.SnesTile;

/// <summary>Assembles SNES 4BPP planar tile data bytes from pixel data.</summary>
public static class SnesTileWriter {

  public static byte[] ToBytes(SnesTileFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var tilesX = file.Width / SnesTileFile.TileSize;
    var tilesY = file.Height / SnesTileFile.TileSize;
    var tileCount = tilesX * tilesY;
    var result = new byte[tileCount * SnesTileFile.BytesPerTile];

    for (var ty = 0; ty < tilesY; ++ty)
      for (var tx = 0; tx < tilesX; ++tx) {
        var tileIndex = ty * tilesX + tx;
        var tileOffset = tileIndex * SnesTileFile.BytesPerTile;

        for (var row = 0; row < SnesTileFile.TileSize; ++row) {
          byte plane0 = 0, plane1 = 0, plane2 = 0, plane3 = 0;

          for (var bit = 0; bit < SnesTileFile.TileSize; ++bit) {
            var px = tx * SnesTileFile.TileSize + bit;
            var py = ty * SnesTileFile.TileSize + row;
            var pixelValue = px < file.Width && py < file.Height
              ? file.PixelData[py * file.Width + px] & 0x0F
              : 0;

            var shift = 7 - bit;
            if ((pixelValue & 1) != 0) plane0 |= (byte)(1 << shift);
            if ((pixelValue & 2) != 0) plane1 |= (byte)(1 << shift);
            if ((pixelValue & 4) != 0) plane2 |= (byte)(1 << shift);
            if ((pixelValue & 8) != 0) plane3 |= (byte)(1 << shift);
          }

          result[tileOffset + row * 2] = plane0;
          result[tileOffset + row * 2 + 1] = plane1;
          result[tileOffset + 16 + row * 2] = plane2;
          result[tileOffset + 16 + row * 2 + 1] = plane3;
        }
      }

    return result;
  }
}
