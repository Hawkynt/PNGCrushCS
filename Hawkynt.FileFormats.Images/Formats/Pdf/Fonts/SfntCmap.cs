using System;
using System.Collections.Generic;

namespace FileFormat.Pdf.Fonts;

/// <summary>One parsed sfnt cmap subtable: formats 0, 4, 6 and 12.</summary>
/// <remarks>
/// Shared by TrueType programs embedded in a PDF and by installed fonts standing in for a
/// non-embedded CID font, which reach their glyphs through Unicode.
/// </remarks>
internal sealed class SfntCmap {

  private readonly Dictionary<int, int>? _map;
  private readonly (uint Start, uint End, uint Glyph)[]? _groups;

  private SfntCmap(Dictionary<int, int>? map, (uint, uint, uint)[]? groups) {
    this._map = map;
    this._groups = groups;
  }

  /// <summary>The glyph id for a character code, or -1 when the subtable gives none.</summary>
  public int Lookup(int code) {
    if (this._map != null)
      return this._map.TryGetValue(code, out var gid) ? gid : -1;

    if (this._groups == null || code < 0)
      return -1;

    var c = (uint)code;
    int lo = 0, hi = this._groups.Length - 1;
    while (lo <= hi) {
      var mid = (lo + hi) >> 1;
      var (start, end, glyph) = this._groups[mid];
      if (c < start)
        hi = mid - 1;
      else if (c > end)
        lo = mid + 1;
      else
        return (int)(glyph + (c - start));
    }

    return -1;
  }

  /// <summary>Reads the subtable at an offset into the font data, or null for a format this does not read.</summary>
  public static SfntCmap? Read(byte[] data, int offset) {
    var format = _U16(data, offset);
    var map = new Dictionary<int, int>();
    switch (format) {
      case 0:
        for (var code = 0; code < 256; ++code)
          if (offset + 6 + code < data.Length && data[offset + 6 + code] != 0)
            map[code] = data[offset + 6 + code];
        return new(map, null);

      case 4: {
        var segments = _U16(data, offset + 6) / 2;
        var endsAt = offset + 14;
        var startsAt = endsAt + segments * 2 + 2;
        var deltasAt = startsAt + segments * 2;
        var rangesAt = deltasAt + segments * 2;
        for (var s = 0; s < segments; ++s) {
          int end = _U16(data, endsAt + s * 2);
          int start = _U16(data, startsAt + s * 2);
          var delta = _U16(data, deltasAt + s * 2);
          var rangeOffset = _U16(data, rangesAt + s * 2);
          if (start > end || (start == 0xFFFF && end == 0xFFFF))
            continue;

          for (var code = start; code <= end; ++code) {
            int gid;
            if (rangeOffset == 0)
              gid = (code + delta) & 0xFFFF;
            else {
              var at = rangesAt + s * 2 + rangeOffset + (code - start) * 2;
              if (at + 2 > data.Length)
                continue;
              gid = _U16(data, at);
              if (gid != 0)
                gid = (gid + delta) & 0xFFFF;
            }

            if (gid != 0)
              map[code] = gid;
          }
        }
        return new(map, null);
      }

      case 6: {
        var first = _U16(data, offset + 6);
        var count = _U16(data, offset + 8);
        for (var k = 0; k < count; ++k) {
          var gid = _U16(data, offset + 10 + k * 2);
          if (gid != 0)
            map[first + k] = gid;
        }
        return new(map, null);
      }

      case 12: {
        var count = _U32(data, offset + 12);
        if (count > (data.Length - offset) / 12)
          count = (uint)((data.Length - offset) / 12);
        var groups = new (uint, uint, uint)[count];
        for (var k = 0; k < count; ++k) {
          var at = offset + 16 + k * 12;
          groups[k] = (_U32(data, at), _U32(data, at + 4), _U32(data, at + 8));
        }
        Array.Sort(groups, static (x, y) => x.Item1.CompareTo(y.Item1));
        return new(null, groups);
      }

      default:
        return null;
    }
  }

  private static ushort _U16(byte[] data, int at) => (ushort)((data[at] << 8) | data[at + 1]);

  private static uint _U32(byte[] data, int at) => (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
}
