using System;

namespace FileFormat.Wic;

/// <summary>The three single-precision wavelet filters JWIC offers, with circular boundaries.</summary>
/// <remarks>
/// Every multiply and add is single precision and happens in a fixed order; the order is part of the
/// format in practice, because the decoded picture is truncated to bytes and a different rounding of
/// one sum moves a pixel by one. A transform level filters the rows and then the columns of the
/// top-left <c>w × h</c> block; the inverse undoes the columns and then the rows.
/// </remarks>
internal static class WicWavelet {

  // Daubechies N = 2 (four taps), as GWIC took them from Vetterli & Kovačević, "Wavelets and Subband
  // Coding", p. 260, and the biorthogonal 9/7 analysis/synthesis pairs: values copied as published in
  // the JWIC sources, single precision.
  private static readonly float[] _DaubechiesHigh = [.129409522f, .22414386f, -.8365163f, .48296291f];
  private static readonly float[] _DaubechiesLow = [.48296291f, .8365163f, .22414386f, -.129409522f];
  private static readonly float[] _B97ForwardHigh = [0.788485f, -0.418092f, -0.040690f, 0.064539f, 0.0f];
  private static readonly float[] _B97InverseHigh = [0.852699f, -0.377403f, -0.110624f, 0.023849f, 0.037829f];
  private static readonly float[] _B97ForwardLow = [0.852699f, 0.377403f, -0.110624f, -0.023849f, 0.037829f];
  private static readonly float[] _B97InverseLow = [0.788485f, 0.418092f, -0.040690f, -0.064539f, 0.0f];

  /// <summary>How many levels a matrix of this size is decomposed into: halve while both sides are even and at least 8.</summary>
  public static int LevelsFor(int width, int height) {
    var levels = 0;
    while (((width | height) & 1) == 0 && width >= 8 && height >= 8) {
      width >>= 1;
      height >>= 1;
      ++levels;
    }

    return levels;
  }

  /// <summary>Decomposes the matrix in place and returns the number of levels.</summary>
  public static int Forward(float[] matrix, int offset, int width, int height, WicTransform transform) {
    var line = new float[Math.Max(width, height)];
    var filtered = new float[line.Length];
    var levels = 0;
    for (int w = width, h = height; ((w | h) & 1) == 0 && w >= 8 && h >= 8; w >>= 1, h >>= 1, ++levels) {
      for (var row = 0; row < h; ++row) {
        matrix.AsSpan(offset + row * width, w).CopyTo(line);
        _Analyse(line, filtered, w, transform);
        filtered.AsSpan(0, w).CopyTo(matrix.AsSpan(offset + row * width, w));
      }

      for (var column = 0; column < w; ++column) {
        for (var row = 0; row < h; ++row)
          line[row] = matrix[offset + row * width + column];
        _Analyse(line, filtered, h, transform);
        for (var row = 0; row < h; ++row)
          matrix[offset + row * width + column] = filtered[row];
      }
    }

    return levels;
  }

  /// <summary>Reassembles the matrix in place, re-deriving the level count from its size as JWIC decoders do.</summary>
  public static void Inverse(float[] matrix, int width, int height, WicTransform transform) {
    var line = new float[Math.Max(width, height)];
    var synthesised = new float[line.Length];
    var levels = LevelsFor(width, height);
    for (var level = levels - 1; level >= 0; --level) {
      var w = width >> level;
      var h = height >> level;
      for (var column = 0; column < w; ++column) {
        for (var row = 0; row < h; ++row)
          line[row] = matrix[row * width + column];
        _Synthesise(line, synthesised, h, transform);
        for (var row = 0; row < h; ++row)
          matrix[row * width + column] = synthesised[row];
      }

      for (var row = 0; row < h; ++row) {
        matrix.AsSpan(row * width, w).CopyTo(line);
        _Synthesise(line, synthesised, w, transform);
        synthesised.AsSpan(0, w).CopyTo(matrix.AsSpan(row * width, w));
      }
    }
  }

  private static int _Wrap(int index, int length) => index >= length ? index - length : index < 0 ? index + length : index;

  /// <summary>One analysis step: low-pass half then high-pass half.</summary>
  private static void _Analyse(float[] input, float[] output, int length, WicTransform transform) {
    var half = length >> 1;
    switch (transform) {
      case WicTransform.Haar:
        for (var i = 0; i < half; ++i) {
          var k = i << 1;
          output[i] = (input[k] + input[k + 1]) / 2;
          output[i + half] = input[k + 1] - input[k];
        }

        break;
      case WicTransform.Daubechies4:
        for (var i = 0; i < half; ++i) {
          float low = 0, high = 0;
          for (var tap = 0; tap < 4; ++tap) {
            var k = _Wrap((i << 1) + tap, length);
            low += input[k] * _DaubechiesLow[tap];
            high += input[k] * _DaubechiesHigh[tap];
          }

          output[i] = low;
          output[i + half] = high;
        }

        break;
      case WicTransform.Biorthogonal97:
        for (var i = 0; i < half; ++i) {
          var k = i << 1;
          var low = input[k] * _B97ForwardLow[0];
          var high = input[k + 1] * _B97ForwardHigh[0];
          for (var tap = 1; tap < 5; ++tap) {
            low += (input[_Wrap(k + tap, length)] + input[_Wrap(k - tap, length)]) * _B97ForwardLow[tap];
            high += (input[_Wrap(k + tap + 1, length)] + input[_Wrap(k - tap + 1, length)]) * _B97ForwardHigh[tap];
          }

          output[i] = low;
          output[i + half] = high;
        }

        break;
      default:
        throw new NotSupportedException($"JWIC transform {(int)transform} is not defined.");
    }
  }

  /// <summary>One synthesis step, accumulating every tap's contribution in the order the analysis visits them.</summary>
  private static void _Synthesise(float[] input, float[] output, int length, WicTransform transform) {
    var half = length >> 1;
    Array.Clear(output, 0, length);
    switch (transform) {
      case WicTransform.Haar:
        for (var i = 0; i < half; ++i) {
          var k = i << 1;
          output[k] = input[i] - input[i + half] / 2;
          output[k + 1] = input[i] + input[i + half] / 2;
        }

        break;
      case WicTransform.Daubechies4:
        for (var i = 0; i < half; ++i)
          for (var tap = 0; tap < 4; ++tap)
            output[_Wrap((i << 1) + tap, length)] += input[i] * _DaubechiesLow[tap] + input[i + half] * _DaubechiesHigh[tap];

        break;
      case WicTransform.Biorthogonal97:
        for (var i = 0; i < half; ++i) {
          var k = i << 1;
          var low = input[i];
          var high = input[i + half];
          output[k] += low * _B97InverseLow[0];
          output[k + 1] += high * _B97InverseHigh[0];
          for (var tap = 1; tap < 5; ++tap) {
            output[_Wrap(k + tap, length)] += low * _B97InverseLow[tap];
            output[_Wrap(k - tap, length)] += low * _B97InverseLow[tap];
            output[_Wrap(k + tap + 1, length)] += high * _B97InverseHigh[tap];
            output[_Wrap(k - tap + 1, length)] += high * _B97InverseHigh[tap];
          }
        }

        break;
      default:
        throw new NotSupportedException($"JWIC transform {(int)transform} is not defined.");
    }
  }
}
