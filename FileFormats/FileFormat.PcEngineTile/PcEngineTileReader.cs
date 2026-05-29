using System;
using System.IO;

namespace FileFormat.PcEngineTile;

/// <summary>Reads PC Engine/TurboGrafx-16 4BPP planar tile data from bytes, streams, or file paths.</summary>
public static class PcEngineTileReader {

  public static PcEngineTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PCE tile file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PcEngineTileFile FromStream(Stream stream) {
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

  public static PcEngineTileFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PcEngineTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid PC Engine tile file. Minimum {PcEngineTileFile.BytesPerTile} bytes, got {data.Length}.");
    if (data.Length % PcEngineTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"PC Engine tile data must be a multiple of {PcEngineTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / PcEngineTileFile.BytesPerTile;
    var tileRows = (tileCount + PcEngineTileFile.TilesPerRow - 1) / PcEngineTileFile.TilesPerRow;
    var width = PcEngineTileFile.FixedWidth;
    var height = tileRows * PcEngineTileFile.TileSize;
    var pixels = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileX = t % PcEngineTileFile.TilesPerRow;
      var tileY = t / PcEngineTileFile.TilesPerRow;
      var tileOffset = t * PcEngineTileFile.BytesPerTile;

      for (var row = 0; row < PcEngineTileFile.TileSize; ++row) {
        // SNES interleave: planes 0+1 interleaved in first 16 bytes
        var plane0 = data[tileOffset + row * 2];
        var plane1 = data[tileOffset + row * 2 + 1];
        // planes 2+3 interleaved in next 16 bytes
        var plane2 = data[tileOffset + 16 + row * 2];
        var plane3 = data[tileOffset + 16 + row * 2 + 1];

        for (var bit = 0; bit < PcEngineTileFile.TileSize; ++bit) {
          var shift = 7 - bit;
          var b0 = (plane0 >> shift) & 1;
          var b1 = (plane1 >> shift) & 1;
          var b2 = (plane2 >> shift) & 1;
          var b3 = (plane3 >> shift) & 1;
          var pixelValue = (byte)(b0 | (b1 << 1) | (b2 << 2) | (b3 << 3));

          var px = tileX * PcEngineTileFile.TileSize + bit;
          var py = tileY * PcEngineTileFile.TileSize + row;
          pixels[py * width + px] = pixelValue;
        }
      }
    }

    return new PcEngineTileFile {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
    }

  public static PcEngineTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PcEngineTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid PC Engine tile file. Minimum {PcEngineTileFile.BytesPerTile} bytes, got {data.Length}.");
    if (data.Length % PcEngineTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"PC Engine tile data must be a multiple of {PcEngineTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / PcEngineTileFile.BytesPerTile;
    var tileRows = (tileCount + PcEngineTileFile.TilesPerRow - 1) / PcEngineTileFile.TilesPerRow;
    var width = PcEngineTileFile.FixedWidth;
    var height = tileRows * PcEngineTileFile.TileSize;
    var pixels = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileX = t % PcEngineTileFile.TilesPerRow;
      var tileY = t / PcEngineTileFile.TilesPerRow;
      var tileOffset = t * PcEngineTileFile.BytesPerTile;

      for (var row = 0; row < PcEngineTileFile.TileSize; ++row) {
        // SNES interleave: planes 0+1 interleaved in first 16 bytes
        var plane0 = data[tileOffset + row * 2];
        var plane1 = data[tileOffset + row * 2 + 1];
        // planes 2+3 interleaved in next 16 bytes
        var plane2 = data[tileOffset + 16 + row * 2];
        var plane3 = data[tileOffset + 16 + row * 2 + 1];

        for (var bit = 0; bit < PcEngineTileFile.TileSize; ++bit) {
          var shift = 7 - bit;
          var b0 = (plane0 >> shift) & 1;
          var b1 = (plane1 >> shift) & 1;
          var b2 = (plane2 >> shift) & 1;
          var b3 = (plane3 >> shift) & 1;
          var pixelValue = (byte)(b0 | (b1 << 1) | (b2 << 2) | (b3 << 3));

          var px = tileX * PcEngineTileFile.TileSize + bit;
          var py = tileY * PcEngineTileFile.TileSize + row;
          pixels[py * width + px] = pixelValue;
        }
      }
    }

    return new PcEngineTileFile {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }
}
