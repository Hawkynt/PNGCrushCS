using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The thirty-five intra prediction modes — ITU-T H.265, clause 8.4.4.2.
/// </summary>
/// <remarks>
/// Thirty-three directions, plus planar and direct current. H.264 had eight directions and a
/// 16x16 block could only use four of them; HEVC gives every block size all thirty-five, which is
/// most of why it codes intra pictures so much more tightly. The directions are not evenly spaced in
/// angle but evenly spaced in <em>displacement</em>: the table below is how far along the reference
/// row the predictor advances per row of the block, in thirty-seconds of a sample, so the modes
/// crowd together near horizontal and vertical where the eye notices a wrong angle most.
/// <para/>
/// <b>The reference samples are one array, not two.</b> A block predicts from the row above it and
/// the column to its left, and both may run twice the block's length past its corner. The two are
/// kept end to end here, ordered from the bottom of the left column, through the corner, to the right
/// of the top row — which is the order the standard's substitution walks them in, so filling the
/// holes is one pass rather than a special case at each end.
/// <para/>
/// <b>Three things happen to those samples before a single predictor is computed</b>, and leaving any
/// of them out produces a picture rather than an error. Samples outside the picture, outside the
/// slice, or not yet decoded are substituted from their neighbours. The result is then smoothed, for
/// most modes and most sizes but not all — and for a flat 32x32 block, smoothed by interpolating the
/// whole edge from its two ends, which is what stops a gradient across a large flat area from being
/// coded as visible steps. Finally three of the modes adjust the block's own first row or column
/// afterwards, to soften the step against the neighbour the mode did not predict from.
/// </remarks>
internal static class H265IntraPrediction {

  internal const int PLANAR = 0;

  internal const int DC = 1;

  internal const int HORIZONTAL = 10;

  internal const int VERTICAL = 26;

  /// <summary>
  /// Table 8-5: how far the predictor moves along the reference per row, in thirty-seconds of a sample.
  /// </summary>
  /// <remarks>
  /// Indexed by mode less two. Positive values run away from the corner, negative ones towards and
  /// past it — which is what makes a mode need reference samples from both edges at once.
  /// </remarks>
  private static readonly short[] _Angle = [
    32, 26, 21, 17, 13, 9, 5, 2, 0, -2, -5, -9, -13, -17, -21, -26, -32,
    -26, -21, -17, -13, -9, -5, -2, 0, 2, 5, 9, 13, 17, 21, 26, 32,
  ];

  /// <summary>
  /// Table 8-5: the reciprocal of the angle, for the modes whose predictor runs past the corner.
  /// </summary>
  /// <remarks>
  /// Indexed by mode less eleven, and defined only for modes 11 to 25 — the ones with a negative
  /// angle. It answers the opposite question from <see cref="_Angle"/>: given a position on the
  /// projected reference, which sample of the <em>other</em> edge stands there. That is how the two
  /// edges are stitched into one line for a mode that reaches across the corner.
  /// </remarks>
  private static readonly short[] _InverseAngle = [
    -4096, -1638, -910, -630, -482, -390, -315, -256, -315, -390, -482, -630, -910, -1638, -4096,
  ];

  /// <summary>Where the corner sample sits in a reference array for a block of this size.</summary>
  internal static int CornerIndex(int size) => size << 1;

  /// <summary>Where <c>p[x][-1]</c> sits — the row above, left to right.</summary>
  internal static int AboveIndex(int size, int x) => (size << 1) + 1 + x;

  /// <summary>Where <c>p[-1][y]</c> sits — the column to the left, top to bottom.</summary>
  internal static int LeftIndex(int size, int y) => (size << 1) - 1 - y;

  /// <summary>How many reference samples a block of this size has.</summary>
  internal static int ReferenceCount(int size) => (size << 2) + 1;

  /// <summary>
  /// Fills in the reference samples that were not available — clause 8.4.4.2.2.
  /// </summary>
  /// <remarks>
  /// The walk starts at the far end of the left column and runs to the far end of the row above,
  /// each hole taking the value of the sample before it. With nothing available at all every sample
  /// becomes mid-grey, which is the only defined answer for a block whose neighbours are all outside
  /// the picture.
  /// </remarks>
  /// <returns>Whether anything at all was available.</returns>
  internal static bool Substitute(int[] reference, bool[] available, int size, int bitDepth) {
    var count = ReferenceCount(size);

    var first = -1;
    for (var i = 0; i < count; ++i)
      if (available[i]) {
        first = i;
        break;
      }

    if (first < 0) {
      Array.Fill(reference, 1 << (bitDepth - 1), 0, count);
      return false;
    }

    // Everything before the first available sample takes its value, and everything after takes the
    // value of whatever preceded it.
    for (var i = 0; i < first; ++i)
      reference[i] = reference[first];

    for (var i = first + 1; i < count; ++i)
      if (!available[i])
        reference[i] = reference[i - 1];

    return true;
  }

  /// <summary>
  /// Whether the reference samples are smoothed before this mode uses them — clause 8.4.4.2.3.
  /// </summary>
  /// <remarks>
  /// Never for direct current, which averages them anyway, and never for a 4x4 block, which is too
  /// small for the smoothing to be anything but a loss. Otherwise it turns on how far the mode is
  /// from horizontal or vertical: a mode close to either predicts along a row or a column of the
  /// reference and smoothing would blur an edge the mode is there to reproduce, while a diagonal
  /// mode interpolates between reference samples and is better served by a smooth reference. The
  /// threshold widens with the block size because a larger block is a lower spatial frequency.
  /// </remarks>
  internal static bool FilterReference(int mode, int size) {
    if (mode == DC || size == 4)
      return false;

    var distance = Math.Min(Math.Abs(mode - VERTICAL), Math.Abs(mode - HORIZONTAL));
    var threshold = size switch { 8 => 7, 16 => 1, _ => 0 };
    return distance > threshold;
  }

  /// <summary>
  /// Smooths the reference samples — clause 8.4.4.2.3.
  /// </summary>
  /// <param name="strongSmoothing">
  /// Whether the sequence permits the long interpolation, which applies only to a 32x32 luma block
  /// whose two edges are already nearly straight.
  /// </param>
  internal static void Filter(int[] reference, int size, bool strongSmoothing, int bitDepth) {
    var count = ReferenceCount(size);

    if (strongSmoothing && size == 32 && _EdgesAreFlat(reference, size, bitDepth)) {
      _InterpolateWholeEdges(reference, size);
      return;
    }

    // A three-tap average, run over the array in one pass. The two far ends keep their values,
    // because there is nothing past them to average with. The corner is the one sample whose two
    // neighbours lie on different edges, and the standard averages it with exactly those — which
    // this pass already does, because the two edges are stored adjacent and meet at it.
    var smoothed = new int[count];
    smoothed[0] = reference[0];
    smoothed[count - 1] = reference[count - 1];

    for (var i = 1; i < count - 1; ++i)
      smoothed[i] = (reference[i - 1] + 2 * reference[i] + reference[i + 1] + 2) >> 2;

    Array.Copy(smoothed, reference, count);
  }

  /// <summary>
  /// Whether both reference edges are close enough to straight for the long interpolation.
  /// </summary>
  /// <remarks>
  /// The test is on the middle sample against the average of the two ends: an edge whose midpoint is
  /// within a thirty-second of the sample range of where a straight line would put it is one whose
  /// texture is nothing but the gradient, so replacing it with the line loses nothing and removes the
  /// banding that quantising a gradient produces.
  /// </remarks>
  private static bool _EdgesAreFlat(int[] reference, int size, int bitDepth) {
    var threshold = 1 << (bitDepth - 5);
    var corner = reference[CornerIndex(size)];

    var above = Math.Abs(corner + reference[AboveIndex(size, (size << 1) - 1)]
                         - 2 * reference[AboveIndex(size, size - 1)]);
    var left = Math.Abs(corner + reference[LeftIndex(size, (size << 1) - 1)]
                        - 2 * reference[LeftIndex(size, size - 1)]);

    return above < threshold && left < threshold;
  }

  /// <summary>Replaces both edges with the straight line between the corner and their far ends.</summary>
  private static void _InterpolateWholeEdges(int[] reference, int size) {
    var span = size << 1;
    var corner = reference[CornerIndex(size)];
    var farAbove = reference[AboveIndex(size, span - 1)];
    var farLeft = reference[LeftIndex(size, span - 1)];

    for (var i = 0; i < span - 1; ++i) {
      reference[AboveIndex(size, i)] = ((span - 1 - i) * corner + (i + 1) * farAbove + 32) >> 6;
      reference[LeftIndex(size, i)] = ((span - 1 - i) * corner + (i + 1) * farLeft + 32) >> 6;
    }
  }

  /// <summary>
  /// Predicts one block — clauses 8.4.4.2.4 to 8.4.4.2.6.
  /// </summary>
  /// <param name="prediction">The block, row-major and <paramref name="size"/> across.</param>
  /// <param name="luma">Whether this is a luma block, which is what the three boundary adjustments turn on.</param>
  internal static void Predict(
    int[] prediction, int[] reference, int size, int mode, bool luma, int bitDepth) {
    switch (mode) {
      case PLANAR:
        _Planar(prediction, reference, size);
        return;

      case DC:
        _DirectCurrent(prediction, reference, size, luma);
        return;

      default:
        _Angular(prediction, reference, size, mode, luma, bitDepth);
        return;
    }
  }

  /// <summary>
  /// Planar prediction — clause 8.4.4.2.4: a bilinear surface through all four edges.
  /// </summary>
  /// <remarks>
  /// The two edges the block does not have are stood in for by one sample each: the value below the
  /// left column's last sample and the value right of the top row's last. So the surface is a
  /// weighted average of a horizontal and a vertical gradient, and it is the mode that leaves no
  /// discontinuity at any edge — which is why it is the one flat and gently shaded areas use.
  /// </remarks>
  private static void _Planar(int[] prediction, int[] reference, int size) {
    var log2Size = _Log2(size);
    var right = reference[AboveIndex(size, size)];
    var below = reference[LeftIndex(size, size)];

    for (var y = 0; y < size; ++y) {
      var left = reference[LeftIndex(size, y)];

      for (var x = 0; x < size; ++x)
        prediction[(y << log2Size) + x] =
          ((size - 1 - x) * left
           + (x + 1) * right
           + (size - 1 - y) * reference[AboveIndex(size, x)]
           + (y + 1) * below
           + size) >> (log2Size + 1);
    }
  }

  /// <summary>
  /// Direct current prediction — clause 8.4.4.2.5: the average of both edges, flat across the block.
  /// </summary>
  /// <remarks>
  /// A luma block below 32 samples has its own first row and column pulled towards the neighbours
  /// they touch. Without it a flat block against a gradient shows its outline, which the deblocking
  /// filter would then have to remove — and cannot, because a step the prediction created is not a
  /// step at a transform block edge.
  /// </remarks>
  private static void _DirectCurrent(int[] prediction, int[] reference, int size, bool luma) {
    var log2Size = _Log2(size);

    var sum = size;
    for (var i = 0; i < size; ++i)
      sum += reference[AboveIndex(size, i)] + reference[LeftIndex(size, i)];

    var value = sum >> (log2Size + 1);
    Array.Fill(prediction, value, 0, size * size);

    if (!luma || size >= 32)
      return;

    prediction[0] = (reference[LeftIndex(size, 0)] + 2 * value + reference[AboveIndex(size, 0)] + 2) >> 2;

    for (var x = 1; x < size; ++x)
      prediction[x] = (reference[AboveIndex(size, x)] + 3 * value + 2) >> 2;

    for (var y = 1; y < size; ++y)
      prediction[y << log2Size] = (reference[LeftIndex(size, y)] + 3 * value + 2) >> 2;
  }

  /// <summary>
  /// Angular prediction — clause 8.4.4.2.6.
  /// </summary>
  /// <remarks>
  /// Both halves of this are the same operation with the axes exchanged, and the standard writes them
  /// out twice for that reason. The reference samples are projected onto one line — the row above for
  /// a mode nearer vertical, the column to the left for one nearer horizontal — and each row or
  /// column of the block reads that line at a position the angle displaces, interpolating between two
  /// samples at thirty-seconds of a sample.
  /// <para/>
  /// A negative angle means the projection runs back past the corner, so the line has to be extended
  /// with samples from the <em>other</em> edge, placed by the reciprocal angle. That is the only part
  /// of intra prediction where both edges feed one predictor, and it is what makes a diagonal mode
  /// able to continue a texture that crosses the block's corner.
  /// </remarks>
  private static void _Angular(int[] prediction, int[] reference, int size, int mode, bool luma, int bitDepth) {
    var log2Size = _Log2(size);
    var angle = _Angle[mode - 2];
    var vertical = mode >= 18;

    // The projected line, indexed from minus the block size so that a negative angle has somewhere
    // to reach.
    var line = new int[3 * size + 2];
    var origin = size;

    // The main edge: the row above for a vertical mode, the column to the left for a horizontal one.
    line[origin] = reference[CornerIndex(size)];
    for (var i = 0; i < size; ++i)
      line[origin + 1 + i] = vertical ? reference[AboveIndex(size, i)] : reference[LeftIndex(size, i)];

    if (angle < 0) {
      var reach = (size * angle) >> 5;
      if (reach < -1) {
        var inverse = _InverseAngle[mode - 11];

        // The other edge, projected onto the same line by the reciprocal angle. Position zero is the
        // corner, which is already there.
        for (var i = -1; i >= reach; --i) {
          var at = (i * inverse + 128) >> 8;
          line[origin + i] = at <= 0
            ? reference[CornerIndex(size)]
            : vertical ? reference[LeftIndex(size, at - 1)] : reference[AboveIndex(size, at - 1)];
        }
      }
    } else
      // A positive angle runs away from the corner and may reach twice the block's length along the
      // main edge, which is why the reference row is that long.
      for (var i = size; i < size << 1; ++i)
        line[origin + 1 + i] = vertical ? reference[AboveIndex(size, i)] : reference[LeftIndex(size, i)];

    for (var outer = 0; outer < size; ++outer) {
      var displacement = (outer + 1) * angle;
      var whole = displacement >> 5;
      var fraction = displacement & 31;

      for (var inner = 0; inner < size; ++inner) {
        var at = origin + inner + whole + 1;
        var value = fraction == 0
          ? line[at]
          : ((32 - fraction) * line[at] + fraction * line[at + 1] + 16) >> 5;

        prediction[vertical ? (outer << log2Size) + inner : (inner << log2Size) + outer] = value;
      }
    }

    // The two modes that predict straight down or straight across leave the block's other edge
    // discontinuous against its neighbour, and for a small luma block half that step is taken back.
    if (!luma || size >= 32)
      return;

    var maximum = (1 << bitDepth) - 1;
    var corner = reference[CornerIndex(size)];

    if (mode == VERTICAL)
      for (var y = 0; y < size; ++y)
        prediction[y << log2Size] = Math.Clamp(
          reference[AboveIndex(size, 0)] + ((reference[LeftIndex(size, y)] - corner) >> 1), 0, maximum);
    else if (mode == HORIZONTAL)
      for (var x = 0; x < size; ++x)
        prediction[x] = Math.Clamp(
          reference[LeftIndex(size, 0)] + ((reference[AboveIndex(size, x)] - corner) >> 1), 0, maximum);
  }

  private static int _Log2(int size) {
    var log2 = 0;
    while (1 << log2 < size)
      ++log2;

    return log2;
  }
}
