using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Per-restoration-unit parameters read from the tile data (AV1 5.11.57 read_lr_unit).
/// </summary>
/// <remarks>
/// The tile decoder writes one unit at a time: every array is a flat per-plane buffer sized for
/// <c>UnitRows[plane] * UnitCols[plane]</c> units and addressed by
/// <c>unitRow * UnitCols[plane] + unitCol</c> (see <see cref="Index"/>). The unit counts differ
/// between luma and chroma, which is why they are stored per plane rather than derived once.
/// </remarks>
internal sealed class Av1LoopRestorationUnits {

  /// <summary>Allocates empty (RESTORE_NONE) unit grids for every plane.</summary>
  /// <param name="planes">Number of planes in the frame.</param>
  /// <param name="unitCols">Restoration units across the frame, one entry per plane.</param>
  /// <param name="unitRows">Restoration units down the frame, one entry per plane.</param>
  public Av1LoopRestorationUnits(int planes, int[] unitCols, int[] unitRows) {
    this.UnitCols = new int[planes];
    this.UnitRows = new int[planes];
    this.Types = new byte[planes][];
    this.WienerVertical = new short[planes][];
    this.WienerHorizontal = new short[planes][];
    this.SgrSet = new byte[planes][];
    this.SgrXqd = new short[planes][];

    for (var plane = 0; plane < planes; ++plane) {
      var cols = Math.Max(1, unitCols[plane]);
      var rows = Math.Max(1, unitRows[plane]);
      this.UnitCols[plane] = cols;
      this.UnitRows[plane] = rows;

      var count = cols * rows;
      this.Types[plane] = new byte[count];
      this.WienerVertical[plane] = new short[count * Av1Constants.WienerCoeffs];
      this.WienerHorizontal[plane] = new short[count * Av1Constants.WienerCoeffs];
      this.SgrSet[plane] = new byte[count];
      this.SgrXqd[plane] = new short[count * 2];
    }
  }

  /// <summary>Restoration units across the frame, per plane.</summary>
  public int[] UnitCols { get; }

  /// <summary>Restoration units down the frame, per plane.</summary>
  public int[] UnitRows { get; }

  /// <summary>Restoration type of each unit as an <see cref="Av1RestorationType"/>; only None,
  /// Wiener and SgrProj occur per unit, Switchable is a frame level choice.</summary>
  public byte[][] Types { get; }

  /// <summary>The three coded vertical Wiener taps of each unit, three entries per unit.</summary>
  public short[][] WienerVertical { get; }

  /// <summary>The three coded horizontal Wiener taps of each unit, three entries per unit.</summary>
  public short[][] WienerHorizontal { get; }

  /// <summary><c>lr_sgr_set</c> of each unit, the index into Sgr_Params.</summary>
  public byte[][] SgrSet { get; }

  /// <summary><c>lr_sgr_xqd</c> of each unit, two entries per unit.</summary>
  public short[][] SgrXqd { get; }

  /// <summary>Index of one unit inside the flat per-plane arrays.</summary>
  public int Index(int plane, int unitRow, int unitCol) => unitRow * this.UnitCols[plane] + unitCol;
}

/// <summary>
/// AV1 loop restoration (specification 7.17), ported from libaom's
/// <c>av1_loop_restoration_filter_frame</c>, <c>wiener_filter_stripe</c> and
/// <c>av1_selfguided_restoration_c</c> in <c>av1/common/restoration.c</c>. libaom implements the
/// stripe boundary rule by temporarily swapping two rows of the frame around every 64 sample
/// stripe; the specification states it as a source selection per fetched sample, which is what is
/// done here.
/// </summary>
internal static class Av1LoopRestoration {

  private const int _MiSize = 4;
  private const int _FilterBits = 7;
  private const int _SgrRstBits = 4;
  private const int _SgrPrjBits = Av1Constants.SgrProjPrecBits;
  private const int _SgrBits = 8;
  private const int _SgrMTableBits = 20;
  private const int _SgrRecipBits = 12;

  /// <summary>AV1 7.17: loop restoration. <paramref name="deblocked"/> holds the frame as it was
  /// before CDEF, which the stripe boundary handling needs.</summary>
  /// <param name="frame">Frame holding the CDEF output; its plane buffers receive the result.</param>
  /// <param name="deblocked">Plane buffers as they were after deblocking and before CDEF, with the
  /// same strides as <paramref name="frame"/>.</param>
  /// <param name="seq">Sequence header the frame was decoded with.</param>
  /// <param name="fh">Frame header carrying the frame level restoration type and unit size.</param>
  /// <param name="units">Per-unit parameters parsed from the tile data.</param>
  public static void Apply(
    Av1DecodedFrame frame, short[][] deblocked, Av1SequenceHeader seq, Av1FrameHeader fh, Av1LoopRestorationUnits units) {
    if (units == null || !seq.EnableRestoration)
      return;

    // UsesLr (AV1 5.9.20): nothing to do when no plane signalled a restoration type.
    var usesLr = false;
    for (var plane = 0; plane < frame.NumPlanes && !usesLr; ++plane)
      usesLr = (Av1RestorationType)fh.LrType[plane] != Av1RestorationType.None;
    if (!usesLr)
      return;

    // 7.17: LrFrame starts as a copy of the CDEF output. Filtering reads that snapshot - and the
    // deblocked frame for rows outside the current stripe - while writing in place, so the source
    // has to be captured before the first block is overwritten.
    var cdef = new short[frame.NumPlanes][];
    for (var plane = 0; plane < frame.NumPlanes; ++plane)
      cdef[plane] = (short[])frame.Planes[plane].Clone();

    // The specification loops blocks first and planes innermost. Swapping the order is safe because
    // every block reads only the two snapshots and writes only its own samples.
    for (var plane = 0; plane < frame.NumPlanes; ++plane) {
      if ((Av1RestorationType)fh.LrType[plane] == Av1RestorationType.None)
        continue;

      _RestorePlane(frame, cdef[plane], deblocked[plane], fh, units, plane);
    }
  }

  /// <summary>AV1 7.17.1 loop restore block process, run over every 4x4 block of one plane.</summary>
  private static void _RestorePlane(
    Av1DecodedFrame frame, short[] cdef, short[] deblocked, Av1FrameHeader fh, Av1LoopRestorationUnits units, int plane) {
    var subX = frame.SubX[plane];
    var subY = frame.SubY[plane];
    var stride = frame.Strides[plane];
    var dst = frame.Planes[plane];

    // LoopRestorationSize (AV1 5.9.20), the same value the tile decoder placed the units with;
    // it equals RESTORATION_TILESIZE_MAX >> (2 - LrUnitShift[plane]) for every plane.
    var unitSize = fh.LoopRestorationSize[plane];
    var unitCols = units.UnitCols[plane];
    var unitRows = units.UnitRows[plane];

    // Super resolution is not applied by this decoder, so the upscaled width is the frame width.
    var planeEndX = ((frame.Width + subX) >> subX) - 1;
    var planeEndY = ((frame.Height + subY) >> subY) - 1;
    var maxSample = (1 << frame.BitDepth) - 1;

    for (var lumaY = 0; lumaY < frame.Height; lumaY += _MiSize) {
      // Stripes are 64 luma samples high but offset upwards by 8, so a superblock row produces
      // exactly the deblocked rows the next stripe needs.
      var stripeNum = (lumaY + 8) / 64;
      var stripeStartY = (-8 + stripeNum * 64) >> subY;
      var stripeEndY = stripeStartY + (64 >> subY) - 1;
      var source = new _Source(cdef, deblocked, stride, planeEndX, planeEndY, stripeStartY, stripeEndY);

      var y = lumaY >> subY;
      var h = Math.Min(_MiSize >> subY, planeEndY - y + 1);
      var unitRow = Math.Min(unitRows - 1, ((lumaY + 8) >> subY) / unitSize);

      for (var lumaX = 0; lumaX < frame.Width; lumaX += _MiSize) {
        var x = lumaX >> subX;
        var w = Math.Min(_MiSize >> subX, planeEndX - x + 1);
        var unitCol = Math.Min(unitCols - 1, (lumaX >> subX) / unitSize);
        var unit = unitRow * unitCols + unitCol;

        switch ((Av1RestorationType)units.Types[plane][unit]) {
          case Av1RestorationType.Wiener:
            _WienerFilter(source, dst, stride, units, plane, unit, x, y, w, h, frame.BitDepth, maxSample);
            break;
          case Av1RestorationType.SgrProj:
            _SelfGuidedFilter(source, cdef, dst, stride, units, plane, unit, x, y, w, h, frame.BitDepth, maxSample);
            break;
          default:
            break;
        }
      }
    }
  }

  /// <summary>AV1 7.17.4 Wiener filter process: a separable symmetric 7 tap convolution with unit
  /// DC gain (libaom <c>av1_wiener_convolve_add_src_c</c>, which folds the DC tap into the source
  /// instead of coding it).</summary>
  private static void _WienerFilter(
    in _Source source, short[] dst, int stride, Av1LoopRestorationUnits units, int plane, int unit,
    int x, int y, int w, int h, int bitDepth, int maxSample) {
    Span<int> vfilter = stackalloc int[7];
    Span<int> hfilter = stackalloc int[7];
    _WienerCoefficients(units.WienerVertical[plane], unit * Av1Constants.WienerCoeffs, vfilter);
    _WienerCoefficients(units.WienerHorizontal[plane], unit * Av1Constants.WienerCoeffs, hfilter);

    // Rounding variables (AV1 7.11.3.2 with isCompound 0), chosen so the horizontal result fits in
    // 16 bits at every bit depth.
    var round0 = bitDepth == 12 ? 5 : 3;
    var round1 = bitDepth == 12 ? 9 : 11;
    var offset = 1 << (bitDepth + _FilterBits - round0 - 1);
    var limit = (1 << (bitDepth + 1 + _FilterBits - round0)) - 1;

    // The horizontal pass runs three rows above and below the block because the vertical pass needs
    // them; the coded coefficients arrive vertical first but are applied horizontal first.
    Span<int> intermediate = stackalloc int[(_MiSize + 6) * _MiSize];
    for (var r = 0; r < h + 6; ++r)
      for (var c = 0; c < w; ++c) {
        var s = 0;
        for (var t = 0; t < 7; ++t)
          s += hfilter[t] * source.Sample(x + c + t - 3, y + r - 3);

        intermediate[r * w + c] = Math.Clamp(_Round2(s, round0), -offset, limit - offset);
      }

    for (var r = 0; r < h; ++r)
      for (var c = 0; c < w; ++c) {
        var s = 0;
        for (var t = 0; t < 7; ++t)
          s += vfilter[t] * intermediate[(r + t) * w + c];

        dst[(y + r) * stride + x + c] = (short)Math.Clamp(_Round2(s, round1), 0, maxSample);
      }
  }

  /// <summary>AV1 7.17.5 Wiener coefficient process: the filter is symmetric with unit DC gain, so
  /// only three of the seven taps are coded.</summary>
  private static void _WienerCoefficients(short[] coeffs, int offset, Span<int> filter) {
    filter[3] = 128;
    for (var i = 0; i < Av1Constants.WienerCoeffs; ++i) {
      int c = coeffs[offset + i];
      filter[i] = c;
      filter[6 - i] = c;
      filter[3] -= 2 * c;
    }
  }

  /// <summary>AV1 7.17.2 self guided filter process: blends the sample with two box filtered
  /// versions of its neighbourhood using the coded projection weights.</summary>
  private static void _SelfGuidedFilter(
    in _Source source, short[] cdef, short[] dst, int stride, Av1LoopRestorationUnits units, int plane, int unit,
    int x, int y, int w, int h, int bitDepth, int maxSample) {
    var set = units.SgrSet[plane][unit];
    int w0 = units.SgrXqd[plane][unit * 2];
    int w1 = units.SgrXqd[plane][unit * 2 + 1];
    var w2 = (1 << _SgrPrjBits) - w0 - w1;

    // Sgr_Params is stored in libaom's layout: r0, r1, then the two precomputed 1/(n*n*eps) scales
    // that the specification derives from the coded eps values (see _BoxFilter).
    var r0 = Av1PostFilterTables.SgrParams[set * 4];
    var r1 = Av1PostFilterTables.SgrParams[set * 4 + 1];

    Span<int> flt0 = stackalloc int[_MiSize * _MiSize];
    Span<int> flt1 = stackalloc int[_MiSize * _MiSize];
    if (r0 != 0)
      _BoxFilter(source, cdef, stride, x, y, w, h, set, 0, bitDepth, flt0);
    if (r1 != 0)
      _BoxFilter(source, cdef, stride, x, y, w, h, set, 1, bitDepth, flt1);

    for (var i = 0; i < h; ++i)
      for (var j = 0; j < w; ++j) {
        var u = cdef[(y + i) * stride + x + j] << _SgrRstBits;
        var v = w1 * u;
        v += w0 * (r0 != 0 ? flt0[i * w + j] : u);
        v += w2 * (r1 != 0 ? flt1[i * w + j] : u);
        dst[(y + i) * stride + x + j] = (short)Math.Clamp(_Round2(v, _SgrRstBits + _SgrPrjBits), 0, maxSample);
      }
  }

  /// <summary>
  /// AV1 7.17.3 box filter process (libaom <c>calculate_intermediate_result</c> plus
  /// <c>selfguided_restoration_fast_internal</c> / <c>selfguided_restoration_internal</c>).
  /// Pass 0 uses a 5x5 box and only odd rows of the intermediate arrays; pass 1 uses a 3x3 box and
  /// all rows.
  /// </summary>
  private static void _BoxFilter(
    in _Source source, short[] cdef, int stride, int x, int y, int w, int h,
    int set, int pass, int bitDepth, Span<int> output) {
    var r = Av1PostFilterTables.SgrParams[set * 4 + pass];
    if (r == 0)
      return;

    // The specification computes s = ((1 << SGRPROJ_MTABLE_BITS) + n2e / 2) / n2e from the coded
    // eps; libaom - and therefore Av1PostFilterTables.SgrParams - stores the result of that
    // division directly, so the values look nothing like the eps column in 7.17.3.
    var s = Av1PostFilterTables.SgrParams[set * 4 + 2 + pass];
    var n = (2 * r + 1) * (2 * r + 1);
    var oneOverN = Av1PostFilterTables.OneByX[n - 1];
    var bdShift = bitDepth - 8;

    // A and B carry one extra sample of border on every side.
    var pitch = _MiSize + 2;
    Span<int> aArr = stackalloc int[pitch * pitch];
    Span<int> bArr = stackalloc int[pitch * pitch];

    for (var i = -1; i < h + 1; ++i)
      for (var j = -1; j < w + 1; ++j) {
        var sumSq = 0;
        var sum = 0;
        for (var dy = -r; dy <= r; ++dy)
          for (var dx = -r; dx <= r; ++dx) {
            var c = source.Sample(x + j + dx, y + i + dy);
            sumSq += c * c;
            sum += c;
          }

        var a = _Round2(sumSq, 2 * bdShift);
        var d = _Round2(sum, bdShift);

        // Popoviciu bounds p below 2^26, but p * s only fits in 32 bits unsigned, so widen.
        var p = Math.Max(0, (long)a * n - (long)d * d);
        var z = (int)_Round2(p * s, _SgrMTableBits);

        // Saturating z == 0 to 1 rather than 0 keeps (256 - a2) inside 8 bits.
        var a2 = Av1PostFilterTables.XByXPlus1[Math.Min(z, 255)];
        var b2 = (long)((1 << _SgrBits) - a2) * sum * oneOverN;

        var k = (i + 1) * pitch + (j + 1);
        aArr[k] = a2;
        bArr[k] = (int)_Round2(b2, _SgrRecipBits);
      }

    for (var i = 0; i < h; ++i) {
      // Pass 0 leaves the even rows of A and B unused, so its odd rows carry a smaller weight sum.
      var shift = pass == 0 && (i & 1) != 0 ? 4 : 5;
      for (var j = 0; j < w; ++j) {
        var a = 0;
        var b = 0;
        for (var dy = -1; dy <= 1; ++dy)
          for (var dx = -1; dx <= 1; ++dx) {
            int weight;
            if (pass == 0)
              weight = ((i + dy) & 1) != 0 ? dx == 0 ? 6 : 5 : 0;
            else
              weight = dx == 0 || dy == 0 ? 4 : 3;

            var k = (i + dy + 1) * pitch + (j + dx + 1);
            a += weight * aArr[k];
            b += weight * bArr[k];
          }

        var v = a * cdef[(y + i) * stride + x + j] + b;
        output[i * w + j] = _Round2(v, _SgrBits + shift - _SgrRstBits);
      }
    }
  }

  /// <summary>AV1 4.7 Round2.</summary>
  private static int _Round2(int value, int n) => n == 0 ? value : (value + (1 << (n - 1))) >> n;

  /// <summary>AV1 4.7 Round2 for intermediates that overflow 32 bits.</summary>
  private static long _Round2(long value, int n) => n == 0 ? value : (value + (1L << (n - 1))) >> n;

  /// <summary>
  /// AV1 7.17.6 get source sample process. Samples inside the current stripe come from the CDEF
  /// output, samples outside it from the deblocked frame, and a fetch is never allowed more than
  /// two rows past the stripe so that no extra line buffers are needed.
  /// </summary>
  private readonly struct _Source(
    short[] cdef, short[] deblocked, int stride, int endX, int endY, int stripeStartY, int stripeEndY) {

    public int Sample(int sampleX, int sampleY) {
      sampleX = Math.Clamp(sampleX, 0, endX);
      sampleY = Math.Clamp(sampleY, 0, endY);
      if (sampleY < stripeStartY)
        return deblocked[Math.Max(stripeStartY - 2, sampleY) * stride + sampleX];
      if (sampleY > stripeEndY)
        return deblocked[Math.Min(stripeEndY + 2, sampleY) * stride + sampleX];

      return cdef[sampleY * stride + sampleX];
    }
  }
}
