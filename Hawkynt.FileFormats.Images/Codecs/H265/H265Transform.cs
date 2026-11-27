using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The inverse transforms — ITU-T H.265, clause 8.6.4.
/// </summary>
/// <remarks>
/// Both of these are exact integer transforms, and that is the whole reason a decoder written from
/// the standard can be checked against another decoder sample for sample. There is no floating point
/// anywhere in the chain, no implementation-defined rounding and no tolerance: two conforming
/// decoders produce identical residuals from identical coefficients, or one of them is wrong.
/// <para/>
/// <b>One matrix, four sizes.</b> The 32x32 matrix is the only one the standard tabulates; the
/// smaller transforms are its every second, fourth or eighth row, truncated. That is not a
/// coincidence to exploit but the property the matrix was designed around, and it is why a decoder
/// needs one table rather than four. Only half of it is written out here, because row <c>k</c> is
/// symmetric about its middle for even <c>k</c> and antisymmetric for odd — a property that also
/// makes half a transposition typo visible as a broken symmetry rather than as a slightly wrong
/// picture.
/// <para/>
/// <b>Two transforms, not one.</b> A 4x4 luma block of an intra coding unit uses a sine transform
/// rather than a cosine one, and it is the better fit for exactly that case: intra prediction
/// propagates from the block's top and left edge, so the residual is small next to the reference
/// samples and grows away from them, and a basis whose first function rises across the block
/// matches that shape where a cosine basis, which starts flat, does not.
/// </remarks>
internal static class H265Transform {

  /// <summary>
  /// The left half of the 32x32 transform matrix of clause 8.6.4.2.
  /// </summary>
  /// <remarks>
  /// Row <c>k</c>, column <c>n</c> for <c>n</c> past the middle is <c>(-1)^k</c> times the entry at
  /// <c>31 − n</c>, which is what <see cref="_Expand"/> does with this.
  /// </remarks>
  private static readonly short[] _MatrixLeftHalf = [
    64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
    90, 90, 88, 85, 82, 78, 73, 67, 61, 54, 46, 38, 31, 22, 13, 4,
    90, 87, 80, 70, 57, 43, 25, 9, -9, -25, -43, -57, -70, -80, -87, -90,
    90, 82, 67, 46, 22, -4, -31, -54, -73, -85, -90, -88, -78, -61, -38, -13,
    89, 75, 50, 18, -18, -50, -75, -89, -89, -75, -50, -18, 18, 50, 75, 89,
    88, 67, 31, -13, -54, -82, -90, -78, -46, -4, 38, 73, 90, 85, 61, 22,
    87, 57, 9, -43, -80, -90, -70, -25, 25, 70, 90, 80, 43, -9, -57, -87,
    85, 46, -13, -67, -90, -73, -22, 38, 82, 88, 54, -4, -61, -90, -78, -31,
    83, 36, -36, -83, -83, -36, 36, 83, 83, 36, -36, -83, -83, -36, 36, 83,
    82, 22, -54, -90, -61, 13, 78, 85, 31, -46, -90, -67, 4, 73, 88, 38,
    80, 9, -70, -87, -25, 57, 90, 43, -43, -90, -57, 25, 87, 70, -9, -80,
    78, -4, -82, -73, 13, 85, 67, -22, -88, -61, 31, 90, 54, -38, -90, -46,
    75, -18, -89, -50, 50, 89, 18, -75, -75, 18, 89, 50, -50, -89, -18, 75,
    73, -31, -90, -22, 78, 67, -38, -90, -13, 82, 61, -46, -88, -4, 85, 54,
    70, -43, -87, 9, 90, 25, -80, -57, 57, 80, -25, -90, -9, 87, 43, -70,
    67, -54, -78, 38, 85, -22, -90, 4, 90, 13, -88, -31, 82, 46, -73, -61,
    64, -64, -64, 64, 64, -64, -64, 64, 64, -64, -64, 64, 64, -64, -64, 64,
    61, -73, -46, 82, 31, -88, -13, 90, -4, -90, 22, 85, -38, -78, 54, 67,
    57, -80, -25, 90, -9, -87, 43, 70, -70, -43, 87, 9, -90, 25, 80, -57,
    54, -85, -4, 88, -46, -61, 82, 13, -90, 38, 67, -78, -22, 90, -31, -73,
    50, -89, 18, 75, -75, -18, 89, -50, -50, 89, -18, -75, 75, 18, -89, 50,
    46, -90, 38, 54, -90, 31, 61, -88, 22, 67, -85, 13, 73, -82, 4, 78,
    43, -90, 57, 25, -87, 70, 9, -80, 80, -9, -70, 87, -25, -57, 90, -43,
    38, -88, 73, -4, -67, 90, -46, -31, 85, -78, 13, 61, -90, 54, 22, -82,
    36, -83, 83, -36, -36, 83, -83, 36, 36, -83, 83, -36, -36, 83, -83, 36,
    31, -78, 90, -61, 4, 54, -88, 82, -38, -22, 73, -90, 67, -13, -46, 85,
    25, -70, 90, -80, 43, 9, -57, 87, -87, 57, -9, -43, 80, -90, 70, -25,
    22, -61, 85, -90, 73, -38, -4, 46, -78, 90, -82, 54, -13, -31, 67, -88,
    18, -50, 75, -89, 89, -75, 50, -18, -18, 50, -75, 89, -89, 75, -50, 18,
    13, -38, 61, -78, 88, -90, 85, -73, 54, -31, 4, 22, -46, 67, -82, 90,
    9, -25, 43, -57, 70, -80, 87, -90, 90, -87, 80, -70, 57, -43, 25, -9,
    4, -13, 22, -31, 38, -46, 54, -61, 67, -73, 78, -82, 85, -88, 90, -90,
  ];

  /// <summary>The 4x4 sine transform of clause 8.6.4.2, which only intra luma blocks use.</summary>
  private static readonly short[] _SineMatrix = [
    29, 55, 74, 84,
    74, 74, 0, -74,
    84, -29, -74, 55,
    55, -84, 74, -29,
  ];

  private static readonly short[] _Matrix = _Expand();

  /// <summary>Whatever a coefficient may be after dequantisation, at the precision this decoder uses.</summary>
  internal const int COEFFICIENT_MINIMUM = -32768;

  internal const int COEFFICIENT_MAXIMUM = 32767;

  /// <summary>The whole 32x32 matrix, for the test that checks it is the orthogonal one it claims to be.</summary>
  internal static ReadOnlySpan<short> Matrix => _Matrix;

  internal static ReadOnlySpan<short> SineMatrix => _SineMatrix;

  /// <summary>
  /// Turns a block of dequantised coefficients into residual samples, in place.
  /// </summary>
  /// <param name="block">
  /// The coefficients, row-major and <c>1 &lt;&lt; log2Size</c> across, replaced by the residual.
  /// </param>
  /// <param name="log2Size">The block's size as a base-two logarithm: 2, 3, 4 or 5.</param>
  /// <param name="sine">Whether to use the sine transform — a 4x4 luma block of an intra coding unit.</param>
  /// <param name="bitDepth">The sample depth, which sets the final shift.</param>
  internal static void Inverse(int[] block, int log2Size, bool sine, int bitDepth) {
    var size = 1 << log2Size;
    var intermediate = new int[size * size];

    // The columns first, then the rows. Between the two the intermediate result is brought back
    // within sixteen bits, which is what makes the whole transform fit in a machine word — the
    // standard fixes that shift rather than leaving the intermediate precision to the implementation,
    // because two decoders that clipped differently would differ on a block with large coefficients.
    _TransformColumns(block, intermediate, size, log2Size, sine);
    _TransformRows(intermediate, block, size, log2Size, sine, 20 - bitDepth);
  }

  /// <summary>
  /// The reconstruction of a block whose residual was sent untransformed — clause 8.6.2.
  /// </summary>
  /// <remarks>
  /// The shift is what the two transform stages would have applied, so that a skipped block and a
  /// transformed one arrive at the residual through the same dequantiser scale.
  /// </remarks>
  internal static void Skip(int[] block, int log2Size, int bitDepth) {
    var shift = 5 + log2Size;
    var bdShift = 20 - bitDepth;
    var rounding = 1 << (bdShift - 1);

    for (var i = 0; i < 1 << (log2Size << 1); ++i)
      block[i] = ((block[i] << shift) + rounding) >> bdShift;
  }

  private static void _TransformColumns(int[] source, int[] target, int size, int log2Size, bool sine) {
    // Row k of the matrix for a block this size is row k * 32/size of the tabulated one, which is
    // what makes one table serve all four sizes.
    var step = sine ? 1 : 32 >> log2Size;
    var stride = sine ? 4 : 32;
    var matrix = sine ? _SineMatrix : _Matrix;

    for (var x = 0; x < size; ++x)
      for (var y = 0; y < size; ++y) {
        var sum = 0;
        for (var k = 0; k < size; ++k)
          sum += matrix[k * step * stride + y] * source[k * size + x];

        target[y * size + x] = Math.Clamp((sum + 64) >> 7, COEFFICIENT_MINIMUM, COEFFICIENT_MAXIMUM);
      }
  }

  private static void _TransformRows(int[] source, int[] target, int size, int log2Size, bool sine, int bdShift) {
    var step = sine ? 1 : 32 >> log2Size;
    var stride = sine ? 4 : 32;
    var matrix = sine ? _SineMatrix : _Matrix;
    var rounding = 1 << (bdShift - 1);

    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        var sum = 0;
        for (var k = 0; k < size; ++k)
          sum += matrix[k * step * stride + x] * source[y * size + k];

        target[y * size + x] = (sum + rounding) >> bdShift;
      }
  }

  /// <summary>Mirrors the stored left half into the full matrix, alternating the sign by row.</summary>
  private static short[] _Expand() {
    var matrix = new short[32 * 32];

    for (var k = 0; k < 32; ++k) {
      var sign = (k & 1) == 0 ? 1 : -1;

      for (var n = 0; n < 16; ++n) {
        var value = _MatrixLeftHalf[(k << 4) + n];
        matrix[(k << 5) + n] = value;
        matrix[(k << 5) + 31 - n] = (short)(sign * value);
      }
    }

    return matrix;
  }
}
