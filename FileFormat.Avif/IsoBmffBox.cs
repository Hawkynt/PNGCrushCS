using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Avif;

/// <summary>Represents an ISOBMFF (ISO Base Media File Format) box with type and payload.</summary>
internal sealed class IsoBmffBox {

  /// <summary>Four-character box type code.</summary>
  public uint Type { get; init; }

  /// <summary>Raw payload data (excluding the 8-byte box header).</summary>
  public byte[] Data { get; init; } = [];

  /// <summary>Minimum box header size (4 bytes size + 4 bytes type).</summary>
  internal const int HeaderSize = 8;

  // Well-known box type constants
  internal static readonly uint Ftyp = FourCC("ftyp");
  internal static readonly uint Meta = FourCC("meta");
  internal static readonly uint Hdlr = FourCC("hdlr");
  internal static readonly uint Pitm = FourCC("pitm");
  internal static readonly uint Iloc = FourCC("iloc");
  internal static readonly uint Iprp = FourCC("iprp");
  internal static readonly uint Ipco = FourCC("ipco");
  internal static readonly uint Ipma = FourCC("ipma");
  internal static readonly uint Ispe = FourCC("ispe");
  internal static readonly uint Av1C = FourCC("av1C");
  internal static readonly uint Pixi = FourCC("pixi");
  internal static readonly uint Mdat = FourCC("mdat");
  internal static readonly uint Iinf = FourCC("iinf");
  internal static readonly uint Infe = FourCC("infe");

  /// <summary>Converts a 4-character ASCII string to a big-endian uint.</summary>
  internal static uint FourCC(string s) {
    if (s.Length != 4)
      throw new ArgumentException("FourCC must be exactly 4 characters.", nameof(s));

    return (uint)((s[0] << 24) | (s[1] << 16) | (s[2] << 8) | s[3]);
  }

  /// <summary>Converts a big-endian uint to a 4-character ASCII string.</summary>
  internal static string FourCCToString(uint value) {
    var chars = new char[4];
    chars[0] = (char)((value >> 24) & 0xFF);
    chars[1] = (char)((value >> 16) & 0xFF);
    chars[2] = (char)((value >> 8) & 0xFF);
    chars[3] = (char)(value & 0xFF);
    return new(chars);
  }

  /// <summary>Parses all top-level boxes from the given byte array.</summary>
  internal static List<IsoBmffBox> ReadBoxes(byte[] data, int offset, int length) {
    var boxes = new List<IsoBmffBox>();
    var end = offset + length;

    while (offset + HeaderSize <= end) {
      var boxSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
      var boxType = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4));

      if (boxSize < HeaderSize)
        break;

      if (offset + boxSize > end)
        break;

      var payloadLength = boxSize - HeaderSize;
      var payload = new byte[payloadLength];
      if (payloadLength > 0)
        Array.Copy(data, offset + HeaderSize, payload, 0, payloadLength);

      boxes.Add(new IsoBmffBox { Type = boxType, Data = payload });
      offset += boxSize;
    }

    return boxes;
  }

  /// <summary>Writes a single box (header + payload) to the given byte array at the specified offset.</summary>
  internal static int WriteBox(byte[] buffer, int offset, uint type, byte[] payload) {
    var boxSize = HeaderSize + payload.Length;
    BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), (uint)boxSize);
    BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset + 4), type);
    if (payload.Length > 0)
      Array.Copy(payload, 0, buffer, offset + HeaderSize, payload.Length);

    return boxSize;
  }

  /// <summary>Builds a box as a standalone byte array.</summary>
  internal static byte[] BuildBox(uint type, byte[] payload) {
    var result = new byte[HeaderSize + payload.Length];
    WriteBox(result, 0, type, payload);
    return result;
  }
}
