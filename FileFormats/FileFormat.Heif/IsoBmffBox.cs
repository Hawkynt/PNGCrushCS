using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Heif;

/// <summary>Represents an ISO Base Media File Format box (atom).</summary>
internal readonly struct IsoBmffBox {

  /// <summary>Four-character code identifying the box type.</summary>
  public string Type { get; }

  /// <summary>Raw payload data of the box (excluding the 8-byte header).</summary>
  public byte[] Data { get; }

  public IsoBmffBox(string type, byte[] data) {
    Type = type;
    Data = data;
  }

  // Well-known box type constants
  internal const string Ftyp = "ftyp";
  internal const string Meta = "meta";
  internal const string Hdlr = "hdlr";
  internal const string Pitm = "pitm";
  internal const string Iloc = "iloc";
  internal const string Iprp = "iprp";
  internal const string Ipco = "ipco";
  internal const string Ipma = "ipma";
  internal const string Ispe = "ispe";
  internal const string HvcC = "hvcC";
  internal const string Mdat = "mdat";

  private const int _HEADER_SIZE = 8;

  /// <summary>Parses ISOBMFF boxes from raw data.</summary>
  internal static List<IsoBmffBox> ReadBoxes(byte[] data, int offset, int length) {
    var boxes = new List<IsoBmffBox>();
    var end = offset + length;

    while (offset + _HEADER_SIZE <= end) {
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
      var type = Encoding.ASCII.GetString(data, offset + 4, 4);

      int dataOffset;
      int dataLength;

      if (size == 0) {
        // box extends to end of data
        dataOffset = offset + _HEADER_SIZE;
        dataLength = end - dataOffset;
        offset = end;
      } else if (size == 1) {
        // 64-bit extended size
        if (offset + 16 > end)
          break;

        var extendedSize = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 8));
        dataOffset = offset + 16;
        dataLength = (int)(extendedSize - 16);
        if (dataOffset + dataLength > end)
          dataLength = end - dataOffset;

        offset = dataOffset + dataLength;
      } else {
        if (size < _HEADER_SIZE)
          break;

        dataOffset = offset + _HEADER_SIZE;
        dataLength = size - _HEADER_SIZE;
        if (dataOffset + dataLength > end)
          dataLength = end - dataOffset;

        offset += size;
      }

      var boxData = new byte[dataLength];
      if (dataLength > 0)
        Array.Copy(data, dataOffset, boxData, 0, dataLength);

      boxes.Add(new(type, boxData));
    }

    return boxes;
  }

  /// <summary>Writes a standard ISOBMFF box with 32-bit size header.</summary>
  internal static byte[] WriteBox(string type, byte[] data) {
    var totalSize = _HEADER_SIZE + data.Length;
    var result = new byte[totalSize];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0), (uint)totalSize);
    Encoding.ASCII.GetBytes(type, 0, 4, result, 4);
    Array.Copy(data, 0, result, _HEADER_SIZE, data.Length);
    return result;
  }

  /// <summary>Writes a FullBox (box with version byte and 3 flags bytes before the payload).</summary>
  internal static byte[] WriteFullBox(string type, byte version, uint flags, byte[] data) {
    var innerLength = 4 + data.Length;
    var totalSize = _HEADER_SIZE + innerLength;
    var result = new byte[totalSize];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0), (uint)totalSize);
    Encoding.ASCII.GetBytes(type, 0, 4, result, 4);
    result[8] = version;
    result[9] = (byte)((flags >> 16) & 0xFF);
    result[10] = (byte)((flags >> 8) & 0xFF);
    result[11] = (byte)(flags & 0xFF);
    Array.Copy(data, 0, result, 12, data.Length);
    return result;
  }
}
