// AV1 intra prediction, ported from the libaom reference implementation (BSD 2-Clause,
// Alliance for Open Media). See THIRD_PARTY_NOTICES.md. The ported functions are
// av1/common/reconintra.c (build_directional_and_filter_intra_predictors,
// build_non_directional_intra_predictors, av1_dr_prediction_z1/z2/z3,
// av1_filter_intra_predictor, intra_edge_filter_strength, av1_filter_intra_edge,
// filter_intra_edge_corner, av1_upsample_intra_edge, dr_predictor),
// aom_dsp/intrapred.c (the dc/v/h/paeth/smooth family) and av1/common/cfl.c
// (cfl_luma_subsampling_*, cfl_pad, subtract_average, cfl_predict).
//
// The arithmetic is written bit-depth generic: libaom's 8-bit and high-bit-depth paths
// differ only in the "no neighbours" fill value (128 << (bitDepth - 8)) and in the
// rectangular DC divide constants, both of which are parameterised here.

using System;

namespace FileFormat.Avif.Codec;

/// <summary>AV1 intra prediction (specification section 7.11.2) and chroma-from-luma
/// (section 7.11.5). Predictions are written directly into the plane buffer, reading their
/// neighbours from the row above and the column to the left, exactly as libaom does.</summary>
internal static class Av1IntraPrediction {

  // libaom NEED_* flags of extend_modes[].
  private const int _NeedLeft = 1 << 1;
  private const int _NeedAbove = 1 << 2;
  private const int _NeedAboveRight = 1 << 3;
  private const int _NeedAboveLeft = 1 << 4;
  private const int _NeedBottomLeft = 1 << 5;

  /// <summary>libaom <c>extend_modes[INTRA_MODES]</c>: which neighbours each mode reads.</summary>
  private static readonly byte[] _ExtendModes = [
    _NeedAbove | _NeedLeft,                    // DC
    _NeedAbove,                                // V
    _NeedLeft,                                 // H
    _NeedAbove | _NeedAboveRight,              // D45
    _NeedLeft | _NeedAbove | _NeedAboveLeft,   // D135
    _NeedLeft | _NeedAbove | _NeedAboveLeft,   // D113
    _NeedLeft | _NeedAbove | _NeedAboveLeft,   // D157
    _NeedLeft | _NeedBottomLeft,               // D203
    _NeedAbove | _NeedAboveRight,              // D67
    _NeedLeft | _NeedAbove,                    // SMOOTH
    _NeedLeft | _NeedAbove,                    // SMOOTH_V
    _NeedLeft | _NeedAbove,                    // SMOOTH_H
    _NeedLeft | _NeedAbove | _NeedAboveLeft,   // PAETH
  ];

  // libaom NUM_INTRA_NEIGHBOUR_PIXELS = MAX_TX_SIZE * 2 + 32, with the sample at offset 0
  // placed 16 entries in so that the corner and the upsampled half-samples (p[-1], p[-2])
  // have room in front of it.
  private const int _NeighbourCount = 160;
  private const int _NeighbourOffset = 16;

  private const int _FilterIntraScaleBits = 4;
  private const int _SmoothWeightLog2Scale = 8;
  private const int _MaxUpsampleSize = 16;

  /// <summary>libaom ANGLE_STEP: the degrees one signalled angle-delta step is worth.</summary>
  private const int _AngleStep = 3;

  // Multiply-shift constants of aom_dsp/intrapred.c's dc_predictor_rect(); the high-bit-depth
  // variants use one more shift because the sums are wider.
  private const int _DcMultiplier1x2 = 0x5556;
  private const int _DcMultiplier1x4 = 0x3334;
  private const int _DcShift2 = 16;
  private const int _HighbdDcMultiplier1x2 = 0xAAAB;
  private const int _HighbdDcMultiplier1x4 = 0x6667;
  private const int _HighbdDcShift2 = 17;

  /// <summary>AV1 7.11.2 / libaom <c>av1_predict_intra_block</c>: predicts one transform block in
  /// place, reading its neighbours from the same plane.</summary>
  /// <param name="plane">Whole plane buffer of one colour component. It is allocated wider and
  /// taller than the visible frame, so reads past the block never leave the array; the neighbour
  /// counts alone decide how much of what is read is real.</param>
  /// <param name="stride">Samples per row of <paramref name="plane"/>.</param>
  /// <param name="x">Column of the block's top-left sample.</param>
  /// <param name="y">Row of the block's top-left sample.</param>
  /// <param name="txSize">Transform size; its dimensions come from
  /// <see cref="Av1StructureTables.TxWidth"/> and <see cref="Av1StructureTables.TxHeight"/>.</param>
  /// <param name="mode">Intra prediction mode; <see cref="Av1PredictionMode.UvCflPred"/> is not a
  /// prediction of its own and must be issued as DC_PRED plus <see cref="PredictCfl"/>.</param>
  /// <param name="angleDelta">Signalled angle delta index in [-3, +3]; scaled by 3 degrees here,
  /// matching libaom's <c>angle_delta * ANGLE_STEP</c>.</param>
  /// <param name="topPixels">libaom <c>n_top_px</c>: usable samples on the row above, already
  /// clamped to the frame edge, 0 when the row above is unavailable.</param>
  /// <param name="topRightPixels">libaom <c>n_topright_px</c>: further usable samples past the
  /// block's right edge on the row above, 0 when there are none.</param>
  /// <param name="leftPixels">libaom <c>n_left_px</c>: usable samples in the column to the left,
  /// 0 when that column is unavailable.</param>
  /// <param name="bottomLeftPixels">libaom <c>n_bottomleft_px</c>: further usable samples below
  /// the block in the column to the left, 0 when there are none.</param>
  /// <param name="useFilterIntra">Whether the recursive filter-intra predictor is signalled.</param>
  /// <param name="filterIntraMode">Filter-intra mode, used only when
  /// <paramref name="useFilterIntra"/> is set.</param>
  /// <param name="enableIntraEdgeFilter">Sequence-header flag enabling edge filtering and
  /// upsampling of the directional reference.</param>
  /// <param name="bitDepth">8, 10 or 12.</param>
  /// <param name="intraEdgeFilterType">libaom's <c>intra_edge_filter_type</c>, its second edge
  /// filter tuning: <c>get_intra_edge_filter_type</c> returns 1 when the above or the left
  /// neighbouring block of this plane predicts with a SMOOTH mode, otherwise 0. It depends on
  /// neighbour mode info this API does not carry, so the caller supplies it; the default 0 is
  /// libaom's behaviour for a block with no smooth neighbour.</param>
  public static void Predict(
    short[] plane, int stride,
    int x, int y, Av1TxSize txSize,
    Av1PredictionMode mode, int angleDelta,
    int topPixels, int topRightPixels, int leftPixels, int bottomLeftPixels,
    bool useFilterIntra, Av1FilterIntraMode filterIntraMode,
    bool enableIntraEdgeFilter, int bitDepth, int intraEdgeFilterType = 0) {
    ArgumentNullException.ThrowIfNull(plane);
    if (bitDepth is not (8 or 10 or 12))
      throw new NotSupportedException($"AV1 intra prediction supports 8, 10 and 12 bits, not {bitDepth}.");
    if ((uint)txSize >= (uint)Av1StructureTables.TxWidth.Length)
      throw new NotSupportedException($"Unknown AV1 transform size {txSize}.");
    if (mode == Av1PredictionMode.UvCflPred)
      throw new NotSupportedException("UvCflPred is not a predictor: issue DC_PRED then PredictCfl.");
    if ((uint)mode >= (uint)_ExtendModes.Length)
      throw new NotSupportedException($"Unknown AV1 prediction mode {mode}.");
    if (angleDelta is < -3 or > 3)
      throw new ArgumentOutOfRangeException(nameof(angleDelta), angleDelta, "Expected the signalled index in [-3, 3].");

    var width = Av1StructureTables.TxWidth[(int)txSize];
    var height = Av1StructureTables.TxHeight[(int)txSize];
    if (topPixels < 0 || topPixels > width || leftPixels < 0 || leftPixels > height)
      throw new ArgumentOutOfRangeException(nameof(topPixels), "Neighbour counts cannot exceed the block dimensions.");
    if (topRightPixels < 0 || topRightPixels > width || bottomLeftPixels < 0 || bottomLeftPixels > height)
      throw new ArgumentOutOfRangeException(nameof(topRightPixels), "Neighbour counts cannot exceed the block dimensions.");

    var isDirectional = mode is >= Av1PredictionMode.VPred and <= Av1PredictionMode.D67Pred;

    // DC/SMOOTH*/PAETH never need the above-right or below-left extension, so libaom splits them
    // off into a cheaper builder that skips the edge filter entirely.
    if (!useFilterIntra && !isDirectional) {
      _BuildNonDirectional(plane, stride, x, y, width, height, mode, topPixels, leftPixels, bitDepth);
      return;
    }

    if (useFilterIntra) {
      if ((uint)filterIntraMode > (uint)Av1FilterIntraMode.Paeth)
        throw new NotSupportedException($"Unknown AV1 filter-intra mode {filterIntraMode}.");
      if (width > 32 || height > 32)
        throw new NotSupportedException($"Filter intra is defined only up to 32x32, not {width}x{height}.");
    }

    var angle = isDirectional ? Av1IntraTables.ModeToAngle[(int)mode] + angleDelta * _AngleStep : 0;

    var needTopRight = (_ExtendModes[(int)mode] & _NeedAboveRight) != 0;
    var needBottomLeft = (_ExtendModes[(int)mode] & _NeedBottomLeft) != 0;
    if (useFilterIntra)
      needTopRight = needBottomLeft = false;
    if (isDirectional) {
      needTopRight = angle < 90;
      needBottomLeft = angle > 180;
    }

    // libaom encodes "not needed" as -1 so the builder can tell it apart from "needed but none
    // available", which decides whether the reference is extended past the block at all. The
    // caller passes plain counts, so the sentinel is restored here.
    var topRight = needTopRight ? topRightPixels : -1;
    var bottomLeft = needBottomLeft ? bottomLeftPixels : -1;

    _BuildDirectionalAndFilter(
      plane, stride, x, y, width, height, mode, angle, useFilterIntra, filterIntraMode,
      !enableIntraEdgeFilter, topPixels, topRight, leftPixels, bottomLeft,
      intraEdgeFilterType, bitDepth);
  }

  /// <summary>AV1 7.11.5: builds the luma AC contribution a chroma block's CFL prediction uses.</summary>
  /// <param name="lumaPlane">Reconstructed luma plane.</param>
  /// <param name="lumaStride">Samples per row of <paramref name="lumaPlane"/>.</param>
  /// <param name="lumaX">Column of the co-located luma block.</param>
  /// <param name="lumaY">Row of the co-located luma block.</param>
  /// <param name="chromaWidth">Chroma block width, at most 32.</param>
  /// <param name="chromaHeight">Chroma block height, at most 32.</param>
  /// <param name="subX">Horizontal chroma subsampling, 0 or 1.</param>
  /// <param name="subY">Vertical chroma subsampling, 0 or 1.</param>
  /// <param name="lumaWidth">Luma samples actually available across; short of
  /// <paramref name="chromaWidth"/> &lt;&lt; <paramref name="subX"/> at the frame edge.</param>
  /// <param name="lumaHeight">Luma samples actually available down.</param>
  /// <param name="acBuffer">Receives chromaWidth * chromaHeight row-major Q3 AC values.</param>
  public static void BuildCflAcBuffer(
    short[] lumaPlane, int lumaStride,
    int lumaX, int lumaY, int chromaWidth, int chromaHeight,
    int subX, int subY, int lumaWidth, int lumaHeight,
    Span<int> acBuffer) {
    ArgumentNullException.ThrowIfNull(lumaPlane);
    if (subX is not (0 or 1) || subY is not (0 or 1))
      throw new NotSupportedException($"AV1 supports only 4:2:0, 4:2:2 and 4:4:4, not subsampling {subX}/{subY}.");
    if (!_IsValidDimension(chromaWidth) || !_IsValidDimension(chromaHeight) || chromaWidth > 32 || chromaHeight > 32)
      throw new NotSupportedException($"CFL is defined only up to 32x32 chroma, not {chromaWidth}x{chromaHeight}.");
    if (acBuffer.Length < chromaWidth * chromaHeight)
      throw new ArgumentException("Buffer is smaller than chromaWidth * chromaHeight.", nameof(acBuffer));

    var bufferWidth = lumaWidth >> subX;
    var bufferHeight = lumaHeight >> subY;
    if (bufferWidth <= 0 || bufferHeight <= 0)
      throw new NotSupportedException("CFL needs at least one subsampled luma sample.");

    // cfl_luma_subsampling_420/422/444: sum the co-located luma samples and scale to Q3.
    for (var row = 0; row < bufferHeight; ++row) {
      var source = (lumaY + (row << subY)) * lumaStride + lumaX;
      var target = row * chromaWidth;
      for (var column = 0; column < bufferWidth; ++column) {
        var left = source + (column << subX);
        int value;
        if (subX == 1 && subY == 1)
          value = (lumaPlane[left] + lumaPlane[left + 1] + lumaPlane[left + lumaStride] + lumaPlane[left + lumaStride + 1]) << 1;
        else if (subX == 1)
          value = (lumaPlane[left] + lumaPlane[left + 1]) << 2;
        else
          value = lumaPlane[left] << 3;
        acBuffer[target + column] = value;
      }
    }

    // cfl_pad: chroma can cover more area than luma at the frame edge, so repeat the last
    // column and then the last row into the shortfall.
    if (bufferWidth < chromaWidth) {
      var rows = bufferHeight < chromaHeight ? bufferHeight : chromaHeight;
      for (var row = 0; row < rows; ++row) {
        var target = row * chromaWidth;
        var last = acBuffer[target + bufferWidth - 1];
        for (var column = bufferWidth; column < chromaWidth; ++column)
          acBuffer[target + column] = last;
      }
    }
    if (bufferHeight < chromaHeight)
      for (var row = bufferHeight; row < chromaHeight; ++row) {
        var target = row * chromaWidth;
        var previous = target - chromaWidth;
        for (var column = 0; column < chromaWidth; ++column)
          acBuffer[target + column] = acBuffer[previous + column];
      }

    // subtract_average_c: the rounded mean is removed so only the AC part scales by alpha.
    var count = chromaWidth * chromaHeight;
    var sum = count >> 1;
    for (var i = 0; i < count; ++i)
      sum += acBuffer[i];
    var average = sum >> _Log2(count);
    for (var i = 0; i < count; ++i)
      acBuffer[i] -= average;
  }

  /// <summary>AV1 7.11.5: adds the scaled luma AC contribution to a DC prediction already in the
  /// plane.</summary>
  /// <param name="plane">Chroma plane holding the DC prediction of the block.</param>
  /// <param name="stride">Samples per row of <paramref name="plane"/>.</param>
  /// <param name="x">Column of the block's top-left sample.</param>
  /// <param name="y">Row of the block's top-left sample.</param>
  /// <param name="width">Chroma block width.</param>
  /// <param name="height">Chroma block height.</param>
  /// <param name="acBuffer">AC values from <see cref="BuildCflAcBuffer"/>, row-major.</param>
  /// <param name="alpha">Signed Q3 alpha of this plane.</param>
  /// <param name="bitDepth">8, 10 or 12.</param>
  public static void PredictCfl(
    short[] plane, int stride,
    int x, int y, int width, int height,
    ReadOnlySpan<int> acBuffer, int alpha, int bitDepth) {
    ArgumentNullException.ThrowIfNull(plane);
    if (bitDepth is not (8 or 10 or 12))
      throw new NotSupportedException($"AV1 CFL supports 8, 10 and 12 bits, not {bitDepth}.");
    if (acBuffer.Length < width * height)
      throw new ArgumentException("Buffer is smaller than width * height.", nameof(acBuffer));

    var maximum = (1 << bitDepth) - 1;
    for (var row = 0; row < height; ++row) {
      var target = (y + row) * stride + x;
      var source = row * width;
      for (var column = 0; column < width; ++column) {
        // get_scaled_luma_q0: Round2Signed(alpha * ac, 6).
        var scaled = alpha * acBuffer[source + column];
        scaled = scaled < 0 ? -((-scaled + 32) >> 6) : (scaled + 32) >> 6;
        plane[target + column] = (short)_Clip(plane[target + column] + scaled, maximum);
      }
    }
  }

  private static bool _IsValidDimension(int value) => value is 4 or 8 or 16 or 32 or 64;

  private static int _Log2(int value) {
    var result = 0;
    while ((1 << result) < value)
      ++result;
    return result;
  }

  private static int _Clip(int value, int maximum) => value < 0 ? 0 : value > maximum ? maximum : value;

  private static void _Fill(short[] plane, int stride, int destination, int width, int height, int value) {
    var sample = (short)value;
    for (var row = 0; row < height; ++row) {
      var target = destination + row * stride;
      for (var column = 0; column < width; ++column)
        plane[target + column] = sample;
    }
  }

  /// <summary>libaom <c>build_non_directional_intra_predictors</c>: DC, SMOOTH, SMOOTH_V,
  /// SMOOTH_H and PAETH, which read at most one row above and one column left.</summary>
  private static void _BuildNonDirectional(
    short[] plane, int stride, int x, int y, int width, int height,
    Av1PredictionMode mode, int topPixels, int leftPixels, int bitDepth) {
    var destination = y * stride + x;
    var aboveRef = destination - stride;
    var leftRef = destination - 1;
    var extend = _ExtendModes[(int)mode];
    var needLeft = (extend & _NeedLeft) != 0;
    var needAbove = (extend & _NeedAbove) != 0;
    var needAboveLeft = (extend & _NeedAboveLeft) != 0;
    var half = 128 << (bitDepth - 8);

    // With the only needed side missing there is nothing to interpolate: libaom fills the block
    // with the other side's first sample, or with the base +/- 1 defaults.
    if ((!needAbove && leftPixels == 0) || (!needLeft && topPixels == 0)) {
      var flat = needLeft
        ? topPixels > 0 ? plane[aboveRef] : half + 1
        : leftPixels > 0 ? plane[leftRef] : half - 1;
      _Fill(plane, stride, destination, width, height, flat);
      return;
    }

    Span<short> leftData = stackalloc short[_NeighbourCount];
    Span<short> aboveData = stackalloc short[_NeighbourCount];
    leftData.Fill((short)(half + 1));
    aboveData.Fill((short)(half - 1));

    if (needLeft) {
      var i = 0;
      if (leftPixels > 0) {
        for (; i < leftPixels; ++i)
          leftData[_NeighbourOffset + i] = plane[leftRef + i * stride];
        for (; i < height; ++i)
          leftData[_NeighbourOffset + i] = leftData[_NeighbourOffset + i - 1];
      } else if (topPixels > 0)
        leftData.Slice(_NeighbourOffset, height).Fill(plane[aboveRef]);
    }

    if (needAbove) {
      var i = 0;
      if (topPixels > 0) {
        for (; i < topPixels; ++i)
          aboveData[_NeighbourOffset + i] = plane[aboveRef + i];
        for (; i < width; ++i)
          aboveData[_NeighbourOffset + i] = aboveData[_NeighbourOffset + i - 1];
      } else if (leftPixels > 0)
        aboveData.Slice(_NeighbourOffset, width).Fill(plane[leftRef]);
    }

    if (needAboveLeft) {
      var corner = topPixels > 0 && leftPixels > 0 ? plane[aboveRef - 1]
        : topPixels > 0 ? plane[aboveRef]
        : leftPixels > 0 ? plane[leftRef]
        : half;
      aboveData[_NeighbourOffset - 1] = (short)corner;
      leftData[_NeighbourOffset - 1] = (short)corner;
    }

    switch (mode) {
      case Av1PredictionMode.DcPred:
        _DcPredict(plane, stride, destination, width, height, aboveData, leftData, leftPixels > 0, topPixels > 0, bitDepth);
        break;
      case Av1PredictionMode.PaethPred:
        _PaethPredict(plane, stride, destination, width, height, aboveData, leftData);
        break;
      case Av1PredictionMode.SmoothPred:
        _SmoothPredict(plane, stride, destination, width, height, aboveData, leftData);
        break;
      case Av1PredictionMode.SmoothVPred:
        _SmoothVPredict(plane, stride, destination, width, height, aboveData, leftData);
        break;
      case Av1PredictionMode.SmoothHPred:
        _SmoothHPredict(plane, stride, destination, width, height, aboveData, leftData);
        break;
      default:
        throw new NotSupportedException($"{mode} is not a non-directional AV1 intra mode.");
    }
  }

  /// <summary>libaom <c>build_directional_and_filter_intra_predictors</c>: the reference is
  /// extended past the block, optionally edge filtered and upsampled, then sampled along the
  /// prediction angle.</summary>
  private static void _BuildDirectionalAndFilter(
    short[] plane, int stride, int x, int y, int width, int height,
    Av1PredictionMode mode, int angle, bool useFilterIntra, Av1FilterIntraMode filterIntraMode,
    bool disableEdgeFilter, int topPixels, int topRightPixels, int leftPixels, int bottomLeftPixels,
    int intraEdgeFilterType, int bitDepth) {
    var destination = y * stride + x;
    var aboveRef = destination - stride;
    var leftRef = destination - 1;
    var extend = _ExtendModes[(int)mode];
    var needLeft = (extend & _NeedLeft) != 0;
    var needAbove = (extend & _NeedAbove) != 0;
    var needAboveLeft = (extend & _NeedAboveLeft) != 0;
    var isDirectional = mode is >= Av1PredictionMode.VPred and <= Av1PredictionMode.D67Pred;
    var half = 128 << (bitDepth - 8);

    Span<short> leftData = stackalloc short[_NeighbourCount];
    Span<short> aboveData = stackalloc short[_NeighbourCount];
    // The defaults matter: a zone-2 prediction with no neighbours at all still reads these.
    leftData.Fill((short)(half + 1));
    aboveData.Fill((short)(half - 1));

    if (isDirectional)
      if (angle <= 90) {
        needAbove = true;
        needLeft = false;
        needAboveLeft = true;
      } else if (angle < 180) {
        needAbove = true;
        needLeft = true;
        needAboveLeft = true;
      } else {
        needAbove = false;
        needLeft = true;
        needAboveLeft = true;
      }
    if (useFilterIntra)
      needLeft = needAbove = needAboveLeft = true;

    if ((!needAbove && leftPixels == 0) || (!needLeft && topPixels == 0)) {
      var flat = needLeft
        ? topPixels > 0 ? plane[aboveRef] : half + 1
        : leftPixels > 0 ? plane[leftRef] : half - 1;
      _Fill(plane, stride, destination, width, height, flat);
      return;
    }

    if (needLeft) {
      // A needed-but-unavailable below-left still reserves its space, so the extension repeats
      // the last real sample all the way down instead of stopping at the block height.
      var needed = height + (bottomLeftPixels >= 0 ? width : 0);
      var i = 0;
      if (leftPixels > 0) {
        for (; i < leftPixels; ++i)
          leftData[_NeighbourOffset + i] = plane[leftRef + i * stride];
        if (bottomLeftPixels > 0)
          for (; i < height + bottomLeftPixels; ++i)
            leftData[_NeighbourOffset + i] = plane[leftRef + i * stride];
        for (; i < needed; ++i)
          leftData[_NeighbourOffset + i] = leftData[_NeighbourOffset + i - 1];
      } else if (topPixels > 0)
        leftData.Slice(_NeighbourOffset, needed).Fill(plane[aboveRef]);
    }

    if (needAbove) {
      var needed = width + (topRightPixels >= 0 ? height : 0);
      var i = 0;
      if (topPixels > 0) {
        for (; i < topPixels; ++i)
          aboveData[_NeighbourOffset + i] = plane[aboveRef + i];
        if (topRightPixels > 0)
          for (; i < width + topRightPixels; ++i)
            aboveData[_NeighbourOffset + i] = plane[aboveRef + i];
        for (; i < needed; ++i)
          aboveData[_NeighbourOffset + i] = aboveData[_NeighbourOffset + i - 1];
      } else if (leftPixels > 0)
        aboveData.Slice(_NeighbourOffset, needed).Fill(plane[leftRef]);
    }

    if (needAboveLeft) {
      var corner = topPixels > 0 && leftPixels > 0 ? plane[aboveRef - 1]
        : topPixels > 0 ? plane[aboveRef]
        : leftPixels > 0 ? plane[leftRef]
        : half;
      aboveData[_NeighbourOffset - 1] = (short)corner;
      leftData[_NeighbourOffset - 1] = (short)corner;
    }

    if (useFilterIntra) {
      _FilterIntraPredict(plane, stride, destination, width, height, aboveData, leftData, filterIntraMode, bitDepth);
      return;
    }

    var upsampleAbove = 0;
    var upsampleLeft = 0;
    if (!disableEdgeFilter) {
      var needRight = angle < 90;
      var needBottom = angle > 180;
      // 7.11.2.4: the exactly horizontal and vertical angles read a single row or column, so
      // there is nothing along the edge to smooth.
      if (angle != 90 && angle != 180) {
        if (needAbove && needLeft && width + height >= 24)
          _FilterIntraEdgeCorner(aboveData, leftData);
        if (needAbove && topPixels > 0) {
          var strength = _IntraEdgeFilterStrength(width, height, angle - 90, intraEdgeFilterType);
          var count = topPixels + 1 + (needRight ? height : 0);
          _FilterIntraEdge(aboveData, _NeighbourOffset - 1, count, strength);
        }
        if (needLeft && leftPixels > 0) {
          var strength = _IntraEdgeFilterStrength(height, width, angle - 180, intraEdgeFilterType);
          var count = leftPixels + 1 + (needBottom ? width : 0);
          _FilterIntraEdge(leftData, _NeighbourOffset - 1, count, strength);
        }
      }
      upsampleAbove = _UseIntraEdgeUpsample(width, height, angle - 90, intraEdgeFilterType) ? 1 : 0;
      if (needAbove && upsampleAbove != 0)
        _UpsampleIntraEdge(aboveData, width + (needRight ? height : 0), bitDepth);
      upsampleLeft = _UseIntraEdgeUpsample(height, width, angle - 180, intraEdgeFilterType) ? 1 : 0;
      if (needLeft && upsampleLeft != 0)
        _UpsampleIntraEdge(leftData, height + (needBottom ? width : 0), bitDepth);
    }

    // libaom dr_predictor().
    if (angle > 0 && angle < 90)
      _DrPredictionZ1(plane, stride, destination, width, height, aboveData, upsampleAbove, _GetDx(angle));
    else if (angle > 90 && angle < 180)
      _DrPredictionZ2(plane, stride, destination, width, height, aboveData, leftData, upsampleAbove, upsampleLeft, _GetDx(angle), _GetDy(angle));
    else if (angle > 180 && angle < 270)
      _DrPredictionZ3(plane, stride, destination, width, height, leftData, upsampleLeft, _GetDy(angle));
    else if (angle == 90)
      _VPredict(plane, stride, destination, width, height, aboveData);
    else if (angle == 180)
      _HPredict(plane, stride, destination, width, height, leftData);
    else
      throw new NotSupportedException($"Directional prediction angle {angle} is outside (0, 270).");
  }

  /// <summary>libaom <c>av1_get_dx</c>: horizontal step per row, scaled by 256.</summary>
  private static int _GetDx(int angle) => angle switch {
    > 0 and < 90 => Av1IntraTables.DrIntraDerivative[angle],
    > 90 and < 180 => Av1IntraTables.DrIntraDerivative[180 - angle],
    _ => 1,
  };

  /// <summary>libaom <c>av1_get_dy</c>: vertical step per column, scaled by 256.</summary>
  private static int _GetDy(int angle) => angle switch {
    > 90 and < 180 => Av1IntraTables.DrIntraDerivative[angle - 90],
    > 180 and < 270 => Av1IntraTables.DrIntraDerivative[270 - angle],
    _ => 1,
  };

  /// <summary>libaom <c>intra_edge_filter_strength</c>: a shallower angle needs more smoothing,
  /// and so does a larger block.</summary>
  private static int _IntraEdgeFilterStrength(int size0, int size1, int delta, int type) {
    var d = Math.Abs(delta);
    var strength = 0;
    var blockWh = size0 + size1;
    if (type == 0) {
      if (blockWh <= 8) {
        if (d >= 56)
          strength = 1;
      } else if (blockWh <= 12) {
        if (d >= 40)
          strength = 1;
      } else if (blockWh <= 16) {
        if (d >= 40)
          strength = 1;
      } else if (blockWh <= 24) {
        if (d >= 8)
          strength = 1;
        if (d >= 16)
          strength = 2;
        if (d >= 32)
          strength = 3;
      } else if (blockWh <= 32) {
        if (d >= 1)
          strength = 1;
        if (d >= 4)
          strength = 2;
        if (d >= 32)
          strength = 3;
      } else if (d >= 1)
        strength = 3;
    } else if (blockWh <= 8) {
      if (d >= 40)
        strength = 1;
      if (d >= 64)
        strength = 2;
    } else if (blockWh <= 16) {
      if (d >= 20)
        strength = 1;
      if (d >= 48)
        strength = 2;
    } else if (blockWh <= 24) {
      if (d >= 4)
        strength = 3;
    } else if (d >= 1)
      strength = 3;
    return strength;
  }

  /// <summary>libaom <c>av1_use_intra_edge_upsample</c>.</summary>
  private static bool _UseIntraEdgeUpsample(int size0, int size1, int delta, int type) {
    var d = Math.Abs(delta);
    var blockWh = size0 + size1;
    if (d == 0 || d >= 40)
      return false;
    return type != 0 ? blockWh <= 8 : blockWh <= 16;
  }

  /// <summary>libaom <c>av1_filter_intra_edge_c</c>: a 5-tap smoothing of the reference, in place,
  /// leaving p[0] untouched.</summary>
  private static void _FilterIntraEdge(Span<short> p, int start, int count, int strength) {
    if (strength == 0)
      return;

    ReadOnlySpan<int> kernels = [0, 4, 8, 4, 0, 0, 5, 6, 5, 0, 2, 4, 4, 4, 2];
    var kernel = (strength - 1) * 5;
    Span<short> edge = stackalloc short[129];
    p.Slice(start, count).CopyTo(edge);

    for (var i = 1; i < count; ++i) {
      var sum = 0;
      for (var j = 0; j < 5; ++j) {
        var k = i - 2 + j;
        k = k < 0 ? 0 : k > count - 1 ? count - 1 : k;
        sum += edge[k] * kernels[kernel + j];
      }
      p[start + i] = (short)((sum + 8) >> 4);
    }
  }

  /// <summary>libaom <c>filter_intra_edge_corner</c>: the shared corner sample is smoothed
  /// against its two neighbours before the edges are filtered.</summary>
  private static void _FilterIntraEdgeCorner(Span<short> above, Span<short> left) {
    var sum = left[_NeighbourOffset] * 5 + above[_NeighbourOffset - 1] * 6 + above[_NeighbourOffset] * 5;
    var corner = (short)((sum + 8) >> 4);
    above[_NeighbourOffset - 1] = corner;
    left[_NeighbourOffset - 1] = corner;
  }

  /// <summary>libaom <c>av1_upsample_intra_edge_c</c>: doubles the reference resolution with a
  /// 4-tap interpolation so shallow angles keep sub-sample accuracy.</summary>
  private static void _UpsampleIntraEdge(Span<short> p, int count, int bitDepth) {
    if (count > _MaxUpsampleSize)
      throw new NotSupportedException($"Intra edge upsampling is defined for at most {_MaxUpsampleSize} samples, not {count}.");

    Span<short> input = stackalloc short[_MaxUpsampleSize + 3];
    input[0] = p[_NeighbourOffset - 1];
    input[1] = p[_NeighbourOffset - 1];
    for (var i = 0; i < count; ++i)
      input[i + 2] = p[_NeighbourOffset + i];
    input[count + 2] = p[_NeighbourOffset + count - 1];

    var maximum = (1 << bitDepth) - 1;
    p[_NeighbourOffset - 2] = input[0];
    for (var i = 0; i < count; ++i) {
      var sum = -input[i] + 9 * input[i + 1] + 9 * input[i + 2] - input[i + 3];
      p[_NeighbourOffset + 2 * i - 1] = (short)_Clip((sum + 8) >> 4, maximum);
      p[_NeighbourOffset + 2 * i] = input[i + 2];
    }
  }

  /// <summary>libaom <c>av1_dr_prediction_z1_c</c>: 0 &lt; angle &lt; 90, above only.</summary>
  private static void _DrPredictionZ1(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> above, int upsampleAbove, int dx) {
    var maxBaseX = (width + height - 1) << upsampleAbove;
    var fractionBits = 6 - upsampleAbove;
    var baseIncrement = 1 << upsampleAbove;
    var x = dx;

    for (var r = 0; r < height; ++r, x += dx) {
      var target = destination + r * stride;
      var b = x >> fractionBits;
      var shift = ((x << upsampleAbove) & 0x3F) >> 1;

      if (b >= maxBaseX) {
        // Past the end of the reference every remaining row is the last sample repeated.
        var edge = above[_NeighbourOffset + maxBaseX];
        for (var i = r; i < height; ++i)
          for (var c = 0; c < width; ++c)
            plane[destination + i * stride + c] = edge;
        return;
      }

      for (var c = 0; c < width; ++c, b += baseIncrement)
        plane[target + c] = b < maxBaseX
          ? (short)((above[_NeighbourOffset + b] * (32 - shift) + above[_NeighbourOffset + b + 1] * shift + 16) >> 5)
          : above[_NeighbourOffset + maxBaseX];
    }
  }

  /// <summary>libaom <c>av1_dr_prediction_z2_c</c>: 90 &lt; angle &lt; 180, projecting onto the
  /// above row where possible and onto the left column otherwise.</summary>
  private static void _DrPredictionZ2(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> above, ReadOnlySpan<short> left,
    int upsampleAbove, int upsampleLeft, int dx, int dy) {
    var minBaseX = -(1 << upsampleAbove);
    var fractionBitsX = 6 - upsampleAbove;
    var fractionBitsY = 6 - upsampleLeft;

    for (var r = 0; r < height; ++r) {
      var target = destination + r * stride;
      for (var c = 0; c < width; ++c) {
        int value;
        var y = r + 1;
        var x = (c << 6) - y * dx;
        var baseX = x >> fractionBitsX;
        if (baseX >= minBaseX) {
          var shift = (x * (1 << upsampleAbove) & 0x3F) >> 1;
          value = above[_NeighbourOffset + baseX] * (32 - shift) + above[_NeighbourOffset + baseX + 1] * shift;
        } else {
          x = c + 1;
          y = (r << 6) - x * dy;
          var baseY = y >> fractionBitsY;
          var shift = (y * (1 << upsampleLeft) & 0x3F) >> 1;
          value = left[_NeighbourOffset + baseY] * (32 - shift) + left[_NeighbourOffset + baseY + 1] * shift;
        }
        plane[target + c] = (short)((value + 16) >> 5);
      }
    }
  }

  /// <summary>libaom <c>av1_dr_prediction_z3_c</c>: 180 &lt; angle &lt; 270, left only.</summary>
  private static void _DrPredictionZ3(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> left, int upsampleLeft, int dy) {
    var maxBaseY = (width + height - 1) << upsampleLeft;
    var fractionBits = 6 - upsampleLeft;
    var baseIncrement = 1 << upsampleLeft;
    var y = dy;

    for (var c = 0; c < width; ++c, y += dy) {
      var b = y >> fractionBits;
      var shift = ((y << upsampleLeft) & 0x3F) >> 1;

      for (var r = 0; r < height; ++r, b += baseIncrement) {
        if (b < maxBaseY) {
          var value = left[_NeighbourOffset + b] * (32 - shift) + left[_NeighbourOffset + b + 1] * shift;
          plane[destination + r * stride + c] = (short)((value + 16) >> 5);
          continue;
        }

        var edge = left[_NeighbourOffset + maxBaseY];
        for (; r < height; ++r)
          plane[destination + r * stride + c] = edge;
        break;
      }
    }
  }

  /// <summary>libaom <c>av1_filter_intra_predictor_c</c> (7.11.2.3): a recursive 7-tap filter run
  /// over 4x2 patches in raster order.</summary>
  private static void _FilterIntraPredict(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> above, ReadOnlySpan<short> left, Av1FilterIntraMode mode, int bitDepth) {
    const int bufferStride = 33;
    Span<int> buffer = stackalloc int[bufferStride * bufferStride];
    for (var r = 0; r < height; ++r)
      buffer[(r + 1) * bufferStride] = left[_NeighbourOffset + r];
    for (var c = 0; c <= width; ++c)
      buffer[c] = above[_NeighbourOffset - 1 + c];

    var taps = Av1IntraTables.FilterIntraTaps;
    var modeBase = (int)mode * 64;
    var maximum = (1 << bitDepth) - 1;

    for (var r = 1; r < height + 1; r += 2)
      for (var c = 1; c < width + 1; c += 4) {
        var above0 = (r - 1) * bufferStride + c;
        var p0 = buffer[above0 - 1];
        var p1 = buffer[above0];
        var p2 = buffer[above0 + 1];
        var p3 = buffer[above0 + 2];
        var p4 = buffer[above0 + 3];
        var p5 = buffer[r * bufferStride + c - 1];
        var p6 = buffer[(r + 1) * bufferStride + c - 1];
        for (var k = 0; k < 8; ++k) {
          var tap = modeBase + k * 8;
          var predicted = taps[tap] * p0 + taps[tap + 1] * p1 + taps[tap + 2] * p2 + taps[tap + 3] * p3
            + taps[tap + 4] * p4 + taps[tap + 5] * p5 + taps[tap + 6] * p6;
          buffer[(r + (k >> 2)) * bufferStride + c + (k & 3)] =
            _Clip((predicted + (1 << (_FilterIntraScaleBits - 1))) >> _FilterIntraScaleBits, maximum);
        }
      }

    for (var r = 0; r < height; ++r) {
      var target = destination + r * stride;
      var source = (r + 1) * bufferStride + 1;
      for (var c = 0; c < width; ++c)
        plane[target + c] = (short)buffer[source + c];
    }
  }

  /// <summary>libaom <c>v_predictor</c>.</summary>
  private static void _VPredict(short[] plane, int stride, int destination, int width, int height, ReadOnlySpan<short> above) {
    for (var r = 0; r < height; ++r) {
      var target = destination + r * stride;
      for (var c = 0; c < width; ++c)
        plane[target + c] = above[_NeighbourOffset + c];
    }
  }

  /// <summary>libaom <c>h_predictor</c>.</summary>
  private static void _HPredict(short[] plane, int stride, int destination, int width, int height, ReadOnlySpan<short> left) {
    for (var r = 0; r < height; ++r) {
      var target = destination + r * stride;
      var sample = left[_NeighbourOffset + r];
      for (var c = 0; c < width; ++c)
        plane[target + c] = sample;
    }
  }

  /// <summary>libaom <c>paeth_predictor</c>: pick whichever of left, above and the corner is
  /// closest to their linear estimate.</summary>
  private static void _PaethPredict(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> above, ReadOnlySpan<short> left) {
    var topLeft = above[_NeighbourOffset - 1];
    for (var r = 0; r < height; ++r) {
      var target = destination + r * stride;
      var leftSample = left[_NeighbourOffset + r];
      for (var c = 0; c < width; ++c) {
        var topSample = above[_NeighbourOffset + c];
        var estimate = topSample + leftSample - topLeft;
        var toLeft = Math.Abs(estimate - leftSample);
        var toTop = Math.Abs(estimate - topSample);
        var toCorner = Math.Abs(estimate - topLeft);
        plane[target + c] = toLeft <= toTop && toLeft <= toCorner ? leftSample : toTop <= toCorner ? topSample : topLeft;
      }
    }
  }

  /// <summary>libaom <c>smooth_predictor</c>: a quadratic blend of the four edge estimates.</summary>
  private static void _SmoothPredict(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> above, ReadOnlySpan<short> left) {
    var below = left[_NeighbourOffset + height - 1];
    var right = above[_NeighbourOffset + width - 1];
    var weights = Av1IntraTables.SmoothWeights;
    var weightsW = width - 4;
    var weightsH = height - 4;
    const int scale = 1 << _SmoothWeightLog2Scale;
    const int log2Scale = 1 + _SmoothWeightLog2Scale;

    for (var r = 0; r < height; ++r) {
      var target = destination + r * stride;
      int weightH = weights[weightsH + r];
      var leftSample = left[_NeighbourOffset + r];
      for (var c = 0; c < width; ++c) {
        int weightW = weights[weightsW + c];
        var sum = weightH * above[_NeighbourOffset + c] + (scale - weightH) * below
          + weightW * leftSample + (scale - weightW) * right;
        plane[target + c] = (short)((sum + (1 << (log2Scale - 1))) >> log2Scale);
      }
    }
  }

  /// <summary>libaom <c>smooth_v_predictor</c>.</summary>
  private static void _SmoothVPredict(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> above, ReadOnlySpan<short> left) {
    var below = left[_NeighbourOffset + height - 1];
    var weights = Av1IntraTables.SmoothWeights;
    var weightsH = height - 4;
    const int scale = 1 << _SmoothWeightLog2Scale;

    for (var r = 0; r < height; ++r) {
      var target = destination + r * stride;
      int weight = weights[weightsH + r];
      for (var c = 0; c < width; ++c) {
        var sum = weight * above[_NeighbourOffset + c] + (scale - weight) * below;
        plane[target + c] = (short)((sum + (1 << (_SmoothWeightLog2Scale - 1))) >> _SmoothWeightLog2Scale);
      }
    }
  }

  /// <summary>libaom <c>smooth_h_predictor</c>.</summary>
  private static void _SmoothHPredict(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> above, ReadOnlySpan<short> left) {
    var right = above[_NeighbourOffset + width - 1];
    var weights = Av1IntraTables.SmoothWeights;
    var weightsW = width - 4;
    const int scale = 1 << _SmoothWeightLog2Scale;

    for (var r = 0; r < height; ++r) {
      var target = destination + r * stride;
      var leftSample = left[_NeighbourOffset + r];
      for (var c = 0; c < width; ++c) {
        int weight = weights[weightsW + c];
        var sum = weight * leftSample + (scale - weight) * right;
        plane[target + c] = (short)((sum + (1 << (_SmoothWeightLog2Scale - 1))) >> _SmoothWeightLog2Scale);
      }
    }
  }

  /// <summary>libaom's dc_128/dc_top/dc_left/dc predictors, selected by which neighbours the
  /// block actually has.</summary>
  private static void _DcPredict(
    short[] plane, int stride, int destination, int width, int height,
    ReadOnlySpan<short> above, ReadOnlySpan<short> left, bool hasLeft, bool hasTop, int bitDepth) {
    int dc;
    if (hasLeft && hasTop) {
      var sum = 0;
      for (var i = 0; i < width; ++i)
        sum += above[_NeighbourOffset + i];
      for (var i = 0; i < height; ++i)
        sum += left[_NeighbourOffset + i];
      var count = width + height;
      // Square blocks divide directly; rectangular ones use libaom's multiply-shift so that the
      // division by 3 or 5 stays exact without a divide.
      dc = width == height ? (sum + (count >> 1)) / count : _DcRectangular(sum, width, height, bitDepth);
    } else if (hasLeft) {
      var sum = 0;
      for (var i = 0; i < height; ++i)
        sum += left[_NeighbourOffset + i];
      dc = (sum + (height >> 1)) / height;
    } else if (hasTop) {
      var sum = 0;
      for (var i = 0; i < width; ++i)
        sum += above[_NeighbourOffset + i];
      dc = (sum + (width >> 1)) / width;
    } else
      dc = 128 << (bitDepth - 8);

    _Fill(plane, stride, destination, width, height, dc);
  }

  /// <summary>libaom <c>dc_predictor_rect</c>: width + height reduces to 3 or 5 times a power of
  /// two, so one shift plus a reciprocal multiply replaces the division.</summary>
  private static int _DcRectangular(int sum, int width, int height, int bitDepth) {
    var total = width + height;
    var shift1 = 0;
    var odd = total;
    while ((odd & 1) == 0) {
      odd >>= 1;
      ++shift1;
    }

    int multiplier;
    int shift2;
    if (bitDepth == 8) {
      multiplier = odd == 3 ? _DcMultiplier1x2 : _DcMultiplier1x4;
      shift2 = _DcShift2;
    } else {
      multiplier = odd == 3 ? _HighbdDcMultiplier1x2 : _HighbdDcMultiplier1x4;
      shift2 = _HighbdDcShift2;
    }
    if (odd is not (3 or 5))
      throw new NotSupportedException($"AV1 allows only 1:2 and 1:4 rectangular blocks, not {width}x{height}.");

    return ((sum + (total >> 1)) >> shift1) * multiplier >> shift2;
  }
}
