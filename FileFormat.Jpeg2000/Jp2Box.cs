using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jpeg2000;

/// <summary>Represents a single box in the JP2 container format (ISO 15444-1 Annex I).</summary>
internal sealed class Jp2Box {

  /// <summary>4-byte box type code (e.g. "jP  ", "ftyp", "jp2h", "jp2c").</summary>
  public uint Type { get; init; }

  /// <summary>Raw box payload (excluding the 8-byte box header).</summary>
  public byte[] Data { get; init; } = [];

  /// <summary>JP2 Signature box type: 0x6A502020 ("jP  ").</summary>
  internal const uint TYPE_SIGNATURE = 0x6A502020;

  /// <summary>File Type box type: 0x66747970 ("ftyp").</summary>
  internal const uint TYPE_FILE_TYPE = 0x66747970;

  /// <summary>JP2 Header superbox type: 0x6A703268 ("jp2h").</summary>
  internal const uint TYPE_JP2_HEADER = 0x6A703268;

  /// <summary>Image Header box type: 0x69686472 ("ihdr").</summary>
  internal const uint TYPE_IMAGE_HEADER = 0x69686472;

  /// <summary>Colour Specification box type: 0x636F6C72 ("colr").</summary>
  internal const uint TYPE_COLOUR_SPEC = 0x636F6C72;

  /// <summary>Contiguous Codestream box type: 0x6A703263 ("jp2c").</summary>
  internal const uint TYPE_CODESTREAM = 0x6A703263;

  /// <summary>The 12-byte JP2 signature: 00 00 00 0C 6A 50 20 20 0D 0A 87 0A.</summary>
  internal static readonly byte[] JP2_SIGNATURE_BYTES = [
    0x00, 0x00, 0x00, 0x0C,
    0x6A, 0x50, 0x20, 0x20,
    0x0D, 0x0A, 0x87, 0x0A
  ];

  /// <summary>Reads all boxes from the given data starting at the specified offset.</summary>
  internal static List<Jp2Box> ReadBoxes(byte[] data, int offset, int length) {
    var boxes = new List<Jp2Box>();
    var end = offset + length;
    while (offset + 8 <= end) {
      var boxLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
      var boxType = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4));

      int dataOffset;
      int dataLength;
      if (boxLength == 1) {
        if (offset + 16 > end)
          break;
        var extLength = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 8));
        dataOffset = offset + 16;
        dataLength = (int)(extLength - 16);
      } else if (boxLength == 0) {
        dataOffset = offset + 8;
        dataLength = end - dataOffset;
      } else {
        dataOffset = offset + 8;
        dataLength = boxLength - 8;
      }

      if (dataLength < 0 || dataOffset + dataLength > end)
        break;

      var payload = new byte[dataLength];
      Array.Copy(data, dataOffset, payload, 0, dataLength);
      boxes.Add(new Jp2Box { Type = boxType, Data = payload });

      if (boxLength == 0)
        break;

      offset = boxLength == 1 ? dataOffset + dataLength : offset + boxLength;
    }

    return boxes;
  }

  /// <summary>Writes a box with the given type and payload to the stream.</summary>
  internal static void WriteBox(MemoryStream ms, uint type, byte[] data) {
    var length = data.Length + 8;
    Span<byte> header = stackalloc byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(header, (uint)length);
    BinaryPrimitives.WriteUInt32BigEndian(header[4..], type);
    ms.Write(header);
    ms.Write(data);
  }

  /// <summary>Writes a box header with length=0 (box extends to end of file) followed by data.</summary>
  internal static void WriteBoxToEnd(MemoryStream ms, uint type, byte[] data) {
    Span<byte> header = stackalloc byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(header, 0);
    BinaryPrimitives.WriteUInt32BigEndian(header[4..], type);
    ms.Write(header);
    ms.Write(data);
  }

  /// <summary>Converts a 4-character ASCII string to a big-endian uint32 type code.</summary>
  internal static uint TypeFromString(string fourCC) {
    if (fourCC.Length != 4)
      throw new ArgumentException("FourCC must be exactly 4 characters.", nameof(fourCC));

    return (uint)((fourCC[0] << 24) | (fourCC[1] << 16) | (fourCC[2] << 8) | fourCC[3]);
  }

  /// <summary>Converts a big-endian uint32 type code to a 4-character ASCII string.</summary>
  internal static string TypeToString(uint type) => new([
    (char)((type >> 24) & 0xFF),
    (char)((type >> 16) & 0xFF),
    (char)((type >> 8) & 0xFF),
    (char)(type & 0xFF)
  ]);
}
