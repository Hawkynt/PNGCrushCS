using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// The forward transform the encoder measures blocks with: the accurate integer DCT of the
/// Independent JPEG Group, as FFmpeg carries it.
/// </summary>
/// <remarks>
/// Rows then columns, both in place and both held as sixteen-bit values, with the output left scaled
/// up by eight relative to a true DCT. That scaling is not incidental: DV's forward weighting matrix
/// and the encoder's rounding are written for exactly it, and the decoder's inverse transform undoes
/// it. Feeding a differently normalised DCT into the same weights produces a picture that is dark or
/// washed out rather than a picture that is wrong in some obvious way.
/// <para/>
/// The samples go in unchanged rather than centred on zero, so a mid-grey block has a first
/// coefficient of 8192 rather than nought. That is the convention <see cref="DvInverseDct"/> inverts,
/// and it is why the decoder adds 1024 to a block's first coefficient before transforming it.
/// <para/>
/// The transform is only used to measure and quantise: nothing here has to match another encoder,
/// since a decoder reads the coefficients that come out and never the pixels that went in. It is
/// FFmpeg's rather than something simpler because a fast approximation costs picture quality at a
/// fixed bit rate, and DV's rate is fixed by definition.
/// </remarks>
internal static class DvForwardDct {

  private const int _CONSTANT_BITS = 13;
  private const int _ROW_BITS = 4;

  // cos and sin combinations of k*pi/16, at 1/2^13.
  private const int _FIX_0_298631336 = 2446;
  private const int _FIX_0_390180644 = 3196;
  private const int _FIX_0_541196100 = 4433;
  private const int _FIX_0_765366865 = 6270;
  private const int _FIX_0_899976223 = 7373;
  private const int _FIX_1_175875602 = 9633;
  private const int _FIX_1_501321110 = 12299;
  private const int _FIX_1_847759065 = 15137;
  private const int _FIX_1_961570560 = 16069;
  private const int _FIX_2_053119869 = 16819;
  private const int _FIX_2_562915447 = 20995;
  private const int _FIX_3_072711026 = 25172;

  /// <summary>Transforms one block in place.</summary>
  internal static void Transform(Span<short> block) {
    _Rows(block);

    unchecked {
      for (var column = 0; column < 8; ++column) {
        var tmp0 = block[column] + block[column + 8 * 7];
        var tmp7 = block[column] - block[column + 8 * 7];
        var tmp1 = block[column + 8] + block[column + 8 * 6];
        var tmp6 = block[column + 8] - block[column + 8 * 6];
        var tmp2 = block[column + 8 * 2] + block[column + 8 * 5];
        var tmp5 = block[column + 8 * 2] - block[column + 8 * 5];
        var tmp3 = block[column + 8 * 3] + block[column + 8 * 4];
        var tmp4 = block[column + 8 * 3] - block[column + 8 * 4];

        var tmp10 = tmp0 + tmp3;
        var tmp13 = tmp0 - tmp3;
        var tmp11 = tmp1 + tmp2;
        var tmp12 = tmp1 - tmp2;

        block[column] = (short)_Descale(tmp10 + tmp11, _ROW_BITS);
        block[column + 8 * 4] = (short)_Descale(tmp10 - tmp11, _ROW_BITS);

        var z1 = (tmp12 + tmp13) * _FIX_0_541196100;
        block[column + 8 * 2] = (short)_Descale(z1 + tmp13 * _FIX_0_765366865, _CONSTANT_BITS + _ROW_BITS);
        block[column + 8 * 6] = (short)_Descale(z1 + tmp12 * -_FIX_1_847759065, _CONSTANT_BITS + _ROW_BITS);

        var o1 = tmp4 + tmp7;
        var o2 = tmp5 + tmp6;
        var o3 = tmp4 + tmp6;
        var o4 = tmp5 + tmp7;
        var o5 = (o3 + o4) * _FIX_1_175875602;

        tmp4 *= _FIX_0_298631336;
        tmp5 *= _FIX_2_053119869;
        tmp6 *= _FIX_3_072711026;
        tmp7 *= _FIX_1_501321110;
        o1 *= -_FIX_0_899976223;
        o2 *= -_FIX_2_562915447;
        o3 = o3 * -_FIX_1_961570560 + o5;
        o4 = o4 * -_FIX_0_390180644 + o5;

        block[column + 8 * 7] = (short)_Descale(tmp4 + o1 + o3, _CONSTANT_BITS + _ROW_BITS);
        block[column + 8 * 5] = (short)_Descale(tmp5 + o2 + o4, _CONSTANT_BITS + _ROW_BITS);
        block[column + 8 * 3] = (short)_Descale(tmp6 + o2 + o3, _CONSTANT_BITS + _ROW_BITS);
        block[column + 8] = (short)_Descale(tmp7 + o1 + o4, _CONSTANT_BITS + _ROW_BITS);
      }
    }
  }

  /// <summary>The row pass, whose results are left scaled up by a further 2^4.</summary>
  private static void _Rows(Span<short> block) {
    unchecked {
      for (var offset = 0; offset < 64; offset += 8) {
        var row = block.Slice(offset, 8);

        var tmp0 = row[0] + row[7];
        var tmp7 = row[0] - row[7];
        var tmp1 = row[1] + row[6];
        var tmp6 = row[1] - row[6];
        var tmp2 = row[2] + row[5];
        var tmp5 = row[2] - row[5];
        var tmp3 = row[3] + row[4];
        var tmp4 = row[3] - row[4];

        var tmp10 = tmp0 + tmp3;
        var tmp13 = tmp0 - tmp3;
        var tmp11 = tmp1 + tmp2;
        var tmp12 = tmp1 - tmp2;

        row[0] = (short)((tmp10 + tmp11) << _ROW_BITS);
        row[4] = (short)((tmp10 - tmp11) << _ROW_BITS);

        var z1 = (tmp12 + tmp13) * _FIX_0_541196100;
        row[2] = (short)_Descale(z1 + tmp13 * _FIX_0_765366865, _CONSTANT_BITS - _ROW_BITS);
        row[6] = (short)_Descale(z1 + tmp12 * -_FIX_1_847759065, _CONSTANT_BITS - _ROW_BITS);

        var o1 = tmp4 + tmp7;
        var o2 = tmp5 + tmp6;
        var o3 = tmp4 + tmp6;
        var o4 = tmp5 + tmp7;
        var o5 = (o3 + o4) * _FIX_1_175875602;

        tmp4 *= _FIX_0_298631336;
        tmp5 *= _FIX_2_053119869;
        tmp6 *= _FIX_3_072711026;
        tmp7 *= _FIX_1_501321110;
        o1 *= -_FIX_0_899976223;
        o2 *= -_FIX_2_562915447;
        o3 = o3 * -_FIX_1_961570560 + o5;
        o4 = o4 * -_FIX_0_390180644 + o5;

        row[7] = (short)_Descale(tmp4 + o1 + o3, _CONSTANT_BITS - _ROW_BITS);
        row[5] = (short)_Descale(tmp5 + o2 + o4, _CONSTANT_BITS - _ROW_BITS);
        row[3] = (short)_Descale(tmp6 + o2 + o3, _CONSTANT_BITS - _ROW_BITS);
        row[1] = (short)_Descale(tmp7 + o1 + o4, _CONSTANT_BITS - _ROW_BITS);
      }
    }
  }

  private static int _Descale(int value, int bits) => (value + (1 << (bits - 1))) >> bits;
}
