using System;

namespace FileFormat.Codecs.Vp7;

/// <summary>
/// VP7's inverse transforms: a 4x4 DCT in fixed point with the constants 23170, 12540 and 30274,
/// and the Walsh-Hadamard-shaped transform of the Y2 block that uses the same butterflies.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/vp8dsp.c</c> (<c>vp7_idct_add_c</c>, <c>vp7_idct_dc_add_c</c>,
/// <c>vp7_luma_dc_wht_c</c>, <c>vp7_luma_dc_wht_dc_c</c>), LGPL-2.1-or-later. The intermediate rows
/// are sixteen-bit, as there, and the DC-only forms skip that truncation; a decoder that took the
/// full transform for a lone DC would differ from them on extreme values.
/// </remarks>
internal static class Vp7Transform {

  internal static void InvertWalshHadamard(Span<short> dc, Span<short> coefficients) {
    Span<short> temporary = stackalloc short[16];
    for (var i = 0; i < 4; ++i) {
      var a1 = (dc[i * 4] + dc[i * 4 + 2]) * 23170;
      var b1 = (dc[i * 4] - dc[i * 4 + 2]) * 23170;
      var c1 = dc[i * 4 + 1] * 12540 - dc[i * 4 + 3] * 30274;
      var d1 = dc[i * 4 + 1] * 30274 + dc[i * 4 + 3] * 12540;
      temporary[i * 4] = (short)((a1 + d1) >> 14);
      temporary[i * 4 + 3] = (short)((a1 - d1) >> 14);
      temporary[i * 4 + 1] = (short)((b1 + c1) >> 14);
      temporary[i * 4 + 2] = (short)((b1 - c1) >> 14);
    }

    for (var i = 0; i < 4; ++i) {
      var a1 = (temporary[i] + temporary[i + 8]) * 23170;
      var b1 = (temporary[i] - temporary[i + 8]) * 23170;
      var c1 = temporary[i + 4] * 12540 - temporary[i + 12] * 30274;
      var d1 = temporary[i + 4] * 30274 + temporary[i + 12] * 12540;
      coefficients[(0 * 4 + i) * 16] = (short)((a1 + d1 + 0x20000) >> 18);
      coefficients[(3 * 4 + i) * 16] = (short)((a1 - d1 + 0x20000) >> 18);
      coefficients[(1 * 4 + i) * 16] = (short)((b1 + c1 + 0x20000) >> 18);
      coefficients[(2 * 4 + i) * 16] = (short)((b1 - c1 + 0x20000) >> 18);
    }

    dc.Clear();
  }

  internal static void InvertWalshHadamardDc(Span<short> dc, Span<short> coefficients) {
    var value = (short)((23170 * ((23170 * dc[0]) >> 14) + 0x20000) >> 18);
    dc[0] = 0;
    for (var block = 0; block < 16; ++block)
      coefficients[block * 16] = value;
  }

  internal static void Add(Span<short> block, byte[] plane, int offset, int stride) {
    Span<short> temporary = stackalloc short[16];
    for (var i = 0; i < 4; ++i) {
      var a1 = (block[i * 4] + block[i * 4 + 2]) * 23170;
      var b1 = (block[i * 4] - block[i * 4 + 2]) * 23170;
      var c1 = block[i * 4 + 1] * 12540 - block[i * 4 + 3] * 30274;
      var d1 = block[i * 4 + 1] * 30274 + block[i * 4 + 3] * 12540;
      temporary[i * 4] = (short)((a1 + d1) >> 14);
      temporary[i * 4 + 3] = (short)((a1 - d1) >> 14);
      temporary[i * 4 + 1] = (short)((b1 + c1) >> 14);
      temporary[i * 4 + 2] = (short)((b1 - c1) >> 14);
    }

    block.Clear();
    for (var i = 0; i < 4; ++i) {
      var a1 = (temporary[i] + temporary[i + 8]) * 23170;
      var b1 = (temporary[i] - temporary[i + 8]) * 23170;
      var c1 = temporary[i + 4] * 12540 - temporary[i + 12] * 30274;
      var d1 = temporary[i + 4] * 30274 + temporary[i + 12] * 12540;
      _AddTo(plane, offset + i, (a1 + d1 + 0x20000) >> 18);
      _AddTo(plane, offset + 3 * stride + i, (a1 - d1 + 0x20000) >> 18);
      _AddTo(plane, offset + stride + i, (b1 + c1 + 0x20000) >> 18);
      _AddTo(plane, offset + 2 * stride + i, (b1 - c1 + 0x20000) >> 18);
    }
  }

  internal static void AddDc(Span<short> block, byte[] plane, int offset, int stride) {
    var dc = (23170 * ((23170 * block[0]) >> 14) + 0x20000) >> 18;
    block[0] = 0;
    for (var row = 0; row < 4; ++row)
      for (var column = 0; column < 4; ++column)
        _AddTo(plane, offset + row * stride + column, dc);
  }

  private static void _AddTo(byte[] plane, int at, int residue) {
    var value = plane[at] + residue;
    plane[at] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
