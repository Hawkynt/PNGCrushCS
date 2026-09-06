using System;

namespace FileFormat.Codecs.Cinepak;

/// <summary>
/// Cinepak's own colour space, and the way out of it.
/// </summary>
/// <remarks>
/// Not any of the standard ones. The matrix is
/// <code>
///   | r |   | 1.0  0.0  2.0 | | y |
///   | g | = | 1.0 -0.5 -1.0 | | u |
///   | b |   | 1.0  2.0  0.0 | | v |
/// </code>
/// which was chosen so that a decoder could do it in shifts and adds on a 68000, not because it
/// models anything about vision. Every coefficient is a power of two or a sum of two of them, and the
/// green row is the only one that needs a subtraction.
/// <para/>
/// <b>The chrominance bytes are signed</b>, two's complement, and not biased by 128. The technical
/// note on multimedia.cx says they carry a bias; they do not, and the difference is not subtle — read
/// as biased, a byte of zero becomes no colour at all where it is in fact the largest blue-difference
/// the format can state.
/// <para/>
/// Settled by measurement rather than by reading. A stream whose codebook sweeps every value of each
/// chrominance byte, of both together, and of luminance, decoded by ffmpeg, gives 5120 samples of
/// what the answer has to be; the rule below reproduces all 5120 exactly and the biased reading
/// reproduces none of them.
/// <para/>
/// The halving of the blue difference in the green row truncates toward zero rather than shifting
/// right, which for a negative odd difference is a different number. That is worth a sentence because
/// it is invisible in any single frame and wrong in 319 of those same 5120 samples, by one level
/// each.
/// <para/>
/// <b>The way in is not the way out reversed.</b> The matrix above inverts exactly in real arithmetic,
/// so there is a forward transform and <see cref="ChromaOf"/> is its chrominance half — but rounded to
/// whole bytes it stops being an inverse, and only 2669700 of the 16777216 RGB colours can be stated
/// exactly by any triple of luminance and chrominance at all. So the encoder uses the forward
/// transform to find where to look and not what to write; what it writes is settled by measuring the
/// way out. The luminance half is not here at all, for that reason: nothing needs to guess a
/// luminance when <see cref="CinepakEntrySolver"/> can work out the best one exactly.
/// </remarks>
internal static class CinepakColorConversion {

  /// <summary>
  /// The forward matrix, in the fixed point the reference encoder computes it in: 2^23 times each
  /// coefficient, so the shift below is the whole of the scaling.
  /// </summary>
  /// <remarks>
  /// FFmpeg's <c>libavcodec/cinepakenc.c</c>, verbatim. The comments there give the coefficients as
  /// -0.1429, -0.2857, 0.4286 for the blue difference and 0.3571, -0.2857, -0.0714 for the red —
  /// sevenths, which is what makes the inverse a matrix of halves and doubles — and the rounded
  /// integers are what every file that encoder wrote was made with, so they are copied rather than
  /// re-derived from the fractions.
  /// </remarks>
  private const int _FIXED_POINT_SHIFT = 23;

  private const int _BLUE_DIFFERENCE_RED = -299683;
  private const int _BLUE_DIFFERENCE_GREEN = -599156;
  private const int _BLUE_DIFFERENCE_BLUE = 898839;

  private const int _RED_DIFFERENCE_RED = 748893;
  private const int _RED_DIFFERENCE_GREEN = -599156;
  private const int _RED_DIFFERENCE_BLUE = -149737;

  /// <summary>
  /// Turns one codebook entry's four luminances and one chrominance pair into four RGB triplets.
  /// </summary>
  /// <remarks>
  /// Done once when the entry is stored rather than once per pixel that uses it. A codebook entry is
  /// written a few hundred times a frame at most and read tens of thousands of times, and the four
  /// triplets are what both coding types paint with — a V1 block repeats each of them over a 2x2
  /// square and a V4 block takes one triplet from each of four entries, but neither ever wants
  /// anything but these four colours.
  /// </remarks>
  /// <param name="luminance">The entry's four luminance bytes, y0 to y3.</param>
  /// <param name="u">The blue-difference byte as stored.</param>
  /// <param name="v">The red-difference byte as stored.</param>
  /// <param name="into">Twelve bytes to write the four RGB triplets into.</param>
  internal static void ToRgb(ReadOnlySpan<byte> luminance, byte u, byte v, Span<byte> into) {
    int blueDifference = (sbyte)u;
    int redDifference = (sbyte)v;

    var red = redDifference * 2;
    var green = -(blueDifference / 2) - redDifference;
    var blue = blueDifference * 2;

    for (var sample = 0; sample < 4; ++sample) {
      int y = luminance[sample];
      into[sample * 3] = _Clamp(y + red);
      into[sample * 3 + 1] = _Clamp(y + green);
      into[sample * 3 + 2] = _Clamp(y + blue);
    }
  }

  /// <summary>Turns a grey codebook entry's four luminances into four RGB triplets.</summary>
  /// <remarks>
  /// The 8-bit codebook chunks carry no chrominance at all, which for signed differences means both
  /// are zero: every row of the matrix then collapses to the luminance, so the triplet is the
  /// luminance three times over and no clamping can be needed.
  /// </remarks>
  internal static void ToGrey(ReadOnlySpan<byte> luminance, Span<byte> into) {
    for (var sample = 0; sample < 4; ++sample) {
      into[sample * 3] = luminance[sample];
      into[sample * 3 + 1] = luminance[sample];
      into[sample * 3 + 2] = luminance[sample];
    }
  }

  /// <summary>
  /// The chrominance pair four colours average to, from the sums of their channels.
  /// </summary>
  /// <remarks>
  /// The sums and not the means, because that is what the reference encoder feeds the matrix: one
  /// chrominance pair always covers four samples, and scaling down before the multiply throws away two
  /// bits that the fixed point is there to keep. The division by four is already inside the
  /// coefficients, which is why the chrominance rows look a quarter the size of the luminance ones.
  /// </remarks>
  internal static (int BlueDifference, int RedDifference) ChromaOf(int redSum, int greenSum, int blueSum) {
    var blueDifference = (_BLUE_DIFFERENCE_RED * redSum + _BLUE_DIFFERENCE_GREEN * greenSum + _BLUE_DIFFERENCE_BLUE * blueSum) >> _FIXED_POINT_SHIFT;
    var redDifference = (_RED_DIFFERENCE_RED * redSum + _RED_DIFFERENCE_GREEN * greenSum + _RED_DIFFERENCE_BLUE * blueSum) >> _FIXED_POINT_SHIFT;

    return (_ClampDifference(blueDifference), _ClampDifference(redDifference));
  }

  private static byte _Clamp(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  private static int _ClampDifference(int value) => value < -128 ? -128 : value > 127 ? 127 : value;
}
