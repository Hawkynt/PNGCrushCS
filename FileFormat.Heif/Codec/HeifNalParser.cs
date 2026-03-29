using System;
using System.Collections.Generic;

namespace FileFormat.Heif.Codec;

/// <summary>HEVC NAL unit types (ITU-T H.265 Table 7-1).</summary>
internal enum HevcNalUnitType {
  TrailN = 0,
  TrailR = 1,
  BlaWLp = 16,
  BlaWRadl = 17,
  BlaNLp = 18,
  IdrWRadl = 19,
  IdrNLp = 20,
  CraNut = 21,
  VpsNut = 32,
  SpsNut = 33,
  PpsNut = 34,
  AudNut = 35,
  EosNut = 36,
  EobNut = 37,
  FdNut = 38,
  PrefixSeiNut = 39,
  SuffixSeiNut = 40,
}

/// <summary>A parsed NAL unit with type and payload range.</summary>
internal readonly struct HevcNalUnit {
  public HevcNalUnitType Type { get; init; }
  public int PayloadOffset { get; init; }
  public int PayloadSize { get; init; }
}

/// <summary>Parses HEVC NAL units from both Annex B byte stream format and length-prefixed format (hvcC).</summary>
internal static class HeifNalParser {

  /// <summary>Parses NAL units from length-prefixed format (as stored in ISO BMFF hvcC/mdat).</summary>
  public static List<HevcNalUnit> ParseLengthPrefixed(byte[] data, int offset, int length, int nalLengthSize) {
    var units = new List<HevcNalUnit>();
    var end = offset + length;

    while (offset + nalLengthSize <= end) {
      var nalLength = 0;
      for (var i = 0; i < nalLengthSize; ++i)
        nalLength = (nalLength << 8) | data[offset + i];

      offset += nalLengthSize;
      if (nalLength <= 0 || offset + nalLength > end)
        break;

      if (nalLength >= 2) {
        // NAL header is 2 bytes in HEVC
        var nalHeader = (data[offset] << 8) | data[offset + 1];
        var nalType = (HevcNalUnitType)((nalHeader >> 9) & 0x3F);

        units.Add(new() {
          Type = nalType,
          PayloadOffset = offset + 2,
          PayloadSize = nalLength - 2,
        });
      }

      offset += nalLength;
    }

    return units;
  }

  /// <summary>Parses NAL units from Annex B byte stream format (start code prefixed).</summary>
  public static List<HevcNalUnit> ParseAnnexB(byte[] data, int offset, int length) {
    var units = new List<HevcNalUnit>();
    var end = offset + length;

    while (offset < end - 3) {
      // Find start code (0x000001 or 0x00000001)
      var scOffset = _FindStartCode(data, offset, end);
      if (scOffset < 0)
        break;

      var scLength = (scOffset >= 1 && data[scOffset - 1] == 0) ? 4 : 3;
      var nalStart = scOffset + 3; // past 0x000001

      // Find next start code to determine NAL unit length
      var nextSc = _FindStartCode(data, nalStart, end);
      var nalEnd = nextSc >= 0 ? (nextSc >= 1 && data[nextSc - 1] == 0 ? nextSc - 1 : nextSc) : end;

      if (nalEnd > nalStart + 2) {
        var nalHeader = (data[nalStart] << 8) | data[nalStart + 1];
        var nalType = (HevcNalUnitType)((nalHeader >> 9) & 0x3F);

        units.Add(new() {
          Type = nalType,
          PayloadOffset = nalStart + 2,
          PayloadSize = nalEnd - nalStart - 2,
        });
      }

      offset = nextSc >= 0 ? nextSc : end;
    }

    return units;
  }

  /// <summary>Removes emulation prevention bytes (0x03 in 00 00 03 xx sequences).</summary>
  public static byte[] RemoveEmulationPrevention(byte[] data, int offset, int length) {
    var result = new byte[length];
    var j = 0;
    for (var i = 0; i < length; ++i) {
      if (i >= 2 && data[offset + i - 2] == 0 && data[offset + i - 1] == 0 && data[offset + i] == 3) {
        // Skip emulation prevention byte
        continue;
      }
      result[j++] = data[offset + i];
    }
    return result[..j];
  }

  private static int _FindStartCode(byte[] data, int offset, int end) {
    for (var i = offset; i < end - 2; ++i) {
      if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
        return i;
    }
    return -1;
  }
}
