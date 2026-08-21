using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// The two inverse transforms of VP8 and the summation of residue onto prediction (RFC 6386, 14).
/// </summary>
/// <remarks>
/// Both transforms are specified as arithmetic and not as a formula with a tolerance, unlike the
/// inverse DCT of the MPEG standards. A decoder that computes them any other way — in floating
/// point, at wider precision, with the passes reordered — produces a picture that is close to right
/// and is not right, and because the result is fed back as a reference frame, "close" grows into
/// "wrong" over a group of pictures. So the intermediate values are held in sixteen bits here
/// because the standard says they are, and the rounding is written as the standard writes it.
/// <para/>
/// There is no separate shortcut for a block whose only non-zero coefficient is the first. The full
/// transform applied to such a block already produces a constant, and produces exactly the value the
/// shortcut in the reference decoder does — so the shortcut is a saving of time and never of
/// difference, and is left out in favour of one path that is easier to check.
/// </remarks>
internal static class Vp8Transform {

  private const int _COSINE = 20091;
  private const int _SINE = 35468;

  /// <summary>
  /// Inverts the Walsh-Hadamard transform of the Y2 block and scatters the results into the first
  /// coefficient of each of the sixteen luma blocks (RFC 6386, 14.3).
  /// </summary>
  /// <param name="coefficients">The twenty-five blocks of the macroblock.</param>
  internal static void InvertWalshHadamard(Span<short> coefficients) {
    var input = coefficients.Slice(Vp8TokenReader.Y2_BLOCK * 16, 16);
    Span<short> intermediate = stackalloc short[16];

    for (var column = 0; column < 4; ++column) {
      var a = input[column] + input[12 + column];
      var b = input[4 + column] + input[8 + column];
      var c = input[4 + column] - input[8 + column];
      var d = input[column] - input[12 + column];

      intermediate[column] = (short)(a + b);
      intermediate[4 + column] = (short)(c + d);
      intermediate[8 + column] = (short)(a - b);
      intermediate[12 + column] = (short)(d - c);
    }

    for (var row = 0; row < 4; ++row) {
      var at = row * 4;
      var a = intermediate[at] + intermediate[at + 3];
      var b = intermediate[at + 1] + intermediate[at + 2];
      var c = intermediate[at + 1] - intermediate[at + 2];
      var d = intermediate[at] - intermediate[at + 3];

      coefficients[(at + 0) * 16] = (short)((a + b + 3) >> 3);
      coefficients[(at + 1) * 16] = (short)((c + d + 3) >> 3);
      coefficients[(at + 2) * 16] = (short)((a - b + 3) >> 3);
      coefficients[(at + 3) * 16] = (short)((d - c + 3) >> 3);
    }
  }

  /// <summary>
  /// Inverts the DCT of one 4x4 block and adds the result to the prediction already sitting in the
  /// plane (RFC 6386, 14.4 and 14.5).
  /// </summary>
  /// <param name="block">The sixteen dequantised coefficients, in raster order.</param>
  /// <param name="plane">The plane holding the prediction, which is overwritten with the reconstruction.</param>
  /// <param name="offset">Where the block's top-left sample sits in that plane.</param>
  /// <param name="stride">The plane's row length.</param>
  internal static void AddResidue(ReadOnlySpan<short> block, byte[] plane, int offset, int stride) {
    Span<short> intermediate = stackalloc short[16];

    for (var column = 0; column < 4; ++column) {
      int first = block[column];
      int second = block[4 + column];
      int third = block[8 + column];
      int fourth = block[12 + column];

      var a = first + third;
      var b = first - third;
      var c = ((second * _SINE) >> 16) - (fourth + ((fourth * _COSINE) >> 16));
      var d = second + ((second * _COSINE) >> 16) + ((fourth * _SINE) >> 16);

      intermediate[column] = (short)(a + d);
      intermediate[12 + column] = (short)(a - d);
      intermediate[4 + column] = (short)(b + c);
      intermediate[8 + column] = (short)(b - c);
    }

    for (var row = 0; row < 4; ++row) {
      var at = row * 4;
      int first = intermediate[at];
      int second = intermediate[at + 1];
      int third = intermediate[at + 2];
      int fourth = intermediate[at + 3];

      var a = first + third;
      var b = first - third;
      var c = ((second * _SINE) >> 16) - (fourth + ((fourth * _COSINE) >> 16));
      var d = second + ((second * _COSINE) >> 16) + ((fourth * _SINE) >> 16);

      var target = offset + row * stride;
      plane[target] = _Clamp(plane[target] + ((a + d + 4) >> 3));
      plane[target + 1] = _Clamp(plane[target + 1] + ((b + c + 4) >> 3));
      plane[target + 2] = _Clamp(plane[target + 2] + ((b - c + 4) >> 3));
      plane[target + 3] = _Clamp(plane[target + 3] + ((a - d + 4) >> 3));
    }
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
