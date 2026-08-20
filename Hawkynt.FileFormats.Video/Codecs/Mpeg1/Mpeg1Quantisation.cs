namespace FileFormat.Codecs.Mpeg1;

/// <summary>
/// The scan order and the two quantiser weighting matrices of ISO/IEC 11172-2, and the arithmetic
/// that turns a coded level back into a coefficient.
/// </summary>
/// <remarks>
/// This is the step the "HEVC decoder" in this repository did not have at all, which is why it
/// returned pictures that were the right size and nearly empty. Everything here is the standard's
/// 2.4.4.1 written out; the shape to watch is the oddification, which is not rounding and is not
/// optional.
/// </remarks>
internal static class Mpeg1Quantisation {

  /// <summary>
  /// The zig-zag scan of 11172-2 Figure 2-6: scan position to raster position within the block.
  /// </summary>
  internal static readonly int[] ZigZag = [
     0,  1,  8, 16,  9,  2,  3, 10,
    17, 24, 32, 25, 18, 11,  4,  5,
    12, 19, 26, 33, 40, 48, 41, 34,
    27, 20, 13,  6,  7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36,
    29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46,
    53, 60, 61, 54, 47, 55, 62, 63,
  ];

  /// <summary>
  /// The default intra quantiser matrix, 11172-2 2.4.2.3, in raster order.
  /// </summary>
  /// <remarks>
  /// In raster order and not in scan order. The standard prints it as the sixty-four values a
  /// <c>load_intra_quantizer_matrix</c> would carry, which are in zig-zag scan order, and a matrix
  /// read from a stream therefore has to be un-zig-zagged before it lines up with this one. Storing
  /// both in raster order is what makes the dequantisation a plain index rather than a scan lookup
  /// that would have to be right in two places.
  /// </remarks>
  internal static readonly byte[] DefaultIntraMatrix = [
     8, 16, 19, 22, 26, 27, 29, 34,
    16, 16, 22, 24, 27, 29, 34, 37,
    19, 22, 26, 27, 29, 34, 34, 38,
    22, 22, 26, 27, 29, 34, 37, 40,
    22, 26, 27, 29, 32, 35, 40, 48,
    26, 27, 29, 32, 35, 40, 48, 58,
    26, 27, 29, 34, 38, 46, 56, 69,
    27, 29, 35, 38, 46, 56, 69, 83,
  ];

  /// <summary>The default non-intra quantiser matrix: sixteen everywhere (11172-2, 2.4.2.3).</summary>
  internal static readonly byte[] DefaultNonIntraMatrix = [
    16, 16, 16, 16, 16, 16, 16, 16,
    16, 16, 16, 16, 16, 16, 16, 16,
    16, 16, 16, 16, 16, 16, 16, 16,
    16, 16, 16, 16, 16, 16, 16, 16,
    16, 16, 16, 16, 16, 16, 16, 16,
    16, 16, 16, 16, 16, 16, 16, 16,
    16, 16, 16, 16, 16, 16, 16, 16,
    16, 16, 16, 16, 16, 16, 16, 16,
  ];

  /// <summary>
  /// Reconstructs one coefficient of an intra block from its coded level (11172-2, 2.4.4.1).
  /// </summary>
  /// <remarks>
  /// The DC coefficient is not passed through here; it is the differential predictor times eight and
  /// is written directly.
  /// </remarks>
  internal static int DequantiseIntra(int level, int quantiserScale, int weight) {
    var value = 2 * level * quantiserScale * weight / 16;
    return _Clamp(_MakeOdd(value));
  }

  /// <summary>
  /// Reconstructs one coefficient of a non-intra block from its coded level (11172-2, 2.4.4.1).
  /// </summary>
  /// <remarks>
  /// The extra <c>+ sign(level)</c> before the multiply is the non-intra rule and the whole of the
  /// difference from the intra one: it biases each level away from zero by half a step, because a
  /// non-intra level of <c>n</c> stands for the interval whose centre is at <c>n</c> plus a half
  /// rather than at <c>n</c>. Leaving it out darkens every predicted picture slightly and
  /// progressively, which looks like drift rather than like a missing term.
  /// </remarks>
  internal static int DequantiseNonIntra(int level, int quantiserScale, int weight) {
    if (level == 0)
      return 0;

    var sign = level < 0 ? -1 : 1;
    var value = (2 * level + sign) * quantiserScale * weight / 16;
    return _Clamp(_MakeOdd(value));
  }

  /// <summary>
  /// Forces a reconstructed coefficient to be odd, by moving it one towards zero when it is even.
  /// </summary>
  /// <remarks>
  /// 11172-2 2.4.4.1 calls this the "oddification" and it exists to stop the mismatch between two
  /// decoders' inverse transforms from accumulating: an odd coefficient set cannot sum to the
  /// half-integer values at which two conforming IDCTs are free to round in opposite directions.
  /// Zero stays zero — a coefficient that was not coded is not moved to one.
  /// </remarks>
  private static int _MakeOdd(int value) {
    if (value == 0 || (value & 1) != 0)
      return value;

    return value > 0 ? value - 1 : value + 1;
  }

  /// <summary>Saturates to the range a reconstructed coefficient is defined over.</summary>
  private static int _Clamp(int value) => value < -2048 ? -2048 : value > 2047 ? 2047 : value;
}
