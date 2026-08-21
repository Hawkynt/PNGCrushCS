using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The deblocking filter — ITU-T H.265, clause 8.7.2.
/// </summary>
/// <remarks>
/// Block-based coding produces steps at block edges, because two neighbouring blocks were quantised
/// independently and their errors do not match. The filter removes the step without removing a real
/// edge that happened to fall there, and the whole of its difficulty is telling those two apart.
/// <para/>
/// It does so in three stages. A <em>strength</em> is derived from what the two sides were coded
/// with — intra, or with coefficients, or with different motion — because a step across two blocks
/// predicted identically with no residual cannot be a coding artefact and must be real. Then a pair
/// of thresholds, taken from the quantiser, say how large a step this quantiser could plausibly have
/// created. Then the samples themselves are examined: an edge with texture on both sides of it is
/// left almost alone, while a flat region with a single step in it is smoothed across six samples.
/// <para/>
/// <b>Every vertical edge in the picture is filtered before any horizontal one</b>, and the
/// horizontal pass reads what the vertical pass wrote. Filtering each block's four edges as the block
/// is finished would give a different picture, because a sample near a corner is touched by both
/// passes and the order it is touched in changes its value.
/// <para/>
/// Chroma is filtered only where one side is intra coded. Chroma planes are already half resolution
/// and carry less energy at block edges, so filtering them at every edge would blur more than it
/// repaired.
/// </remarks>
internal static class H265Deblocking {

  /// <summary>Table 8-12: how large a step this quantiser could plausibly have made.</summary>
  private static readonly byte[] _Beta = [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 22, 24,
    26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52, 54, 56,
    58, 60, 62, 64,
  ];

  /// <summary>Table 8-12: how far a single sample may be moved.</summary>
  private static readonly byte[] _Clipping = [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4,
    4, 4, 5, 5, 6, 6, 7, 8, 9, 10, 11, 13, 14, 16, 18, 20, 22, 24,
  ];

  /// <summary>Filters one picture: every vertical edge, then every horizontal one.</summary>
  internal static void Filter(H265FrameDecoder frame) {
    ArgumentNullException.ThrowIfNull(frame);

    _FilterLuma(frame, true);
    _FilterChroma(frame, true);
    _FilterLuma(frame, false);
    _FilterChroma(frame, false);
  }

  private static void _FilterLuma(H265FrameDecoder frame, bool vertical) {
    var picture = frame.Picture;
    var sps = frame.Sps;
    var width = picture.Width;
    var height = picture.Height;

    // Along the edge in fours, across it on the eight-sample grid. The grid is the standard's, and
    // it is what bounds the work: a picture has one edge per eight samples in each direction however
    // finely the quadtree was split.
    var acrossLimit = vertical ? width : height;
    var alongLimit = vertical ? height : width;

    for (var across = 8; across < acrossLimit; across += 8)
      for (var along = 0; along < alongLimit; along += 4) {
        var x = vertical ? across : along;
        var y = vertical ? along : across;

        var strength = _BoundaryStrength(frame, x, y, vertical);
        if (strength == 0)
          continue;

        _FilterLumaSegment(frame, sps, picture, x, y, vertical, strength);
      }
  }

  private static void _FilterChroma(H265FrameDecoder frame, bool vertical) {
    var picture = frame.Picture;
    var width = picture.ChromaWidth;
    var height = picture.ChromaHeight;

    var acrossLimit = vertical ? width : height;
    var alongLimit = vertical ? height : width;

    for (var across = 8; across < acrossLimit; across += 8)
      for (var along = 0; along < alongLimit; along += 4) {
        var chromaX = vertical ? across : along;
        var chromaY = vertical ? along : across;
        var x = chromaX << 1;
        var y = chromaY << 1;

        // Only an intra block on one side is worth filtering chroma across, which is what a strength
        // of two means.
        if (_BoundaryStrength(frame, x, y, vertical) != 2)
          continue;

        for (var component = 0; component < 2; ++component)
          _FilterChromaSegment(frame, picture, component, chromaX, chromaY, x, y, vertical);
      }
  }

  /// <summary>
  /// How strongly an edge is to be filtered — clause 8.7.2.4.
  /// </summary>
  /// <remarks>
  /// Zero says the step cannot be an artefact: both sides were predicted the same way from the same
  /// picture and neither carried a residual, so whatever is there was there before the encoder saw
  /// it. Two says one side is intra coded, where the step is between a prediction and a
  /// reconstruction and is almost always an artefact. One is everything in between.
  /// </remarks>
  private static int _BoundaryStrength(H265FrameDecoder frame, int x, int y, bool vertical) {
    var px = vertical ? x - 1 : x;
    var py = vertical ? y : y - 1;

    var q = frame.BlockIndexAt(x, y);
    var p = frame.BlockIndexAt(px, py);

    // An edge on a slice boundary is filtered only where the slice says it may be. Filtering across
    // one otherwise would make a slice's decoded samples depend on its neighbour's, which is the one
    // thing slices exist to prevent.
    var qSlice = frame.SliceOfBlock(q);
    if (frame.SliceOfBlock(p) != qSlice && !frame.LoopFilterAcrossSlices(qSlice))
      return 0;

    var isEdge = vertical
      ? frame.IsTransformEdgeVertical(q) || frame.IsPredictionEdgeVertical(q)
      : frame.IsTransformEdgeHorizontal(q) || frame.IsPredictionEdgeHorizontal(q);

    if (!isEdge)
      return 0;

    if (frame.IsIntraAt(p) || frame.IsIntraAt(q))
      return 2;

    var isTransformEdge = vertical ? frame.IsTransformEdgeVertical(q) : frame.IsTransformEdgeHorizontal(q);
    if (isTransformEdge && (frame.HasCodedResidualAt(p) || frame.HasCodedResidualAt(q)))
      return 1;

    return _MotionDiffers(frame, p, q) ? 1 : 0;
  }

  /// <summary>
  /// Whether the two sides were predicted differently enough for the step to be an artefact.
  /// </summary>
  /// <remarks>
  /// "Differently enough" is a whole luma sample — four, in the quarter-sample units a vector is
  /// stated in. Two blocks whose vectors differ by less than that are reading almost the same part of
  /// the reference picture and cannot have produced a visible step between them.
  /// <para/>
  /// The bidirectional case has to consider both pairings. Two blocks may name the same two reference
  /// pictures in opposite list order, in which case comparing list against list would find a large
  /// difference where the predictions are in fact identical.
  /// </remarks>
  private static bool _MotionDiffers(H265FrameDecoder frame, int p, int q) {
    var motion = frame.Picture.Motion;

    var pRefs = _References(frame, motion, p);
    var qRefs = _References(frame, motion, q);

    if (pRefs.Count != qRefs.Count)
      return true;

    if (pRefs.Count == 0)
      return false;

    if (pRefs.Count == 1) {
      if (pRefs.First != qRefs.First)
        return true;

      return _Far(pRefs.FirstX, pRefs.FirstY, qRefs.FirstX, qRefs.FirstY);
    }

    if (pRefs.First == pRefs.Second) {
      // Both vectors of both blocks point at the same picture, so either pairing is admissible and
      // the step is an artefact only if neither pairing matches.
      if (qRefs.First != pRefs.First || qRefs.Second != pRefs.First)
        return true;

      var straight = _Far(pRefs.FirstX, pRefs.FirstY, qRefs.FirstX, qRefs.FirstY)
                     || _Far(pRefs.SecondX, pRefs.SecondY, qRefs.SecondX, qRefs.SecondY);
      var crossed = _Far(pRefs.FirstX, pRefs.FirstY, qRefs.SecondX, qRefs.SecondY)
                    || _Far(pRefs.SecondX, pRefs.SecondY, qRefs.FirstX, qRefs.FirstY);

      return straight && crossed;
    }

    if (pRefs.First == qRefs.First && pRefs.Second == qRefs.Second)
      return _Far(pRefs.FirstX, pRefs.FirstY, qRefs.FirstX, qRefs.FirstY)
             || _Far(pRefs.SecondX, pRefs.SecondY, qRefs.SecondX, qRefs.SecondY);

    if (pRefs.First == qRefs.Second && pRefs.Second == qRefs.First)
      return _Far(pRefs.FirstX, pRefs.FirstY, qRefs.SecondX, qRefs.SecondY)
             || _Far(pRefs.SecondX, pRefs.SecondY, qRefs.FirstX, qRefs.FirstY);

    return true;
  }

  private static bool _Far(int ax, int ay, int bx, int by) => Math.Abs(ax - bx) >= 4 || Math.Abs(ay - by) >= 4;

  /// <summary>
  /// What a block predicts from, named by picture rather than by list index.
  /// </summary>
  /// <remarks>
  /// By picture, because two blocks may reach the same reference through different indices of
  /// differently ordered lists, and an edge between them has no step to remove. Comparing indices
  /// would put a strength of one on it.
  /// </remarks>
  private static (int Count, object? First, object? Second, int FirstX, int FirstY, int SecondX, int SecondY)
    _References(H265FrameDecoder frame, H265MotionField motion, int block) {
    object? first = null;
    object? second = null;
    var firstX = 0;
    var firstY = 0;
    var secondX = 0;
    var secondY = 0;
    var count = 0;

    for (var list = 0; list < 2; ++list) {
      if (!motion.PredictionFlag(list, block))
        continue;

      var index = motion.RefIdx(list, block);
      var pictures = frame.ReferenceList(list);
      object? picture = index >= 0 && index < pictures.Count ? pictures[index] : null;

      if (count == 0) {
        first = picture;
        firstX = motion.MvX(list, block);
        firstY = motion.MvY(list, block);
      } else {
        second = picture;
        secondX = motion.MvX(list, block);
        secondY = motion.MvY(list, block);
      }

      ++count;
    }

    return (count, first, second, firstX, firstY, secondX, secondY);
  }

  private static void _FilterLumaSegment(
    H265FrameDecoder frame, H265SequenceParameterSet sps, H265Picture picture,
    int x, int y, bool vertical, int strength) {
    var plane = picture.Luma;
    var stride = picture.Width;

    var step = vertical ? 1 : stride;
    var along = vertical ? stride : 1;
    var origin = y * stride + x;

    var qSlice = frame.SliceOfBlock(frame.BlockIndexAt(x, y));
    var (disabled, betaOffset, tcOffset) = frame.DeblockingParameters(qSlice);
    if (disabled)
      return;

    var pQp = frame.QuantiserAt(vertical ? x - 1 : x, vertical ? y : y - 1);
    var qQp = frame.QuantiserAt(x, y);
    var averageQp = (pQp + qQp + 1) >> 1;

    var beta = _Beta[Math.Clamp(averageQp + (betaOffset << 1), 0, 51)] * (1 << (sps.BitDepthLuma - 8));
    var clip = _Clipping[Math.Clamp(averageQp + 2 * (strength - 1) + (tcOffset << 1), 0, 53)]
               * (1 << (sps.BitDepthLuma - 8));

    // The decision is taken from the first and the last of the four lines and applied to all four,
    // so that a decoder does not have to look at every line to decide and so that the four are
    // filtered alike — a boundary that changed its mind halfway would show as a new edge.
    var firstLine = origin;
    var lastLine = origin + 3 * along;

    var dp0 = Math.Abs(plane[firstLine - 3 * step] - 2 * plane[firstLine - 2 * step] + plane[firstLine - step]);
    var dq0 = Math.Abs(plane[firstLine + 2 * step] - 2 * plane[firstLine + step] + plane[firstLine]);
    var dp3 = Math.Abs(plane[lastLine - 3 * step] - 2 * plane[lastLine - 2 * step] + plane[lastLine - step]);
    var dq3 = Math.Abs(plane[lastLine + 2 * step] - 2 * plane[lastLine + step] + plane[lastLine]);

    var d = dp0 + dq0 + dp3 + dq3;
    if (d >= beta)
      return;

    var strong = _StrongDecision(plane, firstLine, step, 2 * (dp0 + dq0), beta, clip)
                 && _StrongDecision(plane, lastLine, step, 2 * (dp3 + dq3), beta, clip);

    var narrowThreshold = (beta + (beta >> 1)) >> 3;
    var filterP1 = dp0 + dp3 < narrowThreshold;
    var filterQ1 = dq0 + dq3 < narrowThreshold;

    // A block coded losslessly, or one whose samples were sent raw with the filter turned off for
    // them, keeps every sample it was given. Filtering it would make a lossless coding lossy.
    var pIndex = frame.BlockIndexAt(vertical ? x - 1 : x, vertical ? y : y - 1);
    var qIndex = frame.BlockIndexAt(x, y);
    var keepP = _KeepsItsSamples(frame, sps, pIndex);
    var keepQ = _KeepsItsSamples(frame, sps, qIndex);

    var maximum = (1 << sps.BitDepthLuma) - 1;

    for (var line = 0; line < 4; ++line) {
      var at = origin + line * along;

      if (strong)
        _FilterStrong(plane, at, step, clip, maximum, keepP, keepQ);
      else
        _FilterWeak(plane, at, step, clip, maximum, filterP1, filterQ1, keepP, keepQ);
    }
  }

  /// <summary>
  /// Whether the region around the edge is flat enough to smooth six samples — clause 8.7.2.5.6.
  /// </summary>
  /// <remarks>
  /// Three separate tests, and each rules out a different reason the step might be real: curvature
  /// close to the edge, a gradient across the whole eight samples, and a step too large for this
  /// quantiser to have produced.
  /// </remarks>
  private static bool _StrongDecision(byte[] plane, int at, int step, int curvature, int beta, int clip) {
    if (curvature >= beta >> 2)
      return false;

    var span = Math.Abs(plane[at - 4 * step] - plane[at - step]) + Math.Abs(plane[at] - plane[at + 3 * step]);
    if (span >= beta >> 3)
      return false;

    return Math.Abs(plane[at - step] - plane[at]) < (5 * clip + 1) >> 1;
  }

  private static void _FilterStrong(
    byte[] plane, int at, int step, int clip, int maximum, bool keepP, bool keepQ) {
    var p3 = plane[at - 4 * step];
    var p2 = plane[at - 3 * step];
    var p1 = plane[at - 2 * step];
    var p0 = plane[at - step];
    var q0 = plane[at];
    var q1 = plane[at + step];
    var q2 = plane[at + 2 * step];
    var q3 = plane[at + 3 * step];

    if (!keepP) {
      plane[at - step] = _Move(p0, (p2 + 2 * p1 + 2 * p0 + 2 * q0 + q1 + 4) >> 3, clip << 1, maximum);
      plane[at - 2 * step] = _Move(p1, (p2 + p1 + p0 + q0 + 2) >> 2, clip << 1, maximum);
      plane[at - 3 * step] = _Move(p2, (2 * p3 + 3 * p2 + p1 + p0 + q0 + 4) >> 3, clip << 1, maximum);
    }

    if (keepQ)
      return;

    plane[at] = _Move(q0, (p1 + 2 * p0 + 2 * q0 + 2 * q1 + q2 + 4) >> 3, clip << 1, maximum);
    plane[at + step] = _Move(q1, (p0 + q0 + q1 + q2 + 2) >> 2, clip << 1, maximum);
    plane[at + 2 * step] = _Move(q2, (p0 + q0 + q1 + 3 * q2 + 2 * q3 + 4) >> 3, clip << 1, maximum);
  }

  private static void _FilterWeak(
    byte[] plane, int at, int step, int clip, int maximum, bool filterP1, bool filterQ1,
    bool keepP, bool keepQ) {
    var p2 = plane[at - 3 * step];
    var p1 = plane[at - 2 * step];
    var p0 = plane[at - step];
    var q0 = plane[at];
    var q1 = plane[at + step];
    var q2 = plane[at + 2 * step];

    var delta = (9 * (q0 - p0) - 3 * (q1 - p1) + 8) >> 4;
    if (Math.Abs(delta) >= clip * 10)
      return;

    delta = Math.Clamp(delta, -clip, clip);

    if (!keepP) {
      plane[at - step] = (byte)Math.Clamp(p0 + delta, 0, maximum);

      if (filterP1) {
        var adjust = Math.Clamp((((p2 + p0 + 1) >> 1) - p1 + delta) >> 1, -(clip >> 1), clip >> 1);
        plane[at - 2 * step] = (byte)Math.Clamp(p1 + adjust, 0, maximum);
      }
    }

    if (keepQ)
      return;

    plane[at] = (byte)Math.Clamp(q0 - delta, 0, maximum);

    if (!filterQ1)
      return;

    var adjustQ = Math.Clamp((((q2 + q0 + 1) >> 1) - q1 - delta) >> 1, -(clip >> 1), clip >> 1);
    plane[at + step] = (byte)Math.Clamp(q1 + adjustQ, 0, maximum);
  }

  private static void _FilterChromaSegment(
    H265FrameDecoder frame, H265Picture picture, int component, int chromaX, int chromaY,
    int x, int y, bool vertical) {
    var sps = frame.Sps;
    var qSlice = frame.SliceOfBlock(frame.BlockIndexAt(x, y));
    var (disabled, _, tcOffset) = frame.DeblockingParameters(qSlice);
    if (disabled)
      return;

    var plane = picture.Chroma(component);
    var stride = picture.ChromaWidth;
    var step = vertical ? 1 : stride;
    var along = vertical ? stride : 1;
    var origin = chromaY * stride + chromaX;

    var pQp = frame.QuantiserAt(vertical ? x - 1 : x, vertical ? y : y - 1);
    var qQp = frame.QuantiserAt(x, y);

    // The picture's own chroma offset only: a slice's offset is deliberately left out, so that two
    // slices meeting at an edge filter it with the same threshold.
    var offset = component == 0 ? frame.Pps.CbQpOffset : frame.Pps.CrQpOffset;
    var index = Math.Clamp(((pQp + qQp + 1) >> 1) + offset, -sps.QpBdOffsetChroma, 57);
    var chromaQp = H265Dequantiser.ChromaQp(index);

    var clip = _Clipping[Math.Clamp(chromaQp + 2 + (tcOffset << 1), 0, 53)] * (1 << (sps.BitDepthChroma - 8));
    var maximum = (1 << sps.BitDepthChroma) - 1;

    var pIndex = frame.BlockIndexAt(vertical ? x - 1 : x, vertical ? y : y - 1);
    var qIndex = frame.BlockIndexAt(x, y);
    var keepP = _KeepsItsSamples(frame, sps, pIndex);
    var keepQ = _KeepsItsSamples(frame, sps, qIndex);

    for (var line = 0; line < 4; ++line) {
      var at = origin + line * along;

      var p1 = plane[at - 2 * step];
      var p0 = plane[at - step];
      var q0 = plane[at];
      var q1 = plane[at + step];

      var delta = Math.Clamp((((q0 - p0) << 2) + p1 - q1 + 4) >> 3, -clip, clip);

      if (!keepP)
        plane[at - step] = (byte)Math.Clamp(p0 + delta, 0, maximum);

      if (!keepQ)
        plane[at] = (byte)Math.Clamp(q0 - delta, 0, maximum);
    }
  }

  /// <summary>Whether a block's samples must be left exactly as they were decoded.</summary>
  private static bool _KeepsItsSamples(H265FrameDecoder frame, H265SequenceParameterSet sps, int block)
    => frame.IsTransquantBypassAt(block)
       || (sps.PcmLoopFilterDisabled && frame.IsPulseCodeModulatedAt(block));

  private static byte _Move(int from, int to, int limit, int maximum)
    => (byte)Math.Clamp(Math.Clamp(to, from - limit, from + limit), 0, maximum);
}
