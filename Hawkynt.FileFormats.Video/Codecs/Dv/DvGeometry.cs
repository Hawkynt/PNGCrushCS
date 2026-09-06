using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// Where each of a frame's video segments lives in the DIF stream, and which five macroblocks of the
/// picture it holds.
/// </summary>
/// <remarks>
/// A DV frame is not stored in picture order and is not meant to be. Its five macroblocks are taken
/// from five widely separated parts of the frame, and successive segments walk the picture in a
/// serpentine that never puts neighbours next to each other on tape. That is a recording format's
/// answer to a dropout: a lost DIF block costs five scattered macroblocks rather than a stripe, and a
/// concealer can interpolate every one of them from undamaged neighbours.
/// <para/>
/// The shuffle is therefore not derivable from anything — it is a table, and it is
/// <c>dv_calc_mb_coordinates</c> from FFmpeg's <c>libavcodec/dv.c</c>, restricted to the 720-sample
/// rasters. Getting it wrong does not produce a broken picture; it produces a picture made of the
/// right blocks in the wrong places, which is why it is worth stating that none of these numbers was
/// worked out here.
/// </remarks>
internal static class DvGeometry {

  /// <summary>Which DIF sequence each of the five macroblocks of a segment is displaced into.</summary>
  private static readonly int[] _SequenceOffsets = [2, 6, 8, 0, 4];

  /// <summary>The horizontal starting column of each of the five macroblocks, 4:2:0 and 4:2:2.</summary>
  private static readonly int[] _ColumnStarts = [18, 9, 27, 0, 36];

  /// <summary>The horizontal starting column of each of the five macroblocks, 4:1:1.</summary>
  private static readonly int[] _ColumnStarts411 = [9, 4, 13, 0, 18];

  /// <summary>The vertical serpentine within a segment, 4:2:0 and 4:2:2 — twenty-seven slots.</summary>
  private static readonly int[] _Serpent = [
    0, 1, 2, 2, 1, 0,
    0, 1, 2, 2, 1, 0,
    0, 1, 2, 2, 1, 0,
    0, 1, 2, 2, 1, 0,
    0, 1, 2,
  ];

  /// <summary>The vertical serpentine within a segment, 4:1:1 — thirty slots, three of them borrowed.</summary>
  private static readonly int[] _Serpent411 = [
    0, 1, 2, 3, 4, 5, 5, 4, 3, 2, 1, 0,
    0, 1, 2, 3, 4, 5, 5, 4, 3, 2, 1, 0,
    0, 1, 2, 3, 4, 5,
  ];

  /// <summary>One video segment: where its bits are and which five macroblocks it holds.</summary>
  /// <param name="BlockOffset">The segment's first DIF block, counted from the start of the frame.</param>
  /// <param name="MacroblockX">The left column of each macroblock, in units of eight luma samples.</param>
  /// <param name="MacroblockY">The top row of each macroblock, in units of eight luma samples.</param>
  internal readonly record struct Segment(int BlockOffset, int[] MacroblockX, int[] MacroblockY);

  /// <summary>
  /// Lays out every video segment of a frame in the order they appear on tape.
  /// </summary>
  /// <remarks>
  /// The walk over the DIF stream is what fixes the offsets: six control blocks open every sequence,
  /// then twenty-seven segments of five video blocks each, with an audio block inserted before every
  /// third segment. Counting them in that order is how a segment's five macroblocks find their bits.
  /// </remarks>
  internal static Segment[] Segments(DvProfile profile) {
    ArgumentNullException.ThrowIfNull(profile);

    var segments = new Segment[profile.SegmentCount];
    var written = 0;
    var block = 0;

    for (var channel = 0; channel < profile.ChannelCount; ++channel)
      for (var sequence = 0; sequence < profile.SequencesPerChannel; ++sequence) {
        block += 6; // header, two subcode and three VAUX blocks open every DIF sequence
        for (var slot = 0; slot < DvProfile.SegmentsPerSequence; ++slot) {
          // One audio block precedes every third video segment, nine to a sequence.
          if (slot % 3 == 0)
            ++block;

          var x = new int[DvProfile.MacroblocksPerSegment];
          var y = new int[DvProfile.MacroblocksPerSegment];
          for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock)
            (x[macroblock], y[macroblock]) = _Coordinates(profile, channel, sequence, slot, macroblock);

          segments[written++] = new(block, x, y);
          block += DvProfile.MacroblocksPerSegment;
        }
      }

    return segments;
  }

  /// <summary>Where one macroblock of one segment sits in the picture.</summary>
  private static (int X, int Y) _Coordinates(DvProfile profile, int channel, int sequence, int slot, int macroblock) {
    var sequences = profile.SequencesPerChannel;
    var displaced = (sequence + _SequenceOffsets[macroblock]) % sequences;

    switch (profile.Sampling) {
      case DvSampling.FourTwoTwo: {
        // Two DIF channels interleave by row triple, which is why the channel joins the row index.
        var x = _ColumnStarts[macroblock] + slot / 3;
        var y = _Serpent[slot] + (((displaced << 1) + channel) * 3);
        return (x << 1, y);
      }

      case DvSampling.FourTwoZero: {
        var x = _ColumnStarts[macroblock] + slot / 3;
        var y = _Serpent[slot] + displaced * 3;
        // A 4:2:0 macroblock is sixteen luma lines tall, so its row index counts in twos.
        return (x << 1, y << 1);
      }

      default: {
        // 4:1:1 takes three of its five macroblocks from further down the serpentine, which is what
        // the borrowed slots at the end of the table are for.
        var borrowed = slot + (macroblock is 1 or 2 ? 3 : 0);
        var x = _ColumnStarts411[macroblock] + borrowed / 6;
        var y = _Serpent411[borrowed] + displaced * 6;

        // Past column 21 the raster has only sixteen luma samples left rather than thirty-two, so the
        // macroblock is folded into a 16x16 square and covers two rows of the serpentine at once.
        if (x > 21)
          y = y * 2 - displaced * 6;

        return (x << 2, y);
      }
    }
  }
}
