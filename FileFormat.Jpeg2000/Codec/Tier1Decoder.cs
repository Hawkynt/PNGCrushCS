using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>EBCOT bit-plane decoder: significance propagation, magnitude refinement, cleanup passes (ITU-T T.800 Section D).</summary>
internal static class Tier1Decoder {

  // MQ context indices (ITU-T T.800 Table D.1)
  private const int _CX_UNI = 0;   // Uniform context
  private const int _CX_RL = 1;    // Run-length context
  private const int _CX_SIG = 2;   // Significance contexts: 2..10 (9 contexts)
  private const int _CX_SIGN = 11; // Sign contexts: 11..15 (5 contexts)
  private const int _CX_MAG = 16;  // Magnitude refinement contexts: 16..18 (3 contexts)
  private const int _NUM_CONTEXTS = 19;

  /// <summary>Decode a code-block's wavelet coefficients from MQ-coded data.</summary>
  /// <param name="data">Compressed code-block data.</param>
  /// <param name="width">Code-block width.</param>
  /// <param name="height">Code-block height.</param>
  /// <param name="numPasses">Number of coding passes to decode.</param>
  /// <param name="zeroBitPlanes">Number of leading zero bit-planes.</param>
  /// <returns>2D array of decoded integer coefficients.</returns>
  public static int[,] DecodeCodeBlock(byte[] data, int width, int height, int numPasses, int zeroBitPlanes) {
    if (width <= 0 || height <= 0 || numPasses <= 0)
      return new int[height, width];

    var mq = new MqDecoder(data, 0, data.Length, _NUM_CONTEXTS);
    // Initialize contexts per spec: uniform context at state 46, run-length at state 3
    mq.SetContext(_CX_UNI, 46, 0);
    mq.SetContext(_CX_RL, 3, 0);

    var coeffs = new int[height, width];
    var significance = new bool[height, width];
    var refined = new bool[height, width]; // Track whether a coefficient has been refined at least once

    // Determine the number of magnitude bits and starting bit-plane
    // Total bit-planes = zeroBitPlanes + ceil(numPasses / 3)
    var codingBitPlanes = (numPasses + 2) / 3;
    var totalBitPlanes = zeroBitPlanes + codingBitPlanes;

    var passIndex = 0;
    for (var bp = totalBitPlanes - 1; bp >= zeroBitPlanes && passIndex < numPasses; --bp) {
      var bitValue = 1 << bp;

      // Each bit-plane has up to 3 passes: significance propagation, magnitude refinement, cleanup
      // First bit-plane only has cleanup pass

      if (bp == totalBitPlanes - 1) {
        // First coded bit-plane: only cleanup pass
        _CleanupPass(mq, coeffs, significance, width, height, bitValue);
        ++passIndex;
      } else {
        // Significance propagation pass
        if (passIndex < numPasses) {
          _SignificancePropagationPass(mq, coeffs, significance, width, height, bitValue);
          ++passIndex;
        }

        // Magnitude refinement pass
        if (passIndex < numPasses) {
          _MagnitudeRefinementPass(mq, coeffs, significance, refined, width, height, bitValue);
          ++passIndex;
        }

        // Cleanup pass
        if (passIndex < numPasses) {
          _CleanupPass(mq, coeffs, significance, width, height, bitValue);
          ++passIndex;
        }
      }
    }

    return coeffs;
  }

  /// <summary>Significance propagation pass: decode insignificant samples with at least one significant neighbor.</summary>
  private static void _SignificancePropagationPass(
    MqDecoder mq, int[,] coeffs, bool[,] significance,
    int width, int height, int bitValue
  ) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (significance[y, x])
          continue;

        if (!_HasSignificantNeighbor(significance, x, y, width, height))
          continue;

        var ctx = _CX_SIG + _GetSignificanceContext(significance, x, y, width, height);
        var bit = mq.DecodeBit(ctx);
        if (bit != 0) {
          significance[y, x] = true;
          var sign = _DecodeSign(mq, significance, coeffs, x, y, width, height);
          coeffs[y, x] = sign == 0 ? bitValue : -bitValue;
        }
      }
  }

  /// <summary>Magnitude refinement pass: refine already-significant samples.</summary>
  private static void _MagnitudeRefinementPass(
    MqDecoder mq, int[,] coeffs, bool[,] significance, bool[,] refined,
    int width, int height, int bitValue
  ) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (!significance[y, x])
          continue;

        var ctx = _CX_MAG + _GetMagnitudeContext(significance, refined, x, y, width, height);
        var bit = mq.DecodeBit(ctx);
        if (bit != 0) {
          if (coeffs[y, x] > 0)
            coeffs[y, x] |= bitValue;
          else
            coeffs[y, x] = -((-coeffs[y, x]) | bitValue);
        }

        refined[y, x] = true;
      }
  }

  /// <summary>Cleanup pass: decode all remaining insignificant samples. Uses run-length mode for vertical stripes of 4.</summary>
  private static void _CleanupPass(
    MqDecoder mq, int[,] coeffs, bool[,] significance,
    int width, int height, int bitValue
  ) {
    for (var y = 0; y < height; y += 4)
      for (var x = 0; x < width; ++x) {
        var rowsInStripe = Math.Min(4, height - y);

        // Check if run-length mode can be used: all 4 samples insignificant with no significant neighbors
        var canRunLength = rowsInStripe == 4;
        if (canRunLength)
          for (var r = 0; r < 4; ++r)
            if (significance[y + r, x] || _HasSignificantNeighbor(significance, x, y + r, width, height)) {
              canRunLength = false;
              break;
            }

        if (canRunLength) {
          // Run-length mode: decode 1 bit in run-length context
          var rl = mq.DecodeBit(_CX_RL);
          if (rl == 0)
            continue; // All 4 are zero

          // Decode the position of the significant sample (2 bits in uniform context)
          var pos = (mq.DecodeBit(_CX_UNI) << 1) | mq.DecodeBit(_CX_UNI);
          for (var r = 0; r < 4; ++r) {
            if (r == pos) {
              significance[y + r, x] = true;
              var sign = _DecodeSign(mq, significance, coeffs, x, y + r, width, height);
              coeffs[y + r, x] = sign == 0 ? bitValue : -bitValue;
            } else if (r > pos) {
              // Process remaining samples normally
              var ctx = _CX_SIG + _GetSignificanceContext(significance, x, y + r, width, height);
              var bit = mq.DecodeBit(ctx);
              if (bit != 0) {
                significance[y + r, x] = true;
                var sign = _DecodeSign(mq, significance, coeffs, x, y + r, width, height);
                coeffs[y + r, x] = sign == 0 ? bitValue : -bitValue;
              }
            }
          }
        } else {
          // Normal mode: process each row in the stripe
          for (var r = 0; r < rowsInStripe; ++r) {
            var yy = y + r;
            if (significance[yy, x])
              continue;

            var ctx = _CX_SIG + _GetSignificanceContext(significance, x, yy, width, height);
            var bit = mq.DecodeBit(ctx);
            if (bit != 0) {
              significance[yy, x] = true;
              var sign = _DecodeSign(mq, significance, coeffs, x, yy, width, height);
              coeffs[yy, x] = sign == 0 ? bitValue : -bitValue;
            }
          }
        }
      }
  }

  /// <summary>Decode the sign of a newly-significant coefficient (ITU-T T.800 Table D.6).</summary>
  private static int _DecodeSign(MqDecoder mq, bool[,] significance, int[,] coeffs, int x, int y, int width, int height) {
    var hContrib = _GetSignContribution(significance, coeffs, x - 1, y, width, height)
                 + _GetSignContribution(significance, coeffs, x + 1, y, width, height);
    var vContrib = _GetSignContribution(significance, coeffs, x, y - 1, width, height)
                 + _GetSignContribution(significance, coeffs, x, y + 1, width, height);

    // Map (hContrib, vContrib) to context index and XOR bit
    int ctxOffset;
    int xorBit;
    _GetSignContext(hContrib, vContrib, out ctxOffset, out xorBit);

    var bit = mq.DecodeBit(_CX_SIGN + ctxOffset);
    return bit ^ xorBit;
  }

  /// <summary>Get the contribution of a neighbor to sign prediction.</summary>
  private static int _GetSignContribution(bool[,] significance, int[,] coeffs, int x, int y, int width, int height) {
    if (x < 0 || x >= width || y < 0 || y >= height || !significance[y, x])
      return 0;

    return coeffs[y, x] > 0 ? 1 : -1;
  }

  /// <summary>Map horizontal/vertical sign contributions to sign context and XOR bit (ITU-T T.800 Table D.6).</summary>
  private static void _GetSignContext(int h, int v, out int ctxOffset, out int xorBit) {
    // Clamp contributions to [-1, 0, 1]
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
      // Mixed signs
      if (Math.Abs(h) > Math.Abs(v))
        xorBit = h < 0 ? 1 : 0;
      else
        xorBit = v < 0 ? 1 : 0;

      ctxOffset = 1;
    }
  }

  /// <summary>Check if any of the 8-connected neighbors is significant.</summary>
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

  /// <summary>Compute significance context (0-8) based on neighbor significance pattern (ITU-T T.800 Table D.3).</summary>
  private static int _GetSignificanceContext(bool[,] significance, int x, int y, int width, int height) {
    var h = 0; // Horizontal significant neighbors
    var v = 0; // Vertical significant neighbors
    var d = 0; // Diagonal significant neighbors

    if (x > 0 && significance[y, x - 1]) ++h;
    if (x + 1 < width && significance[y, x + 1]) ++h;
    if (y > 0 && significance[y - 1, x]) ++v;
    if (y + 1 < height && significance[y + 1, x]) ++v;

    if (x > 0 && y > 0 && significance[y - 1, x - 1]) ++d;
    if (x + 1 < width && y > 0 && significance[y - 1, x + 1]) ++d;
    if (x > 0 && y + 1 < height && significance[y + 1, x - 1]) ++d;
    if (x + 1 < width && y + 1 < height && significance[y + 1, x + 1]) ++d;

    // Map (h, v, d) to context index 0-8 (simplified mapping for HL/LH/HH subbands)
    if (h == 2)
      return 8;
    if (h == 1) {
      if (v >= 1) return 7;
      if (d >= 1) return 6;
      return 5;
    }
    if (v == 2)
      return 4;
    if (v == 1) {
      if (d >= 1) return 3;
      return 2;
    }
    if (d >= 2)
      return 1;

    return 0;
  }

  /// <summary>Compute magnitude refinement context (0-2) (ITU-T T.800 Table D.5).</summary>
  private static int _GetMagnitudeContext(bool[,] significance, bool[,] refined, int x, int y, int width, int height) {
    if (!refined[y, x]) {
      // First refinement: check if any neighbor is significant
      if (_HasSignificantNeighbor(significance, x, y, width, height))
        return 1;

      return 0;
    }

    return 2;
  }
}
