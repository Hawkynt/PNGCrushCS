using System;
using System.IO;

namespace FileFormat.Xcf;

/// <summary>Assembles XCF file bytes from an XCF data model.</summary>
public static class XcfWriter {

  private const int TILE_SIZE = 64;

  public static byte[] ToBytes(XcfFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Magic: "gimp xcf v001\0"
    var magic = System.Text.Encoding.ASCII.GetBytes("gimp xcf v001");
    ms.Write(magic);
    ms.WriteByte(0);

    // Canvas: width, height, colorMode (3 x uint32 BE)
    _WriteUInt32BE(ms, (uint)file.Width);
    _WriteUInt32BE(ms, (uint)file.Height);
    _WriteUInt32BE(ms, (uint)file.ColorMode);

    // Properties
    // PROP_COMPRESSION (type=17, size=1, value=0 = None)
    _WriteUInt32BE(ms, 17); // PROP_COMPRESSION
    _WriteUInt32BE(ms, 1);  // size
    ms.WriteByte(0);        // None

    // PROP_COLORMAP for indexed mode
    if (file.ColorMode == XcfColorMode.Indexed && file.Palette != null) {
      var colorCount = file.Palette.Length / 3;
      _WriteUInt32BE(ms, 1); // PROP_COLORMAP
      _WriteUInt32BE(ms, (uint)(4 + file.Palette.Length)); // size
      _WriteUInt32BE(ms, (uint)colorCount);
      ms.Write(file.Palette);
    }

    // PROP_END
    _WriteUInt32BE(ms, 0); // type
    _WriteUInt32BE(ms, 0); // size

    // We need to compute offsets for layer pointer, so use a two-pass approach:
    // Record position for layer pointer, then write the layer.
    var layerPointerPos = (int)ms.Position;
    _WriteUInt32BE(ms, 0); // placeholder for layer pointer
    _WriteUInt32BE(ms, 0); // null terminator for layer list

    // Channel pointers (none)
    _WriteUInt32BE(ms, 0); // null terminator for channel list

    // Write the layer
    var layerStart = (int)ms.Position;

    // Patch layer pointer
    var savedPos = ms.Position;
    ms.Position = layerPointerPos;
    _WriteUInt32BE(ms, (uint)layerStart);
    ms.Position = savedPos;

    var bpp = _ColorModeToBpp(file.ColorMode);
    var layerType = _ColorModeToLayerType(file.ColorMode);

    // Layer: width, height, type
    _WriteUInt32BE(ms, (uint)file.Width);
    _WriteUInt32BE(ms, (uint)file.Height);
    _WriteUInt32BE(ms, layerType);

    // Layer name: uint32 length (including null) + string + null
    var layerName = System.Text.Encoding.ASCII.GetBytes("Background");
    _WriteUInt32BE(ms, (uint)(layerName.Length + 1));
    ms.Write(layerName);
    ms.WriteByte(0);

    // Layer properties
    // PROP_OPACITY (type=6, size=4, value=255)
    _WriteUInt32BE(ms, 6);
    _WriteUInt32BE(ms, 4);
    _WriteUInt32BE(ms, 255);

    // PROP_VISIBLE (type=8, size=4, value=1)
    _WriteUInt32BE(ms, 8);
    _WriteUInt32BE(ms, 4);
    _WriteUInt32BE(ms, 1);

    // PROP_END
    _WriteUInt32BE(ms, 0);
    _WriteUInt32BE(ms, 0);

    // Hierarchy pointer placeholder
    var hierarchyPointerPos = (int)ms.Position;
    _WriteUInt32BE(ms, 0); // placeholder

    // Mask pointer (none)
    _WriteUInt32BE(ms, 0);

    // Write hierarchy
    var hierarchyStart = (int)ms.Position;
    ms.Position = hierarchyPointerPos;
    _WriteUInt32BE(ms, (uint)hierarchyStart);
    ms.Position = hierarchyStart;

    // Hierarchy: width, height, bpp
    _WriteUInt32BE(ms, (uint)file.Width);
    _WriteUInt32BE(ms, (uint)file.Height);
    _WriteUInt32BE(ms, (uint)bpp);

    // Level pointer placeholder
    var levelPointerPos = (int)ms.Position;
    _WriteUInt32BE(ms, 0); // placeholder
    _WriteUInt32BE(ms, 0); // null terminator

    // Write level
    var levelStart = (int)ms.Position;
    ms.Position = levelPointerPos;
    _WriteUInt32BE(ms, (uint)levelStart);
    ms.Position = levelStart;

    // Level: width, height
    _WriteUInt32BE(ms, (uint)file.Width);
    _WriteUInt32BE(ms, (uint)file.Height);

    // Compute tiles
    var tilesPerRow = (file.Width + TILE_SIZE - 1) / TILE_SIZE;
    var tilesPerCol = (file.Height + TILE_SIZE - 1) / TILE_SIZE;
    var totalTiles = tilesPerRow * tilesPerCol;

    // Tile offset placeholders
    var tileOffsetPositions = new int[totalTiles];
    for (var i = 0; i < totalTiles; ++i) {
      tileOffsetPositions[i] = (int)ms.Position;
      _WriteUInt32BE(ms, 0); // placeholder
    }
    _WriteUInt32BE(ms, 0); // null terminator

    // Write tile data (uncompressed, channel-planar)
    for (var tileIndex = 0; tileIndex < totalTiles; ++tileIndex) {
      var tileX = tileIndex % tilesPerRow;
      var tileY = tileIndex / tilesPerRow;
      var tileW = Math.Min(TILE_SIZE, file.Width - tileX * TILE_SIZE);
      var tileH = Math.Min(TILE_SIZE, file.Height - tileY * TILE_SIZE);

      var tileStart = (int)ms.Position;

      // Patch tile offset
      var curPos = ms.Position;
      ms.Position = tileOffsetPositions[tileIndex];
      _WriteUInt32BE(ms, (uint)tileStart);
      ms.Position = curPos;

      // Write tile as channel-planar (all channel0 bytes, then channel1, etc.)
      for (var channel = 0; channel < bpp; ++channel) {
        for (var row = 0; row < tileH; ++row) {
          for (var col = 0; col < tileW; ++col) {
            var srcX = tileX * TILE_SIZE + col;
            var srcY = tileY * TILE_SIZE + row;
            var srcIndex = (srcY * file.Width + srcX) * bpp + channel;
            if (srcIndex < file.PixelData.Length)
              ms.WriteByte(file.PixelData[srcIndex]);
            else
              ms.WriteByte(0);
          }
        }
      }
    }

    return ms.ToArray();
  }

  private static int _ColorModeToBpp(XcfColorMode colorMode) => colorMode switch {
    XcfColorMode.Rgb => 4,       // RGBA
    XcfColorMode.Grayscale => 2, // GrayA
    XcfColorMode.Indexed => 1,   // index byte
    _ => 4
  };

  private static uint _ColorModeToLayerType(XcfColorMode colorMode) => colorMode switch {
    XcfColorMode.Rgb => 1,       // RGBA
    XcfColorMode.Grayscale => 3, // GRAYA
    XcfColorMode.Indexed => 4,   // INDEXED
    _ => 1
  };

  private static void _WriteUInt32BE(Stream stream, uint value) {
    stream.WriteByte((byte)(value >> 24));
    stream.WriteByte((byte)(value >> 16));
    stream.WriteByte((byte)(value >> 8));
    stream.WriteByte((byte)value);
  }
}
