using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Heif.Codec;

/// <summary>HEVC intra prediction modes: 35 modes (Planar, DC, and 33 angular directions).</summary>
internal enum HevcIntraPredMode {
  Planar = 0,
  Dc = 1,
  Angular2 = 2,   // vertical-ish
  Angular3 = 3,
  Angular4 = 4,
  Angular5 = 5,
  Angular6 = 6,
  Angular7 = 7,
  Angular8 = 8,
  Angular9 = 9,
  Angular10 = 10, // horizontal
  Angular11 = 11,
  Angular12 = 12,
  Angular13 = 13,
  Angular14 = 14,
  Angular15 = 15,
  Angular16 = 16,
  Angular17 = 17,
  Angular18 = 18, // diagonal (135 degrees)
  Angular19 = 19,
  Angular20 = 20,
  Angular21 = 21,
  Angular22 = 22,
  Angular23 = 23,
  Angular24 = 24,
  Angular25 = 25,
  Angular26 = 26, // vertical
  Angular27 = 27,
  Angular28 = 28,
  Angular29 = 29,
  Angular30 = 30,
  Angular31 = 31,
  Angular32 = 32,
  Angular33 = 33,
  Angular34 = 34, // horizontal-ish
}

/// <summary>Implements HEVC intra prediction for luma and chroma blocks.
/// Supports Planar, DC, and 33 angular prediction modes.</summary>
internal static class HeifIntraPredictor {

  // Intrinsic angles for modes 2-34 (H.265 Table 8-4)
  private static readonly int[] _INTRA_PRED_ANGLE = [
    0, 0, 32, 26, 21, 17, 13, 9, 5, 2, 0, -2, -5, -9, -13, -17, -21, -26,
    -32, -26, -21, -17, -13, -9, -5, -2, 0, 2, 5, 9, 13, 17, 21, 26, 32
  ];

  // Inverse angle table for modes that need projection from other side
  private static readonly int[] _INV_ANGLE = [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    -4096, -1638, -910, -630, -482, -390, -315,
    0, -315, -390, -482, -630, -910, -1638, -4096,
    0, 0, 0, 0, 0, 0, 0, 0, 0
  ];

  /// <summary>Predicts a block using the specified HEVC intra mode.</summary>
  public static void Predict(
    HevcIntraPredMode mode,
    int nTbS, // transform block size (4, 8, 16, 32)
    int bitDepth,
    short[] above,  // reference samples above (2*nTbS + 1, starting from top-left corner)
    short[] left,   // reference samples left (2*nTbS + 1, starting from top-left corner)
    short topLeft,
    short[] output,
    int outputStride
  ) {
    switch (mode) {
      case HevcIntraPredMode.Planar:
        _PredictPlanar(nTbS, above, left, output, outputStride);
        break;
      case HevcIntraPredMode.Dc:
        _PredictDc(nTbS, bitDepth, above, left, output, outputStride);
        break;
      default:
        _PredictAngular((int)mode, nTbS, bitDepth, above, left, topLeft, output, outputStride);
        break;
    }
  }

  private static void _PredictPlanar(int nTbS, short[] above, short[] left, short[] output, int stride) {
    var log2Size = _Log2(nTbS);
    var topRight = above[nTbS];
    var bottomLeft = left[nTbS];

    for (var y = 0; y < nTbS; ++y) {
      for (var x = 0; x < nTbS; ++x) {
        var pred =
          (nTbS - 1 - x) * left[y + 1] +
          (x + 1) * topRight +
          (nTbS - 1 - y) * above[x + 1] +
          (y + 1) * bottomLeft;

        output[y * stride + x] = (short)((pred + nTbS) >> (log2Size + 1));
      }
    }
  }

  private static void _PredictDc(int nTbS, int bitDepth, short[] above, short[] left, short[] output, int stride) {
    var dcVal = 0;
    for (var i = 0; i < nTbS; ++i) {
      dcVal += above[i + 1]; // above[0] is top-left, above[1..nTbS] are the top samples
      dcVal += left[i + 1];  // same for left
    }
    dcVal = (dcVal + nTbS) / (2 * nTbS);
    var dc = (short)dcVal;

    for (var y = 0; y < nTbS; ++y)
      for (var x = 0; x < nTbS; ++x)
        output[y * stride + x] = dc;

    // DC filtering for luma: smooth top-left corner
    if (nTbS < 32) {
      output[0] = (short)((above[1] + left[1] + 2 * dc + 2) >> 2);
      for (var x = 1; x < nTbS; ++x)
        output[x] = (short)((above[x + 1] + 3 * dc + 2) >> 2);
      for (var y = 1; y < nTbS; ++y)
        output[y * stride] = (short)((left[y + 1] + 3 * dc + 2) >> 2);
    }
  }

  private static void _PredictAngular(int mode, int nTbS, int bitDepth, short[] above, short[] left, short topLeft, short[] output, int stride) {
    var angle = _INTRA_PRED_ANGLE[mode];
    var maxVal = (1 << bitDepth) - 1;

    // Build reference array
    // For modes 2-17 (left-based): ref = left samples
    // For modes 18-34 (above-based): ref = above samples
    var refArray = new short[2 * nTbS + 1];

    if (mode >= 18) {
      // Above-based angular prediction
      refArray[0] = topLeft;
      for (var i = 0; i < 2 * nTbS && i + 1 < above.Length; ++i)
        refArray[i + 1] = above[i + 1];

      // Project from left side for negative angles
      if (angle < 0) {
        var invAngle = _INV_ANGLE[mode];
        var invAngleSum = 128;
        for (var i = -1; i >= -(nTbS * angle >> 5); --i) {
          invAngleSum += invAngle;
          var leftIdx = (invAngleSum >> 8) + 1;
          if (leftIdx >= 0 && leftIdx < left.Length)
            refArray[i + nTbS + 1] = left[leftIdx]; // shifted indexing
        }
      }

      for (var y = 0; y < nTbS; ++y) {
        for (var x = 0; x < nTbS; ++x) {
          var iIdx = ((y + 1) * angle) >> 5;
          var iFact = ((y + 1) * angle) & 31;

          var refIdx = x + 1 + iIdx;
          if (refIdx < 0) refIdx = 0;
          if (refIdx >= refArray.Length) refIdx = refArray.Length - 1;
          var refIdxNext = Math.Min(refIdx + 1, refArray.Length - 1);

          if (iFact != 0) {
            output[y * stride + x] = (short)Math.Clamp(
              ((32 - iFact) * refArray[refIdx] + iFact * refArray[refIdxNext] + 16) >> 5,
              0, maxVal);
          } else {
            output[y * stride + x] = refArray[refIdx];
          }
        }
      }
    } else {
      // Left-based angular prediction (modes 2-17)
      refArray[0] = topLeft;
      for (var i = 0; i < 2 * nTbS && i + 1 < left.Length; ++i)
        refArray[i + 1] = left[i + 1];

      // Project from above side for negative angles
      if (angle < 0) {
        var invAngle = _INV_ANGLE[mode];
        var invAngleSum = 128;
        for (var i = -1; i >= -(nTbS * angle >> 5); --i) {
          invAngleSum += invAngle;
          var aboveIdx = (invAngleSum >> 8) + 1;
          if (aboveIdx >= 0 && aboveIdx < above.Length)
            refArray[i + nTbS + 1] = above[aboveIdx];
        }
      }

      for (var y = 0; y < nTbS; ++y) {
        for (var x = 0; x < nTbS; ++x) {
          var iIdx = ((x + 1) * angle) >> 5;
          var iFact = ((x + 1) * angle) & 31;

          var refIdx = y + 1 + iIdx;
          if (refIdx < 0) refIdx = 0;
          if (refIdx >= refArray.Length) refIdx = refArray.Length - 1;
          var refIdxNext = Math.Min(refIdx + 1, refArray.Length - 1);

          if (iFact != 0) {
            output[y * stride + x] = (short)Math.Clamp(
              ((32 - iFact) * refArray[refIdx] + iFact * refArray[refIdxNext] + 16) >> 5,
              0, maxVal);
          } else {
            output[y * stride + x] = refArray[refIdx];
          }
        }
      }
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _Log2(int n) {
    var r = 0;
    while ((1 << r) < n)
      ++r;
    return r;
  }
}
