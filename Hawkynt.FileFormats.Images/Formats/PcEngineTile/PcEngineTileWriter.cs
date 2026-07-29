using System;

namespace FileFormat.PcEngineTile;

/// <summary>Assembles PC Engine/TurboGrafx-16 4BPP planar tile data bytes from pixel data.</summary>
public static class PcEngineTileWriter {

  public static byte[] ToBytes(PcEngineTileFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var tilesX = file.Width / PcEngineTileFile.TileSize;
    var tilesY = file.Height / PcEngineTileFile.TileSize;
    var tileCount = tilesX * tilesY;
    var result = new byte[tileCount * PcEngineTileFile.BytesPerTile];

    for (var ty = 0; ty < tilesY; ++ty)
      for (var tx = 0; tx < tilesX; ++tx) {
        var tileIndex = ty * tilesX + tx;
        var tileOffset = tileIndex * PcEngineTileFile.BytesPerTile;

        for (var row = 0; row < PcEngineTileFile.TileSize; ++row) {
          byte plane0 = 0;
          byte plane1 = 0;
          byte plane2 = 0;
          byte plane3 = 0;

          for (var bit = 0; bit < PcEngineTileFile.TileSize; ++bit) {
            var px = tx * PcEngineTileFile.TileSize + bit;
            var py = ty * PcEngineTileFile.TileSize + row;
            var pixelValue = px < file.Width && py < file.Height
              ? file.PixelData[py * file.Width + px] & 0x0F
              : 0;

            var mask = (byte)(1 << (7 - bit));
            if ((pixelValue & 1) != 0)
              plane0 |= mask;
            if ((pixelValue & 2) != 0)
              plane1 |= mask;
            if ((pixelValue & 4) != 0)
              plane2 |= mask;
            if ((pixelValue & 8) != 0)
              plane3 |= mask;
          }

          // SNES interleave: planes 0+1 interleaved in first 16 bytes
          result[tileOffset + row * 2] = plane0;
          result[tileOffset + row * 2 + 1] = plane1;
          // planes 2+3 interleaved in next 16 bytes
          result[tileOffset + 16 + row * 2] = plane2;
          result[tileOffset + 16 + row * 2 + 1] = plane3;
        }
      }

    return result;
  }
}
