using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// The two inverse transforms a DV block may have been coded with: the ordinary 8x8 one and the
/// 2-4-8 one an interlaced macroblock uses.
/// </summary>
/// <remarks>
/// This is the integer transform FFmpeg calls <c>simple_idct</c>, carried across coefficient for
/// coefficient and rounding step for rounding step, because an inverse DCT is only defined to a
/// tolerance and two implementations inside that tolerance still disagree by a level here and there.
/// Matching one exactly is what lets this decoder be checked sample for sample against FFmpeg's
/// rather than to within a plausible-looking error.
/// <para/>
/// Rows first, then columns, both in place in the block. The intermediate row results are held as
/// sixteen-bit values on purpose — that truncation is part of the transform's definition here, not an
/// accident of storage, and widening it changes the output.
/// <para/>
/// The row pass has a shortcut for a row whose only non-zero coefficient is the first, and the
/// shortcut is <b>not</b> arithmetically the same as the general path: for a first coefficient above
/// 1023 the two differ by one. Both are the transform as it is actually specified by the code every
/// DV file in existence was written and read by, and a DV block's first coefficient reaches 2044, so
/// the shortcut is a rule rather than an optimisation.
/// </remarks>
internal static class DvInverseDct {

  // cos(i * pi / 16) * sqrt(2) * 2^14, rounded.
  private const int _W1 = 22725;
  private const int _W2 = 21407;
  private const int _W3 = 19266;
  private const int _W4 = 16383;
  private const int _W5 = 12873;
  private const int _W6 = 8867;
  private const int _W7 = 4520;

  private const int _ROW_SHIFT = 11;
  private const int _COLUMN_SHIFT = 20;
  private const int _DC_SHIFT = 3;

  /// <summary>The rounding term the column pass folds into its first coefficient.</summary>
  private const int _COLUMN_BIAS = (1 << (_COLUMN_SHIFT - 1)) / _W4;

  // The 2-4-8 transform's quarter-height column pass: cos(i * pi / 16) * 2^12, rounded.
  private const int _C1 = 2676;
  private const int _C2 = 1108;
  private const int _C_SHIFT = 4 + 1 + 12;

  /// <summary>
  /// Transforms a block and writes it as eight-bit samples.
  /// </summary>
  internal static void Put(Span<short> block, byte[] plane, int offset, int stride) {
    for (var i = 0; i < 8; ++i)
      _Row(block.Slice(i * 8, 8));

    for (var i = 0; i < 8; ++i)
      _ColumnPut(block, i, plane, offset + i, stride);
  }

  /// <summary>
  /// Transforms a block coded with the 2-4-8 transform and writes it as eight-bit samples.
  /// </summary>
  /// <remarks>
  /// A butterfly across the two fields, then the ordinary row pass, then a four-point column pass run
  /// twice — once over each field. The butterfly is what makes the transform worth having on
  /// interlaced material: it turns a pair of lines from opposite fields into their sum and difference,
  /// and a still picture puts almost nothing in the difference.
  /// </remarks>
  internal static void Put248(Span<short> block, byte[] plane, int offset, int stride) {
    unchecked {
      for (var pair = 0; pair < 4; ++pair) {
        var top = pair * 16;
        for (var k = 0; k < 8; ++k) {
          int a = block[top + k];
          int b = block[top + 8 + k];
          block[top + k] = (short)(a + b);
          block[top + 8 + k] = (short)(a - b);
        }
      }
    }

    for (var i = 0; i < 8; ++i)
      _Row(block.Slice(i * 8, 8));

    for (var i = 0; i < 8; ++i) {
      _QuarterColumnPut(block, i, plane, offset + i, stride * 2);
      _QuarterColumnPut(block, 8 + i, plane, offset + stride + i, stride * 2);
    }
  }

  /// <summary>One row of the eight-point transform, in place.</summary>
  private static void _Row(Span<short> row) {
    unchecked {
      if ((row[1] | row[2] | row[3] | row[4] | row[5] | row[6] | row[7]) == 0) {
        var flat = (short)(row[0] << _DC_SHIFT);
        row[0] = flat;
        row[1] = flat;
        row[2] = flat;
        row[3] = flat;
        row[4] = flat;
        row[5] = flat;
        row[6] = flat;
        row[7] = flat;
        return;
      }

      var a0 = _W4 * row[0] + (1 << (_ROW_SHIFT - 1));
      var a1 = a0;
      var a2 = a0;
      var a3 = a0;

      a0 += _W2 * row[2];
      a1 += _W6 * row[2];
      a2 -= _W6 * row[2];
      a3 -= _W2 * row[2];

      var b0 = _W1 * row[1] + _W3 * row[3];
      var b1 = _W3 * row[1] - _W7 * row[3];
      var b2 = _W5 * row[1] - _W1 * row[3];
      var b3 = _W7 * row[1] - _W5 * row[3];

      a0 += _W4 * row[4] + _W6 * row[6];
      a1 += -_W4 * row[4] - _W2 * row[6];
      a2 += -_W4 * row[4] + _W2 * row[6];
      a3 += _W4 * row[4] - _W6 * row[6];

      b0 += _W5 * row[5] + _W7 * row[7];
      b1 += -_W1 * row[5] - _W5 * row[7];
      b2 += _W7 * row[5] + _W3 * row[7];
      b3 += _W3 * row[5] - _W1 * row[7];

      row[0] = (short)((a0 + b0) >> _ROW_SHIFT);
      row[7] = (short)((a0 - b0) >> _ROW_SHIFT);
      row[1] = (short)((a1 + b1) >> _ROW_SHIFT);
      row[6] = (short)((a1 - b1) >> _ROW_SHIFT);
      row[2] = (short)((a2 + b2) >> _ROW_SHIFT);
      row[5] = (short)((a2 - b2) >> _ROW_SHIFT);
      row[3] = (short)((a3 + b3) >> _ROW_SHIFT);
      row[4] = (short)((a3 - b3) >> _ROW_SHIFT);
    }
  }

  /// <summary>One column of the eight-point transform, written straight out as samples.</summary>
  private static void _ColumnPut(Span<short> block, int column, byte[] plane, int offset, int stride) {
    unchecked {
      var a0 = _W4 * (block[column] + _COLUMN_BIAS);
      var a1 = a0;
      var a2 = a0;
      var a3 = a0;

      a0 += _W2 * block[column + 8 * 2];
      a1 += _W6 * block[column + 8 * 2];
      a2 -= _W6 * block[column + 8 * 2];
      a3 -= _W2 * block[column + 8 * 2];

      var b0 = _W1 * block[column + 8] + _W3 * block[column + 8 * 3];
      var b1 = _W3 * block[column + 8] - _W7 * block[column + 8 * 3];
      var b2 = _W5 * block[column + 8] - _W1 * block[column + 8 * 3];
      var b3 = _W7 * block[column + 8] - _W5 * block[column + 8 * 3];

      a0 += _W4 * block[column + 8 * 4] + _W6 * block[column + 8 * 6];
      a1 += -_W4 * block[column + 8 * 4] - _W2 * block[column + 8 * 6];
      a2 += -_W4 * block[column + 8 * 4] + _W2 * block[column + 8 * 6];
      a3 += _W4 * block[column + 8 * 4] - _W6 * block[column + 8 * 6];

      b0 += _W5 * block[column + 8 * 5] + _W7 * block[column + 8 * 7];
      b1 += -_W1 * block[column + 8 * 5] - _W5 * block[column + 8 * 7];
      b2 += _W7 * block[column + 8 * 5] + _W3 * block[column + 8 * 7];
      b3 += _W3 * block[column + 8 * 5] - _W1 * block[column + 8 * 7];

      plane[offset] = _Clamp((a0 + b0) >> _COLUMN_SHIFT);
      plane[offset + stride] = _Clamp((a1 + b1) >> _COLUMN_SHIFT);
      plane[offset + stride * 2] = _Clamp((a2 + b2) >> _COLUMN_SHIFT);
      plane[offset + stride * 3] = _Clamp((a3 + b3) >> _COLUMN_SHIFT);
      plane[offset + stride * 4] = _Clamp((a3 - b3) >> _COLUMN_SHIFT);
      plane[offset + stride * 5] = _Clamp((a2 - b2) >> _COLUMN_SHIFT);
      plane[offset + stride * 6] = _Clamp((a1 - b1) >> _COLUMN_SHIFT);
      plane[offset + stride * 7] = _Clamp((a0 - b0) >> _COLUMN_SHIFT);
    }
  }

  /// <summary>One column of the four-point transform the 2-4-8 arrangement ends in.</summary>
  private static void _QuarterColumnPut(Span<short> block, int column, byte[] plane, int offset, int stride) {
    unchecked {
      int a0 = block[column];
      int a1 = block[column + 8 * 2];
      int a2 = block[column + 8 * 4];
      int a3 = block[column + 8 * 6];

      var c0 = ((a0 + a2) << (12 - 1)) + (1 << (_C_SHIFT - 1));
      var c2 = ((a0 - a2) << (12 - 1)) + (1 << (_C_SHIFT - 1));
      var c1 = a1 * _C1 + a3 * _C2;
      var c3 = a1 * _C2 - a3 * _C1;

      plane[offset] = _Clamp((c0 + c1) >> _C_SHIFT);
      plane[offset + stride] = _Clamp((c2 + c3) >> _C_SHIFT);
      plane[offset + stride * 2] = _Clamp((c2 - c3) >> _C_SHIFT);
      plane[offset + stride * 3] = _Clamp((c0 - c1) >> _C_SHIFT);
    }
  }

  private static byte _Clamp(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}
