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
/// </remarks>
internal static class CinepakColorConversion {

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

  private static byte _Clamp(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}
