using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Fetches a prediction block out of the reference pictures — ITU-T H.265, clause 8.5.3.3.
/// </summary>
/// <remarks>
/// A motion vector points at quarter-sample precision for luma and eighth-sample for chroma, so most
/// of this is interpolation. HEVC uses an eight-tap filter for luma where H.264 used six, and a
/// four-tap one for chroma where H.264 used two — which is most of why HEVC's predicted pictures need
/// so much less residual. The taps are integers and the shifts between the two passes are fixed, so a
/// predicted sample is reproducible exactly; there is no tolerance here any more than in the
/// transform.
/// <para/>
/// <b>Everything is carried at fourteen bits until the very end.</b> The horizontal pass keeps its
/// full precision, the vertical pass reads that, and only the final combination shifts back down to
/// sample depth. Rounding between the passes would cost about a tenth of a decibel and, more to the
/// point, would put this decoder's predictions a sample-value away from every other decoder's.
/// <para/>
/// <b>A vector may point outside the picture</b>, and that is legal and common — an object entering
/// from the edge is coded by predicting from beyond it. The reference is extended by repeating its
/// edge samples, which is why every fetch below clamps its coordinates rather than refusing.
/// </remarks>
internal static class H265MotionCompensation {

  /// <summary>Table 8-11: the eight-tap luma filters, one per quarter-sample position.</summary>
  /// <remarks>
  /// Each row sums to sixty-four, so the filter has unit gain and the shift that follows it is exact.
  /// The half-sample row is symmetric; the two quarter-sample rows are each other reversed, which is
  /// what makes a displacement of a quarter forwards the mirror of a quarter backwards.
  /// </remarks>
  private static readonly short[] _LumaFilter = [
    0, 0, 0, 64, 0, 0, 0, 0,
    -1, 4, -10, 58, 17, -5, 1, 0,
    -1, 4, -11, 40, 40, -11, 4, -1,
    0, 1, -5, 17, 58, -10, 4, -1,
  ];

  /// <summary>Table 8-13: the four-tap chroma filters, one per eighth-sample position.</summary>
  private static readonly short[] _ChromaFilter = [
    0, 64, 0, 0,
    -2, 58, 10, -2,
    -4, 54, 16, -2,
    -6, 46, 28, -4,
    -4, 36, 36, -4,
    -4, 28, 46, -6,
    -2, 16, 54, -4,
    -2, 10, 58, -2,
  ];

  /// <summary>Predicts one block from one or two reference pictures and writes it into the picture.</summary>
  internal static void Predict(
    H265FrameDecoder frame, int x, int y, int width, int height, in H265MotionInfo motion) {
    ArgumentNullException.ThrowIfNull(frame);

    if (!motion.PredictL0 && !motion.PredictL1)
      throw new System.IO.InvalidDataException(
        $"An H.265 prediction block at ({x}, {y}) predicts from neither reference list. Every inter block must "
        + "predict from at least one.");

    var luma = new int[2][];
    var chromaCb = new int[2][];
    var chromaCr = new int[2][];

    var chromaWidth = width >> 1;
    var chromaHeight = height >> 1;

    for (var list = 0; list < 2; ++list) {
      if (!motion.Predicts(list))
        continue;

      var pictures = frame.ReferenceList(list);
      var index = motion.RefIdx(list);
      if (index < 0 || index >= pictures.Count)
        throw new System.IO.InvalidDataException(
          $"An H.265 prediction block at ({x}, {y}) names reference {index} of list {list}, which holds "
          + $"{pictures.Count} pictures.");

      var reference = pictures[index];
      var mvX = motion.MvX(list);
      var mvY = motion.MvY(list);

      luma[list] = new int[width * height];
      _InterpolateLuma(reference.Luma, reference.Width, reference.Height, x, y, width, height,
        mvX, mvY, luma[list], frame.Sps.BitDepthLuma);

      chromaCb[list] = new int[chromaWidth * chromaHeight];
      chromaCr[list] = new int[chromaWidth * chromaHeight];

      // The chroma planes are half the size, so a vector stated in quarter luma samples is already
      // in eighth chroma samples — the same number means a finer step.
      _InterpolateChroma(reference.Cb, reference.ChromaWidth, reference.ChromaHeight,
        x >> 1, y >> 1, chromaWidth, chromaHeight, mvX, mvY, chromaCb[list], frame.Sps.BitDepthChroma);
      _InterpolateChroma(reference.Cr, reference.ChromaWidth, reference.ChromaHeight,
        x >> 1, y >> 1, chromaWidth, chromaHeight, mvX, mvY, chromaCr[list], frame.Sps.BitDepthChroma);
    }

    var picture = frame.Picture;
    _Combine(frame, motion, luma, picture.Luma, picture.Width, x, y, width, height,
      frame.Sps.BitDepthLuma, -1);
    _Combine(frame, motion, chromaCb, picture.Cb, picture.ChromaWidth, x >> 1, y >> 1,
      chromaWidth, chromaHeight, frame.Sps.BitDepthChroma, 0);
    _Combine(frame, motion, chromaCr, picture.Cr, picture.ChromaWidth, x >> 1, y >> 1,
      chromaWidth, chromaHeight, frame.Sps.BitDepthChroma, 1);
  }

  /// <summary>The luma interpolation of clause 8.5.3.3.3.2.</summary>
  private static void _InterpolateLuma(
    byte[] reference, int referenceWidth, int referenceHeight, int x, int y, int width, int height,
    int mvX, int mvY, int[] target, int bitDepth) {
    var xInteger = x + (mvX >> 2);
    var yInteger = y + (mvY >> 2);
    var xFraction = mvX & 3;
    var yFraction = mvY & 3;

    var shift1 = bitDepth - 8;
    var shift3 = 14 - bitDepth;

    if (xFraction == 0 && yFraction == 0) {
      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column)
          target[row * width + column] =
            _At(reference, referenceWidth, referenceHeight, xInteger + column, yInteger + row) << shift3;

      return;
    }

    if (yFraction == 0) {
      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column)
          target[row * width + column] =
            _FilterHorizontally(reference, referenceWidth, referenceHeight,
              xInteger + column, yInteger + row, xFraction) >> shift1;

      return;
    }

    if (xFraction == 0) {
      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column)
          target[row * width + column] =
            _FilterVertically(reference, referenceWidth, referenceHeight,
              xInteger + column, yInteger + row, yFraction) >> shift1;

      return;
    }

    // Horizontal first, into a strip three rows taller above and four below, then vertical over that.
    // The intermediate keeps its full precision: rounding it would lose about a tenth of a decibel
    // and, more to the point, would not be what every other decoder computes.
    var strip = new int[width * (height + 7)];
    for (var row = -3; row < height + 4; ++row)
      for (var column = 0; column < width; ++column)
        strip[(row + 3) * width + column] =
          _FilterHorizontally(reference, referenceWidth, referenceHeight,
            xInteger + column, yInteger + row, xFraction) >> shift1;

    var taps = yFraction << 3;
    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column) {
        var sum = 0;
        for (var tap = 0; tap < 8; ++tap)
          sum += _LumaFilter[taps + tap] * strip[(row + tap) * width + column];

        target[row * width + column] = sum >> 6;
      }
  }

  private static int _FilterHorizontally(
    byte[] reference, int width, int height, int x, int y, int fraction) {
    var taps = fraction << 3;
    var sum = 0;
    for (var tap = 0; tap < 8; ++tap)
      sum += _LumaFilter[taps + tap] * _At(reference, width, height, x + tap - 3, y);

    return sum;
  }

  private static int _FilterVertically(
    byte[] reference, int width, int height, int x, int y, int fraction) {
    var taps = fraction << 3;
    var sum = 0;
    for (var tap = 0; tap < 8; ++tap)
      sum += _LumaFilter[taps + tap] * _At(reference, width, height, x, y + tap - 3);

    return sum;
  }

  /// <summary>The chroma interpolation of clause 8.5.3.3.3.3.</summary>
  private static void _InterpolateChroma(
    byte[] reference, int referenceWidth, int referenceHeight, int x, int y, int width, int height,
    int mvX, int mvY, int[] target, int bitDepth) {
    var xInteger = x + (mvX >> 3);
    var yInteger = y + (mvY >> 3);
    var xFraction = mvX & 7;
    var yFraction = mvY & 7;

    var shift1 = bitDepth - 8;
    var shift3 = 14 - bitDepth;

    if (xFraction == 0 && yFraction == 0) {
      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column)
          target[row * width + column] =
            _At(reference, referenceWidth, referenceHeight, xInteger + column, yInteger + row) << shift3;

      return;
    }

    if (yFraction == 0) {
      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column)
          target[row * width + column] =
            _ChromaHorizontally(reference, referenceWidth, referenceHeight,
              xInteger + column, yInteger + row, xFraction) >> shift1;

      return;
    }

    if (xFraction == 0) {
      var taps = yFraction << 2;
      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column) {
          var sum = 0;
          for (var tap = 0; tap < 4; ++tap)
            sum += _ChromaFilter[taps + tap]
                   * _At(reference, referenceWidth, referenceHeight, xInteger + column, yInteger + row + tap - 1);

          target[row * width + column] = sum >> shift1;
        }

      return;
    }

    var strip = new int[width * (height + 3)];
    for (var row = -1; row < height + 2; ++row)
      for (var column = 0; column < width; ++column)
        strip[(row + 1) * width + column] =
          _ChromaHorizontally(reference, referenceWidth, referenceHeight,
            xInteger + column, yInteger + row, xFraction) >> shift1;

    var verticalTaps = yFraction << 2;
    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column) {
        var sum = 0;
        for (var tap = 0; tap < 4; ++tap)
          sum += _ChromaFilter[verticalTaps + tap] * strip[(row + tap) * width + column];

        target[row * width + column] = sum >> 6;
      }
  }

  private static int _ChromaHorizontally(
    byte[] reference, int width, int height, int x, int y, int fraction) {
    var taps = fraction << 2;
    var sum = 0;
    for (var tap = 0; tap < 4; ++tap)
      sum += _ChromaFilter[taps + tap] * _At(reference, width, height, x + tap - 1, y);

    return sum;
  }

  /// <summary>One reference sample, with the picture extended by repeating its edge.</summary>
  private static int _At(byte[] plane, int width, int height, int x, int y)
    => plane[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];

  /// <summary>
  /// Brings one or two predictions back to sample depth and writes them out — clause 8.5.3.3.4.
  /// </summary>
  /// <param name="component">-1 for luma, 0 for Cb, 1 for Cr — which set of weights applies.</param>
  private static void _Combine(
    H265FrameDecoder frame, in H265MotionInfo motion, int[][] predictions, byte[] plane, int stride,
    int x, int y, int width, int height, int bitDepth, int component) {
    var maximum = (1 << bitDepth) - 1;
    var weights = frame.Header.PredictionWeights;
    var bidirectional = motion.PredictL0 && motion.PredictL1;

    if (weights == null) {
      _CombineUnweighted(predictions, plane, stride, x, y, width, height, bitDepth, maximum, bidirectional);
      return;
    }

    var denominator = (component < 0 ? weights.LumaLog2WeightDenom : weights.ChromaLog2WeightDenom)
                      + 14 - bitDepth;
    var scale = 1 << (bitDepth - 8);
    var refIdxL0 = (int)motion.RefIdxL0;
    var refIdxL1 = (int)motion.RefIdxL1;

    int Weight(int list) {
      var index = list == 0 ? refIdxL0 : refIdxL1;
      return component < 0 ? weights.LumaWeight(list, index) : weights.ChromaWeight(list, index, component);
    }

    int Offset(int list) {
      var index = list == 0 ? refIdxL0 : refIdxL1;
      return (component < 0
        ? weights.LumaOffset(list, index)
        : weights.ChromaOffset(list, index, component)) * scale;
    }

    if (bidirectional) {
      var w0 = Weight(0);
      var w1 = Weight(1);
      var rounding = (Offset(0) + Offset(1) + 1) << denominator;

      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column) {
          var at = row * width + column;
          plane[(y + row) * stride + x + column] = (byte)Math.Clamp(
            (predictions[0][at] * w0 + predictions[1][at] * w1 + rounding) >> (denominator + 1), 0, maximum);
        }

      return;
    }

    var list0 = motion.PredictL0 ? 0 : 1;
    var weight = Weight(list0);
    var offset = Offset(list0);
    var samples = predictions[list0];

    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column) {
        var at = row * width + column;
        var value = denominator >= 1
          ? ((samples[at] * weight + (1 << (denominator - 1))) >> denominator) + offset
          : samples[at] * weight + offset;

        plane[(y + row) * stride + x + column] = (byte)Math.Clamp(value, 0, maximum);
      }
  }

  /// <summary>The unweighted combination of clause 8.5.3.3.4.2: a straight average, or a shift.</summary>
  private static void _CombineUnweighted(
    int[][] predictions, byte[] plane, int stride, int x, int y, int width, int height, int bitDepth,
    int maximum, bool bidirectional) {
    if (bidirectional) {
      var shift = 15 - bitDepth;
      var rounding = 1 << (shift - 1);

      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column) {
          var at = row * width + column;
          plane[(y + row) * stride + x + column] =
            (byte)Math.Clamp((predictions[0][at] + predictions[1][at] + rounding) >> shift, 0, maximum);
        }

      return;
    }

    var samples = predictions[0] ?? predictions[1];
    var uniShift = 14 - bitDepth;
    var uniRounding = uniShift > 0 ? 1 << (uniShift - 1) : 0;

    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column)
        plane[(y + row) * stride + x + column] = (byte)Math.Clamp(
          (samples[row * width + column] + uniRounding) >> uniShift, 0, maximum);
  }
}
