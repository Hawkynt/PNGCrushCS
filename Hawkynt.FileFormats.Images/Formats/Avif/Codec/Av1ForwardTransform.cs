using System;

namespace FileFormat.Avif.Codec;

/// <summary>The forward transforms the still-picture encoder needs.</summary>
internal static class Av1ForwardTransform {

  /// <summary>
  /// The lossless 4x4 Walsh-Hadamard transform, in place and row-major, from libaom's
  /// <c>av1_fwht4x4_c</c>. It is the exact inverse of
  /// <see cref="Av1InverseTransform.InverseWalshHadamard4x4"/> once the quantiser has multiplied by
  /// four again, which is what makes the lossless path lossless: the lifting steps are reversible
  /// even though they contain a right shift.
  /// </summary>
  /// <param name="block">Residual on entry, coefficients on exit; at least 16 entries.</param>
  public static void WalshHadamard4x4(Span<int> block) {
    if (block.Length < 16)
      throw new ArgumentException($"The lossless 4x4 transform needs 16 samples, not {block.Length}.", nameof(block));

    // Columns first, then rows: the inverse runs rows first, then columns.
    for (var c = 0; c < 4; ++c)
      _Forward1d(block[c], block[4 + c], block[8 + c], block[12 + c], block[c..], 4);

    for (var r = 0; r < 4; ++r) {
      var row = block.Slice(r * 4, 4);
      _Forward1d(row[0], row[1], row[2], row[3], row, 1);
    }
  }

  /// <summary>libaom's 4-point forward Walsh-Hadamard butterfly. Inputs arrive in natural order and
  /// leave in the a/c/d/b order the inverse butterfly loads them in.</summary>
  private static void _Forward1d(int a1, int b1, int c1, int d1, Span<int> output, int stride) {
    a1 += b1;
    d1 -= c1;
    var e1 = (a1 - d1) >> 1;
    b1 = e1 - b1;
    c1 = e1 - c1;
    a1 -= c1;
    d1 += b1;
    output[0] = a1;
    output[stride] = c1;
    output[stride * 2] = d1;
    output[stride * 3] = b1;
  }
}
