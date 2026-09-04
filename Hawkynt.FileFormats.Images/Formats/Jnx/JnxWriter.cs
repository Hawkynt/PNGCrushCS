using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Jnx;

/// <summary>Assembles a Garmin JNX map of one level from its tiles.</summary>
/// <remarks>
/// Version 3 is written, which is the version whose header has no fields a
/// device fills in: version 4 adds a zoom order and a per-level copyright string
/// this has nothing true to put in.
/// </remarks>
public static class JnxWriter {

  private const int _Version = 3;
  private const int _HeaderSize = 4 * 12;
  // Four bounds, a width and height of sixteen bits each, then the tile's
  // length and offset: 16 + 4 + 4 + 4.
  private const int _TileDescriptorSize = 28;
  private const int _LevelDescriptorSize = 12;

  public static byte[] ToBytes(JnxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Tiles is not { Count: > 0 })
      throw new ArgumentException("A JNX needs at least one tile.", nameof(file));

    foreach (var tile in file.Tiles) {
      if (tile.JpegData is not { Length: > 2 })
        throw new ArgumentException("A JNX tile needs its JPEG.", nameof(file));
      if (tile.JpegData[0] != 0xFF || tile.JpegData[1] != 0xD8)
        throw new ArgumentException("A JNX tile's picture has to be a JPEG.", nameof(file));
      if (tile.Width is <= 0 or > ushort.MaxValue || tile.Height is <= 0 or > ushort.MaxValue)
        throw new ArgumentException($"A tile of {tile.Width}x{tile.Height} is outside what a descriptor can state.", nameof(file));
    }

    var tileCount = file.Tiles.Count;
    var tableOffset = _HeaderSize + _LevelDescriptorSize;
    var dataOffset = tableOffset + tileCount * _TileDescriptorSize;

    var total = dataOffset;
    foreach (var tile in file.Tiles)
      total += tile.JpegData.Length - 2;

    var result = new byte[total];

    var at = 0;
    void Write(int value) {
      BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), value);
      at += 4;
    }

    Write(_Version);
    Write(file.Serial);
    Write(file.NorthEastX);
    Write(file.NorthEastY);
    Write(file.SouthWestX);
    Write(file.SouthWestY);
    Write(1); // one level
    Write(file.Expiry);
    Write(file.ProductId);
    Write(file.Crc);
    Write(file.Signature);
    Write(file.SignatureOffset);

    // The single level: how many tiles it holds, where their table starts, and
    // the scale it is drawn at.
    Write(tileCount);
    Write(tableOffset);
    Write(file.LevelScales is { Length: > 0 } ? file.LevelScales[0] : 0);

    var payloadAt = dataOffset;
    foreach (var tile in file.Tiles) {
      Write(tile.NorthEastX);
      Write(tile.NorthEastY);
      Write(tile.SouthWestX);
      Write(tile.SouthWestY);
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at), (ushort)tile.Width);
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at + 2), (ushort)tile.Height);
      at += 4;

      // The stored length and offset are of the JPEG without its two-byte
      // start-of-image marker, which the format leaves out.
      var length = tile.JpegData.Length - 2;
      Write(length);
      Write(payloadAt);

      tile.JpegData.AsSpan(2).CopyTo(result.AsSpan(payloadAt));
      payloadAt += length;
    }

    return result;
  }
}
