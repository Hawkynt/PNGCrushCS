using System;
using System.Collections.Generic;

namespace FileFormat.Avif.Codec;

/// <summary>OBU type identifiers (AV1 spec 6.2.2).</summary>
internal enum Av1ObuType {
  SequenceHeader = 1,
  TemporalDelimiter = 2,
  FrameHeader = 3,
  TileGroup = 4,
  Metadata = 5,
  Frame = 6,
  RedundantFrameHeader = 7,
  TileList = 8,
  Padding = 15,
}

/// <summary>Parsed OBU with header metadata and payload range.</summary>
internal readonly struct Av1Obu {
  public Av1ObuType Type { get; init; }
  public bool HasExtension { get; init; }
  public bool HasSize { get; init; }
  public int TemporalId { get; init; }
  public int SpatialId { get; init; }
  public int PayloadOffset { get; init; }
  public int PayloadSize { get; init; }
  public int TotalSize { get; init; }
}

/// <summary>Parses AV1 Open Bitstream Units from raw byte data.</summary>
internal static class Av1ObuParser {

  /// <summary>Parses all OBUs from an AV1 bitstream byte array.</summary>
  public static List<Av1Obu> ParseObus(byte[] data, int offset, int length) {
    var obus = new List<Av1Obu>();
    var end = offset + length;

    while (offset < end) {
      var obu = ParseSingleObu(data, offset, end - offset);
      obus.Add(obu);
      offset += obu.TotalSize;
    }

    return obus;
  }

  /// <summary>Parses a single OBU starting at the given offset.</summary>
  public static Av1Obu ParseSingleObu(byte[] data, int offset, int maxLength) {
    if (maxLength < 1)
      throw new InvalidOperationException("AV1: not enough data for OBU header.");

    var startOffset = offset;
    var header = data[offset++];

    // obu_forbidden_bit must be 0
    if ((header & 0x80) != 0)
      throw new InvalidOperationException("AV1: obu_forbidden_bit is set.");

    var type = (Av1ObuType)((header >> 3) & 0x0F);
    var hasExtension = (header & 0x04) != 0;
    var hasSize = (header & 0x02) != 0;

    var temporalId = 0;
    var spatialId = 0;

    if (hasExtension) {
      if (offset >= startOffset + maxLength)
        throw new InvalidOperationException("AV1: not enough data for OBU extension header.");

      var extHeader = data[offset++];
      temporalId = (extHeader >> 5) & 0x07;
      spatialId = (extHeader >> 3) & 0x03;
    }

    int payloadSize;
    if (hasSize) {
      payloadSize = (int)_ReadLeb128(data, ref offset, startOffset + maxLength);
    } else {
      payloadSize = startOffset + maxLength - offset;
    }

    var payloadOffset = offset;
    var totalSize = (payloadOffset - startOffset) + payloadSize;

    return new() {
      Type = type,
      HasExtension = hasExtension,
      HasSize = hasSize,
      TemporalId = temporalId,
      SpatialId = spatialId,
      PayloadOffset = payloadOffset,
      PayloadSize = payloadSize,
      TotalSize = totalSize,
    };
  }

  private static ulong _ReadLeb128(byte[] data, ref int offset, int end) {
    var value = 0UL;
    for (var i = 0; i < 8; ++i) {
      if (offset >= end)
        throw new InvalidOperationException("AV1: leb128 truncated.");

      var b = data[offset++];
      value |= ((ulong)(b & 0x7F)) << (i * 7);
      if ((b & 0x80) == 0)
        break;
    }
    return value;
  }
}
