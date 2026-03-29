using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Avif.Codec;

/// <summary>AV1 intra prediction modes (AV1 spec 6.4.1).</summary>
internal enum Av1PredictionMode {
  DcPred = 0,
  VPred = 1,    // Vertical
  HPred = 2,    // Horizontal
  D45Pred = 3,
  D135Pred = 4,
  D113Pred = 5,
  D157Pred = 6,
  D203Pred = 7,
  D67Pred = 8,
  SmoothPred = 9,
  SmoothVPred = 10,
  SmoothHPred = 11,
  PaethPred = 12,
  // UV modes only:
  UvCflPred = 13, // Chroma from Luma
}

/// <summary>Implements AV1 intra prediction for luma and chroma blocks.
/// Supports DC, directional (angles 45-203), smooth, paeth, and CFL modes.</summary>
internal static class Av1IntraPredictor {

  // Directional prediction angle table: mode index 1-8 maps to base angles
  private static readonly int[] _BASE_ANGLES = [0, 90, 180, 45, 135, 113, 157, 203, 67];

  /// <summary>Predicts a block using the specified intra mode.</summary>
  public static void Predict(
    Av1PredictionMode mode,
    int angle, // delta angle offset for directional modes
    int width,
    int height,
    int bitDepth,
    short[] above,   // top reference samples (width + height + 1 or more)
    short[] left,    // left reference samples (height + width + 1 or more)
    short topLeft,   // top-left corner sample
    short[] output,  // output buffer (width * height)
    int outputStride,
    // CFL parameters
    short[]? lumaResiduals = null,
    int cflAlphaSign = 0,
    int cflAlphaU = 0,
    int cflAlphaV = 0,
    bool isChromaU = false
  ) {
    var maxVal = (short)((1 << bitDepth) - 1);

    switch (mode) {
      case Av1PredictionMode.DcPred:
        _PredictDc(width, height, above, left, output, outputStride);
        break;
      case Av1PredictionMode.VPred:
        _PredictVertical(width, height, above, output, outputStride);
        break;
      case Av1PredictionMode.HPred:
        _PredictHorizontal(width, height, left, output, outputStride);
        break;
      case Av1PredictionMode.SmoothPred:
        _PredictSmooth(width, height, above, left, output, outputStride);
        break;
      case Av1PredictionMode.SmoothVPred:
        _PredictSmoothV(width, height, above, left, output, outputStride);
        break;
      case Av1PredictionMode.SmoothHPred:
        _PredictSmoothH(width, height, above, left, output, outputStride);
        break;
      case Av1PredictionMode.PaethPred:
        _PredictPaeth(width, height, above, left, topLeft, output, outputStride);
        break;
      case Av1PredictionMode.UvCflPred:
        _PredictCfl(width, height, above, left, output, outputStride, maxVal, lumaResiduals, isChromaU ? cflAlphaU : cflAlphaV, isChromaU ? cflAlphaSign >> 1 : cflAlphaSign & 1);
        break;
      default:
        // Directional modes (D45, D135, D113, D157, D203, D67 + delta angle)
        _PredictDirectional(mode, angle, width, height, above, left, topLeft, output, outputStride);
        break;
    }
  }

  private static void _PredictDc(int w, int h, short[] above, short[] left, short[] output, int stride) {
    var sum = 0;
    for (var i = 0; i < w; ++i)
      sum += above[i];
    for (var i = 0; i < h; ++i)
      sum += left[i];

    var avg = (short)((sum + (w + h) / 2) / (w + h));
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        output[y * stride + x] = avg;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _PredictVertical(int w, int h, short[] above, short[] output, int stride) {
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        output[y * stride + x] = above[x];
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _PredictHorizontal(int w, int h, short[] left, short[] output, int stride) {
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        output[y * stride + x] = left[y];
  }

  private static void _PredictSmooth(int w, int h, short[] above, short[] left, short[] output, int stride) {
    var belowPred = left[h - 1];
    var rightPred = above[w - 1];
    var smWeightsW = _GetSmoothWeights(w);
    var smWeightsH = _GetSmoothWeights(h);

    for (var y = 0; y < h; ++y) {
      for (var x = 0; x < w; ++x) {
        var smoothPred =
          smWeightsH[y] * above[x] +
          (256 - smWeightsH[y]) * belowPred +
          smWeightsW[x] * left[y] +
          (256 - smWeightsW[x]) * rightPred;

        output[y * stride + x] = (short)((smoothPred + 256) >> 9);
      }
    }
  }

  private static void _PredictSmoothV(int w, int h, short[] above, short[] left, short[] output, int stride) {
    var belowPred = left[h - 1];
    var smWeightsH = _GetSmoothWeights(h);

    for (var y = 0; y < h; ++y) {
      for (var x = 0; x < w; ++x) {
        var smoothPred = smWeightsH[y] * above[x] + (256 - smWeightsH[y]) * belowPred;
        output[y * stride + x] = (short)((smoothPred + 128) >> 8);
      }
    }
  }

  private static void _PredictSmoothH(int w, int h, short[] above, short[] left, short[] output, int stride) {
    var rightPred = above[w - 1];
    var smWeightsW = _GetSmoothWeights(w);

    for (var y = 0; y < h; ++y) {
      for (var x = 0; x < w; ++x) {
        var smoothPred = smWeightsW[x] * left[y] + (256 - smWeightsW[x]) * rightPred;
        output[y * stride + x] = (short)((smoothPred + 128) >> 8);
      }
    }
  }

  private static void _PredictPaeth(int w, int h, short[] above, short[] left, short topLeft, short[] output, int stride) {
    for (var y = 0; y < h; ++y) {
      for (var x = 0; x < w; ++x) {
        var top = above[x];
        var lft = left[y];
        var tl = topLeft;
        var base_ = top + lft - tl;
        var pTop = Math.Abs(base_ - top);
        var pLeft = Math.Abs(base_ - lft);
        var pTl = Math.Abs(base_ - tl);

        if (pTop <= pLeft && pTop <= pTl)
          output[y * stride + x] = top;
        else if (pLeft <= pTl)
          output[y * stride + x] = lft;
        else
          output[y * stride + x] = tl;
      }
    }
  }

  private static void _PredictDirectional(Av1PredictionMode mode, int angleDelta, int w, int h, short[] above, short[] left, short topLeft, short[] output, int stride) {
    var modeIndex = (int)mode;
    if (modeIndex < 1 || modeIndex > 8) {
      // Fallback to DC for invalid mode
      _PredictDc(w, h, above, left, output, stride);
      return;
    }

    var nominalAngle = _BASE_ANGLES[modeIndex];
    var angle = nominalAngle + angleDelta * 3;

    if (angle >= 0 && angle < 90)
      _PredictDirectionalAngle(angle, w, h, above, left, topLeft, output, stride, true);
    else if (angle == 90)
      _PredictVertical(w, h, above, output, stride);
    else if (angle > 90 && angle < 180)
      _PredictDirectionalAngle(angle, w, h, above, left, topLeft, output, stride, false);
    else if (angle == 180)
      _PredictHorizontal(w, h, left, output, stride);
    else if (angle > 180 && angle < 270)
      _PredictDirectionalAngle(angle, w, h, above, left, topLeft, output, stride, false);
    else
      _PredictDc(w, h, above, left, output, stride);
  }

  private static void _PredictDirectionalAngle(int angle, int w, int h, short[] above, short[] left, short topLeft, short[] output, int stride, bool useAbove) {
    // For directional prediction, we use a tangent-based approach
    // Angles in AV1 are defined as: 0=horizontal-right, 90=vertical-down, 180=horizontal-left, 270=vertical-up
    // The angle determines which reference samples to use and at what sub-pixel offset

    if (angle < 90) {
      // Use above samples, project at angle from top
      var dx = _GetDirectionalDx(angle);
      for (var y = 0; y < h; ++y) {
        for (var x = 0; x < w; ++x) {
          var shift = (y + 1) * dx;
          var basePos = x + (shift >> 6);
          var frac = shift & 63;

          if (frac == 0)
            output[y * stride + x] = _SafeRef(above, basePos, topLeft, w + h);
          else {
            var a = _SafeRef(above, basePos, topLeft, w + h);
            var b = _SafeRef(above, basePos + 1, topLeft, w + h);
            output[y * stride + x] = (short)((a * (64 - frac) + b * frac + 32) >> 6);
          }
        }
      }
    } else if (angle > 90 && angle < 180) {
      // Mix of above and left
      var dy = _GetDirectionalDy(angle);
      for (var y = 0; y < h; ++y) {
        for (var x = 0; x < w; ++x) {
          var shift = (x + 1) * dy;
          var basePos = y + (shift >> 6);
          var frac = shift & 63;

          if (basePos < 0) {
            // Use above samples
            var aboveIdx = x + ((y + 1) * _GetDirectionalDx(angle) >> 6);
            output[y * stride + x] = _SafeRef(above, aboveIdx, topLeft, w + h);
          } else if (frac == 0) {
            output[y * stride + x] = _SafeRef(left, basePos, topLeft, w + h);
          } else {
            var a = _SafeRef(left, basePos, topLeft, w + h);
            var b = _SafeRef(left, basePos + 1, topLeft, w + h);
            output[y * stride + x] = (short)((a * (64 - frac) + b * frac + 32) >> 6);
          }
        }
      }
    } else {
      // angle > 180: use left samples
      var dy = _GetDirectionalDy(angle);
      for (var y = 0; y < h; ++y) {
        for (var x = 0; x < w; ++x) {
          var shift = (x + 1) * dy;
          var basePos = y + (shift >> 6);
          var frac = shift & 63;

          if (frac == 0)
            output[y * stride + x] = _SafeRef(left, basePos, topLeft, w + h);
          else {
            var a = _SafeRef(left, basePos, topLeft, w + h);
            var b = _SafeRef(left, basePos + 1, topLeft, w + h);
            output[y * stride + x] = (short)((a * (64 - frac) + b * frac + 32) >> 6);
          }
        }
      }
    }
  }

  private static void _PredictCfl(int w, int h, short[] above, short[] left, short[] output, int stride, short maxVal, short[]? lumaResiduals, int alpha, int sign) {
    // First apply DC prediction
    _PredictDc(w, h, above, left, output, stride);

    if (lumaResiduals == null || alpha == 0)
      return;

    // Scale luma to chroma resolution and apply
    var scaledAlpha = sign == 0 ? alpha : -alpha;
    for (var y = 0; y < h; ++y) {
      for (var x = 0; x < w; ++x) {
        var lumaIdx = y * w + x;
        if (lumaIdx < lumaResiduals.Length) {
          var pred = output[y * stride + x] + ((scaledAlpha * lumaResiduals[lumaIdx] + 32) >> 6);
          output[y * stride + x] = (short)Math.Clamp(pred, 0, maxVal);
        }
      }
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static short _SafeRef(short[] refs, int index, short fallback, int maxLen) {
    if (index < 0)
      return fallback;
    if (index >= refs.Length || index >= maxLen)
      return refs.Length > 0 ? refs[Math.Min(index, refs.Length - 1)] : fallback;
    return refs[index];
  }

  // Smooth weight tables from AV1 spec (for smooth prediction)
  private static readonly int[] _SMOOTH_WEIGHTS_4 = [255, 149, 85, 64];
  private static readonly int[] _SMOOTH_WEIGHTS_8 = [255, 197, 146, 105, 73, 50, 37, 32];
  private static readonly int[] _SMOOTH_WEIGHTS_16 = [255, 225, 196, 170, 145, 123, 102, 84, 68, 54, 43, 33, 26, 20, 17, 16];
  private static readonly int[] _SMOOTH_WEIGHTS_32 = [
    255, 240, 225, 210, 196, 182, 169, 157, 145, 133, 122, 111, 101, 92, 83, 74,
    66, 59, 52, 45, 39, 34, 29, 25, 21, 17, 14, 12, 10, 9, 8, 8
  ];
  private static readonly int[] _SMOOTH_WEIGHTS_64 = [
    255, 248, 240, 233, 225, 218, 210, 203, 196, 189, 182, 176, 169, 163, 156, 150,
    144, 138, 133, 127, 121, 116, 111, 106, 101, 96, 91, 86, 82, 77, 73, 69,
    65, 61, 57, 54, 50, 47, 44, 41, 38, 35, 32, 29, 27, 25, 22, 20,
    18, 16, 15, 13, 12, 10, 9, 8, 7, 6, 6, 5, 5, 4, 4, 4
  ];

  private static int[] _GetSmoothWeights(int size) => size switch {
    4 => _SMOOTH_WEIGHTS_4,
    8 => _SMOOTH_WEIGHTS_8,
    16 => _SMOOTH_WEIGHTS_16,
    32 => _SMOOTH_WEIGHTS_32,
    64 => _SMOOTH_WEIGHTS_64,
    _ => _GenerateSmoothWeights(size),
  };

  private static int[] _GenerateSmoothWeights(int size) {
    var weights = new int[size];
    for (var i = 0; i < size; ++i)
      weights[i] = 256 * (size - 1 - i) / (size - 1);
    return weights;
  }

  // Directional prediction tangent lookup
  // dx and dy are in 1/64 pixel units for directional angles
  private static int _GetDirectionalDx(int angle) {
    if (angle == 0 || angle == 180)
      return 0;
    // Approximate: for angle a, dx = 64 * cot(a * PI / 180)
    var rad = angle * Math.PI / 180.0;
    var tanVal = Math.Tan(rad);
    if (Math.Abs(tanVal) < 0.001)
      return 0;
    return (int)Math.Round(64.0 / tanVal);
  }

  private static int _GetDirectionalDy(int angle) {
    if (angle == 90 || angle == 270)
      return 0;
    var rad = angle * Math.PI / 180.0;
    return (int)Math.Round(64.0 * Math.Tan(rad));
  }
}
