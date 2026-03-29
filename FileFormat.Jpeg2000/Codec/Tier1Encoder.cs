using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>EBCOT bit-plane encoder: significance propagation, magnitude refinement, cleanup passes (ITU-T T.800 Section D).</summary>
internal static class Tier1Encoder {

  // MQ context indices matching the decoder
  private const int _CX_UNI = 0;
  private const int _CX_RL = 1;
  private const int _CX_SIG = 2;
  private const int _CX_SIGN = 11;
  private const int _CX_MAG = 16;
  private const int _NUM_CONTEXTS = 19;

  /// <summary>Encode a code-block's wavelet coefficients using EBCOT.</summary>
  /// <param name="coeffs">2D coefficient array [height, width].</param>
  /// <param name="width">Code-block width.</param>
  /// <param name="height">Code-block height.</param>
  /// <param name="numPasses">Output: number of coding passes actually emitted.</param>
  /// <param name="zeroBitPlanes">Output: number of leading zero bit-planes.</param>
  /// <returns>MQ-coded compressed data.</returns>
  public static byte[] EncodeCodeBlock(int[,] coeffs, int width, int height, out int numPasses, out int zeroBitPlanes) {
    numPasses = 0;
    zeroBitPlanes = 0;

    if (width <= 0 || height <= 0) {
      numPasses = 0;
      zeroBitPlanes = 0;
      return [];
    }

    // Determine max magnitude to find the number of bit-planes
    var maxMag = 0;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var mag = Math.Abs(coeffs[y, x]);
        if (mag > maxMag)
          maxMag = mag;
      }

    if (maxMag == 0) {
      numPasses = 0;
      zeroBitPlanes = 0;
      return [];
    }

    // Find highest bit position
    var totalBitPlanes = 0;
    {
      var tmp = maxMag;
      while (tmp > 0) {
        ++totalBitPlanes;
        tmp >>= 1;
      }
    }

    // Count leading zero bit-planes
    zeroBitPlanes = 0;
    for (var bp = totalBitPlanes - 1; bp >= 0; --bp) {
      var bitValue = 1 << bp;
      var anySet = false;
      for (var y = 0; y < height && !anySet; ++y)
        for (var x = 0; x < width && !anySet; ++x)
          if ((Math.Abs(coeffs[y, x]) & bitValue) != 0)
            anySet = true;

      if (anySet)
        break;

      ++zeroBitPlanes;
    }

    var codingBitPlanes = totalBitPlanes - zeroBitPlanes;
    if (codingBitPlanes <= 0) {
      numPasses = 0;
      return [];
    }

    var mq = new MqEncoder(_NUM_CONTEXTS);
    mq.SetContext(_CX_UNI, 46, 0);
    mq.SetContext(_CX_RL, 3, 0);

    var significance = new bool[height, width];
    var refined = new bool[height, width];
    var signs = new int[height, width]; // 0 = positive, 1 = negative

    // Pre-compute sign array
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        signs[y, x] = coeffs[y, x] < 0 ? 1 : 0;

    numPasses = 0;
    for (var bpIdx = 0; bpIdx < codingBitPlanes; ++bpIdx) {
      var bp = totalBitPlanes - 1 - zeroBitPlanes - bpIdx;
      var bitValue = 1 << bp;

      if (bpIdx == 0) {
        // First coded bit-plane: only cleanup pass
        _CleanupPassEncode(mq, coeffs, signs, significance, width, height, bitValue);
        ++numPasses;
      } else {
        _SignificancePropagationPassEncode(mq, coeffs, signs, significance, width, height, bitValue);
        ++numPasses;
        _MagnitudeRefinementPassEncode(mq, coeffs, significance, refined, width, height, bitValue);
        ++numPasses;
        _CleanupPassEncode(mq, coeffs, signs, significance, width, height, bitValue);
        ++numPasses;
      }
    }

    return mq.Flush();
  }

  private static void _SignificancePropagationPassEncode(
    MqEncoder mq, int[,] coeffs, int[,] signs, bool[,] significance,
    int width, int height, int bitValue
  ) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (significance[y, x])
          continue;

        if (!_HasSignificantNeighbor(significance, x, y, width, height))
          continue;

        var ctx = _CX_SIG + _GetSignificanceContext(significance, x, y, width, height);
        var mag = Math.Abs(coeffs[y, x]);
        var bit = (mag & bitValue) != 0 ? 1 : 0;
        mq.EncodeBit(ctx, bit);

        if (bit != 0) {
          significance[y, x] = true;
          _EncodeSign(mq, significance, coeffs, signs, x, y, width, height);
        }
      }
  }

  private static void _MagnitudeRefinementPassEncode(
    MqEncoder mq, int[,] coeffs, bool[,] significance, bool[,] refined,
    int width, int height, int bitValue
  ) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (!significance[y, x])
          continue;

        var ctx = _CX_MAG + _GetMagnitudeContext(significance, refined, x, y, width, height);
        var mag = Math.Abs(coeffs[y, x]);
        var bit = (mag & bitValue) != 0 ? 1 : 0;
        mq.EncodeBit(ctx, bit);
        refined[y, x] = true;
      }
  }

  private static void _CleanupPassEncode(
    MqEncoder mq, int[,] coeffs, int[,] signs, bool[,] significance,
    int width, int height, int bitValue
  ) {
    for (var y = 0; y < height; y += 4)
      for (var x = 0; x < width; ++x) {
        var rowsInStripe = Math.Min(4, height - y);

        // Check if run-length mode can be used
        var canRunLength = rowsInStripe == 4;
        if (canRunLength)
          for (var r = 0; r < 4; ++r)
            if (significance[y + r, x] || _HasSignificantNeighbor(significance, x, y + r, width, height)) {
              canRunLength = false;
              break;
            }

        if (canRunLength) {
          // Check if any of the 4 samples become significant
          var firstSig = -1;
          for (var r = 0; r < 4; ++r)
            if ((Math.Abs(coeffs[y + r, x]) & bitValue) != 0) {
              firstSig = r;
              break;
            }

          if (firstSig < 0) {
            mq.EncodeBit(_CX_RL, 0); // All zero
            continue;
          }

          mq.EncodeBit(_CX_RL, 1); // At least one significant
          mq.EncodeBit(_CX_UNI, (firstSig >> 1) & 1);
          mq.EncodeBit(_CX_UNI, firstSig & 1);

          significance[y + firstSig, x] = true;
          _EncodeSign(mq, significance, coeffs, signs, x, y + firstSig, width, height);

          // Process remaining samples normally
          for (var r = firstSig + 1; r < 4; ++r) {
            var yy = y + r;
            if (significance[yy, x])
              continue;

            var ctx = _CX_SIG + _GetSignificanceContext(significance, x, yy, width, height);
            var mag = Math.Abs(coeffs[yy, x]);
            var bit = (mag & bitValue) != 0 ? 1 : 0;
            mq.EncodeBit(ctx, bit);
            if (bit != 0) {
              significance[yy, x] = true;
              _EncodeSign(mq, significance, coeffs, signs, x, yy, width, height);
            }
          }
        } else {
          for (var r = 0; r < rowsInStripe; ++r) {
            var yy = y + r;
            if (significance[yy, x])
              continue;

            var ctx = _CX_SIG + _GetSignificanceContext(significance, x, yy, width, height);
            var mag = Math.Abs(coeffs[yy, x]);
            var bit = (mag & bitValue) != 0 ? 1 : 0;
            mq.EncodeBit(ctx, bit);
            if (bit != 0) {
              significance[yy, x] = true;
              _EncodeSign(mq, significance, coeffs, signs, x, yy, width, height);
            }
          }
        }
      }
  }

  private static void _EncodeSign(MqEncoder mq, bool[,] significance, int[,] coeffs, int[,] signs, int x, int y, int width, int height) {
    var hContrib = _GetSignContribution(significance, coeffs, x - 1, y, width, height)
                 + _GetSignContribution(significance, coeffs, x + 1, y, width, height);
    var vContrib = _GetSignContribution(significance, coeffs, x, y - 1, width, height)
                 + _GetSignContribution(significance, coeffs, x, y + 1, width, height);

    _GetSignContext(hContrib, vContrib, out var ctxOffset, out var xorBit);
    mq.EncodeBit(_CX_SIGN + ctxOffset, signs[y, x] ^ xorBit);
  }

  private static int _GetSignContribution(bool[,] significance, int[,] coeffs, int x, int y, int width, int height) {
    if (x < 0 || x >= width || y < 0 || y >= height || !significance[y, x])
      return 0;

    return coeffs[y, x] > 0 ? 1 : -1;
  }

  private static void _GetSignContext(int h, int v, out int ctxOffset, out int xorBit) {
    if (h > 1) h = 1;
    if (h < -1) h = -1;
    if (v > 1) v = 1;
    if (v < -1) v = -1;

    if (h == 0 && v == 0) {
      ctxOffset = 0;
      xorBit = 0;
    } else if (h >= 0 && v >= 0) {
      ctxOffset = h + v > 1 ? 4 : h + v == 1 ? 2 : 0;
      xorBit = 0;
    } else if (h <= 0 && v <= 0) {
      ctxOffset = (-h) + (-v) > 1 ? 4 : (-h) + (-v) == 1 ? 2 : 0;
      xorBit = 1;
    } else {
      if (Math.Abs(h) > Math.Abs(v))
        xorBit = h < 0 ? 1 : 0;
      else
        xorBit = v < 0 ? 1 : 0;

      ctxOffset = 1;
    }
  }

  private static bool _HasSignificantNeighbor(bool[,] significance, int x, int y, int width, int height) {
    for (var dy = -1; dy <= 1; ++dy)
      for (var dx = -1; dx <= 1; ++dx) {
        if (dx == 0 && dy == 0)
          continue;

        var nx = x + dx;
        var ny = y + dy;
        if (nx >= 0 && nx < width && ny >= 0 && ny < height && significance[ny, nx])
          return true;
      }

    return false;
  }

  private static int _GetSignificanceContext(bool[,] significance, int x, int y, int width, int height) {
    var h = 0;
    var v = 0;
    var d = 0;

    if (x > 0 && significance[y, x - 1]) ++h;
    if (x + 1 < width && significance[y, x + 1]) ++h;
    if (y > 0 && significance[y - 1, x]) ++v;
    if (y + 1 < height && significance[y + 1, x]) ++v;
    if (x > 0 && y > 0 && significance[y - 1, x - 1]) ++d;
    if (x + 1 < width && y > 0 && significance[y - 1, x + 1]) ++d;
    if (x > 0 && y + 1 < height && significance[y + 1, x - 1]) ++d;
    if (x + 1 < width && y + 1 < height && significance[y + 1, x + 1]) ++d;

    if (h == 2) return 8;
    if (h == 1) {
      if (v >= 1) return 7;
      if (d >= 1) return 6;
      return 5;
    }
    if (v == 2) return 4;
    if (v == 1) {
      if (d >= 1) return 3;
      return 2;
    }
    if (d >= 2) return 1;
    return 0;
  }

  private static int _GetMagnitudeContext(bool[,] significance, bool[,] refined, int x, int y, int width, int height) {
    if (!refined[y, x])
      return _HasSignificantNeighbor(significance, x, y, width, height) ? 1 : 0;

    return 2;
  }
}
