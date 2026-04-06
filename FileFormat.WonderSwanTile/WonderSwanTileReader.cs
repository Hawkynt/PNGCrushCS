using System;
using System.IO;

namespace FileFormat.WonderSwanTile;

/// <summary>Reads WonderSwan 2bpp tile data data from bytes, streams, or file paths.</summary>
public static class WonderSwanTileReader {

  public static WonderSwanTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WonderSwanTile file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WonderSwanTileFile FromStream(Stream stream) {
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

  public static WonderSwanTileFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static WonderSwanTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < WonderSwanTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid WonderSwanTile file (minimum {WonderSwanTileFile.BytesPerTile} bytes).");
    if (data.Length % WonderSwanTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"WonderSwanTile tile data must be a multiple of {WonderSwanTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / WonderSwanTileFile.BytesPerTile;
    var tileRows = (tileCount + WonderSwanTileFile.TilesPerRow - 1) / WonderSwanTileFile.TilesPerRow;
    var width = WonderSwanTileFile.TilesPerRow * WonderSwanTileFile.TileSize;
    var height = tileRows * WonderSwanTileFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % WonderSwanTileFile.TilesPerRow;
      var tileRow = t / WonderSwanTileFile.TilesPerRow;
      var tileOffset = t * WonderSwanTileFile.BytesPerTile;

      for (var row = 0; row < WonderSwanTileFile.TileSize; ++row) {
        var plane0 = data[tileOffset + row * 2];
        var plane1 = data[tileOffset + row * 2 + 1];
        for (var bit = 0; bit < 8; ++bit) {
          var shift = 7 - bit;
          var colorIndex = (byte)(((plane1 >> shift) & 1) << 1 | ((plane0 >> shift) & 1));
          var px = tileCol * WonderSwanTileFile.TileSize + bit;
          var py = tileRow * WonderSwanTileFile.TileSize + row;
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
