using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// Smooths the edges between macroblocks and between subblocks, once the whole frame has been
/// reconstructed (RFC 6386, 15).
/// </summary>
/// <remarks>
/// Not a cosmetic pass. The filtered frame is what later frames are predicted from, so an error here
/// is not a blemish on one picture — it is a wrong reference, and every frame predicted from it
/// inherits the error and adds its own. That is what makes the loop filter the place a decoder is
/// most often nearly right: the differences it produces are small, plausible, spread over the whole
/// picture, and they grow.
/// <para/>
/// The order the edges are taken in matters, because most samples sit on more than one of them and
/// are filtered more than once. Per macroblock: the edge on its left, then its internal vertical
/// edges, then the edge above it, then its internal horizontal edges — and macroblocks in raster
/// order. Within one edge the segments are independent and their order does not matter.
/// <para/>
/// A macroblock skips its internal edges when it was predicted as a whole and carried no residue,
/// since then there is nothing at those edges for the coding to have broken.
/// </remarks>
internal static class Vp8LoopFilter {

  internal static void Apply(
    Vp8Frame frame,
    Vp8MacroblockGrid grid,
    Vp8Segmentation segmentation,
    Vp8LoopFilterHeader header,
    bool isKeyFrame) {
    if (header.Level == 0)
      return;

    var lumaStride = frame.LumaWidth;
    var chromaStride = frame.ChromaWidth;

    for (var row = 0; row < grid.Rows; ++row)
      for (var column = 0; column < grid.Columns; ++column) {
        var index = grid.IndexOf(row, column);
        var level = _LevelFor(grid, index, segmentation, header);
        if (level == 0)
          continue;

        var interiorLimit = _InteriorLimit(level, header.Sharpness);
        var highEdgeVarianceThreshold = _HighEdgeVarianceThreshold(level, isKeyFrame);
        var macroblockLimit = (level + 2) * 2 + interiorLimit;
        var subblockLimit = level * 2 + interiorLimit;

        var mode = grid.LumaMode[index];
        var filterSubblocks = grid.HasResidue[index]
                              || mode == Vp8Mode.SPLIT_MV
                              || mode == Vp8Mode.SUBBLOCK_PREDICTION;

        var luma = row * 16 * lumaStride + column * 16;
        var chroma = row * 8 * chromaStride + column * 8;

        if (header.Simple) {
          _FilterSimpleMacroblock(frame.Luma, luma, lumaStride, column > 0, row > 0, filterSubblocks, macroblockLimit, subblockLimit);
          continue;
        }

        _FilterNormalPlane(
          frame.Luma, luma, lumaStride, 16, column > 0, row > 0, filterSubblocks,
          macroblockLimit, subblockLimit, interiorLimit, highEdgeVarianceThreshold);
        _FilterNormalPlane(
          frame.Cb, chroma, chromaStride, 8, column > 0, row > 0, filterSubblocks,
          macroblockLimit, subblockLimit, interiorLimit, highEdgeVarianceThreshold);
        _FilterNormalPlane(
          frame.Cr, chroma, chromaStride, 8, column > 0, row > 0, filterSubblocks,
          macroblockLimit, subblockLimit, interiorLimit, highEdgeVarianceThreshold);
      }
  }

  // ============================================================================================
  // Control parameters — RFC 6386, 15.4
  // ============================================================================================

  /// <summary>
  /// The filter level for one macroblock: the frame's, adjusted for its segment and then for its
  /// reference frame and coding mode (RFC 6386, 9.3, 9.4 and 15.4).
  /// </summary>
  /// <remarks>The result is clamped to the range of the field twice, once after each adjustment.</remarks>
  private static int _LevelFor(
    Vp8MacroblockGrid grid, int index, Vp8Segmentation segmentation, Vp8LoopFilterHeader header) {
    var level = segmentation.LoopFilterLevelFor(grid.Segment[index], header.Level);
    level = level < 0 ? 0 : level > 63 ? 63 : level;

    if (!header.DeltasEnabled)
      return level;

    var reference = grid.ReferenceFrame[index];
    var mode = grid.LumaMode[index];
    level += header.ReferenceDelta[reference];

    if (reference == Vp8Reference.CURRENT) {
      if (mode == Vp8Mode.SUBBLOCK_PREDICTION)
        level += header.ModeDelta[0];
    } else if (mode == Vp8Mode.ZERO_MV)
      level += header.ModeDelta[1];
    else if (mode == Vp8Mode.SPLIT_MV)
      level += header.ModeDelta[3];
    else
      level += header.ModeDelta[2];

    return level < 0 ? 0 : level > 63 ? 63 : level;
  }

  private static int _InteriorLimit(int level, int sharpness) {
    var limit = level;

    if (sharpness != 0) {
      limit >>= sharpness > 4 ? 2 : 1;
      if (limit > 9 - sharpness)
        limit = 9 - sharpness;
    }

    return limit < 1 ? 1 : limit;
  }

  private static int _HighEdgeVarianceThreshold(int level, bool isKeyFrame) {
    var threshold = level >= 15 ? 1 : 0;
    if (level >= 40)
      ++threshold;

    if (level >= 20 && !isKeyFrame)
      ++threshold;

    return threshold;
  }

  // ============================================================================================
  // The normal filter — RFC 6386, 15.3
  // ============================================================================================

  private static void _FilterNormalPlane(
    byte[] plane, int at, int stride, int size,
    bool hasLeft, bool hasAbove, bool filterSubblocks,
    int macroblockLimit, int subblockLimit, int interiorLimit, int highEdgeVarianceThreshold) {
    if (hasLeft)
      _FilterMacroblockEdge(plane, at, stride, 1, size, macroblockLimit, interiorLimit, highEdgeVarianceThreshold);

    if (filterSubblocks)
      for (var offset = 4; offset < size; offset += 4)
        _FilterSubblockEdge(plane, at + offset, stride, 1, size, subblockLimit, interiorLimit, highEdgeVarianceThreshold);

    if (hasAbove)
      _FilterMacroblockEdge(plane, at, 1, stride, size, macroblockLimit, interiorLimit, highEdgeVarianceThreshold);

    if (!filterSubblocks)
      return;

    for (var offset = 4; offset < size; offset += 4)
      _FilterSubblockEdge(plane, at + offset * stride, 1, stride, size, subblockLimit, interiorLimit, highEdgeVarianceThreshold);
  }

  /// <summary>
  /// Filters one edge between macroblocks, which reaches three samples either side (RFC 6386, 15.3).
  /// </summary>
  /// <param name="advance">The step from one segment of the edge to the next, along the edge.</param>
  /// <param name="step">The step from one sample of a segment to the next, across the edge.</param>
  private static void _FilterMacroblockEdge(
    byte[] plane, int at, int advance, int step, int count,
    int edgeLimit, int interiorLimit, int highEdgeVarianceThreshold) {
    for (var i = 0; i < count; ++i, at += advance) {
      if (!_PassesNormalThreshold(plane, at, step, edgeLimit, interiorLimit))
        continue;

      if (_HasHighEdgeVariance(plane, at, step, highEdgeVarianceThreshold)) {
        _AdjustCommon(plane, at, step, true);
        continue;
      }

      int p2 = plane[at - 3 * step], p1 = plane[at - 2 * step], p0 = plane[at - step];
      int q0 = plane[at], q1 = plane[at + step], q2 = plane[at + 2 * step];

      var w = _SaturateSigned(_SaturateSigned(p1 - q1) + 3 * (q0 - p0));

      var adjustment = (27 * w + 63) >> 7;
      plane[at - step] = _SaturateSample(p0 + adjustment);
      plane[at] = _SaturateSample(q0 - adjustment);

      adjustment = (18 * w + 63) >> 7;
      plane[at - 2 * step] = _SaturateSample(p1 + adjustment);
      plane[at + step] = _SaturateSample(q1 - adjustment);

      adjustment = (9 * w + 63) >> 7;
      plane[at - 3 * step] = _SaturateSample(p2 + adjustment);
      plane[at + 2 * step] = _SaturateSample(q2 - adjustment);
    }
  }

  /// <summary>Filters one edge between subblocks, which reaches two samples either side (RFC 6386, 15.3).</summary>
  private static void _FilterSubblockEdge(
    byte[] plane, int at, int advance, int step, int count,
    int edgeLimit, int interiorLimit, int highEdgeVarianceThreshold) {
    for (var i = 0; i < count; ++i, at += advance)
      if (_PassesNormalThreshold(plane, at, step, edgeLimit, interiorLimit))
        _AdjustCommon(plane, at, step, _HasHighEdgeVariance(plane, at, step, highEdgeVarianceThreshold));
  }

  // ============================================================================================
  // The simple filter — RFC 6386, 15.2
  // ============================================================================================

  /// <summary>
  /// Applies the simple filter to one macroblock's luma, which is all it applies to.
  /// </summary>
  /// <remarks>Chroma is left alone entirely, which is where most of the time it saves comes from.</remarks>
  private static void _FilterSimpleMacroblock(
    byte[] plane, int at, int stride, bool hasLeft, bool hasAbove, bool filterSubblocks,
    int macroblockLimit, int subblockLimit) {
    if (hasLeft)
      _FilterSimpleEdge(plane, at, stride, 1, macroblockLimit);

    if (filterSubblocks)
      for (var offset = 4; offset < 16; offset += 4)
        _FilterSimpleEdge(plane, at + offset, stride, 1, subblockLimit);

    if (hasAbove)
      _FilterSimpleEdge(plane, at, 1, stride, macroblockLimit);

    if (!filterSubblocks)
      return;

    for (var offset = 4; offset < 16; offset += 4)
      _FilterSimpleEdge(plane, at + offset * stride, 1, stride, subblockLimit);
  }

  private static void _FilterSimpleEdge(byte[] plane, int at, int advance, int step, int limit) {
    for (var i = 0; i < 16; ++i, at += advance)
      if (_PassesSimpleThreshold(plane, at, step, limit))
        _AdjustCommon(plane, at, step, true);
  }

  // ============================================================================================
  // The arithmetic both filters share — RFC 6386, 15.2
  // ============================================================================================

  /// <summary>
  /// Brings the two samples straddling an edge towards each other, and optionally the two beyond
  /// them (RFC 6386, 15.2).
  /// </summary>
  /// <param name="useOuterTaps">
  /// Whether the samples one step out from the edge take part in the calculation. When they do not,
  /// they are instead adjusted by half of what the edge samples were.
  /// </param>
  private static void _AdjustCommon(byte[] plane, int at, int step, bool useOuterTaps) {
    int p1 = plane[at - 2 * step], p0 = plane[at - step];
    int q0 = plane[at], q1 = plane[at + step];

    var a = 3 * (q0 - p0);
    if (useOuterTaps)
      a += _SaturateSigned(p1 - q1);

    a = _SaturateSigned(a);

    // The two roundings differ by one so that a difference whose eighth part is exactly a half is
    // shared out evenly rather than always rounded the same way.
    var forQ0 = (a + 4 > 127 ? 127 : a + 4) >> 3;
    var forP0 = (a + 3 > 127 ? 127 : a + 3) >> 3;

    plane[at - step] = _SaturateSample(p0 + forP0);
    plane[at] = _SaturateSample(q0 - forQ0);

    if (useOuterTaps)
      return;

    var outer = (forQ0 + 1) >> 1;
    plane[at - 2 * step] = _SaturateSample(p1 + outer);
    plane[at + step] = _SaturateSample(q1 - outer);
  }

  private static bool _PassesSimpleThreshold(byte[] plane, int at, int step, int limit)
    => _Absolute(plane[at - step] - plane[at]) * 2 + (_Absolute(plane[at - 2 * step] - plane[at + step]) >> 1) <= limit;

  private static bool _PassesNormalThreshold(byte[] plane, int at, int step, int edgeLimit, int interiorLimit)
    => _PassesSimpleThreshold(plane, at, step, edgeLimit)
       && _Absolute(plane[at - 4 * step] - plane[at - 3 * step]) <= interiorLimit
       && _Absolute(plane[at - 3 * step] - plane[at - 2 * step]) <= interiorLimit
       && _Absolute(plane[at - 2 * step] - plane[at - step]) <= interiorLimit
       && _Absolute(plane[at + 3 * step] - plane[at + 2 * step]) <= interiorLimit
       && _Absolute(plane[at + 2 * step] - plane[at + step]) <= interiorLimit
       && _Absolute(plane[at + step] - plane[at]) <= interiorLimit;

  private static bool _HasHighEdgeVariance(byte[] plane, int at, int step, int threshold)
    => _Absolute(plane[at - 2 * step] - plane[at - step]) > threshold
       || _Absolute(plane[at + step] - plane[at]) > threshold;

  private static int _Absolute(int value) => value < 0 ? -value : value;

  private static int _SaturateSigned(int value) => value < -128 ? -128 : value > 127 ? 127 : value;

  private static byte _SaturateSample(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
