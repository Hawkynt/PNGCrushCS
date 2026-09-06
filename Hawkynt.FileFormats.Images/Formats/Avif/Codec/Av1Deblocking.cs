using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// AV1 deblocking filter (specification 7.14). The edge selection follows libaom's
/// <c>set_lpf_parameters</c> / <c>get_filter_level</c> in <c>av1/common/av1_loopfilter.c</c> and the
/// sample filters are ports of <c>filter4</c>, <c>filter6</c>, <c>filter8</c> and <c>filter14</c>
/// from <c>aom_dsp/loopfilter.c</c>. The structure follows the specification instead of libaom's
/// frame-level driver: libaom precomputes per-segment level tables, which buys nothing for a single
/// still picture, and its wide filters are unrolled per size where the specification states one
/// parameterised low-pass loop that produces identical taps.
/// </summary>
internal static class Av1Deblocking {

  private const int _MiSize = 4;
  private const int _MaxLoopFilter = 63;

  /// <summary>AV1 7.14: deblocks the frame in place.</summary>
  /// <param name="frame">Reconstructed frame; its plane buffers are modified.</param>
  /// <param name="seq">Sequence header the frame was decoded with.</param>
  /// <param name="fh">Frame header carrying the loop filter levels, sharpness and deltas.</param>
  public static void Apply(Av1DecodedFrame frame, Av1SequenceHeader seq, Av1FrameHeader fh) {
    // AV1 7.20: the loop filter is only invoked when one of the two luma levels is non-zero. The
    // test cannot be folded into the per-edge level derivation, because a positive intra reference
    // delta would otherwise lift a zero level to 1 and filter a frame that must stay untouched.
    if (fh.LoopFilterLevel[0] == 0 && fh.LoopFilterLevel[1] == 0)
      return;

    for (var plane = 0; plane < frame.NumPlanes; ++plane) {
      if (plane > 0 && fh.LoopFilterLevel[1 + plane] == 0)
        continue;

      var subX = frame.SubX[plane];
      var subY = frame.SubY[plane];
      var rowStep = plane == 0 ? 1 : 1 << subY;
      var colStep = plane == 0 ? 1 : 1 << subX;

      // All vertical boundaries of a plane are filtered before any of its horizontal ones; within a
      // pass the order is irrelevant (7.14.1 note).
      for (var pass = 0; pass < 2; ++pass)
        for (var row = 0; row < frame.MiRows; row += rowStep)
          for (var col = 0; col < frame.MiCols; col += colStep)
            _FilterEdge(frame, fh, plane, pass, row, col, subX, subY);
    }
  }

  /// <summary>AV1 7.14.2 edge loop filter process for one 4x4 edge.</summary>
  private static void _FilterEdge(
    Av1DecodedFrame frame, Av1FrameHeader fh, int plane, int pass, int row, int col, int subX, int subY) {
    // (dx,dy) is the direction perpendicular to the boundary, (dy,dx) runs along it.
    var dx = pass == 0 ? 1 : 0;
    var dy = pass == 1 ? 1 : 0;

    var x = col * _MiSize;
    var y = row * _MiSize;

    // onScreen: both sides of the boundary must lie inside the visible frame, and the first
    // row/column of the frame has nothing on the far side.
    if (x >= frame.Width || y >= frame.Height)
      return;
    if (pass == 0 && x == 0)
      return;
    if (pass == 1 && y == 0)
      return;

    // Chroma of a sub-8x8 luma block is coded at the bottom/right mode info of the 8x8 region, so
    // the mode info lookups move there before reading.
    row |= subY;
    col |= subX;

    var xP = x >> subX;
    var yP = y >> subY;
    var prevRow = row - (dy << subY);
    var prevCol = col - (dx << subX);

    // LoopfilterTxSizes is addressed in plane resolution (spec: [ row >> subY ][ col >> subX ]),
    // unlike the mode info arrays above; the tile decoder writes it with the same convention, using
    // the luma MiCols as the row stride.
    var txSz = frame.TxSizes[plane][(row >> subY) * frame.MiCols + (col >> subX)];
    var prevTxSz = frame.TxSizes[plane][(prevRow >> subY) * frame.MiCols + (prevCol >> subX)];

    var isTxEdge = pass == 0
      ? xP % Av1StructureTables.TxWidth[txSz] == 0
      : yP % Av1StructureTables.TxHeight[txSz] == 0;
    if (!isTxEdge)
      return;

    // applyFilter (7.14.2) is isTxEdge && ( isBlockEdge || !skip || isIntra ). Every block reaching
    // this decoder is intra, so isIntra is 1 and a transform edge always filters. libaom instead
    // suppresses the edge only when *both* sides are skipped inter blocks, which is the same thing
    // for intra frames and differs only for inter ones.
    var filterSize = _FilterSize(txSz, prevTxSz, pass, plane);

    var lvl = _FilterStrength(frame, fh, row, col, plane, pass);
    if (lvl == 0)
      lvl = _FilterStrength(frame, fh, prevRow, prevCol, plane, pass);
    if (lvl == 0)
      return;

    // 7.14.4: sharpness both scales the level down and caps it (libaom update_sharpness).
    var sharpness = fh.LoopFilterSharpness;
    var shift = sharpness > 4 ? 2 : sharpness > 0 ? 1 : 0;
    var limit = sharpness > 0
      ? Math.Clamp(lvl >> shift, 1, 9 - sharpness)
      : Math.Max(1, lvl >> shift);
    var blimit = 2 * (lvl + 2) + limit;
    var thresh = lvl >> 4;

    var buffer = frame.Planes[plane];
    var stride = frame.Strides[plane];
    for (var i = 0; i < _MiSize; ++i)
      _SampleFilter(
        buffer, stride, xP + dy * i, yP + dx * i, dx, dy,
        frame.BitDepth, limit, blimit, thresh, filterSize, plane);
  }

  /// <summary>AV1 7.14.3 filter size process: the widest filter both sides can support.</summary>
  private static int _FilterSize(int txSz, int prevTxSz, int pass, int plane) {
    var baseSize = pass == 0
      ? Math.Min(Av1StructureTables.TxWidth[prevTxSz], Av1StructureTables.TxWidth[txSz])
      : Math.Min(Av1StructureTables.TxHeight[prevTxSz], Av1StructureTables.TxHeight[txSz]);
    return Math.Min(plane == 0 ? 16 : 8, baseSize);
  }

  /// <summary>AV1 7.14.4/7.14.5 adaptive filter strength for the block at the given position.</summary>
  private static int _FilterStrength(
    Av1DecodedFrame frame, Av1FrameHeader fh, int row, int col, int plane, int pass) {
    var i = plane == 0 ? pass : plane + 1;
    var mi = row * frame.MiCols + col;
    var deltaLf = fh.DeltaLfMulti ? frame.DeltaLf[mi * 4 + i] : frame.DeltaLf[mi * 4];
    var lvl = Math.Clamp(deltaLf + fh.LoopFilterLevel[i], 0, _MaxLoopFilter);

    // Step 3 (SEG_LVL_ALT_LF_* feature data) is skipped: this decoder never parses segment feature
    // data, so seg_feature_active_idx is always 0.

    if (fh.LoopFilterDeltaEnabled) {
      // Only intra frames occur here, so ref is INTRA_FRAME (delta index 0) and the mode delta
      // never applies (mode delta index 0 is reserved for intra/GLOBALMV in 7.14.5 step 4b).
      var nShift = lvl >> 5;
      lvl = Math.Clamp(lvl + (fh.LoopFilterRefDeltas[0] << nShift), 0, _MaxLoopFilter);
    }

    return lvl;
  }

  /// <summary>AV1 7.14.6.1/7.14.6.2: builds the masks and dispatches to the narrow or wide filter.</summary>
  private static void _SampleFilter(
    short[] buffer, int stride, int x, int y, int dx, int dy,
    int bitDepth, int limit, int blimit, int thresh, int filterSize, int plane) {
    var pos = y * stride + x;
    var step = dy * stride + dx;
    var bdShift = bitDepth - 8;

    // p3..p0 and q0..q3 always exist: the boundary is a transform edge, so at least four samples of
    // the neighbouring transform block sit on either side.
    int q0 = buffer[pos];
    int q1 = buffer[pos + step];
    int q2 = buffer[pos + 2 * step];
    int q3 = buffer[pos + 3 * step];
    int p0 = buffer[pos - step];
    int p1 = buffer[pos - 2 * step];
    int p2 = buffer[pos - 3 * step];
    int p3 = buffer[pos - 4 * step];

    var threshBd = thresh << bdShift;
    var hevMask = Math.Abs(p1 - p0) > threshBd || Math.Abs(q1 - q0) > threshBd;

    var filterLen = filterSize == 4 ? 4 : plane != 0 ? 6 : filterSize == 8 ? 8 : 16;
    var limitBd = limit << bdShift;
    var blimitBd = blimit << bdShift;

    var reject = Math.Abs(p1 - p0) > limitBd
      || Math.Abs(q1 - q0) > limitBd
      || Math.Abs(p0 - q0) * 2 + Math.Abs(p1 - q1) / 2 > blimitBd;
    if (filterLen >= 6)
      reject |= Math.Abs(p2 - p1) > limitBd || Math.Abs(q2 - q1) > limitBd;
    if (filterLen >= 8)
      reject |= Math.Abs(p3 - p2) > limitBd || Math.Abs(q3 - q2) > limitBd;
    if (reject)
      return;

    if (filterSize == 4) {
      _NarrowFilter(buffer, pos, step, bitDepth, hevMask);
      return;
    }

    var flatBd = 1 << bdShift;
    var flatMask = Math.Abs(p1 - p0) <= flatBd
      && Math.Abs(q1 - q0) <= flatBd
      && Math.Abs(p2 - p0) <= flatBd
      && Math.Abs(q2 - q0) <= flatBd
      && (filterLen < 8 || (Math.Abs(p3 - p0) <= flatBd && Math.Abs(q3 - q0) <= flatBd));
    if (!flatMask) {
      _NarrowFilter(buffer, pos, step, bitDepth, hevMask);
      return;
    }

    if (filterSize == 8) {
      _WideFilter(buffer, pos, step, 3, plane);
      return;
    }

    // filterSize 16 is luma only, so the outer six samples on each side are inside the two
    // transform blocks that meet at this edge.
    int q4 = buffer[pos + 4 * step];
    int q5 = buffer[pos + 5 * step];
    int q6 = buffer[pos + 6 * step];
    int p4 = buffer[pos - 5 * step];
    int p5 = buffer[pos - 6 * step];
    int p6 = buffer[pos - 7 * step];
    var flatMask2 = Math.Abs(p6 - p0) <= flatBd
      && Math.Abs(q6 - q0) <= flatBd
      && Math.Abs(p5 - p0) <= flatBd
      && Math.Abs(q5 - q0) <= flatBd
      && Math.Abs(p4 - p0) <= flatBd
      && Math.Abs(q4 - q0) <= flatBd;
    _WideFilter(buffer, pos, step, flatMask2 ? 4 : 3, plane);
  }

  /// <summary>AV1 7.14.6.3 narrow filter (libaom <c>filter4</c>).</summary>
  private static void _NarrowFilter(short[] buffer, int pos, int step, int bitDepth, bool hevMask) {
    // The samples are biased to a signed range so that the taps work on differences.
    var half = 0x80 << (bitDepth - 8);
    var lo = -(1 << (bitDepth - 1));
    var hi = (1 << (bitDepth - 1)) - 1;

    var q0 = buffer[pos] - half;
    var q1 = buffer[pos + step] - half;
    var p0 = buffer[pos - step] - half;
    var p1 = buffer[pos - 2 * step] - half;

    var filter = hevMask ? Math.Clamp(p1 - q1, lo, hi) : 0;
    filter = Math.Clamp(filter + 3 * (q0 - p0), lo, hi);

    // The bottom three bits are kept so that one side rounds by +4 and the other by +3.
    var filter1 = Math.Clamp(filter + 4, lo, hi) >> 3;
    var filter2 = Math.Clamp(filter + 3, lo, hi) >> 3;

    buffer[pos] = (short)(Math.Clamp(q0 - filter1, lo, hi) + half);
    buffer[pos - step] = (short)(Math.Clamp(p0 + filter2, lo, hi) + half);
    if (hevMask)
      return;

    // Without high edge variance the outer pair is nudged as well.
    var outer = _Round2(filter1, 1);
    buffer[pos + step] = (short)(Math.Clamp(q1 - outer, lo, hi) + half);
    buffer[pos - 2 * step] = (short)(Math.Clamp(p1 + outer, lo, hi) + half);
  }

  /// <summary>
  /// AV1 7.14.6.4 wide filter. One loop covers libaom's <c>filter6</c> (chroma, 5 taps),
  /// <c>filter8</c> (luma, 7 taps) and <c>filter14</c> (luma, 13 taps): n taps each side, the
  /// innermost 2*n2+1 of them doubled for unit DC gain, with the input clamped to the outermost
  /// available sample so that the edge taps repeat it.
  /// </summary>
  private static void _WideFilter(short[] buffer, int pos, int step, int log2Size, int plane) {
    var n = log2Size == 4 ? 6 : plane == 0 ? 3 : 2;
    var n2 = log2Size == 3 && plane == 0 ? 0 : 1;

    Span<int> filtered = stackalloc int[12];
    for (var i = -n; i < n; ++i) {
      var t = 0;
      for (var j = -n; j <= n; ++j) {
        var p = Math.Clamp(i + j, -(n + 1), n);
        var tap = Math.Abs(j) <= n2 ? 2 : 1;
        t += buffer[pos + p * step] * tap;
      }

      filtered[i + n] = _Round2(t, log2Size);
    }

    for (var i = -n; i < n; ++i)
      buffer[pos + i * step] = (short)filtered[i + n];
  }

  /// <summary>AV1 4.7 Round2.</summary>
  private static int _Round2(int value, int n) => n == 0 ? value : (value + (1 << (n - 1))) >> n;
}
