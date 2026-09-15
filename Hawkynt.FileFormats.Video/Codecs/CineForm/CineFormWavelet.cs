using System;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// The CineForm 2/6 reversible wavelet in both directions, plus the companding used around its
/// highpass coefficients.
/// </summary>
/// <remarks>
/// SMPTE ST 2073-1:2017, Annex A gives the inverse one-dimensional transform and Annex E.5/E.6 its
/// forward counterpart. A lowpass array <c>L</c> and highpass array <c>H</c>, each of length <c>n</c>,
/// reconstruct <c>2n</c> samples; the forward transform makes those same two bands from pairs of
/// samples, with the format's asymmetric six-tap border filters at either end. GoPro's MIT/Apache-2.0
/// WaveletDemo was used as a second source for the +4 rounding and border equations; the implementation
/// here is expressed independently around spans and the existing inverse rather than porting its C
/// control flow.
/// <para/>
/// Section 11.3 turns the one-dimensional transform into the two-dimensional spatial one. Encoding is
/// horizontal then vertical; decoding is the reverse, vertical then horizontal. The four resulting
/// bands are named LL, LH, HL and HH in the order the bitstream's three highpass subbands use.
/// <para/>
/// Annex F gives the companding curve used for highpass coefficients:
/// <c>c' = floor(768*|c|^3 / 255^3) + |c|</c>. Decoding applies that curve and then the subband's
/// quantisation value; encoding chooses the nearest codebook magnitude on the same curve.
/// </remarks>
internal static class CineFormWavelet {

  private static int Ash(int x, int b) => x >> b;

  /// <summary>Returns Annex F's inverse-companded magnitude for one codebook magnitude.</summary>
  internal static int CompandedMagnitude(int magnitude)
    => (int)((768L * magnitude * magnitude * magnitude) / (255L * 255L * 255L) + magnitude);

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
      var dequantized = CompandedMagnitude(magnitude) * quantization;
      coefficients[i] = c < 0 ? -dequantized : dequantized;
    }
  }

  /// <summary>
  /// Annex E's forward one-dimensional 2/6 transform: <paramref name="input"/> of length <c>2n</c>
  /// becomes <paramref name="low"/> and <paramref name="high"/> of length <c>n</c> each.
  /// </summary>
  internal static void ForwardOneDimensional(ReadOnlySpan<int> input, Span<int> low, Span<int> high) {
    var n = input.Length >> 1;
    if (input.Length < 6 || (input.Length & 1) != 0 || low.Length < n || high.Length < n)
      throw new ArgumentException("The CineForm 2/6 forward transform needs an even input of at least six samples and two half-sized outputs.");

    low[0] = input[0] + input[1];
    high[0] = Ash(5 * input[0] - 11 * input[1] + 4 * input[2] + 4 * input[3] - input[4] - input[5] + 4, 3);

    for (var i = 1; i < n - 1; ++i) {
      var x = i << 1;
      low[i] = input[x] + input[x + 1];
      high[i] = Ash(-input[x - 2] - input[x - 1] + input[x + 2] + input[x + 3] + 4, 3)
        + input[x] - input[x + 1];
    }

    var last = input.Length - 2;
    low[n - 1] = input[last] + input[last + 1];
    high[n - 1] = Ash(
      11 * input[last] - 5 * input[last + 1]
      - 4 * input[last - 1] - 4 * input[last - 2]
      + input[last - 3] + input[last - 4] + 4,
      3);
  }

  /// <summary>
  /// The forward two-dimensional spatial transform: one <paramref name="width"/> by
  /// <paramref name="height"/> image becomes four half-width, half-height bands.
  /// </summary>
  internal static (int[] Ll, int[] Lh, int[] Hl, int[] Hh) ForwardSpatial(
    ReadOnlySpan<int> input, int width, int height) {

    if (width < 6 || height < 6 || (width & 1) != 0 || (height & 1) != 0 || input.Length < width * height)
      throw new ArgumentException("A CineForm spatial transform needs even dimensions of at least six samples.");

    var bandWidth = width >> 1;
    var bandHeight = height >> 1;

    // Horizontal first: every source row becomes an L row and an H row.
    var horizontalLow = new int[bandWidth * height];
    var horizontalHigh = new int[bandWidth * height];
    for (var y = 0; y < height; ++y)
      ForwardOneDimensional(
        input.Slice(y * width, width),
        horizontalLow.AsSpan(y * bandWidth, bandWidth),
        horizontalHigh.AsSpan(y * bandWidth, bandWidth));

    // Vertical second: L -> (LL,HL), H -> (LH,HH). This naming deliberately matches
    // InverseSpatial's pairs, so the two methods remain mathematical inverses rather than merely
    // agreeing on some private permutation of the three highpass bands.
    var ll = new int[bandWidth * bandHeight];
    var lh = new int[bandWidth * bandHeight];
    var hl = new int[bandWidth * bandHeight];
    var hh = new int[bandWidth * bandHeight];
    var column = new int[height];
    var columnLow = new int[bandHeight];
    var columnHigh = new int[bandHeight];

    for (var x = 0; x < bandWidth; ++x) {
      for (var y = 0; y < height; ++y)
        column[y] = horizontalLow[y * bandWidth + x];

      ForwardOneDimensional(column, columnLow, columnHigh);
      for (var y = 0; y < bandHeight; ++y) {
        ll[y * bandWidth + x] = columnLow[y];
        hl[y * bandWidth + x] = columnHigh[y];
      }

      for (var y = 0; y < height; ++y)
        column[y] = horizontalHigh[y * bandWidth + x];

      ForwardOneDimensional(column, columnLow, columnHigh);
      for (var y = 0; y < bandHeight; ++y) {
        lh[y * bandWidth + x] = columnLow[y];
        hh[y * bandWidth + x] = columnHigh[y];
      }
    }

    return (ll, lh, hl, hh);
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
  internal static int[] InverseSpatial(
    ReadOnlySpan<int> ll, ReadOnlySpan<int> lh, ReadOnlySpan<int> hl, ReadOnlySpan<int> hh,
    int width, int height, out int outputWidth, out int outputHeight) {

    outputWidth = width * 2;
    outputHeight = height * 2;

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

    var output = new int[outputWidth * outputHeight];
    var rowLow = new int[width];
    var rowHigh = new int[width];
    var rowOutput = new int[outputWidth];

    for (var y = 0; y < outputHeight; ++y) {
      var row = y * width;
      for (var x = 0; x < width; ++x) {
        rowLow[x] = lColumns[row + x];
        rowHigh[x] = hColumns[row + x];
      }

      InverseOneDimensional(rowLow, rowHigh, rowOutput);
      Array.Copy(rowOutput, 0, output, y * outputWidth, outputWidth);
    }

    return output;
  }
}
