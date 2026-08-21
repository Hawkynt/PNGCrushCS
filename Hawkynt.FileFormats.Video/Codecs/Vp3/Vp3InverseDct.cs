namespace FileFormat.Codecs.Vp3;

/// <summary>
/// The integer inverse DCT of Section 7.9.3, written the one way it is allowed to be written.
/// </summary>
/// <remarks>
/// This is the part of the format with no room in it. A decoder that produced a value one different
/// from this anywhere would not merely show a slightly wrong picture: the reconstructed frame is what
/// the next frame predicts from, so the error would be added to the next frame's error and the one
/// after that, growing without bound until the next intra frame. The specification therefore fixes
/// the transform exactly rather than to a tolerance, and says so.
/// <para/>
/// What that costs is the truncations. Every intermediate is held to thirty-two bits and the output
/// of each one-dimensional pass to sixteen, in the wrap-around sense — the high bits are thrown away,
/// not clamped, so a sum that overflows comes out negative rather than at the maximum. Coefficients
/// large enough to overflow do occur, because the forward transform of legal pixel values can reach
/// past what sixteen bits hold once the scale factor of four is in, and quantisation error can push
/// it further; the values simply wrap, in the encoder as in the decoder, and both get the same
/// answer. The truncation before each multiplication by C4 is there so that the whole transform fits
/// in sixteen-bit registers, and a decoder with wider ones has to sign-extend to match.
/// <para/>
/// The two passes are over rows and then over columns, and the column pass is what divides by
/// sixteen, rounding ties towards positive infinity. Nothing rounds before then; each pass scales by
/// two relative to the orthonormal transform, and the factor of four that leaves is what the division
/// undoes.
/// <para/>
/// A block whose only coefficient is the DC one does not come through here at all. That case is in
/// <see cref="Vp3Decoder"/>, where the specification puts it, and it is not the same arithmetic —
/// it skips the intermediate multiplications and rounds differently — so using the full transform
/// for it would be wrong rather than merely slow.
/// </remarks>
internal static class Vp3InverseDct {

  /// <summary>
  /// Transforms one block of dequantised coefficients in natural order into its residual.
  /// </summary>
  /// <param name="coefficients">Sixty-four dequantised coefficients, row-major, row zero at the bottom.</param>
  /// <param name="residual">Sixty-four residual values to fill, in the same layout.</param>
  internal static void Transform(short[] coefficients, short[] residual) {
    var line = new short[8];
    var output = new short[8];

    for (var row = 0; row < 8; ++row) {
      var at = row * 8;
      for (var column = 0; column < 8; ++column)
        line[column] = coefficients[at + column];

      _Transform1D(line, output);

      for (var column = 0; column < 8; ++column)
        residual[at + column] = output[column];
    }

    for (var column = 0; column < 8; ++column) {
      for (var row = 0; row < 8; ++row)
        line[row] = residual[row * 8 + column];

      _Transform1D(line, output);

      for (var row = 0; row < 8; ++row)
        residual[row * 8 + column] = (short)((output[row] + 8) >> 4);
    }
  }

  /// <summary>
  /// The eight-point one-dimensional inverse DCT of Section 7.9.3.1, on the Chen factorisation.
  /// </summary>
  private static void _Transform1D(short[] input, short[] output) {
    var c = Vp3Tables.Cosines;

    var t0 = c[4] * (short)(input[0] + input[4]) >> 16;
    var t1 = c[4] * (short)(input[0] - input[4]) >> 16;
    var t2 = (c[6] * input[2] >> 16) - (c[2] * input[6] >> 16);
    var t3 = (c[2] * input[2] >> 16) + (c[6] * input[6] >> 16);
    var t4 = (c[7] * input[1] >> 16) - (c[1] * input[7] >> 16);
    var t5 = (c[3] * input[5] >> 16) - (c[5] * input[3] >> 16);
    var t6 = (c[5] * input[5] >> 16) + (c[3] * input[3] >> 16);
    var t7 = (c[1] * input[1] >> 16) + (c[7] * input[7] >> 16);

    var r = t4 + t5;
    t5 = c[4] * (short)(t4 - t5) >> 16;
    t4 = r;

    r = t7 + t6;
    t6 = c[4] * (short)(t7 - t6) >> 16;
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

    output[0] = (short)(t0 + t7);
    output[1] = (short)(t1 + t6);
    output[2] = (short)(t2 + t5);
    output[3] = (short)(t3 + t4);
    output[4] = (short)(t3 - t4);
    output[5] = (short)(t2 - t5);
    output[6] = (short)(t1 - t6);
    output[7] = (short)(t0 - t7);
  }
}
