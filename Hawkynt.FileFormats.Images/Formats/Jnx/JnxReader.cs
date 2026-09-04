using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jnx;

/// <summary>Reads a Garmin JNX map and the tiles it is made of.</summary>
/// <remarks>
/// Layout, all little-endian: a header of ten 32-bit fields (an eleventh,
/// <c>order</c>, in version 4), then one descriptor per level giving that
/// level's tile count and the offset its tile table starts at, then the tile
/// tables, then the tile data. A tile descriptor is its four bounds, its width
/// and height as 16-bit counts, and the length and offset of its JPEG.
///
/// <para>Versions 3 and 4 are read, which is what the format is met as. A level
/// descriptor in version 4 carries a copyright string after the scale, ended by
/// a zero 16-bit unit.</para>
/// </remarks>
public static class JnxReader {

  private const int _Version3 = 3;
  private const int _Version4 = 4;

  /// <summary>What a level may state before the file is treated as malformed.</summary>
  private const int _MaxLevels = 32;
  private const int _MaxTilesPerLevel = 50_000;

  public static JnxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JNX file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static JnxFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromSpan(buffer.ToArray());
  }

  public static JnxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static JnxFile FromSpan(ReadOnlySpan<byte> data) {
    var at = 0;
    var version = _Int32(data, ref at);
    if (version is not (_Version3 or _Version4))
      throw new InvalidDataException($"JNX states version {version}; only 3 and 4 are defined.");

    var serial = _Int32(data, ref at);
    var northEastX = _Int32(data, ref at);
    var northEastY = _Int32(data, ref at);
    var southWestX = _Int32(data, ref at);
    var southWestY = _Int32(data, ref at);
    var levels = _Int32(data, ref at);
    if (levels is <= 0 or > _MaxLevels)
      throw new InvalidDataException($"JNX states {levels} levels.");

    var expiry = _Int32(data, ref at);
    var productId = _Int32(data, ref at);
    var crc = _Int32(data, ref at);
    var signature = _Int32(data, ref at);
    var signatureOffset = _Int32(data, ref at);

    // Version 3 has no zoom-order field and behaves as though it stated 30.
    var order = version > _Version3 ? _Int32(data, ref at) : 30;

    var levelCounts = new int[levels];
    var levelOffsets = new int[levels];
    var levelScales = new int[levels];
    for (var level = 0; level < levels; ++level) {
      levelCounts[level] = _Int32(data, ref at);
      if (levelCounts[level] is < 0 or > _MaxTilesPerLevel)
        throw new InvalidDataException($"JNX level {level} states {levelCounts[level]} tiles.");

      levelOffsets[level] = _Int32(data, ref at);
      levelScales[level] = _Int32(data, ref at);
      if (version <= _Version3)
        continue;

      // Copyright: a 32-bit field then a string of 16-bit units ended by zero.
      _ = _Int32(data, ref at);
      while (true) {
        if (at + 2 > data.Length)
          throw new InvalidDataException("JNX level copyright runs past the end of the file.");
        var unit = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
        at += 2;
        if (unit == 0)
          break;
      }
    }

    var tiles = new List<JnxTile>();
    for (var level = 0; level < levels; ++level) {
      var tableAt = levelOffsets[level];
      for (var tile = 0; tile < levelCounts[level]; ++tile) {
        if (tableAt < 0 || tableAt + 28 > data.Length)
          throw new InvalidDataException($"JNX level {level} tile {tile} sits past the end of the file.");

        var tileNorthEastX = _Int32(data, ref tableAt);
        var tileNorthEastY = _Int32(data, ref tableAt);
        var tileSouthWestX = _Int32(data, ref tableAt);
        var tileSouthWestY = _Int32(data, ref tableAt);
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[tableAt..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[(tableAt + 2)..]);
        tableAt += 4;
        var length = _Int32(data, ref tableAt);
        var offset = _Int32(data, ref tableAt);

        // A tile may state -1 for its offset, which is how a level leaves a
        // square of the map empty rather than drawing nothing over it.
        if (offset < 0)
          continue;
        if (length < 0 || offset + length > data.Length)
          throw new InvalidDataException($"JNX level {level} tile {tile} states {length} bytes at {offset}.");

        // The start-of-image marker every tile would repeat is left out of the
        // file and put back here, so what comes out is an ordinary JPEG.
        var jpeg = new byte[length + 2];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        data.Slice(offset, length).CopyTo(jpeg.AsSpan(2));

        tiles.Add(new JnxTile {
          JpegData = jpeg,
          Width = width,
          Height = height,
          NorthEastX = tileNorthEastX,
          NorthEastY = tileNorthEastY,
          SouthWestX = tileSouthWestX,
          SouthWestY = tileSouthWestY,
        });
      }
    }

    if (tiles.Count == 0)
      throw new InvalidDataException("JNX carries no tile with any picture in it.");

    return new JnxFile {
      Version = version,
      Serial = serial,
      NorthEastX = northEastX,
      NorthEastY = northEastY,
      SouthWestX = southWestX,
      SouthWestY = southWestY,
      Expiry = expiry,
      ProductId = productId,
      Crc = crc,
      Signature = signature,
      SignatureOffset = signatureOffset,
      ZoomOrder = order,
      LevelScales = levelScales,
      Tiles = tiles,
    };
  }

  private static int _Int32(ReadOnlySpan<byte> data, ref int at) {
    if (at + 4 > data.Length)
      throw new InvalidDataException("JNX ends inside a header field.");

    var value = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    at += 4;
    return value;
  }
}
