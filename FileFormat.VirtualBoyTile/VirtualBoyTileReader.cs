using System;
using System.IO;

namespace FileFormat.VirtualBoyTile;

/// <summary>Reads Virtual Boy 2bpp red tile data data from bytes, streams, or file paths.</summary>
public static class VirtualBoyTileReader {

  public static VirtualBoyTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VirtualBoyTile file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VirtualBoyTileFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static VirtualBoyTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < VirtualBoyTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid VirtualBoyTile file (minimum {VirtualBoyTileFile.BytesPerTile} bytes).");
    if (data.Length % VirtualBoyTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"VirtualBoyTile tile data must be a multiple of {VirtualBoyTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / VirtualBoyTileFile.BytesPerTile;
    var tileRows = (tileCount + VirtualBoyTileFile.TilesPerRow - 1) / VirtualBoyTileFile.TilesPerRow;
    var width = VirtualBoyTileFile.TilesPerRow * VirtualBoyTileFile.TileSize;
    var height = tileRows * VirtualBoyTileFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % VirtualBoyTileFile.TilesPerRow;
      var tileRow = t / VirtualBoyTileFile.TilesPerRow;
      var tileOffset = t * VirtualBoyTileFile.BytesPerTile;

      for (var row = 0; row < VirtualBoyTileFile.TileSize; ++row) {
        var plane0 = data[tileOffset + row * 2];
        var plane1 = data[tileOffset + row * 2 + 1];
        for (var bit = 0; bit < 8; ++bit) {
          var shift = 7 - bit;
          var colorIndex = (byte)(((plane1 >> shift) & 1) << 1 | ((plane0 >> shift) & 1));
          var px = tileCol * VirtualBoyTileFile.TileSize + bit;
          var py = tileRow * VirtualBoyTileFile.TileSize + row;
          pixelData[py * width + px] = colorIndex;
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
