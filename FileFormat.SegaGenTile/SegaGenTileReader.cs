using System;
using System.IO;

namespace FileFormat.SegaGenTile;

/// <summary>Reads Sega Genesis 4BPP tile data from bytes, streams, or file paths.</summary>
public static class SegaGenTileReader {

  public static SegaGenTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sega Genesis tile file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SegaGenTileFile FromStream(Stream stream) {
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

  public static SegaGenTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SegaGenTileFile.BytesPerTile)
      throw new InvalidDataException($"Data too small for a valid Sega Genesis tile file. Minimum {SegaGenTileFile.BytesPerTile} bytes, got {data.Length}.");
    if (data.Length % SegaGenTileFile.BytesPerTile != 0)
      throw new InvalidDataException($"Sega Genesis tile data must be a multiple of {SegaGenTileFile.BytesPerTile} bytes, got {data.Length}.");

    var tileCount = data.Length / SegaGenTileFile.BytesPerTile;
    var tileRows = (tileCount + SegaGenTileFile.TilesPerRow - 1) / SegaGenTileFile.TilesPerRow;
    var width = SegaGenTileFile.FixedWidth;
    var height = tileRows * SegaGenTileFile.TileSize;
    var pixels = new byte[width * height];

    for (var t = 0; t < tileCount; ++t) {
      var tileX = t % SegaGenTileFile.TilesPerRow;
      var tileY = t / SegaGenTileFile.TilesPerRow;
      var tileOffset = t * SegaGenTileFile.BytesPerTile;

      for (var row = 0; row < SegaGenTileFile.TileSize; ++row) {
        var rowOffset = tileOffset + row * 4;
        for (var col = 0; col < 4; ++col) {
          var b = data[rowOffset + col];
          var hiNibble = (b >> 4) & 0x0F;
          var loNibble = b & 0x0F;
          var px = tileX * SegaGenTileFile.TileSize + col * 2;
          var py = tileY * SegaGenTileFile.TileSize + row;
          pixels[py * width + px] = (byte)hiNibble;
          pixels[py * width + px + 1] = (byte)loNibble;
        }
      }
    }

    return new SegaGenTileFile {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }
}
