using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// The inverse transforms: the discrete cosine transform at four sizes, the asymmetric discrete sine
/// transform at three, and the Walsh-Hadamard transform a lossless frame uses (specification 8.7).
/// </summary>
/// <remarks>
/// These are specified exactly, down to the rounding of every intermediate, rather than as a real
/// arithmetic transform a decoder may approximate. That is what makes bit-exactness the right bar for
/// a VP9 decoder: two decoders reading the same bitstream must produce the same samples, so any
/// difference at all is a mistake in one of them rather than a difference of precision.
/// <para/>
/// The cosine transform is written as the specification writes it — a recursion in which the transform
/// of length 2^n contains the one of length 2^(n-1), followed by a fixed pattern of butterflies. That
/// costs a little against a flat implementation and buys the property that the four sizes cannot drift
/// apart, since there is only one of them.
/// <para/>
/// The sine transform keeps its intermediates in a separate array of wider values. Its butterflies
/// leave results at fourteen bits of extra precision that are only rounded away one stage later, and
/// rounding them where the cosine transform does would change the answer.
/// </remarks>
internal sealed class Vp9InverseTransform {

  private const int SINPI_1_9 = 5283;
  private const int SINPI_2_9 = 9929;
  private const int SINPI_3_9 = 13377;
  private const int SINPI_4_9 = 15212;

  /// <summary>The working array every one-dimensional transform runs in.</summary>
  private readonly int[] _t = new int[32];

  /// <summary>The wider intermediates of the sine transform.</summary>
  private readonly long[] _s = new long[32];

  private readonly int[] _copy = new int[32];

  /// <summary>
  /// Transforms a block of coefficients in place (specification 8.7.2).
  /// </summary>
  /// <param name="block">The dequantised coefficients, <c>1 &lt;&lt; sizeLog2</c> square, row major.</param>
  /// <param name="sizeLog2">Base two logarithm of the transform's width, between two and five.</param>
  /// <param name="transformType">Which pair of one-dimensional transforms to use.</param>
  /// <param name="lossless">Whether this frame uses the Walsh-Hadamard transform instead.</param>
  internal void Apply(Span<int> block, int sizeLog2, int transformType, bool lossless) {
    var size = 1 << sizeLog2;
    var t = this._t;

    for (var row = 0; row < size; ++row) {
      var at = row * size;
      for (var column = 0; column < size; ++column)
        t[column] = block[at + column];

      if (lossless)
        this._InverseWalshHadamard(2);
      else if (transformType is DCT_DCT or ADST_DCT) {
        this._PermuteForCosine(sizeLog2);
        this._InverseCosine(sizeLog2);
      } else
        this._InverseSine(sizeLog2);

      for (var column = 0; column < size; ++column)
        block[at + column] = t[column];
    }

    var shift = Math.Min(6, sizeLog2 + 2);
    for (var column = 0; column < size; ++column) {
      for (var row = 0; row < size; ++row)
        t[row] = block[row * size + column];

      if (lossless)
        this._InverseWalshHadamard(0);
      else if (transformType is DCT_DCT or DCT_ADST) {
        this._PermuteForCosine(sizeLog2);
        this._InverseCosine(sizeLog2);
      } else
        this._InverseSine(sizeLog2);

      if (lossless)
        for (var row = 0; row < size; ++row)
          block[row * size + column] = t[row];
      else
        for (var row = 0; row < size; ++row)
          block[row * size + column] = _Round2(t[row], shift);
    }
  }

  // ============================================================================================
  // Butterflies (specification 8.7.1.1)
  // ============================================================================================

  private static int _Round2(long value, int bits) => (int)((value + (1L << (bits - 1))) >> bits);

  /// <summary>
  /// <c>round(16384 * cos(angle * pi / 64))</c> for any integer angle, from a quarter turn of it.
  /// </summary>
  private static int _Cosine64(int angle) {
    var reduced = angle & 127;
    if (reduced <= 32)
      return Vp9Tables.Cosine64[reduced];

    if (reduced <= 64)
      return -Vp9Tables.Cosine64[64 - reduced];

    return reduced <= 96 ? -Vp9Tables.Cosine64[reduced - 64] : Vp9Tables.Cosine64[128 - reduced];
  }

  private static int _Sine64(int angle) => _Cosine64(angle - 32);

  /// <summary>A butterfly rotation of two working values, optionally exchanging them afterwards.</summary>
  private void _Butterfly(int a, int b, int angle, bool flip) {
    var cosine = _Cosine64(angle);
    var sine = _Sine64(angle);
    var t = this._t;

    var x = (long)t[a] * cosine - (long)t[b] * sine;
    var y = (long)t[a] * sine + (long)t[b] * cosine;

    var first = _Round2(x, 14);
    var second = _Round2(y, 14);

    t[a] = flip ? second : first;
    t[b] = flip ? first : second;
  }

  /// <summary>A Hadamard rotation: the sum and the difference, in that order or the other one.</summary>
  private void _Hadamard(int a, int b, bool flip) {
    if (flip)
      (a, b) = (b, a);

    var t = this._t;
    var x = t[a];
    var y = t[b];
    t[a] = x + y;
    t[b] = x - y;
  }

  /// <summary>A butterfly rotation whose result is kept at full precision in the wider array.</summary>
  private void _WideButterfly(int a, int b, int angle, bool flip) {
    var cosine = _Cosine64(angle);
    var sine = _Sine64(angle);
    var t = this._t;

    var first = (long)t[a] * cosine - (long)t[b] * sine;
    var second = (long)t[a] * sine + (long)t[b] * cosine;

    this._s[a] = flip ? second : first;
    this._s[b] = flip ? first : second;
  }

  /// <summary>A Hadamard rotation that takes the wider intermediates and rounds them back down.</summary>
  private void _WideHadamard(int a, int b) {
    var s = this._s;
    this._t[a] = _Round2(s[a] + s[b], 14);
    this._t[b] = _Round2(s[a] - s[b], 14);
  }

  private static int _BitReverse(int bits, int value) {
    var result = 0;
    for (var i = 0; i < bits; ++i)
      result += ((value >> i) & 1) << (bits - 1 - i);

    return result;
  }

  // ============================================================================================
  // Inverse discrete cosine transform (specification 8.7.1.2 and 8.7.1.3)
  // ============================================================================================

  private void _PermuteForCosine(int n) {
    var size = 1 << n;
    var t = this._t;
    var copy = this._copy;

    for (var i = 0; i < size; ++i)
      copy[i] = t[i];

    for (var i = 0; i < size; ++i)
      t[i] = copy[_BitReverse(n, i)];
  }

  private void _InverseCosine(int n) {
    var n0 = 1 << n;
    var n1 = 1 << (n - 1);
    var n2 = 1 << (n - 2);
    var n3 = n >= 3 ? 1 << (n - 3) : 0;

    if (n == 2)
      this._Butterfly(0, 1, 16, true);
    else
      this._InverseCosine(n - 1);

    for (var i = 0; i < n2; ++i)
      this._Butterfly(n1 + i, n0 - 1 - i, 32 - _BitReverse(5, n1 + i), false);

    if (n >= 3)
      for (var i = 0; i < n3; ++i)
      for (var j = 0; j < 2; ++j)
        this._Hadamard(n1 + 4 * i + 2 * j, n1 + 1 + 4 * i + 2 * j, j != 0);

    if (n == 5) {
      for (var i = 0; i < 2; ++i)
      for (var j = 0; j < 2; ++j)
        this._Butterfly(n0 - n + 3 - n2 * j - 4 * i, n1 + n - 4 + n2 * j + 4 * i, 28 - 16 * i + 56 * j, true);

      for (var i = 0; i < 2; ++i)
      for (var j = 0; j < 4; ++j)
        this._Hadamard(n1 + n3 * j + i, n1 + n2 - 5 + n3 * j - i, (j & 1) != 0);
    }

    if (n >= 4) {
      for (var i = 0; i <= (n == 5 ? 1 : 0); ++i)
      for (var j = 0; j < 2; ++j)
        this._Butterfly(n0 - n + 2 - i - n2 * j, n1 + n - 3 + i + n2 * j, 24 + 48 * j, true);

      for (var i = 0; i <= 2 * n - 7; ++i)
      for (var j = 0; j < 2; ++j)
        this._Hadamard(n1 + n2 * j + i, n1 + n2 - 1 + n2 * j - i, (j & 1) != 0);
    }

    if (n >= 3)
      for (var i = 0; i < n3; ++i)
        this._Butterfly(n0 - n3 - 1 - i, n1 + n3 + i, 16, true);

    for (var i = 0; i < n1; ++i)
      this._Hadamard(i, n0 - 1 - i, false);
  }

  // ============================================================================================
  // Inverse asymmetric discrete sine transform (specification 8.7.1.4 to 8.7.1.9)
  // ============================================================================================

  private void _InverseSine(int n) {
    switch (n) {
      case 2:
        this._InverseSine4();
        break;
      case 3:
        this._InverseSine8();
        break;
      default:
        this._InverseSine16();
        break;
    }
  }

  private void _PermuteSineInput(int n) {
    var n0 = 1 << n;
    var n1 = 1 << (n - 1);
    var t = this._t;
    var copy = this._copy;

    for (var i = 0; i < n0; ++i)
      copy[i] = t[i];

    for (var i = 0; i < n1; ++i) {
      t[2 * i] = copy[n0 - 1 - 2 * i];
      t[2 * i + 1] = copy[2 * i];
    }
  }

  private void _PermuteSineOutput(int n) {
    var t = this._t;
    var copy = this._copy;
    var size = 1 << n;

    for (var i = 0; i < size; ++i)
      copy[i] = t[i];

    if (n == 4)
      for (var a = 0; a < 2; ++a)
      for (var b = 0; b < 2; ++b)
      for (var c = 0; c < 2; ++c)
      for (var d = 0; d < 2; ++d)
        t[8 * a + 4 * b + 2 * c + d] = copy[8 * (d ^ c) + 4 * (c ^ b) + 2 * (b ^ a) + a];
    else
      for (var a = 0; a < 2; ++a)
      for (var b = 0; b < 2; ++b)
      for (var c = 0; c < 2; ++c)
        t[4 * a + 2 * b + c] = copy[4 * (c ^ b) + 2 * (b ^ a) + a];
  }

  /// <summary>
  /// The four point sine transform, which the specification writes out rather than deriving
  /// (specification 8.7.1.6).
  /// </summary>
  private void _InverseSine4() {
    var t = this._t;

    var s0 = (long)SINPI_1_9 * t[0];
    var s1 = (long)SINPI_2_9 * t[0];
    var s2 = (long)SINPI_3_9 * t[1];
    var s3 = (long)SINPI_4_9 * t[2];
    var s4 = (long)SINPI_1_9 * t[2];
    var s5 = (long)SINPI_2_9 * t[3];
    var s6 = (long)SINPI_4_9 * t[3];
    var s7 = (long)SINPI_3_9 * (t[0] - t[2] + t[3]);

    var x0 = s0 + s3 + s5;
    var x1 = s1 - s4 - s6;
    var x2 = s7;
    var x3 = s2;

    t[0] = _Round2(x0 + x3, 14);
    t[1] = _Round2(x1 + x3, 14);
    t[2] = _Round2(x2, 14);
    t[3] = _Round2(x0 + x1 - x3, 14);
  }

  private void _InverseSine8() {
    this._PermuteSineInput(3);

    for (var i = 0; i < 4; ++i)
      this._WideButterfly(2 * i, 1 + 2 * i, 30 - 8 * i, true);

    for (var i = 0; i < 4; ++i)
      this._WideHadamard(i, 4 + i);

    for (var i = 0; i < 2; ++i)
      this._WideButterfly(4 + 3 * i, 5 + i, 24 - 16 * i, true);

    for (var i = 0; i < 2; ++i)
      this._WideHadamard(4 + i, 6 + i);

    for (var i = 0; i < 2; ++i)
      this._Hadamard(i, 2 + i, false);

    for (var i = 0; i < 2; ++i)
      this._Butterfly(2 + 4 * i, 3 + 4 * i, 16, true);

    this._PermuteSineOutput(3);

    for (var i = 0; i < 4; ++i)
      this._t[1 + 2 * i] = -this._t[1 + 2 * i];
  }

  private void _InverseSine16() {
    this._PermuteSineInput(4);

    for (var i = 0; i < 8; ++i)
      this._WideButterfly(2 * i, 1 + 2 * i, 31 - 4 * i, true);

    for (var i = 0; i < 8; ++i)
      this._WideHadamard(i, 8 + i);

    for (var i = 0; i < 4; ++i)
      this._WideButterfly(8 + 2 * i, 9 + 2 * i, 28 - 16 * i, true);

    for (var i = 0; i < 4; ++i)
      this._WideHadamard(8 + i, 12 + i);

    for (var i = 0; i < 4; ++i)
      this._Hadamard(i, 4 + i, false);

    for (var i = 0; i < 2; ++i)
    for (var j = 0; j < 2; ++j)
      this._WideButterfly(4 + 8 * i + 3 * j, 5 + 8 * i + j, 24 - 16 * j, true);

    for (var i = 0; i < 2; ++i)
    for (var j = 0; j < 2; ++j)
      this._WideHadamard(4 + 8 * j + i, 6 + 8 * j + i);

    for (var i = 0; i < 2; ++i)
    for (var j = 0; j < 2; ++j)
      this._Hadamard(8 * j + i, 2 + 8 * j + i, false);

    for (var i = 0; i < 2; ++i)
    for (var j = 0; j < 2; ++j)
      this._Butterfly(2 + 4 * j + 8 * i, 3 + 4 * j + 8 * i, 48 + 64 * (i ^ j), false);

    this._PermuteSineOutput(4);

    for (var i = 0; i < 2; ++i)
    for (var j = 0; j < 2; ++j)
      this._t[1 + 12 * j + 2 * i] = -this._t[1 + 12 * j + 2 * i];
  }

  // ============================================================================================
  // Inverse Walsh-Hadamard transform (specification 8.7.1.10)
  // ============================================================================================

  /// <summary>
  /// The transform a lossless frame uses, which is exactly invertible in integers and so adds no
  /// error of its own.
  /// </summary>
  private void _InverseWalshHadamard(int shift) {
    var t = this._t;

    var a = t[0] >> shift;
    var c = t[1] >> shift;
    var d = t[2] >> shift;
    var b = t[3] >> shift;

    a += c;
    d -= b;
    var e = (a - d) >> 1;
    b = e - b;
    c = e - c;
    a -= b;
    d += c;

    t[0] = a;
    t[1] = b;
    t[2] = c;
    t[3] = d;
  }
}
