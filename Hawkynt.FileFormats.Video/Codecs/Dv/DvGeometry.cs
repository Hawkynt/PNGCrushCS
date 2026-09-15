using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// Where each video segment lives in the DIF stream and which five macroblocks of the picture it
/// carries.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's LGPL-2.1-or-later <c>dv_calc_mb_coordinates</c> and
/// <c>ff_dv_init_dynamic_tables</c> in <c>libavcodec/dv.c</c>; see
/// <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>. The HD cases are the normative SMPTE 370M shuffles, including
/// the deliberately unused DIF regions in 1080/50i and 720/50p.
/// </remarks>
internal static class DvGeometry {

  private static readonly int[] _SequenceOffsets = [2, 6, 8, 0, 4];
  private static readonly int[] _ColumnStarts = [18, 9, 27, 0, 36];
  private static readonly int[] _ColumnStarts411 = [9, 4, 13, 0, 18];
  private static readonly int[] _HdColumnStarts1080 = [36, 18, 54, 0, 72];
  private static readonly int[] _HdColumnStarts720 = [24, 12, 36, 0, 48];
  private static readonly int[] _HdLineStarts = [0, 4, 9, 13, 18, 22, 27, 31, 36, 40];

  private static readonly int[] _Serpent = [
    0, 1, 2, 2, 1, 0,
    0, 1, 2, 2, 1, 0,
    0, 1, 2, 2, 1, 0,
    0, 1, 2, 2, 1, 0,
    0, 1, 2,
  ];

  private static readonly int[] _Serpent411 = [
    0, 1, 2, 3, 4, 5, 5, 4, 3, 2, 1, 0,
    0, 1, 2, 3, 4, 5, 5, 4, 3, 2, 1, 0,
    0, 1, 2, 3, 4, 5,
  ];

  /// <summary>
  /// The right-edge remap used by 1280-sample 1080/60i. Values are mandated by the SMPTE 370M
  /// macroblock shuffle; they are factual interoperability data rather than an implementation choice.
  /// </summary>
  private static readonly (int X, int Y)[] _Hd1080Remap = [
    (0, 0), (0, 0), (0, 0), (0, 0),
    (0, 0), (0, 1), (0, 2), (0, 3), (10, 0),
    (10, 1), (10, 2), (10, 3), (20, 0), (20, 1),
    (20, 2), (20, 3), (30, 0), (30, 1), (30, 2),
    (30, 3), (40, 0), (40, 1), (40, 2), (40, 3),
    (50, 0), (50, 1), (50, 2), (50, 3), (60, 0),
    (60, 1), (60, 2), (60, 3), (70, 0), (70, 1),
    (70, 2), (70, 3), (0, 64), (0, 65), (0, 66),
    (10, 64), (10, 65), (10, 66), (20, 64), (20, 65),
    (20, 66), (30, 64), (30, 65), (30, 66), (40, 64),
    (40, 65), (40, 66), (50, 64), (50, 65), (50, 66),
    (60, 64), (60, 65), (60, 66), (70, 64), (70, 65),
    (70, 66), (0, 67), (20, 67), (40, 67), (60, 67),
  ];

  /// <summary>One video segment: where its bits are and which five macroblocks it holds.</summary>
  /// <param name="BlockOffset">The segment's first video DIF block, counted from the frame start.</param>
  /// <param name="MacroblockX">Left coordinate in units of eight luma samples.</param>
  /// <param name="MacroblockY">Top coordinate in units of eight luma samples.</param>
  internal readonly record struct Segment(int BlockOffset, int[] MacroblockX, int[] MacroblockY);

  /// <summary>Lays out every coded video segment in physical DIF order.</summary>
  internal static Segment[] Segments(DvProfile profile) {
    ArgumentNullException.ThrowIfNull(profile);

    var segments = new List<Segment>(profile.SegmentCount);
    var block = 0;

    for (var channel = 0; channel < profile.ChannelCount; ++channel)
      for (var sequence = 0; sequence < profile.SequencesPerChannel; ++sequence) {
        block += 6;
        for (var slot = 0; slot < DvProfile.SegmentsPerSequence; ++slot) {
          if (slot % 3 == 0)
            ++block;

          if (profile.IsVideoSequenceActive(channel, sequence)) {
            var x = new int[DvProfile.MacroblocksPerSegment];
            var y = new int[DvProfile.MacroblocksPerSegment];
            for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock)
              (x[macroblock], y[macroblock]) = _Coordinates(profile, channel, sequence, slot, macroblock);

            segments.Add(new(block, x, y));
          }

          block += DvProfile.MacroblocksPerSegment;
        }
      }

    return [.. segments];
  }

  /// <summary>
  /// Returns a macroblock coordinate after the 720p half-frame channel displacement has been applied.
  /// </summary>
  internal static (int X, int Y) MacroblockForFrame(
    DvProfile profile, in Segment segment, int macroblock, ReadOnlySpan<byte> frame) {
    var x = segment.MacroblockX[macroblock];
    var y = segment.MacroblockY[macroblock];

    // SMPTE 370M alternates 720p frames between DIF channel pairs 0/1 and 2/3. The second pair is
    // identified by FSP in the first DIF ID and uses the complementary vertical shuffle.
    if (profile.Height == 720 && frame.Length > 1 && (frame[1] & 0x0c) == 0)
      y -= y > 17 ? 18 : -72;

    return (x, y);
  }

  private static (int X, int Y) _Coordinates(DvProfile profile, int channel, int sequence, int slot, int macroblock) {
    if (profile.IsDv100)
      return _HdCoordinates(profile, channel, sequence, slot, macroblock);

    var sequences = profile.SequencesPerChannel;
    var displaced = (sequence + _SequenceOffsets[macroblock]) % sequences;

    switch (profile.Sampling) {
      case DvSampling.FourTwoTwo: {
        var x = _ColumnStarts[macroblock] + slot / 3;
        var y = _Serpent[slot] + (((displaced << 1) + channel) * 3);
        return (x << 1, y);
      }

      case DvSampling.FourTwoZero: {
        var x = _ColumnStarts[macroblock] + slot / 3;
        var y = _Serpent[slot] + displaced * 3;
        return (x << 1, y << 1);
      }

      default: {
        var borrowed = slot + (macroblock is 1 or 2 ? 3 : 0);
        var x = _ColumnStarts411[macroblock] + borrowed / 6;
        var y = _Serpent411[borrowed] + displaced * 6;

        if (x > 21)
          y = y * 2 - displaced * 6;

        return (x << 2, y);
      }
    }
  }

  private static (int X, int Y) _HdCoordinates(
    DvProfile profile, int channel, int sequence, int slot, int macroblock) {
    var offset = _SequenceOffsets[macroblock];

    switch (profile.Width) {
      case 1440: {
        var linear = (channel * 11 + sequence) * 27 + slot;
        int x;
        int y;

        if (channel == 0 && sequence == 11) {
          x = macroblock * 27 + slot;
          if (x < 90) {
            y = 0;
          } else {
            x = (x - 90) * 2;
            y = 67;
          }
        } else {
          var i = (4 * channel + linear + offset) % 11;
          var k = (linear / 11) % 27;
          x = _HdColumnStarts1080[macroblock] + (channel & 1) * 9 + k % 9;
          y = (i * 3 + k / 9) * 2 + (channel >> 1) + 1;
        }

        return (x << 1, y << 1);
      }

      case 1280: {
        var linear = (channel * 10 + sequence) * 27 + slot;
        var i = (4 * channel + sequence / 5 + 2 * linear + offset) % 10;
        var k = (linear / 5) % 27;
        var x = _HdColumnStarts1080[macroblock] + (channel & 1) * 9 + k % 9;
        var y = (i * 3 + k / 9) * 2 + (channel >> 1) + 4;

        if (x >= 80) {
          var remapped = _Hd1080Remap[y];
          x = remapped.X + ((x - 80) << (y > 59 ? 1 : 0));
          y = remapped.Y;
        }

        return (x << 1, y << 1);
      }

      case 960: {
        var linear = (channel * 10 + sequence) * 27 + slot;
        var i = (4 * channel + sequence / 5 + 2 * linear + offset) % 10;
        var k = (linear / 5) % 27 + (i & 1) * 3;
        var x = _HdColumnStarts720[macroblock] + k % 6 + 6 * (channel & 1);
        var y = _HdLineStarts[i] + k / 6 + 45 * (channel >> 1);
        return (x << 1, y << 1);
      }

      default:
        throw new InvalidOperationException($"No DVCPRO HD macroblock shuffle is defined for a {profile.Width}-sample raster.");
    }
  }
}
