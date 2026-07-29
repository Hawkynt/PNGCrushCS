using System;

namespace FileFormat.SegaGenTile;

/// <summary>Assembles Sega Genesis 4BPP tile data bytes from pixel data.</summary>
public static class SegaGenTileWriter {

  public static byte[] ToBytes(SegaGenTileFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var tilesX = file.Width / SegaGenTileFile.TileSize;
    var tilesY = file.Height / SegaGenTileFile.TileSize;
    var tileCount = tilesX * tilesY;
    var result = new byte[tileCount * SegaGenTileFile.BytesPerTile];

    for (var ty = 0; ty < tilesY; ++ty)
      for (var tx = 0; tx < tilesX; ++tx) {
        var tileIndex = ty * tilesX + tx;
        var tileOffset = tileIndex * SegaGenTileFile.BytesPerTile;

        for (var row = 0; row < SegaGenTileFile.TileSize; ++row) {
          var rowOffset = tileOffset + row * 4;
          for (var col = 0; col < 4; ++col) {
            var px = tx * SegaGenTileFile.TileSize + col * 2;
            var py = ty * SegaGenTileFile.TileSize + row;

            var hi = px < file.Width && py < file.Height ? file.PixelData[py * file.Width + px] & 0x0F : 0;
            var lo = px + 1 < file.Width && py < file.Height ? file.PixelData[py * file.Width + px + 1] & 0x0F : 0;
            result[rowOffset + col] = (byte)((hi << 4) | lo);
          }
        }
      }

    return result;
  }
}
