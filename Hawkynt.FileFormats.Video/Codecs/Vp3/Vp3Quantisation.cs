namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Builds the quantisation matrix for a quantisation type, colour plane and quantisation index.
/// </summary>
/// <remarks>
/// The procedure is the one in Section 6.4.3 of the Theora specification, with the part that VP3 does
/// not have taken out. Theora lets a stream define up to sixty-three ranges of quantisation index per
/// quantisation type and plane, each with a base matrix at either end, and interpolates between them;
/// VP3 has exactly one range covering every quantisation index with the same base matrix at both
/// ends, so the interpolation collapses — with both endpoints equal, the weighted average of a value
/// with itself is that value — and the base matrix is used as it stands.
/// <para/>
/// The scale that multiplies it is in hundredths of a pixel value, hence the division by a hundred,
/// and the result is then multiplied by four to match the scaling of the transform, whose output is
/// four times that of the orthonormal DCT. The floor of sixteen values in Table 6.18 is what keeps a
/// coarse quantiser from becoming a lossless one at the top of the range, and the ceiling of 4096 is
/// what keeps a dequantised coefficient inside the range the transform is defined for.
/// <para/>
/// There are only six matrices a frame can use — two quantisation types by three planes, since VP3
/// has one quantisation index per frame — so they are built once per frame rather than per block.
/// </remarks>
internal static class Vp3Quantisation {

  /// <summary>The smallest quantiser allowed, by quantisation type and by whether it is the DC coefficient (Table 6.18).</summary>
  private static readonly int[][] _Minimum = [[16, 8], [32, 16]];

  /// <summary>The largest dequantised step allowed.</summary>
  private const int _MAXIMUM = 4096;

  /// <summary>
  /// Fills <paramref name="matrix"/> with the sixty-four quantisers, in natural coefficient order.
  /// </summary>
  /// <param name="quantisationType">Zero for an intra block, one for an inter block.</param>
  /// <param name="plane">The colour plane index.</param>
  /// <param name="quantisationIndex">The frame's quantisation index.</param>
  /// <param name="matrix">A sixty-four element array to fill.</param>
  internal static void Build(int quantisationType, int plane, int quantisationIndex, int[] matrix) {
    var baseMatrix = Vp3Tables.BaseMatrices[Vp3Tables.BaseMatrixOf[quantisationType][plane]];
    var acScale = Vp3Tables.AcScale[quantisationIndex];
    var dcScale = Vp3Tables.DcScale[quantisationIndex];
    var minimum = _Minimum[quantisationType];

    for (var coefficient = 0; coefficient < 64; ++coefficient) {
      var scale = coefficient == 0 ? dcScale : acScale;
      var scaled = scale * baseMatrix[coefficient] / 100 * 4;
      if (scaled > _MAXIMUM)
        scaled = _MAXIMUM;

      var floor = minimum[coefficient == 0 ? 0 : 1];
      matrix[coefficient] = scaled < floor ? floor : scaled;
    }
  }
}
