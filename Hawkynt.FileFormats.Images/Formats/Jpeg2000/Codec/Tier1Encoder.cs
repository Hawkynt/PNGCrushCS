using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>EBCOT bit-plane encoder for the baseline LL authoring path (ITU-T T.800 Annex D).</summary>
internal static class Tier1Encoder {

  // Internal indices are implementation-private. They keep one independent MQ state for every
  // semantic context; the mapping does not have to equal the numeric labels printed in Annex D.
  private const int _CX_UNI = 0;
  private const int _CX_RL = 1;
  private const int _CX_SIG = 2;   // 2..10, Table D.1 contexts 0..8
  private const int _CX_SIGN = 11; // 11..15, five Table D.3 contexts
  private const int _CX_MAG = 16;  // 16..18, three Table D.4 contexts
  private const int _NUM_CONTEXTS = 19;

  /// <summary>
  /// Encodes one code-block. This package authors 8-bit reversible JPEG 2000, so all eight available
  /// magnitude bit-planes are coded for every non-zero block and P is deliberately zero. Coding a
  /// few leading all-zero planes is less compact than signalling P, but is completely normative and
  /// avoids making rate allocation part of the lossless writer.
  /// </summary>
  public static byte[] EncodeCodeBlock(int[,] coeffs, int width, int height, out int numPasses, out int zeroBitPlanes) {
    ArgumentNullException.ThrowIfNull(coeffs);
    numPasses = 0;
    zeroBitPlanes = 0;

    if (width <= 0 || height <= 0)
      return [];

    var maxMagnitude = 0;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        maxMagnitude = Math.Max(maxMagnitude, Math.Abs(coeffs[y, x]));

    if (maxMagnitude == 0)
      return [];

    // The RawImage writer is 8-bit and uses zero wavelet decompositions. After the unsigned DC
    // level shift its coefficients are in [-128,127], so eight magnitude planes are sufficient.
    // Refuse a manually constructed higher-dynamic-range block rather than emit a truncated one.
    if (maxMagnitude > 255)
      throw new NotSupportedException(
        $"The JPEG 2000 baseline writer received a code-block magnitude of {maxMagnitude}; its 8-bit authoring profile permits at most 255.");

    const int codingBitPlanes = 8;

    var mq = new MqEncoder(_NUM_CONTEXTS);
    mq.SetContext(_CX_UNI, 46, 0);
    mq.SetContext(_CX_RL, 3, 0);
    mq.SetContext(_CX_SIG, 4, 0); // Table D.7: all-zero-neighbours significance context

    var significance = new bool[height, width];
    var refined = new bool[height, width];
    var newlySignificant = new bool[height, width];
    var signs = new int[height, width];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        signs[y, x] = coeffs[y, x] < 0 ? 1 : 0;

    for (var planeIndex = 0; planeIndex < codingBitPlanes; ++planeIndex) {
      var bit = codingBitPlanes - 1 - planeIndex;
      var bitValue = 1 << bit;
      Array.Clear(newlySignificant);

      if (planeIndex == 0) {
        _CleanupPassEncode(mq, coeffs, signs, significance, width, height, bitValue);
        ++numPasses;
        continue;
      }

      _SignificancePropagationPassEncode(
        mq, coeffs, signs, significance, newlySignificant, width, height, bitValue);
      ++numPasses;

      _MagnitudeRefinementPassEncode(
        mq, coeffs, significance, newlySignificant, refined, width, height, bitValue);
      ++numPasses;

      _CleanupPassEncode(mq, coeffs, signs, significance, width, height, bitValue);
      ++numPasses;
    }

    return mq.Flush();
  }

  private static void _SignificancePropagationPassEncode(
    MqEncoder mq,
    int[,] coeffs,
    int[,] signs,
    bool[,] significance,
    bool[,] newlySignificant,
    int width,
    int height,
    int bitValue
  ) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (significance[y, x] || !_HasSignificantNeighbor(significance, x, y, width, height))
          continue;

        var context = _CX_SIG + _GetLlSignificanceContext(significance, x, y, width, height);
        var symbol = (Math.Abs(coeffs[y, x]) & bitValue) != 0 ? 1 : 0;
        mq.EncodeBit(context, symbol);

        if (symbol == 0)
          continue;

        significance[y, x] = true;
        newlySignificant[y, x] = true;
        _EncodeSign(mq, significance, coeffs, signs, x, y, width, height);
      }
  }

  private static void _MagnitudeRefinementPassEncode(
    MqEncoder mq,
    int[,] coeffs,
    bool[,] significance,
    bool[,] newlySignificant,
    bool[,] refined,
    int width,
    int height,
    int bitValue
  ) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        // D.3.3 explicitly excludes a coefficient that became significant in the immediately
        // preceding significance-propagation pass. The old encoder refined it immediately.
        if (!significance[y, x] || newlySignificant[y, x])
          continue;

        var context = _CX_MAG + _GetMagnitudeContext(significance, refined, x, y, width, height);
        mq.EncodeBit(context, (Math.Abs(coeffs[y, x]) & bitValue) != 0 ? 1 : 0);
        refined[y, x] = true;
      }
  }

  private static void _CleanupPassEncode(
    MqEncoder mq,
    int[,] coeffs,
    int[,] signs,
    bool[,] significance,
    int width,
    int height,
    int bitValue
  ) {
    for (var stripeY = 0; stripeY < height; stripeY += 4)
      for (var x = 0; x < width; ++x) {
        var rows = Math.Min(4, height - stripeY);
        var canUseRunMode = rows == 4;

        if (canUseRunMode)
          for (var row = 0; row < 4; ++row)
            if (significance[stripeY + row, x]
                || _HasSignificantNeighbor(significance, x, stripeY + row, width, height)) {
              canUseRunMode = false;
              break;
            }

        if (canUseRunMode) {
          var firstSignificant = -1;
          for (var row = 0; row < 4; ++row)
            if ((Math.Abs(coeffs[stripeY + row, x]) & bitValue) != 0) {
              firstSignificant = row;
              break;
            }

          if (firstSignificant < 0) {
            mq.EncodeBit(_CX_RL, 0);
            continue;
          }

          mq.EncodeBit(_CX_RL, 1);
          mq.EncodeBit(_CX_UNI, (firstSignificant >> 1) & 1);
          mq.EncodeBit(_CX_UNI, firstSignificant & 1);

          significance[stripeY + firstSignificant, x] = true;
          _EncodeSign(mq, significance, coeffs, signs, x, stripeY + firstSignificant, width, height);

          for (var row = firstSignificant + 1; row < 4; ++row) {
            var y = stripeY + row;
            if (significance[y, x])
              continue;

            var context = _CX_SIG + _GetLlSignificanceContext(significance, x, y, width, height);
            var symbol = (Math.Abs(coeffs[y, x]) & bitValue) != 0 ? 1 : 0;
            mq.EncodeBit(context, symbol);
            if (symbol == 0)
              continue;

            significance[y, x] = true;
            _EncodeSign(mq, significance, coeffs, signs, x, y, width, height);
          }

          continue;
        }

        for (var row = 0; row < rows; ++row) {
          var y = stripeY + row;
          if (significance[y, x])
            continue;

          var context = _CX_SIG + _GetLlSignificanceContext(significance, x, y, width, height);
          var symbol = (Math.Abs(coeffs[y, x]) & bitValue) != 0 ? 1 : 0;
          mq.EncodeBit(context, symbol);
          if (symbol == 0)
            continue;

          significance[y, x] = true;
          _EncodeSign(mq, significance, coeffs, signs, x, y, width, height);
        }
      }
  }

  private static void _EncodeSign(
    MqEncoder mq,
    bool[,] significance,
    int[,] coeffs,
    int[,] signs,
    int x,
    int y,
    int width,
    int height
  ) {
    var horizontal = _GetSignContribution(significance, coeffs, x - 1, y, width, height)
                   + _GetSignContribution(significance, coeffs, x + 1, y, width, height);
    var vertical = _GetSignContribution(significance, coeffs, x, y - 1, width, height)
                 + _GetSignContribution(significance, coeffs, x, y + 1, width, height);

    horizontal = Math.Clamp(horizontal, -1, 1);
    vertical = Math.Clamp(vertical, -1, 1);
    _GetSignContext(horizontal, vertical, out var contextOffset, out var xorBit);
    mq.EncodeBit(_CX_SIGN + contextOffset, signs[y, x] ^ xorBit);
  }

  private static int _GetSignContribution(bool[,] significance, int[,] coeffs, int x, int y, int width, int height) {
    if (x < 0 || x >= width || y < 0 || y >= height || !significance[y, x])
      return 0;
    return coeffs[y, x] > 0 ? 1 : -1;
  }

  /// <summary>Five semantic sign contexts from Table D.3, mapped to internal offsets 0..4.</summary>
  private static void _GetSignContext(int horizontal, int vertical, out int contextOffset, out int xorBit) {
    if (horizontal == 0) {
      if (vertical == 0) {
        contextOffset = 0; // Table D.3 label 9
        xorBit = 0;
      } else {
        contextOffset = 1; // label 10
        xorBit = vertical < 0 ? 1 : 0;
      }
      return;
    }

    if (horizontal > 0) {
      contextOffset = vertical switch {
        > 0 => 4, // label 13
        0 => 3,  // label 12
        _ => 2,  // label 11
      };
      xorBit = 0;
      return;
    }

    contextOffset = vertical switch {
      < 0 => 4,
      0 => 3,
      _ => 2,
    };
    xorBit = 1;
  }

  private static bool _HasSignificantNeighbor(bool[,] significance, int x, int y, int width, int height) {
    for (var dy = -1; dy <= 1; ++dy)
      for (var dx = -1; dx <= 1; ++dx) {
        if (dx == 0 && dy == 0)
          continue;

        var nx = x + dx;
        var ny = y + dy;
        if ((uint)nx < (uint)width && (uint)ny < (uint)height && significance[ny, nx])
          return true;
      }
    return false;
  }

  /// <summary>Table D.1 for LL/LH (the zero-decomposition authoring path is LL).</summary>
  private static int _GetLlSignificanceContext(bool[,] significance, int x, int y, int width, int height) {
    var horizontal = 0;
    var vertical = 0;
    var diagonal = 0;

    if (x > 0 && significance[y, x - 1]) ++horizontal;
    if (x + 1 < width && significance[y, x + 1]) ++horizontal;
    if (y > 0 && significance[y - 1, x]) ++vertical;
    if (y + 1 < height && significance[y + 1, x]) ++vertical;
    if (x > 0 && y > 0 && significance[y - 1, x - 1]) ++diagonal;
    if (x + 1 < width && y > 0 && significance[y - 1, x + 1]) ++diagonal;
    if (x > 0 && y + 1 < height && significance[y + 1, x - 1]) ++diagonal;
    if (x + 1 < width && y + 1 < height && significance[y + 1, x + 1]) ++diagonal;

    if (horizontal == 2) return 8;
    if (horizontal == 1 && vertical >= 1) return 7;
    if (horizontal == 1 && diagonal >= 1) return 6;
    if (horizontal == 1) return 5;
    if (vertical == 2) return 4;
    if (vertical == 1) return 3;
    if (diagonal >= 2) return 2;
    if (diagonal == 1) return 1;
    return 0;
  }

  private static int _GetMagnitudeContext(bool[,] significance, bool[,] refined, int x, int y, int width, int height) {
    if (refined[y, x])
      return 2;
    return _HasSignificantNeighbor(significance, x, y, width, height) ? 1 : 0;
  }
}
