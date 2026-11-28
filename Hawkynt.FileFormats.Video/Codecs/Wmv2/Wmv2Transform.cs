using System;

namespace FileFormat.Codecs.Wmv2;

/// <summary>
/// WMV2's inverse transforms: its own 8x8 one, and the 8x4 and 4x8 halves an adaptively transformed
/// block of a predicted macroblock is coded with.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/wmv2dsp.c</c> (the 8x8 transform, which is Microsoft's own
/// and not the <c>simple_idct</c> WMV1 is decoded with) and <c>simple_idct.c</c> (the halves,
/// <c>ff_simple_idct84_add</c> and <c>ff_simple_idct48_add</c>), LGPL-2.1-or-later; see
/// <c>MsMpeg4/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Every intermediate is held in sixteen bits where
/// FFmpeg's block of <c>int16_t</c> holds it, because that truncation is part of what the decoders in
/// use compute.
/// </remarks>
internal static class Wmv2Transform {

  // 2048 * sqrt(2) * cos(i * pi / 16), rounded: wmv2dsp.c's W0 to W7.
  private const int _W0 = 2048;
  private const int _W1 = 2841;
  private const int _W2 = 2676;
  private const int _W3 = 2408;
  private const int _W5 = 1609;
  private const int _W6 = 1108;
  private const int _W7 = 565;

  // simple_idct's eight-point weights: cos(i * pi / 16) * sqrt(2) * 2^14, rounded.
  private const int _S1 = 22725;
  private const int _S2 = 21407;
  private const int _S3 = 19266;
  private const int _S4 = 16383;
  private const int _S5 = 12873;
  private const int _S6 = 8867;
  private const int _S7 = 4520;
  private const int _COLUMN_SHIFT = 20;

  // The four-point column pass: x * sqrt(2) * 2^12, rounded, for x = 0.6532814824, 0.2705980501, 0.5.
  private const int _C1 = 3784;
  private const int _C2 = 1567;
  private const int _C3 = 2896;
  private const int _C_SHIFT = 4 + 1 + 12;

  // The four-point row pass: the same three at 2^15.
  private const int _R1 = 30274;
  private const int _R2 = 12540;
  private const int _R3 = 23170;
  private const int _R_SHIFT = 11;

  /// <summary>The 8x8 transform in place: rows, then columns, as <c>wmv2_idct_put_c</c> and <c>_add_c</c> run it.</summary>
  internal static void Transform(Span<int> block) {
    Span<short> b = stackalloc short[64];
    for (var i = 0; i < 64; ++i)
      b[i] = unchecked((short)block[i]);

    for (var i = 0; i < 64; i += 8)
      _Row(b.Slice(i, 8));
    for (var i = 0; i < 8; ++i)
      _Column(b, i);

    for (var i = 0; i < 64; ++i)
      block[i] = b[i];
  }

  /// <summary>
  /// The 8x4 transform of one half: an eight-point pass along each of the four rows, then a four-point
  /// pass down each column, the residual written to the four rows of <paramref name="residual"/> it
  /// starts at.
  /// </summary>
  internal static void Transform84(ReadOnlySpan<int> levels, Span<int> residual) {
    Span<short> b = stackalloc short[64];
    for (var i = 0; i < 32; ++i)
      b[i] = unchecked((short)levels[i]);

    for (var i = 0; i < 4; ++i)
      Dv.DvInverseDct.Row(b.Slice(i * 8, 8));

    unchecked {
      for (var i = 0; i < 8; ++i) {
        int a0 = b[i], a1 = b[8 + i], a2 = b[16 + i], a3 = b[24 + i];
        var c0 = (a0 + a2) * _C3 + (1 << (_C_SHIFT - 1));
        var c2 = (a0 - a2) * _C3 + (1 << (_C_SHIFT - 1));
        var c1 = a1 * _C1 + a3 * _C2;
        var c3 = a1 * _C2 - a3 * _C1;
        residual[i] = (c0 + c1) >> _C_SHIFT;
        residual[8 + i] = (c2 + c3) >> _C_SHIFT;
        residual[16 + i] = (c2 - c3) >> _C_SHIFT;
        residual[24 + i] = (c0 - c1) >> _C_SHIFT;
      }
    }
  }

  /// <summary>
  /// The 4x8 transform of one half: a four-point pass along each of the eight rows, then an
  /// eight-point pass down each of the four columns, the residual written to the four columns of
  /// <paramref name="residual"/> it starts at, whose rows are eight apart.
  /// </summary>
  internal static void Transform48(ReadOnlySpan<int> levels, Span<int> residual) {
    Span<short> b = stackalloc short[64];
    for (var i = 0; i < 64; ++i)
      b[i] = unchecked((short)levels[i]);

    unchecked {
      for (var i = 0; i < 8; ++i) {
        var row = b.Slice(i * 8, 8);
        int a0 = row[0], a1 = row[1], a2 = row[2], a3 = row[3];
        var c0 = (a0 + a2) * _R3 + (1 << (_R_SHIFT - 1));
        var c2 = (a0 - a2) * _R3 + (1 << (_R_SHIFT - 1));
        var c1 = a1 * _R1 + a3 * _R2;
        var c3 = a1 * _R2 - a3 * _R1;
        row[0] = (short)((c0 + c1) >> _R_SHIFT);
        row[1] = (short)((c2 + c3) >> _R_SHIFT);
        row[2] = (short)((c2 - c3) >> _R_SHIFT);
        row[3] = (short)((c0 - c1) >> _R_SHIFT);
      }

      for (var column = 0; column < 4; ++column) {
        var a0 = _S4 * (b[column] + ((1 << (_COLUMN_SHIFT - 1)) / _S4));
        var a1 = a0;
        var a2 = a0;
        var a3 = a0;

        a0 += _S2 * b[column + 16];
        a1 += _S6 * b[column + 16];
        a2 -= _S6 * b[column + 16];
        a3 -= _S2 * b[column + 16];

        var b0 = _S1 * b[column + 8] + _S3 * b[column + 24];
        var b1 = _S3 * b[column + 8] - _S7 * b[column + 24];
        var b2 = _S5 * b[column + 8] - _S1 * b[column + 24];
        var b3 = _S7 * b[column + 8] - _S5 * b[column + 24];

        a0 += _S4 * b[column + 32] + _S6 * b[column + 48];
        a1 += -_S4 * b[column + 32] - _S2 * b[column + 48];
        a2 += -_S4 * b[column + 32] + _S2 * b[column + 48];
        a3 += _S4 * b[column + 32] - _S6 * b[column + 48];

        b0 += _S5 * b[column + 40] + _S7 * b[column + 56];
        b1 += -_S1 * b[column + 40] - _S5 * b[column + 56];
        b2 += _S7 * b[column + 40] + _S3 * b[column + 56];
        b3 += _S3 * b[column + 40] - _S1 * b[column + 56];

        residual[column] = (a0 + b0) >> _COLUMN_SHIFT;
        residual[column + 8] = (a1 + b1) >> _COLUMN_SHIFT;
        residual[column + 16] = (a2 + b2) >> _COLUMN_SHIFT;
        residual[column + 24] = (a3 + b3) >> _COLUMN_SHIFT;
        residual[column + 32] = (a3 - b3) >> _COLUMN_SHIFT;
        residual[column + 40] = (a2 - b2) >> _COLUMN_SHIFT;
        residual[column + 48] = (a1 - b1) >> _COLUMN_SHIFT;
        residual[column + 56] = (a0 - b0) >> _COLUMN_SHIFT;
      }
    }
  }

  private static void _Row(Span<short> b) {
    unchecked {
      var a1 = _W1 * b[1] + _W7 * b[7];
      var a7 = _W7 * b[1] - _W1 * b[7];
      var a5 = _W5 * b[5] + _W3 * b[3];
      var a3 = _W3 * b[5] - _W5 * b[3];
      var a2 = _W2 * b[2] + _W6 * b[6];
      var a6 = _W6 * b[2] - _W2 * b[6];
      var a0 = _W0 * b[0] + _W0 * b[4];
      var a4 = _W0 * b[0] - _W0 * b[4];

      var s1 = (int)(181u * (uint)(a1 - a5 + a7 - a3) + 128) >> 8;
      var s2 = (int)(181u * (uint)(a1 - a5 - a7 + a3) + 128) >> 8;

      b[0] = (short)((a0 + a2 + a1 + a5 + (1 << 7)) >> 8);
      b[1] = (short)((a4 + a6 + s1 + (1 << 7)) >> 8);
      b[2] = (short)((a4 - a6 + s2 + (1 << 7)) >> 8);
      b[3] = (short)((a0 - a2 + a7 + a3 + (1 << 7)) >> 8);
      b[4] = (short)((a0 - a2 - a7 - a3 + (1 << 7)) >> 8);
      b[5] = (short)((a4 - a6 - s2 + (1 << 7)) >> 8);
      b[6] = (short)((a4 + a6 - s1 + (1 << 7)) >> 8);
      b[7] = (short)((a0 + a2 - a1 - a5 + (1 << 7)) >> 8);
    }
  }

  private static void _Column(Span<short> block, int column) {
    unchecked {
      var a1 = (_W1 * block[column + 8] + _W7 * block[column + 56] + 4) >> 3;
      var a7 = (_W7 * block[column + 8] - _W1 * block[column + 56] + 4) >> 3;
      var a5 = (_W5 * block[column + 40] + _W3 * block[column + 24] + 4) >> 3;
      var a3 = (_W3 * block[column + 40] - _W5 * block[column + 24] + 4) >> 3;
      var a2 = (_W2 * block[column + 16] + _W6 * block[column + 48] + 4) >> 3;
      var a6 = (_W6 * block[column + 16] - _W2 * block[column + 48] + 4) >> 3;
      var a0 = (_W0 * block[column] + _W0 * block[column + 32]) >> 3;
      var a4 = (_W0 * block[column] - _W0 * block[column + 32]) >> 3;

      var s1 = (int)(181u * (uint)(a1 - a5 + a7 - a3) + 128) >> 8;
      var s2 = (int)(181u * (uint)(a1 - a5 - a7 + a3) + 128) >> 8;

      block[column] = (short)((a0 + a2 + a1 + a5 + (1 << 13)) >> 14);
      block[column + 8] = (short)((a4 + a6 + s1 + (1 << 13)) >> 14);
      block[column + 16] = (short)((a4 - a6 + s2 + (1 << 13)) >> 14);
      block[column + 24] = (short)((a0 - a2 + a7 + a3 + (1 << 13)) >> 14);
      block[column + 32] = (short)((a0 - a2 - a7 - a3 + (1 << 13)) >> 14);
      block[column + 40] = (short)((a4 - a6 - s2 + (1 << 13)) >> 14);
      block[column + 48] = (short)((a4 + a6 - s1 + (1 << 13)) >> 14);
      block[column + 56] = (short)((a0 + a2 - a1 - a5 + (1 << 13)) >> 14);
    }
  }
}
