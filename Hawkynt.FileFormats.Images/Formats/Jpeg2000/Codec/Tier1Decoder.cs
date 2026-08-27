using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>EBCOT bit-plane decoder for baseline JPEG 2000 code-blocks (ITU-T T.800 Annex D).</summary>
internal static class Tier1Decoder {

  private const int _CX_UNI = 0;
  private const int _CX_RL = 1;
  private const int _CX_SIG = 2;
  private const int _CX_SIGN = 11;
  private const int _CX_MAG = 16;
  private const int _NUM_CONTEXTS = 19;

  public static int[,] DecodeCodeBlock(byte[] data, int width, int height, int numPasses, int zeroBitPlanes) {
    ArgumentNullException.ThrowIfNull(data);
    _ = zeroBitPlanes; // P removes conceptual leading zero planes; it does not shift q's integer bits.

    var coefficients = new int[height, width];
    if (width <= 0 || height <= 0 || numPasses <= 0)
      return coefficients;

    var mq = new MqDecoder(data, 0, data.Length, _NUM_CONTEXTS);
    mq.SetContext(_CX_UNI, 46, 0);
    mq.SetContext(_CX_RL, 3, 0);
    mq.SetContext(_CX_SIG, 4, 0);

    var significance = new bool[height, width];
    var refined = new bool[height, width];
    var newlySignificant = new bool[height, width];

    // One first cleanup pass followed by groups of significance/refinement/cleanup. P says how many
    // more-significant conceptual planes were absent, but q itself starts at bit codingBitPlanes-1.
    var codingBitPlanes = (numPasses + 2) / 3;
    var pass = 0;

    for (var planeIndex = 0; planeIndex < codingBitPlanes && pass < numPasses; ++planeIndex) {
      var bitValue = 1 << (codingBitPlanes - 1 - planeIndex);
      Array.Clear(newlySignificant);

      if (planeIndex == 0) {
        _CleanupPass(mq, coefficients, significance, width, height, bitValue);
        ++pass;
        continue;
      }

      if (pass < numPasses) {
        _SignificancePropagationPass(
          mq, coefficients, significance, newlySignificant, width, height, bitValue);
        ++pass;
      }

      if (pass < numPasses) {
        _MagnitudeRefinementPass(
          mq, coefficients, significance, newlySignificant, refined, width, height, bitValue);
        ++pass;
      }

      if (pass < numPasses) {
        _CleanupPass(mq, coefficients, significance, width, height, bitValue);
        ++pass;
      }
    }

    return coefficients;
  }

  private static void _SignificancePropagationPass(
    MqDecoder mq,
    int[,] coefficients,
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
        if (mq.DecodeBit(context) == 0)
          continue;

        significance[y, x] = true;
        newlySignificant[y, x] = true;
        var sign = _DecodeSign(mq, significance, coefficients, x, y, width, height);
        coefficients[y, x] = sign == 0 ? bitValue : -bitValue;
      }
  }

  private static void _MagnitudeRefinementPass(
    MqDecoder mq,
    int[,] coefficients,
    bool[,] significance,
    bool[,] newlySignificant,
    bool[,] refined,
    int width,
    int height,
    int bitValue
  ) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (!significance[y, x] || newlySignificant[y, x])
          continue;

        var context = _CX_MAG + _GetMagnitudeContext(significance, refined, x, y, width, height);
        if (mq.DecodeBit(context) != 0) {
          if (coefficients[y, x] >= 0)
            coefficients[y, x] |= bitValue;
          else
            coefficients[y, x] = -((-coefficients[y, x]) | bitValue);
        }
        refined[y, x] = true;
      }
  }

  private static void _CleanupPass(
    MqDecoder mq,
    int[,] coefficients,
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
          if (mq.DecodeBit(_CX_RL) == 0)
            continue;

          var first = (mq.DecodeBit(_CX_UNI) << 1) | mq.DecodeBit(_CX_UNI);
          significance[stripeY + first, x] = true;
          var sign = _DecodeSign(mq, significance, coefficients, x, stripeY + first, width, height);
          coefficients[stripeY + first, x] = sign == 0 ? bitValue : -bitValue;

          for (var row = first + 1; row < 4; ++row) {
            var y = stripeY + row;
            if (significance[y, x])
              continue;

            var context = _CX_SIG + _GetLlSignificanceContext(significance, x, y, width, height);
            if (mq.DecodeBit(context) == 0)
              continue;

            significance[y, x] = true;
            sign = _DecodeSign(mq, significance, coefficients, x, y, width, height);
            coefficients[y, x] = sign == 0 ? bitValue : -bitValue;
          }

          continue;
        }

        for (var row = 0; row < rows; ++row) {
          var y = stripeY + row;
          if (significance[y, x])
            continue;

          var context = _CX_SIG + _GetLlSignificanceContext(significance, x, y, width, height);
          if (mq.DecodeBit(context) == 0)
            continue;

          significance[y, x] = true;
          var sign = _DecodeSign(mq, significance, coefficients, x, y, width, height);
          coefficients[y, x] = sign == 0 ? bitValue : -bitValue;
        }
      }
  }

  private static int _DecodeSign(
    MqDecoder mq,
    bool[,] significance,
    int[,] coefficients,
    int x,
    int y,
    int width,
    int height
  ) {
    var horizontal = _GetSignContribution(significance, coefficients, x - 1, y, width, height)
                   + _GetSignContribution(significance, coefficients, x + 1, y, width, height);
    var vertical = _GetSignContribution(significance, coefficients, x, y - 1, width, height)
                 + _GetSignContribution(significance, coefficients, x, y + 1, width, height);

    horizontal = Math.Clamp(horizontal, -1, 1);
    vertical = Math.Clamp(vertical, -1, 1);
    _GetSignContext(horizontal, vertical, out var contextOffset, out var xorBit);
    return mq.DecodeBit(_CX_SIGN + contextOffset) ^ xorBit;
  }

  private static int _GetSignContribution(bool[,] significance, int[,] coefficients, int x, int y, int width, int height) {
    if (x < 0 || x >= width || y < 0 || y >= height || !significance[y, x])
      return 0;
    return coefficients[y, x] > 0 ? 1 : -1;
  }

  private static void _GetSignContext(int horizontal, int vertical, out int contextOffset, out int xorBit) {
    if (horizontal == 0) {
      if (vertical == 0) {
        contextOffset = 0;
        xorBit = 0;
      } else {
        contextOffset = 1;
        xorBit = vertical < 0 ? 1 : 0;
      }
      return;
    }

    if (horizontal > 0) {
      contextOffset = vertical switch { > 0 => 4, 0 => 3, _ => 2 };
      xorBit = 0;
      return;
    }

    contextOffset = vertical switch { < 0 => 4, 0 => 3, _ => 2 };
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
