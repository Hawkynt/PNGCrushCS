using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Undoes the quantisation — ITU-T H.265, clause 8.6.3.
/// </summary>
/// <remarks>
/// A step that is easy to leave out and impossible to leave out silently. Skip it and the transform
/// receives the coded levels rather than the coefficients they stand for; because a level is
/// typically an order of magnitude smaller than its coefficient, the residual comes back near zero
/// and the picture is the prediction alone. That looks like a picture — smooth, plausible, and with
/// none of the detail the residual carried.
/// <para/>
/// The quantiser is not a divisor but an index into a scale that doubles every six steps, which is
/// why the scale table has six entries and the rest of the quantiser is a shift. So a quantiser six
/// higher means exactly twice the step, and the six ratios in between are the sixth roots of two,
/// rounded once, here.
/// <para/>
/// The shift depends on the block size because the transform's gain does. A 32x32 inverse transform
/// multiplies its input by more than a 4x4 one does, and the dequantiser takes that back out
/// beforehand rather than the transform normalising afterwards — which is what keeps every
/// intermediate value inside sixteen bits.
/// </remarks>
internal static class H265Dequantiser {

  /// <summary>The six ratios one octave of the quantiser is divided into — <c>levelScale</c>.</summary>
  private static readonly int[] _LevelScale = [40, 45, 51, 57, 64, 72];

  /// <summary>
  /// Table 8-10: how a chroma quantiser index maps to the quantiser actually used.
  /// </summary>
  /// <remarks>
  /// Chroma is quantised more gently than luma above index 30, because chroma artefacts are more
  /// visible than the same error in luminance and because the eye's chroma bandwidth is lower — so
  /// past that point the chroma quantiser climbs at roughly half luma's rate and then resumes six
  /// steps behind. Below 30 the two are the same and the table is the identity, which is why only
  /// the fourteen entries where they differ are written out.
  /// </remarks>
  private static readonly int[] _ChromaQpFrom30 = [29, 30, 31, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37];

  /// <summary>The chroma quantiser for a luma-derived index — clause 8.6.1.</summary>
  internal static int ChromaQp(int index) => index switch {
    < 30 => index,
    > 43 => index - 6,
    _ => _ChromaQpFrom30[index - 30],
  };

  /// <summary>
  /// Turns coded levels into transform coefficients, in place.
  /// </summary>
  /// <param name="block">The levels, row-major and <c>1 &lt;&lt; log2Size</c> across.</param>
  /// <param name="log2Size">The transform block's size as a base-two logarithm.</param>
  /// <param name="qp">The quantiser, already offset for the sample depth.</param>
  /// <param name="bitDepth">The sample depth, which with the block size sets the shift.</param>
  /// <param name="scalingList">The weighting matrices, or <c>null</c> for a flat sixteen everywhere.</param>
  /// <param name="matrixId">Which of the six matrices this block uses — Table 7-4.</param>
  internal static void Scale(
    int[] block, int log2Size, int qp, int bitDepth, H265ScalingList? scalingList, int matrixId) {
    var size = 1 << log2Size;

    // The transform's gain grows with the block size and the sample depth's headroom shrinks with
    // it; both are taken out here so that every coefficient reaching the transform fits in sixteen
    // bits. At eight bits this is Log2(nTbS) + 3.
    var shift = bitDepth + log2Size + 10 - 15;
    var rounding = 1 << (shift - 1);

    var scale = _LevelScale[qp % 6];
    var octave = qp / 6;

    if (scalingList == null) {
      // A flat matrix is a multiplication by sixteen, which is four shifts — folded into the one the
      // block size already calls for rather than performed.
      var flatShift = shift - 4;
      var flatRounding = flatShift > 0 ? 1 << (flatShift - 1) : 0;

      for (var i = 0; i < size * size; ++i) {
        if (block[i] == 0)
          continue;

        var scaled = ((long)block[i] * scale << octave) + flatRounding;
        block[i] = (int)Math.Clamp(
          scaled >> flatShift, H265Transform.COEFFICIENT_MINIMUM, H265Transform.COEFFICIENT_MAXIMUM);
      }

      return;
    }

    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        var index = (y << log2Size) + x;
        if (block[index] == 0)
          continue;

        var scaled = ((long)block[index] * scalingList.Factor(log2Size, matrixId, x, y) * scale << octave) + rounding;
        block[index] = (int)Math.Clamp(
          scaled >> shift, H265Transform.COEFFICIENT_MINIMUM, H265Transform.COEFFICIENT_MAXIMUM);
      }
  }
}
