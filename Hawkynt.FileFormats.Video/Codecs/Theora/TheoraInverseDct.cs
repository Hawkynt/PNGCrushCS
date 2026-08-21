using System;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// Theora's inverse discrete cosine transform, which is normative to the bit.
/// </summary>
/// <remarks>
/// Theora specification section 7.9.3. Most codecs specify the transform as a formula with an
/// accuracy bound and let a decoder implement it however it likes; Theora specifies the exact
/// integer operations and requires them, because its DC prediction carries the quantised DC value of
/// one block into the next and a single rounding difference would propagate through the rest of the
/// frame and then through every frame predicted from it.
/// <para/>
/// What is implemented is a 16-bit integerised approximation of the eight-point transform, based on
/// the Chen factorisation: sixteen multiplications and twenty-six additions, with the constants of
/// Table 7.65 standing in for the cosines and sines. The truncations to sixteen bits are part of the
/// specification and not an artefact of a narrow register — they appear at exactly the points where
/// an implementation working entirely in 16-bit registers would have them, and a decoder using wider
/// registers has to reproduce them deliberately. That is why each one is written out here rather
/// than left to the arithmetic.
/// <para/>
/// Each pass scales by two relative to the orthonormal transform, so the two passes scale by four
/// and the final division by sixteen takes that back out along with the encoder's matching factor.
/// Only that last division rounds; everything before it is a shift.
/// </remarks>
internal static class TheoraInverseDct {

  /// <summary>Truncates a value to what a 16-bit signed register would hold, discarding the rest.</summary>
  /// <remarks>
  /// Unsaturated: the high bits are dropped and the low ones kept, rather than the value being
  /// clamped. Section 7.9.3 requires this, so that the additions and subtractions may be reordered
  /// without changing the result.
  /// </remarks>
  private static int _Truncate16(int value) => (short)value;

  /// <summary>
  /// The one-dimensional inverse transform of eight coefficients — section 7.9.3.1.
  /// </summary>
  /// <remarks>
  /// The steps are the specification's, in its order. The permutation of the inputs and the
  /// unpermutation of the outputs are the bit-reversal of a three-bit index and are folded into
  /// which element each step reads and writes.
  /// </remarks>
  private static void _Transform(Span<int> line) {
    var c = TheoraTables.Cosine;

    // T[0] and T[1]: the sum and difference of the two lowest frequencies, each truncated to 16 bits
    // before being scaled, which is the one multiplication a 16-bit implementation cannot do in the
    // high word of a 16x16 product.
    var t0 = c[4] * _Truncate16(line[0] + line[4]) >> 16;
    var t1 = c[4] * _Truncate16(line[0] - line[4]) >> 16;
    var t2 = (c[6] * line[2] >> 16) - (c[2] * line[6] >> 16);
    var t3 = (c[2] * line[2] >> 16) + (c[6] * line[6] >> 16);
    var t4 = (c[7] * line[1] >> 16) - (c[1] * line[7] >> 16);
    var t5 = (c[3] * line[5] >> 16) - (c[5] * line[3] >> 16);
    var t6 = (c[5] * line[5] >> 16) + (c[3] * line[3] >> 16);
    var t7 = (c[1] * line[1] >> 16) + (c[7] * line[7] >> 16);

    var r = t4 + t5;
    t5 = c[4] * _Truncate16(t4 - t5) >> 16;
    t4 = r;

    r = t7 + t6;
    t6 = c[4] * _Truncate16(t7 - t6) >> 16;
    t7 = r;

    r = t0 + t3;
    t3 = t0 - t3;
    t0 = r;

    r = t1 + t2;
    t2 = t1 - t2;
    t1 = r;

    r = t6 + t5;
    t5 = t6 - t5;
    t6 = r;

    line[0] = _Truncate16(t0 + t7);
    line[1] = _Truncate16(t1 + t6);
    line[2] = _Truncate16(t2 + t5);
    line[3] = _Truncate16(t3 + t4);
    line[4] = _Truncate16(t3 - t4);
    line[5] = _Truncate16(t2 - t5);
    line[6] = _Truncate16(t1 - t6);
    line[7] = _Truncate16(t0 - t7);
  }

  /// <summary>
  /// The two-dimensional inverse transform of a dequantised block — section 7.9.3.2.
  /// </summary>
  /// <param name="coefficients">64 dequantised coefficients in natural order.</param>
  /// <param name="residual">64 samples of residual, row-major with row zero at the bottom.</param>
  /// <remarks>
  /// Rows first, then columns, with the column index running bottom to top like every other
  /// coordinate in Theora. The final division by sixteen rounds ties towards positive infinity,
  /// which is what <c>(x + 8) &gt;&gt; 4</c> does for a two's complement number.
  /// </remarks>
  internal static void Transform(ReadOnlySpan<int> coefficients, Span<int> residual) {
    Span<int> line = stackalloc int[8];

    for (var row = 0; row < 8; ++row) {
      for (var column = 0; column < 8; ++column)
        line[column] = coefficients[row * 8 + column];

      _Transform(line);

      for (var column = 0; column < 8; ++column)
        residual[row * 8 + column] = line[column];
    }

    for (var column = 0; column < 8; ++column) {
      for (var row = 0; row < 8; ++row)
        line[row] = residual[row * 8 + column];

      _Transform(line);

      for (var row = 0; row < 8; ++row)
        residual[row * 8 + column] = (line[row] + 8) >> 4;
    }
  }
}
