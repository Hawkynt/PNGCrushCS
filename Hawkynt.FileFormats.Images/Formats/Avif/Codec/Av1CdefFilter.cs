using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// AV1 constrained directional enhancement filter (specification 7.15), ported from libaom's
/// <c>av1_cdef_frame</c> / <c>cdef_filter_fb</c> in <c>av1/common/cdef.c</c> and
/// <c>cdef_find_dir_c</c> / <c>cdef_filter_block_internal</c> in <c>av1/common/cdef_block.c</c>.
/// libaom copies each 64x64 unit into a bordered scratch buffer filled with CDEF_VERY_LARGE and
/// lets the taps run over it; the specification instead tests availability per sample, which is
/// what is done here - the two agree because a CDEF_VERY_LARGE difference always constrains to 0.
/// </summary>
internal static class Av1CdefFilter {

  private const int _MiSize = 4;

  /// <summary>Cdef_Uv_Dir[subX][subY][yDir] (AV1 7.15.1), flattened as (subX * 2 + subY) * 8 + yDir.</summary>
  private static readonly byte[] _CdefUvDir = [
    0, 1, 2, 3, 4, 5, 6, 7,
    1, 2, 2, 2, 3, 4, 6, 0,
    7, 0, 2, 4, 5, 6, 6, 6,
    0, 1, 2, 3, 4, 5, 6, 7,
  ];

  /// <summary>Div_Table[9] (AV1 7.15.2), libaom <c>div_table</c>: 840/n so the direction costs stay
  /// comparable without a division.</summary>
  private static readonly int[] _DivTable = [
    0, 840, 420, 280, 210, 168, 140, 120, 105,
  ];

  /// <summary>AV1 7.15: constrained directional enhancement, reading the deblocked frame and
  /// writing the filtered result back into it.</summary>
  /// <param name="frame">Deblocked frame; its plane buffers are replaced by the CDEF output.</param>
  /// <param name="seq">Sequence header the frame was decoded with.</param>
  /// <param name="fh">Frame header carrying the CDEF damping, bit count and strengths.</param>
  public static void Apply(Av1DecodedFrame frame, Av1SequenceHeader seq, Av1FrameHeader fh) {
    if (!seq.EnableCdef)
      return;

    // With every strength zero the filter is the identity: constrain() returns 0 for a zero
    // threshold, so sum is 0, the rounded correction is 0 and the clip cannot move the sample.
    var strengthCount = 1 << fh.CdefBits;
    var active = false;
    for (var i = 0; i < strengthCount && !active; ++i)
      active = fh.CdefYPriStrength[i] != 0
        || fh.CdefYSecStrength[i] != 0
        || (frame.NumPlanes > 1 && (fh.CdefUvPriStrength[i] != 0 || fh.CdefUvSecStrength[i] != 0));
    if (!active)
      return;

    // 7.15 reads CurrFrame (the deblocked samples) throughout and writes CdefFrame. Filtering in
    // place would let an already filtered 8x8 block feed the taps of its neighbour, so the source
    // is snapshotted; the per-block copy of the specification is then implicit.
    var source = new short[frame.NumPlanes][];
    for (var plane = 0; plane < frame.NumPlanes; ++plane)
      source[plane] = (short[])frame.Planes[plane].Clone();

    // CDEF parameters live per 64x64 luma block, filtering happens per 8x8 block.
    for (var r = 0; r < frame.MiRows; r += 2)
      for (var c = 0; c < frame.MiCols; c += 2) {
        var idx = frame.CdefIndices[(r >> 4) * frame.Cdef64Cols + (c >> 4)];
        if (idx < 0)
          continue;

        _CdefBlock(frame, seq, fh, source, r, c, idx);
      }
  }

  /// <summary>AV1 7.15.1 CDEF block process for one 8x8 luma block.</summary>
  private static void _CdefBlock(
    Av1DecodedFrame frame, Av1SequenceHeader seq, Av1FrameHeader fh, short[][] source, int r, int c, int idx) {
    var mi = r * frame.MiCols + c;
    var skip = frame.Skips[mi]
      && frame.Skips[mi + 1]
      && frame.Skips[mi + frame.MiCols]
      && frame.Skips[mi + frame.MiCols + 1];
    if (skip)
      return;

    var coeffShift = frame.BitDepth - 8;
    var yDir = _FindDirection(source[0], frame.Strides[0], c * _MiSize, r * _MiSize, coeffShift, out var variance);

    var priStr = fh.CdefYPriStrength[idx] << coeffShift;
    var secStr = fh.CdefYSecStrength[idx] << coeffShift;
    var dir = priStr == 0 ? 0 : yDir;

    // libaom adjust_strength(): a strongly directional block tolerates more deringing.
    var varStr = (variance >> 6) != 0 ? Math.Min(_FloorLog2(variance >> 6), 12) : 0;
    priStr = variance != 0 ? (priStr * (4 + varStr) + 8) >> 4 : 0;

    _FilterPlane(frame, source, 0, r, c, priStr, secStr, fh.CdefDamping + coeffShift, dir, coeffShift);
    if (frame.NumPlanes == 1)
      return;

    // Chroma reuses the luma direction through a remap and is damped one step less; the variance
    // adjustment above is luma only.
    priStr = fh.CdefUvPriStrength[idx] << coeffShift;
    secStr = fh.CdefUvSecStrength[idx] << coeffShift;
    dir = priStr == 0 ? 0 : _CdefUvDir[(seq.SubsamplingX * 2 + seq.SubsamplingY) * 8 + yDir];
    var uvDamping = fh.CdefDamping + coeffShift - 1;

    _FilterPlane(frame, source, 1, r, c, priStr, secStr, uvDamping, dir, coeffShift);
    _FilterPlane(frame, source, 2, r, c, priStr, secStr, uvDamping, dir, coeffShift);
  }

  /// <summary>AV1 7.15.3 CDEF filter process for one plane of an 8x8 luma block.</summary>
  private static void _FilterPlane(
    Av1DecodedFrame frame, short[][] source, int plane, int r, int c,
    int priStr, int secStr, int damping, int dir, int coeffShift) {
    var subX = frame.SubX[plane];
    var subY = frame.SubY[plane];
    var stride = frame.Strides[plane];
    var src = source[plane];
    var dst = frame.Planes[plane];

    var x0 = (c * _MiSize) >> subX;
    var y0 = (r * _MiSize) >> subY;
    var w = 8 >> subX;
    var h = 8 >> subY;

    // The tap set alternates with the low bit of the strength so that the total gain stays 12.
    var priTapSet = (priStr >> coeffShift) & 1;

    // The damping shift only depends on the block, so FloorLog2 is hoisted out of constrain().
    var priDamping = priStr == 0 ? 0 : Math.Max(0, damping - _FloorLog2(priStr));
    var secDamping = secStr == 0 ? 0 : Math.Max(0, damping - _FloorLog2(secStr));

    var directions = Av1PostFilterTables.CdefDirections;
    var priTaps = Av1PostFilterTables.CdefPrimaryTaps;
    var secTaps = Av1PostFilterTables.CdefSecondaryTaps;

    for (var i = 0; i < h; ++i)
      for (var j = 0; j < w; ++j) {
        int x = src[(y0 + i) * stride + x0 + j];
        var sum = 0;
        var max = x;
        var min = x;

        for (var k = 0; k < 2; ++k)
          for (var sign = -1; sign <= 1; sign += 2) {
            var py = y0 + i + sign * directions[dir * 4 + k * 2];
            var px = x0 + j + sign * directions[dir * 4 + k * 2 + 1];
            if (_Available(frame, px, py, subX, subY)) {
              int p = src[py * stride + px];
              sum += priTaps[priTapSet * 2 + k] * _Constrain(p - x, priStr, priDamping);
              if (p > max)
                max = p;
              if (p < min)
                min = p;
            }

            // The two directions 45 degrees either side of the primary one carry the secondary taps.
            for (var dirOff = -2; dirOff <= 2; dirOff += 4) {
              var sDir = (dir + dirOff) & 7;
              var sy = y0 + i + sign * directions[sDir * 4 + k * 2];
              var sx = x0 + j + sign * directions[sDir * 4 + k * 2 + 1];
              if (!_Available(frame, sx, sy, subX, subY))
                continue;

              int s = src[sy * stride + sx];
              sum += secTaps[k] * _Constrain(s - x, secStr, secDamping);
              if (s > max)
                max = s;
              if (s < min)
                min = s;
            }
          }

        // The -1 for a negative sum rounds towards zero; the clip keeps the result between the
        // samples that produced it, so CDEF can never introduce a new extreme.
        dst[(y0 + i) * stride + x0 + j] = (short)Math.Clamp(x + ((8 + sum - (sum < 0 ? 1 : 0)) >> 4), min, max);
      }
  }

  /// <summary>
  /// AV1 7.15.2 CDEF direction process (libaom <c>cdef_find_dir_c</c>): accumulates the eight
  /// directional partial sums of the 8x8 luma block and picks the direction with the highest
  /// normalised energy. <paramref name="variance"/> receives the margin over the orthogonal
  /// direction, which drives the strength adjustment.
  /// </summary>
  private static int _FindDirection(short[] plane, int stride, int x0, int y0, int coeffShift, out int variance) {
    Span<int> cost = stackalloc int[8];
    Span<int> partial = stackalloc int[8 * 15];
    cost.Clear();
    partial.Clear();

    for (var i = 0; i < 8; ++i)
      for (var j = 0; j < 8; ++j) {
        // Centring on 0 keeps the squared partial sums in range.
        var v = (plane[(y0 + i) * stride + x0 + j] >> coeffShift) - 128;
        partial[i + j] += v;
        partial[15 + i + j / 2] += v;
        partial[30 + i] += v;
        partial[45 + 3 + i - j / 2] += v;
        partial[60 + 7 + i - j] += v;
        partial[75 + 3 - i / 2 + j] += v;
        partial[90 + j] += v;
        partial[105 + i / 2 + j] += v;
      }

    for (var i = 0; i < 8; ++i) {
      cost[2] += partial[30 + i] * partial[30 + i];
      cost[6] += partial[90 + i] * partial[90 + i];
    }

    cost[2] *= _DivTable[8];
    cost[6] *= _DivTable[8];

    for (var i = 0; i < 7; ++i) {
      cost[0] += (partial[i] * partial[i] + partial[14 - i] * partial[14 - i]) * _DivTable[i + 1];
      cost[4] += (partial[60 + i] * partial[60 + i] + partial[60 + 14 - i] * partial[60 + 14 - i]) * _DivTable[i + 1];
    }

    cost[0] += partial[7] * partial[7] * _DivTable[8];
    cost[4] += partial[60 + 7] * partial[60 + 7] * _DivTable[8];

    for (var i = 1; i < 8; i += 2) {
      var b = i * 15;
      for (var j = 0; j < 5; ++j)
        cost[i] += partial[b + 3 + j] * partial[b + 3 + j];

      cost[i] *= _DivTable[8];
      for (var j = 0; j < 3; ++j)
        cost[i] += (partial[b + j] * partial[b + j] + partial[b + 10 - j] * partial[b + 10 - j]) * _DivTable[2 * j + 2];
    }

    var bestCost = 0;
    var yDir = 0;
    for (var i = 0; i < 8; ++i)
      if (cost[i] > bestCost) {
        bestCost = cost[i];
        yDir = i;
      }

    // Dividing by 1024 instead of the 840 the table introduced is close enough for the strength
    // adjustment this feeds.
    variance = (bestCost - cost[(yDir + 4) & 7]) >> 10;
    return yDir;
  }

  /// <summary>AV1 cdef_get_at / is_inside_filter_region: samples outside the mode info grid of the
  /// frame are unavailable and take no part in the sum, the minimum or the maximum.</summary>
  private static bool _Available(Av1DecodedFrame frame, int x, int y, int subX, int subY) {
    var candidateR = (y << subY) >> Av1Constants.MiSizeLog2;
    var candidateC = (x << subX) >> Av1Constants.MiSizeLog2;
    return candidateR >= 0 && candidateR < frame.MiRows && candidateC >= 0 && candidateC < frame.MiCols;
  }

  /// <summary>AV1 7.15.3 constrain: a soft threshold that falls back to zero for large differences.</summary>
  private static int _Constrain(int diff, int threshold, int dampingAdj) {
    if (threshold == 0)
      return 0;

    var magnitude = Math.Abs(diff);
    var value = Math.Clamp(threshold - (magnitude >> dampingAdj), 0, magnitude);
    return diff < 0 ? -value : value;
  }

  /// <summary>AV1 4.7 FloorLog2, defined for positive values only.</summary>
  private static int _FloorLog2(int value) {
    var result = 0;
    while (value > 1) {
      value >>= 1;
      ++result;
    }

    return result;
  }
}
