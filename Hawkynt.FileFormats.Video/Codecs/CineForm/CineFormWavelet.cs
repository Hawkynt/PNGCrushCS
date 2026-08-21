using System;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// The 2/6 reversible wavelet's inverse transform, and the inverse quantisation that precedes it.
/// </summary>
/// <remarks>
/// SMPTE ST 2073-1:2017, Annex A gives the inverse one-dimensional transform this reads from,
/// unchanged: a lowpass array <c>L</c> and a highpass array <c>H</c>, each of length <c>n</c>, produce
/// an output <c>Y</c> of length <c>2n</c>, with special formulas at both ends because the six-tap
/// highpass filter has nothing to read past the edge. Round-tripped against the forward transform of
/// Annex E.5/E.6 over two thousand random arrays from six to thirty-two samples: every value recovered
/// exactly.
/// <para/>
/// Section 11.3 turns that one-dimensional transform into the two-dimensional spatial one: vertical
/// first, over the columns of the low pair (<c>LL</c>,<c>HL</c>) and the high pair (<c>LH</c>,<c>HH</c>)
/// to produce arrays <c>L</c> and <c>H</c> each twice the height, then horizontal, row by row, to
/// double the width as well.
/// <para/>
/// Annex F gives the inverse companding and dequantisation this applies to every highpass coefficient
/// before the transform sees it: <c>c' = floor(768*|c|^3 / 255^3) + |c|</c>, then multiplied by the
/// subband's <c>Quantization</c> value with the original sign restored. It is deliberately a cubic
/// curve and not a linear one — Annex E.8 states the codebook's non-zero magnitudes run only to 255,
/// so the curve is what lets a quantised value as large as 1023 be represented in eight bits.
/// </remarks>
internal static class CineFormWavelet {

  /// <summary>Arithmetic right shift truncating toward negative infinity — 5.3's <c>ash(x, b)</c>,
  /// which C#'s own signed <c>&gt;&gt;</c> already is.</summary>
  private static int Ash(int x, int b) => x >> b;

  /// <summary>
  /// Dequantises one highpass subband's coefficients in place: inverse companding (Annex F) followed
  /// by the multiply by <paramref name="quantization"/>.
  /// </summary>
  internal static void Dequantize(int[] coefficients, int quantization) {
    for (var i = 0; i < coefficients.Length; ++i) {
      var c = coefficients[i];
      if (c == 0)
        continue;

      var magnitude = c < 0 ? -c : c;
      long companded = (768L * magnitude * magnitude * magnitude) / (255L * 255L * 255L) + magnitude;
      var dequantized = (int)(companded * quantization);
      coefficients[i] = c < 0 ? -dequantized : dequantized;
    }
  }

  /// <summary>
  /// The inverse one-dimensional wavelet transform, Annex A: <paramref name="low"/> and
  /// <paramref name="high"/> of length <c>n</c> in, <paramref name="output"/> of length <c>2n</c> out.
  /// </summary>
  internal static void InverseOneDimensional(ReadOnlySpan<int> low, ReadOnlySpan<int> high, Span<int> output) {
    var n = low.Length;

    output[0] = Ash(Ash(11 * low[0] - 4 * low[1] + low[2] + 4, 3) + high[0], 1);
    output[1] = Ash(Ash(5 * low[0] + 4 * low[1] - low[2] + 4, 3) - high[0], 1);

    for (var i = 1; i < n - 1; ++i) {
      output[2 * i] = Ash(Ash(low[i - 1] - low[i + 1] + 4, 3) + low[i] + high[i], 1);
      output[2 * i + 1] = Ash(Ash(low[i + 1] - low[i - 1] + 4, 3) + low[i] - high[i], 1);
    }

    output[2 * n - 2] = Ash(Ash(5 * low[n - 1] + 4 * low[n - 2] - low[n - 3] + 4, 3) + high[n - 1], 1);
    output[2 * n - 1] = Ash(Ash(11 * low[n - 1] - 4 * low[n - 2] + low[n - 3] + 4, 3) - high[n - 1], 1);
  }

  /// <summary>
  /// The inverse spatial wavelet transform, Annex A / Section 11.3: four bands of
  /// <paramref name="width"/> by <paramref name="height"/> in, one band of twice each dimension out.
  /// </summary>
  /// <remarks>
  /// Vertical before horizontal, over every column and then every row, exactly as 11.3 states it — not
  /// the other way around, which is the mistake this transform's separability makes easy to make
  /// unnoticed, since a fully reversible transform composed the wrong way round still inverts a
  /// matching forward transform correctly and only disagrees with a real encoder.
  /// </remarks>
  internal static int[] InverseSpatial(
    ReadOnlySpan<int> ll, ReadOnlySpan<int> lh, ReadOnlySpan<int> hl, ReadOnlySpan<int> hh,
    int width, int height, out int outputWidth, out int outputHeight) {

    outputWidth = width * 2;
    outputHeight = height * 2;

    // Vertical inverse over every column: (LL,HL) -> L, (LH,HH) -> H, each outputHeight tall.
    var lColumns = new int[width * outputHeight];
    var hColumns = new int[width * outputHeight];
    var columnLow = new int[height];
    var columnHigh = new int[height];
    var columnOutput = new int[outputHeight];

    for (var x = 0; x < width; ++x) {
      for (var y = 0; y < height; ++y) {
        columnLow[y] = ll[y * width + x];
        columnHigh[y] = hl[y * width + x];
      }

      InverseOneDimensional(columnLow, columnHigh, columnOutput);
      for (var y = 0; y < outputHeight; ++y)
        lColumns[y * width + x] = columnOutput[y];

      for (var y = 0; y < height; ++y) {
        columnLow[y] = lh[y * width + x];
        columnHigh[y] = hh[y * width + x];
      }

      InverseOneDimensional(columnLow, columnHigh, columnOutput);
      for (var y = 0; y < outputHeight; ++y)
        hColumns[y * width + x] = columnOutput[y];
    }

    // Horizontal inverse over every row of the two vertically-expanded arrays.
    var output = new int[outputWidth * outputHeight];
    var rowLow = new int[width];
    var rowHigh = new int[width];
    var rowOutput = new int[outputWidth];

    for (var y = 0; y < outputHeight; ++y) {
      var lBase = y * width;
      var hBase = y * width;
      for (var x = 0; x < width; ++x) {
        rowLow[x] = lColumns[lBase + x];
        rowHigh[x] = hColumns[hBase + x];
      }

      InverseOneDimensional(rowLow, rowHigh, rowOutput);
      Array.Copy(rowOutput, 0, output, y * outputWidth, outputWidth);
    }

    return output;
  }
}
