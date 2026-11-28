using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The forward transform and quantisation — the encode direction of ITU-T H.265, clauses 8.6.2 to
/// 8.6.4.
/// </summary>
/// <remarks>
/// Only the inverse is normative. An encoder is free to arrive at its coefficients any way it likes,
/// because what a decoder reconstructs is decided entirely by the levels in the bitstream and the
/// inverse transform applied to them — a forward transform that is a poor approximation of the
/// inverse costs quality and nothing else. That freedom is not a reason to be careless with it: the
/// closer this is to the exact inverse of <see cref="H265Transform"/>, the smaller the residual that
/// is left to code, so it uses the same integer matrix and the shifts that pair with the decoder's.
/// <para/>
/// The two shifts are what keeps the intermediate values inside a machine word. The first stage
/// scales down by the block's size and the sample depth's headroom, the second by the transform's
/// own gain, and together they undo exactly what the two inverse stages apply.
/// <para/>
/// Quantisation is a multiplication rather than a division: the scale table is the reciprocal of the
/// dequantiser's, at a fixed point, so the two are inverses to within the rounding that is the whole
/// point of quantising. The rounding offset leans towards zero — a level that is rounded up costs
/// more bits than the distortion it saves is worth, and a residual that quantises to nothing at all
/// is the cheapest outcome there is.
/// </remarks>
internal static class H265ForwardTransform {

  /// <summary>
  /// The reciprocals of <see cref="H265Dequantiser"/>'s six ratios, at fourteen fractional bits.
  /// </summary>
  /// <remarks>
  /// Each is <c>2^20 / levelScale[k]</c> rounded: 40, 45, 51, 57, 64 and 72 on the way back. The
  /// product of a pair is 2^20 to within a part in ten thousand, which is what makes quantising and
  /// dequantising a round trip rather than a drift.
  /// </remarks>
  private static readonly int[] _QuantScale = [26214, 23302, 20560, 18396, 16384, 14564];

  /// <summary>The fixed point the quantisation scale is held at.</summary>
  private const int _QUANT_SHIFT = 14;

  /// <summary>The dynamic range the transform's output is allowed, which sets the first-stage shift.</summary>
  private const int _TRANSFORM_DYNAMIC_RANGE = 15;

  /// <summary>
  /// Turns residual samples into transform coefficients, in place.
  /// </summary>
  /// <param name="block">
  /// The residual, row-major and <c>1 &lt;&lt; log2Size</c> across, replaced by the coefficients.
  /// </param>
  /// <param name="log2Size">The block's size as a base-two logarithm: 2, 3, 4 or 5.</param>
  /// <param name="sine">Whether to use the sine transform — a 4x4 luma block of an intra coding unit.</param>
  /// <param name="bitDepth">The sample depth, which sets the first shift.</param>
  internal static void Forward(int[] block, int log2Size, bool sine, int bitDepth) {
    var size = 1 << log2Size;
    var intermediate = new int[size * size];

    _TransformRows(block, intermediate, size, log2Size, sine, log2Size - 1 + bitDepth - 8);
    _TransformRows(intermediate, block, size, log2Size, sine, log2Size + 6);
  }

  /// <summary>
  /// Turns transform coefficients into the levels that are coded, in place.
  /// </summary>
  /// <param name="block">The coefficients, replaced by the levels.</param>
  /// <param name="log2Size">The block's size as a base-two logarithm.</param>
  /// <param name="qp">The quantiser, already offset for the sample depth.</param>
  /// <param name="bitDepth">The sample depth.</param>
  /// <param name="intra">
  /// Whether the block belongs to an intra coding unit, which rounds a little more readily: an intra
  /// block has no prediction to fall back on, so dropping its residual to zero costs more.
  /// </param>
  internal static void Quantise(int[] block, int log2Size, int qp, int bitDepth, bool intra) {
    var transformShift = _TRANSFORM_DYNAMIC_RANGE - bitDepth - log2Size;
    var bits = _QUANT_SHIFT + qp / 6 + transformShift;
    var scale = _QuantScale[qp % 6];

    // Biased towards zero rather than to nearest, and further for an inter block. A coefficient that
    // rounds up has to be coded; one that rounds down to zero often costs nothing at all, because a
    // sub-block of zeroes is a single flag and a block of them is not sent.
    var rounding = (long)(intra ? 171 : 85) << (bits - 9);

    for (var i = 0; i < 1 << (log2Size << 1); ++i) {
      var coefficient = block[i];
      if (coefficient == 0)
        continue;

      var magnitude = (Math.Abs((long)coefficient) * scale + rounding) >> bits;
      var level = (int)Math.Min(magnitude, H265Transform.COEFFICIENT_MAXIMUM);
      block[i] = coefficient < 0 ? -level : level;
    }
  }

  /// <summary>
  /// One stage of the transform: every row against the matrix, written out transposed.
  /// </summary>
  /// <remarks>
  /// Transposing on the way out is what lets the same pass serve both stages — running it twice
  /// transforms the rows, then the columns of the result, and leaves the block the way round it
  /// started.
  /// </remarks>
  private static void _TransformRows(int[] source, int[] target, int size, int log2Size, bool sine, int shift) {
    // Row k of the matrix for a block this size is row k * 32/size of the tabulated one, which is
    // what makes one table serve all four sizes.
    var step = sine ? 1 : 32 >> log2Size;
    var stride = sine ? 4 : 32;
    var matrix = sine ? H265Transform.SineMatrix : H265Transform.Matrix;
    var rounding = shift > 0 ? 1 << (shift - 1) : 0;

    for (var j = 0; j < size; ++j)
      for (var k = 0; k < size; ++k) {
        var sum = 0;
        for (var i = 0; i < size; ++i)
          sum += matrix[k * step * stride + i] * source[j * size + i];

        target[k * size + j] = (sum + rounding) >> shift;
      }
  }
}
