using System;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// The scan orders and the quantiser weighting matrices of ISO/IEC 11172-2 and ISO/IEC 13818-2, and
/// the arithmetic that turns a coded level back into a coefficient.
/// </summary>
/// <remarks>
/// This is the step the "HEVC decoder" in this repository did not have at all, which is why it
/// returned pictures that were the right size and nearly empty.
/// <para/>
/// The two standards do this differently and the difference is not a detail. MPEG-1 divides by
/// sixteen and then forces every coefficient odd; MPEG-2 divides by thirty-two, does not oddify, and
/// instead corrects the parity of the whole block once at the end by moving its last coefficient
/// (13818-2, 7.4.4). Applying MPEG-1's rule to an MPEG-2 block gives a picture that is very nearly
/// right — the error is at most one level per coefficient — which is exactly the kind of wrongness
/// that survives being looked at.
/// </remarks>
internal static class MpegQuantisation {

  /// <summary>
  /// The zig-zag scan: scan position to raster position within the block.
  /// </summary>
  /// <remarks>
  /// 11172-2 Figure 2-6 and 13818-2 Figure 7-2 print the same order. The standards print it the
  /// other way round — the scan index at each raster position — so this is that figure inverted,
  /// which is the direction a decoder reads it in.
  /// </remarks>
  internal static readonly int[] ZigZagScan = [
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
  /// The alternate scan of ISO/IEC 13818-2 Figure 7-3, selected by <c>alternate_scan</c>.
  /// </summary>
  /// <remarks>
  /// It runs down the block before it runs across it, which suits a field-coded block: the vertical
  /// frequencies of one field of an interlaced picture carry more of the energy than the horizontal
  /// ones do, so the coefficients that are worth coding come earlier in this order than in the
  /// zig-zag. Inverted from Figure 7-3 the same way the zig-zag above is inverted from Figure 7-2.
  /// </remarks>
  internal static readonly int[] AlternateScan = [
     0,  8, 16, 24,  1,  9,  2, 10,
    17, 25, 32, 40, 48, 56, 57, 49,
    41, 33, 26, 18,  3, 11,  4, 12,
    19, 27, 34, 42, 50, 58, 35, 43,
    51, 59, 20, 28,  5, 13,  6, 14,
    21, 29, 36, 44, 52, 60, 37, 45,
    53, 61, 22, 30,  7, 15, 23, 31,
    38, 46, 54, 62, 39, 47, 55, 63,
  ];

  /// <summary>
  /// The default intra quantiser matrix, 11172-2 2.4.2.3 and 13818-2 Table 7-4, in raster order.
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

  /// <summary>The default non-intra quantiser matrix: sixteen everywhere (11172-2 2.4.2.3, 13818-2 Table 7-5).</summary>
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
  /// The non-linear <c>quantiser_scale_code</c> to <c>quantiser_scale</c> mapping of ISO/IEC 13818-2
  /// Table 7-6, indexed by the code. Entry zero is the forbidden code and is never read.
  /// </summary>
  /// <remarks>
  /// The linear column of the same table is just twice the code, so only this one needs writing out.
  /// It spends its resolution at the fine end — codes one to eight are the scales one to eight, and
  /// the steps grow from there to sixteen at the coarse end — which is what makes it worth having
  /// over the linear one at low bit rates.
  /// </remarks>
  private static readonly int[] _NonLinearScale = [
     0,  1,  2,  3,  4,  5,  6,  7,  8,
    10, 12, 14, 16, 18, 20, 22, 24,
    28, 32, 36, 40, 44, 48, 52, 56,
    64, 72, 80, 88, 96, 104, 112,
  ];

  /// <summary>
  /// Turns a <c>quantiser_scale_code</c> into the <c>quantiser_scale</c> the arithmetic uses
  /// (ISO/IEC 13818-2, 7.4.2.2).
  /// </summary>
  /// <param name="code">The five-bit code, 1 to 31.</param>
  /// <param name="nonLinear"><c>q_scale_type</c> from the picture coding extension.</param>
  internal static int ScaleOf(int code, bool nonLinear) => nonLinear ? _NonLinearScale[code] : code * 2;

  /// <summary>
  /// Reconstructs one coefficient of an intra block from its coded level (11172-2, 2.4.4.1).
  /// </summary>
  /// <remarks>
  /// The DC coefficient is not passed through here; it is the differential predictor times the
  /// intra DC multiplier and is written directly.
  /// </remarks>
  internal static int DequantiseIntraMpeg1(int level, int quantiserScale, int weight) {
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
  internal static int DequantiseNonIntraMpeg1(int level, int quantiserScale, int weight) {
    if (level == 0)
      return 0;

    var sign = level < 0 ? -1 : 1;
    var value = (2 * level + sign) * quantiserScale * weight / 16;
    return _Clamp(_MakeOdd(value));
  }

  /// <summary>
  /// Reconstructs one coefficient of an MPEG-2 intra block (ISO/IEC 13818-2, 7.4.2.3), saturated as
  /// 7.4.3 requires.
  /// </summary>
  internal static int DequantiseIntraMpeg2(int level, int quantiserScale, int weight)
    => _Clamp(2 * level * weight * quantiserScale / 32);

  /// <summary>
  /// Reconstructs one coefficient of an MPEG-2 non-intra block (ISO/IEC 13818-2, 7.4.2.3).
  /// </summary>
  /// <remarks>
  /// The sign term is inside the doubling here and outside it in MPEG-1's spelling, which comes to
  /// the same arithmetic; what does not come to the same thing is the divisor, which is
  /// thirty-two against MPEG-1's sixteen. The two agree in the end only because an MPEG-2
  /// quantiser_scale is twice the code where MPEG-1's is the code.
  /// </remarks>
  internal static int DequantiseNonIntraMpeg2(int level, int quantiserScale, int weight) {
    if (level == 0)
      return 0;

    var sign = level < 0 ? -1 : 1;
    return _Clamp((2 * level + sign) * weight * quantiserScale / 32);
  }

  /// <summary>
  /// Corrects the parity of a dequantised MPEG-2 block (ISO/IEC 13818-2, 7.4.4).
  /// </summary>
  /// <remarks>
  /// MPEG-1 keeps two decoders' inverse transforms from drifting apart by forcing every coefficient
  /// odd. MPEG-2 does the same job far more cheaply: it makes the sum of the whole block odd, which
  /// it can always do by moving the very last coefficient by one, and that is enough to stop the
  /// half-integer results at which two conforming transforms may round in opposite directions.
  /// <para/>
  /// It costs one coefficient of the highest frequency there is, which is why it is invisible, and
  /// it is not optional: leaving it out is a difference of one level in the corner of every block,
  /// which accumulates through prediction exactly as the mismatch it exists to prevent would.
  /// </remarks>
  internal static void CorrectMismatch(Span<int> block) {
    var sum = 0;
    for (var i = 0; i < 64; ++i)
      sum += block[i];

    if ((sum & 1) != 0)
      return;

    block[63] = (block[63] & 1) != 0 ? block[63] - 1 : block[63] + 1;
  }

  /// <summary>
  /// Forces a reconstructed coefficient to be odd, by moving it one towards zero when it is even.
  /// </summary>
  /// <remarks>
  /// 11172-2 2.4.4.1 calls this the "oddification" and it exists to stop the mismatch between two
  /// decoders' inverse transforms from accumulating: an odd coefficient set cannot sum to the
  /// half-integer values at which two conforming IDCTs are free to round in opposite directions.
  /// Zero stays zero — a coefficient that was not coded is not moved to one. MPEG-2 does not do this
  /// at all; see <see cref="CorrectMismatch"/>.
  /// </remarks>
  private static int _MakeOdd(int value) {
    if (value == 0 || (value & 1) != 0)
      return value;

    return value > 0 ? value - 1 : value + 1;
  }

  /// <summary>Saturates to the range a reconstructed coefficient is defined over.</summary>
  private static int _Clamp(int value) => value < -2048 ? -2048 : value > 2047 ? 2047 : value;
}
