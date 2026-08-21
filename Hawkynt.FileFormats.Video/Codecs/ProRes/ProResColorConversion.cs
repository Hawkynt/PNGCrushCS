using FileFormat.Core;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// Turns the reconstructed component planes into the packed 8-bit RGB every reader here hands back.
/// </summary>
/// <remarks>
/// Two things happen here and it is worth keeping them apart, because only one of them is part of
/// decoding ProRes.
/// <para/>
/// <b>The colour conversion.</b> A display convention, not part of the coding. ProRes codes Y′CbCr
/// and, unlike most codecs in this library, states in its frame header which matrix those samples
/// were meant for (RDD 36:2022, Table 6). That statement is honoured where it names one. Where it
/// says "unknown/unspecified" — which is what ffmpeg's own encoder writes — the choice falls back on
/// picture height, BT.601 up to 576 lines and BT.709 above, which is what a player does with an
/// unlabelled picture. The samples are studio swing per 7.5.1, so the conversion expands that range;
/// reading them as though they filled it leaves every picture washed out by about seven per cent of
/// its contrast, which looks like a decode that worked.
/// <para/>
/// <b>The reduction to eight bits.</b> ProRes reconstructs at ten bits, or twelve for 4:4:4, and a
/// <see cref="RawImage"/> here is eight. The reduction is folded into the conversion rather than
/// done first, so a sample is rounded once instead of twice — and, more importantly, so that it is
/// rounded correctly at all.
/// <para/>
/// <b>Reducing these samples is not <see cref="ChannelScaling"/>'s reduction.</b> That class narrows
/// a channel that fills its range, where <c>0xFFFF</c> means the same as <c>0xFF</c> and the
/// conversion is <c>v * 255 / max</c>. A Y′CbCr video sample does not fill its range: RDD 36:2022,
/// 7.5.1 fixes black at <c>16 * 2^(b−8)</c> and nominal peak white at <c>235 * 2^(b−8)</c>, so
/// moving between depths is an exact power of two and nothing else. Ten-bit white is 940, and
/// <c>round(940 * 255 / 1023)</c> is 234 where the format says 235 — a level off at the top of every
/// picture, in the direction that looks like nothing. The scaling below is therefore by
/// <c>2^(b−8)</c>, which is the same arithmetic <see cref="ChannelScaling"/> is defending, applied
/// to a range that is defined differently.
/// <para/>
/// The result is the same 8-bit fixed-point matrix every other colour conversion in this library
/// uses, with the shift widened by the extra bits of depth so that the single rounding at the end
/// covers both the matrix and the reduction.
/// </remarks>
internal static class ProResColorConversion {

  /// <summary>
  /// Packs the planes into 8-bit RGB, or RGBA where the frame carries alpha, cropping to the frame's
  /// stated size.
  /// </summary>
  /// <param name="planes">The reconstructed planes, which are a whole number of macroblocks across.</param>
  /// <param name="width">The frame's <c>horizontal_size</c>; columns past it are discarded.</param>
  /// <param name="height">The frame's <c>vertical_size</c>.</param>
  /// <param name="matrixCoefficients">The frame header's <c>matrix_coefficients</c>.</param>
  internal static byte[] ToPackedColour(ProResPlanes planes, int width, int height, int matrixCoefficients) {
    var alpha = planes.Alpha;
    var channels = alpha == null ? 3 : 4;
    var rgb = new byte[width * height * channels];
    var (redFromCr, greenFromCb, greenFromCr, blueFromCb) = Matrix(matrixCoefficients, height);

    // The extra bits the samples carry over eight, which is exactly the factor between the same
    // signal level at two depths.
    var extra = planes.BitDepth - 8;
    var black = 16 << extra;
    var centre = 128 << extra;

    // One rounding for the matrix and the reduction together: the matrix is 8-bit fixed point and
    // the samples carry `extra` bits more than the result wants.
    var shift = 8 + extra;
    var half = 1 << (shift - 1);

    var subsampled = planes.Width != planes.ChromaWidth;
    var lastChromaColumn = planes.ChromaWidth - 1;

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * planes.Width;
      var chromaRow = y * planes.ChromaWidth;
      var target = y * width * channels;

      for (var x = 0; x < width; ++x) {
        // 298/256 is 219/255 inverted: the studio-swing luma range expanded to fill the byte.
        var scaledLuma = 298 * (planes.Luma[lumaRow + x] - black);
        var blueDifference = _Chroma(planes.Cb, chromaRow, x, subsampled, lastChromaColumn) - centre;
        var redDifference = _Chroma(planes.Cr, chromaRow, x, subsampled, lastChromaColumn) - centre;

        rgb[target] = _Clamp(scaledLuma + redFromCr * redDifference + half, shift);
        rgb[target + 1] = _Clamp(scaledLuma - greenFromCb * blueDifference - greenFromCr * redDifference + half, shift);
        rgb[target + 2] = _Clamp(scaledLuma + blueFromCb * blueDifference + half, shift);
        if (alpha != null)
          rgb[target + 3] = _Alpha(alpha[lumaRow + x], planes.AlphaBitDepth);

        target += channels;
      }
    }

    return rgb;
  }

  /// <summary>
  /// One alpha value at eight bits, RDD 36:2022, 7.5.2.
  /// </summary>
  /// <remarks>
  /// This <i>is</i> <see cref="ChannelScaling"/>'s reduction, and it is worth saying why when the
  /// colour components above are not. 7.5.2 defines the promotion and demotion of alpha by treating
  /// the smallest and largest possible values as opacities of exactly 0.0 and 1.0 — which is a
  /// channel that fills its range, so the conversion is <c>alpha * 255 / 65535</c> to nearest and
  /// <see cref="ChannelScaling.Reduce16"/> is exactly it. Colour components do not fill their range,
  /// which is the whole of the difference between the two cases.
  /// <para/>
  /// Eight-bit alpha needs no conversion at all: 7.5.2 says the samples shall be the decoded values
  /// themselves, and since alpha is coded losslessly they are the values the encoder was handed.
  /// </remarks>
  private static byte _Alpha(ushort value, int alphaBitDepth)
    => alphaBitDepth == 8 ? (byte)value : ChannelScaling.Reduce16(value);

  /// <summary>
  /// One chroma sample at a luma column, at the coded depth.
  /// </summary>
  /// <remarks>
  /// At 4:4:4 there is one chroma sample per luma sample and nothing to do. At 4:2:2 the chroma
  /// sample is co-sited with the even luma column — that is what horizontal subsampling means in
  /// every standard- and high-definition system — so an even column reads its sample whole and an
  /// odd one sits halfway between two and takes their average. Repeating the even sample instead
  /// shifts every colour edge half a sample to the right, which is small, everywhere, and looks like
  /// a decode that worked.
  /// <para/>
  /// It is also why a comparison against another decoder is made on the planes and never on the
  /// packed colour: ffmpeg's scaler replicates where this interpolates, and on a hard-edged source
  /// that disagreement alone runs to tens of thousands of samples — for a decode that is otherwise
  /// within one level everywhere.
  /// </remarks>
  private static int _Chroma(ushort[] plane, int row, int x, bool subsampled, int lastColumn) {
    if (!subsampled)
      return plane[row + x];

    var near = x >> 1;
    if ((x & 1) == 0)
      return plane[row + near];

    var far = near + 1 <= lastColumn ? near + 1 : lastColumn;

    return (plane[row + near] + plane[row + far] + 1) >> 1;
  }

  /// <summary>
  /// The inverse matrix the frame header's <c>matrix_coefficients</c> names, in 8-bit fixed point.
  /// </summary>
  /// <remarks>
  /// RDD 36:2022, Table 6 gives the luma coefficients <c>Kr</c> and <c>Kb</c>; the four terms below
  /// are <c>2(1−Kr)</c>, <c>2Kb(1−Kb)/Kg</c>, <c>2Kr(1−Kr)/Kg</c> and <c>2(1−Kb)</c>, each carrying
  /// the 255/224 that expands studio-swing chroma, and each scaled by 256. Only three values of the
  /// field name a matrix; 0 and 2 both mean "unknown/unspecified" and everything else is reserved,
  /// all of which fall back on the height.
  /// <para/>
  /// The BT.601 row is the 409, 100, 208, 516 that appears in every other colour conversion in this
  /// library, arrived at independently here, which is a useful check that the general form is right.
  /// </remarks>
  internal static (int RedFromCr, int GreenFromCb, int GreenFromCr, int BlueFromCb) Matrix(
    int matrixCoefficients, int height) => matrixCoefficients switch {
    1 => (459, 55, 136, 541),  // ITU-R BT.709
    6 => (409, 100, 208, 516), // ITU-R BT.601
    9 => (430, 48, 167, 548),  // ITU-R BT.2020
    _ => height > 576 ? (459, 55, 136, 541) : (409, 100, 208, 516),
  };

  private static byte _Clamp(int scaled, int shift) {
    var value = scaled >> shift;

    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
