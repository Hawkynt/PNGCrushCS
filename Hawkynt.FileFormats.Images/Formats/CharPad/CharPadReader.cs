using System;
using System.IO;
using System.Text;

namespace FileFormat.CharPad;

/// <summary>Reads CharPad projects from bytes, streams, or file paths.</summary>
public static class CharPadReader {

  public static CharPadFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Project not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CharPadFile FromStream(Stream stream) {
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

  public static CharPadFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 30 || Encoding.ASCII.GetString(data[..CharPadFile.Signature.Length]) != CharPadFile.Signature
        || data[3] != CharPadFile.Version)
      throw new InvalidDataException("Not a CharPad project.");

    var colorMethod = data[8];
    if (colorMethod > 2)
      throw new InvalidDataException($"A CharPad project takes its colours 0, 1 or 2 ways, not {colorMethod}.");

    var flags = data[9];
    var tiles = (flags & 1) != 0;
    // Colours per tile are meaningless without tiles, so that combination is malformed.
    if (colorMethod == 1 && !tiles)
      throw new InvalidDataException("A CharPad project colours by tile but has no tiles.");

    var implied = (flags & 2) != 0;
    var multi = (flags & 4) != 0;

    // The stored counts are one less than the real ones, so 256 characters still fits two bytes.
    var characters = (data[10] | (data[11] << 8)) + 1;
    var tileCount = tiles ? (data[12] | (data[13] << 8)) + 1 : 0;
    var tileWidth = tiles ? data[14] : 1;
    var tileHeight = tiles ? data[15] : 1;
    if (tileWidth == 0 || tileHeight == 0)
      throw new InvalidDataException($"A CharPad tile is not {tileWidth}x{tileHeight} characters.");

    var mapWidth = data[16] | (data[17] << 8);
    var mapHeight = data[18] | (data[19] << 8);

    var tilesOffset = CharPadFile.CharactersOffset + characters * CharPadFile.CharacterLength;
    var tileColorsOffset = implied ? tilesOffset : tilesOffset + tileCount * (tileWidth * tileHeight << 1);
    var mapOffset = colorMethod == 1 ? tileColorsOffset + tileCount : tileColorsOffset;

    if (data.Length != mapOffset + (mapWidth * mapHeight << 1))
      throw new InvalidDataException($"A {mapWidth}x{mapHeight} map does not fit {data.Length} bytes.");

    var width = mapWidth * tileWidth << 3;
    var height = mapHeight * tileHeight << 3;
    if (width == 0 || height == 0)
      throw new InvalidDataException($"A CharPad project is not {width}x{height}.");

    // Every map entry has to name a tile that exists, and every tile a character that does.
    for (var entry = 0; entry < mapWidth * mapHeight; ++entry) {
      var tile = data[mapOffset + (entry << 1)] | (data[mapOffset + (entry << 1) + 1] << 8);
      if (tiles && tile >= tileCount)
        throw new InvalidDataException($"Map entry {entry} names a tile the project does not have.");

      if (!tiles && tile >= characters)
        throw new InvalidDataException($"Map entry {entry} names a character the set does not hold.");
    }

    if (tiles && !implied)
      for (var slot = 0; slot < tileCount * tileWidth * tileHeight; ++slot) {
        var at = tilesOffset + (slot << 1);
        if ((data[at] | (data[at + 1] << 8)) >= characters)
          throw new InvalidDataException($"Tile slot {slot} names a character the set does not hold.");
      }
    else if (tiles)
      if (tileCount * tileHeight * tileWidth > characters)
        throw new InvalidDataException("The tiles imply more characters than the set holds.");

    return new() {
      Data = data.ToArray(),
      Width = width,
      Height = height,
      ColorMethod = colorMethod,
      HasTiles = tiles,
      CharactersAreImplied = implied,
      IsMulticolor = multi,
      CharacterCount = characters,
      TileWidth = tileWidth,
      TileHeight = tileHeight,
      MapWidth = mapWidth,
      TilesOffset = tilesOffset,
      TileColorsOffset = tileColorsOffset,
      MapOffset = mapOffset,
    };
  }

  public static CharPadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
