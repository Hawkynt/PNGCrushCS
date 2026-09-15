using System;

namespace FileFormat.Codecs.H264;

/// <summary>Forward 4x4 transform and encoder-side quantisation matching the decoder's flat scaling list.</summary>
internal static class H264ForwardTransform {

  // MF from H.264 8.5.12 / the inverse of the decoder's flat LevelScale factors.  The three columns
  // are even/even, odd/odd, and mixed coefficient positions respectively.
  private static readonly int[,] _QuantMultiplier = {
    { 13107, 5243, 8066 },
    { 11916, 4660, 7490 },
    { 10082, 4194, 6554 },
    { 9362, 3647, 5825 },
    { 8192, 3355, 5243 },
    { 7282, 2893, 4559 },
  };

  internal static void Quantize4x4(ReadOnlySpan<int> residual, int qp, Span<int> levels, bool omitDc = false) {
    if (residual.Length < 16 || levels.Length < 16)
      throw new ArgumentException("A 4x4 transform needs sixteen samples and coefficient slots.");

    Span<int> transformed = stackalloc int[16];
    Forward4x4(residual, transformed);

    var qbits = 15 + qp / 6;
    var rounding = 1 << (qbits - 1);
    var m = qp % 6;
    levels[..16].Clear();
    for (var scan = omitDc ? 1 : 0; scan < 16; ++scan) {
      var position = H264Transform.ZigZagScan4x4[scan];
      var row = position >> 2;
      var column = position & 3;
      var kind = (row & 1) == 0
        ? (column & 1) == 0 ? 0 : 2
        : (column & 1) == 0 ? 2 : 1;
      var coefficient = transformed[position];
      var magnitude = (Math.Abs(coefficient) * _QuantMultiplier[m, kind] + rounding) >> qbits;
      levels[scan] = coefficient < 0 ? -magnitude : magnitude;
    }
  }

  internal static void Forward4x4(ReadOnlySpan<int> samples, Span<int> transformed) {
    if (samples.Length < 16 || transformed.Length < 16)
      throw new ArgumentException("A 4x4 transform needs sixteen samples and coefficient slots.");

    Span<int> horizontal = stackalloc int[16];
    for (var row = 0; row < 4; ++row) {
      var at = row << 2;
      var s0 = samples[at] + samples[at + 3];
      var s1 = samples[at + 1] + samples[at + 2];
      var s2 = samples[at + 1] - samples[at + 2];
      var s3 = samples[at] - samples[at + 3];
      horizontal[at] = s0 + s1;
      horizontal[at + 1] = (s3 << 1) + s2;
      horizontal[at + 2] = s0 - s1;
      horizontal[at + 3] = s3 - (s2 << 1);
    }

    for (var column = 0; column < 4; ++column) {
      var s0 = horizontal[column] + horizontal[12 + column];
      var s1 = horizontal[4 + column] + horizontal[8 + column];
      var s2 = horizontal[4 + column] - horizontal[8 + column];
      var s3 = horizontal[column] - horizontal[12 + column];
      transformed[column] = s0 + s1;
      transformed[4 + column] = (s3 << 1) + s2;
      transformed[8 + column] = s0 - s1;
      transformed[12 + column] = s3 - (s2 << 1);
    }
  }

  /// <summary>Quantises the four 4:2:0 chroma DC coefficients after the 2x2 Hadamard transform.</summary>
  internal static void QuantizeChromaDc(ReadOnlySpan<int> blockDc, int qp, Span<int> levels) {
    if (blockDc.Length < 4 || levels.Length < 4)
      throw new ArgumentException("4:2:0 chroma DC needs four coefficients.");

    // Forward4x4 carries one quarter of the scale consumed by InverseTransform4x4.  Bring its DC
    // values onto the decoder's dequantised-d coefficient scale before the 2x2 Hadamard inversion.
    var c0 = blockDc[0] << 2;
    var c1 = blockDc[1] << 2;
    var c2 = blockDc[2] << 2;
    var c3 = blockDc[3] << 2;
    Span<int> hadamard = stackalloc int[4] {
      c0 + c1 + c2 + c3,
      c0 - c1 + c2 - c3,
      c0 + c1 - c2 - c3,
      c0 - c1 - c2 + c3,
    };

    Span<int> basis = stackalloc int[4];
    Span<int> unit = stackalloc int[4];
    unit[0] = 1;
    H264Transform.DecodeChromaDc(unit, qp, basis);
    var scale = Math.Abs(basis[0]);
    if (scale == 0)
      throw new InvalidOperationException("The chroma DC inverse-quantisation basis vanished.");

    var divisor = 4 * scale;
    for (var i = 0; i < 4; ++i)
      levels[i] = _DivideRounded(hadamard[i], divisor);
  }

  private static int _DivideRounded(int value, int divisor)
    => value >= 0
      ? (value + (divisor >> 1)) / divisor
      : -((-value + (divisor >> 1)) / divisor);
}
