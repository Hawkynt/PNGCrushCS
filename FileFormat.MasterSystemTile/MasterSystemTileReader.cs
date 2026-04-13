using System;
using System.IO;

namespace FileFormat.MasterSystemTile;

/// <summary>Reads Sega Master System / Game Gear 4bpp planar tile data from bytes, streams, or file paths.</summary>
public static class MasterSystemTileReader {

  public static MasterSystemTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Master System tile file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MasterSystemTileFile FromStream(Stream stream) {
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

  public static MasterSystemTileFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MasterSystemTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid Master System tile file (minimum {MasterSystemTileFile.BytesPerTile} bytes, got {data.Length}).");
    if (data.Length % MasterSystemTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"Master System tile data must be a multiple of {MasterSystemTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / MasterSystemTileFile.BytesPerTile;
    var tileRows = (tileCount + MasterSystemTileFile.TilesPerRow - 1) / MasterSystemTileFile.TilesPerRow;
    var width = MasterSystemTileFile.FixedWidth;
    var height = tileRows * MasterSystemTileFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % MasterSystemTileFile.TilesPerRow;
      var tileRow = t / MasterSystemTileFile.TilesPerRow;
      var tileOffset = t * MasterSystemTileFile.BytesPerTile;

      for (var row = 0; row < MasterSystemTileFile.TileSize; ++row) {
        var rowOffset = tileOffset + row * MasterSystemTileFile.PlanesPerPixel;
        var plane0 = data[rowOffset];
        var plane1 = data[rowOffset + 1];
        var plane2 = data[rowOffset + 2];
        var plane3 = data[rowOffset + 3];

        for (var bit = 0; bit < MasterSystemTileFile.TileSize; ++bit) {
          var shift = 7 - bit;
          var colorIndex = (byte)(
            ((plane0 >> shift) & 1)
            | (((plane1 >> shift) & 1) << 1)
            | (((plane2 >> shift) & 1) << 2)
            | (((plane3 >> shift) & 1) << 3)
          );

          var px = tileCol * MasterSystemTileFile.TileSize + bit;
          var py = tileRow * MasterSystemTileFile.TileSize + row;
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

  public static MasterSystemTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MasterSystemTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid Master System tile file (minimum {MasterSystemTileFile.BytesPerTile} bytes, got {data.Length}).");
    if (data.Length % MasterSystemTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"Master System tile data must be a multiple of {MasterSystemTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / MasterSystemTileFile.BytesPerTile;
    var tileRows = (tileCount + MasterSystemTileFile.TilesPerRow - 1) / MasterSystemTileFile.TilesPerRow;
    var width = MasterSystemTileFile.FixedWidth;
    var height = tileRows * MasterSystemTileFile.TileSize;
    var pixelData = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileCol = t % MasterSystemTileFile.TilesPerRow;
      var tileRow = t / MasterSystemTileFile.TilesPerRow;
      var tileOffset = t * MasterSystemTileFile.BytesPerTile;

      for (var row = 0; row < MasterSystemTileFile.TileSize; ++row) {
        var rowOffset = tileOffset + row * MasterSystemTileFile.PlanesPerPixel;
        var plane0 = data[rowOffset];
        var plane1 = data[rowOffset + 1];
        var plane2 = data[rowOffset + 2];
        var plane3 = data[rowOffset + 3];

        for (var bit = 0; bit < MasterSystemTileFile.TileSize; ++bit) {
          var shift = 7 - bit;
          var colorIndex = (byte)(
            ((plane0 >> shift) & 1)
            | (((plane1 >> shift) & 1) << 1)
            | (((plane2 >> shift) & 1) << 2)
            | (((plane3 >> shift) & 1) << 3)
          );

          var px = tileCol * MasterSystemTileFile.TileSize + bit;
          var py = tileRow * MasterSystemTileFile.TileSize + row;
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
