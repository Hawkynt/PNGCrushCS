using System;

namespace FileFormat.Codecs.Vp9;

/// <summary>The reversible forward transform used by lossless VP9 profile-0 pictures.</summary>
/// <remarks>
/// This is independently derived as the inverse of <see cref="Vp9InverseTransform"/>'s lossless
/// four-point transform and cross-checked against libvpx's BSD-3-Clause <c>vp9_fwht4x4_c</c>.
/// The returned coefficients are already in coded (quantised) units: libvpx's forward transform
/// multiplies its output by the unit quantiser four and the lossless quantiser divides it by four,
/// while the decoder performs the matching multiply by four before the inverse transform.
/// </remarks>
internal static class Vp9ForwardTransform {

  internal static void WalshHadamard4x4(ReadOnlySpan<int> input, Span<int> output) {
    if (input.Length < 16)
      throw new ArgumentException("A VP9 4x4 transform needs sixteen input samples.", nameof(input));
    if (output.Length < 16)
      throw new ArgumentException("A VP9 4x4 transform needs sixteen output coefficients.", nameof(output));

    Span<int> intermediate = stackalloc int[16];

    for (var column = 0; column < 4; ++column)
      _Forward(
        input[column], input[4 + column], input[8 + column], input[12 + column],
        intermediate, column, 4);

    for (var row = 0; row < 4; ++row) {
      var at = row * 4;
      _Forward(
        intermediate[at], intermediate[at + 1], intermediate[at + 2], intermediate[at + 3],
        output, at, 1);
    }
  }

  private static void _Forward(int a, int b, int c, int d, Span<int> output, int at, int stride) {
    a += b;
    d -= c;
    var e = (a - d) >> 1;
    b = e - b;
    c = e - c;
    a -= c;
    d += b;

    output[at] = a;
    output[at + stride] = c;
    output[at + 2 * stride] = d;
    output[at + 3 * stride] = b;
  }
}
