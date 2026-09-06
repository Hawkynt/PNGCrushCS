// AV1 inverse transform, ported from the libaom v3.15.0 reference implementation
// (BSD 2-Clause, Alliance for Open Media). Ported functions: inv_txfm2d_add_c,
// av1_get_inv_txfm_cfg, av1_gen_inv_stage_range, get_flip_cfg, get_rect_tx_log_ratio and
// av1_inv_txfm_shift_ls from av1/common/av1_inv_txfm2d.c; av1_idct4/8/16/32/64,
// av1_iadst4/8/16 and av1_iidentity4/8/16/32_c from av1/common/av1_inv_txfm1d.c;
// round_shift, half_btf, clamp_value/clamp_buf and the cospi/sinpi constant tables from
// av1/common/av1_txfm.{c,h} and av1/common/av1_inv_txfm1d.h;
// av1_highbd_iwht4x4_16_add_c from av1/common/av1_inv_txfm2d.c. Constant tables are copied
// verbatim: a re-derived cosine table is simply a wrong one.

using System;

namespace FileFormat.Avif.Codec;

/// <summary>The AV1 2D inverse transform (AV1 spec 7.13.3). Turns dequantised coefficients into
/// the spatial residual for every TX_SIZE / TX_TYPE combination the format defines.</summary>
internal static class Av1InverseTransform {

  /// <summary>AV1 7.13.3: turns dequantised coefficients into the spatial residual, in place.
  /// The caller adds the result to its prediction at the same (row, column) and clips; neither the
  /// addition nor the clip happens here.
  /// <para>Where the specification and libaom disagree, this follows libaom, which is what encoders
  /// and conformance streams are built against. The one visible difference is FLIPADST: the spec
  /// leaves <c>Residual</c> unflipped and has the reconstruction step in 7.12.3 read it back to
  /// front (its <c>flipUD</c> / <c>flipLR</c> remapping), whereas libaom folds both flips into the
  /// transform itself. This port folds them in, so the caller must add residual (i, j) to
  /// prediction (i, j) directly and must not apply the spec's remapping a second time.</para></summary>
  /// <param name="coefficients">Dequantised coefficients on entry, row-major txHeight rows of txWidth,
  /// and the spatial residual on exit. Length must be at least txWidth * txHeight.</param>
  /// <param name="txSize">The transform size (TX_SIZE).</param>
  /// <param name="txType">The transform type (TX_TYPE).</param>
  /// <param name="bitDepth">Sample bit depth: 8, 10 or 12.</param>
  /// <exception cref="NotSupportedException">The transform type is not defined for the transform
  /// size, or the bit depth is not one AV1 codes.</exception>
  public static void Inverse(Span<int> coefficients, Av1TxSize txSize, Av1TxType txType, int bitDepth) {
    var sizeIndex = (int)txSize;
    if (sizeIndex is < 0 or >= Av1Constants.TxSizesAll)
      throw new ArgumentOutOfRangeException(nameof(txSize));
    var typeIndex = (int)txType;
    if (typeIndex is < 0 or >= Av1Constants.TxTypes)
      throw new ArgumentOutOfRangeException(nameof(txType));

    var width = Av1StructureTables.TxWidth[sizeIndex];
    var height = Av1StructureTables.TxHeight[sizeIndex];
    if (coefficients.Length < width * height)
      throw new ArgumentException($"Buffer holds {coefficients.Length} coefficients, {txSize} needs {width * height}.", nameof(coefficients));

    // libaom av1_gen_inv_stage_range: every stage of a given pass shares one range, picked by
    // bit depth. The ADST4 special case in that function assigns the same value, so a single
    // per-pass bit count reproduces the whole stage_range array.
    int rowRangeBit, colRangeBit;
    switch (bitDepth) {
      case 8: rowRangeBit = 16; colRangeBit = 16; break;
      case 10: rowRangeBit = 18; colRangeBit = 16; break;
      case 12: rowRangeBit = 20; colRangeBit = 18; break;
      default: throw new NotSupportedException($"AV1 defines the inverse transform for 8, 10 and 12 bit samples only, not {bitDepth}.");
    }

    // libaom av1_get_inv_txfm_cfg: the column pass runs the vertical 1D transform at the
    // transform's height, the row pass the horizontal one at its width.
    var colType = Av1StructureTables.TxTypeVertical[typeIndex];
    var rowType = Av1StructureTables.TxTypeHorizontal[typeIndex];
    var colKind = _TxfmTypeLs[Av1StructureTables.TxHeightLog2[sizeIndex] - 2][colType];
    var rowKind = _TxfmTypeLs[Av1StructureTables.TxWidthLog2[sizeIndex] - 2][rowType];
    if (colKind == _TxfmKind.Invalid || rowKind == _TxfmKind.Invalid)
      throw new NotSupportedException($"AV1 does not define transform type {txType} for transform size {txSize}: ADST is defined up to 16 points and DCT up to 64, so a 32- or 64-sample dimension admits only DCT and (at 32) identity.");

    // libaom get_flip_cfg: FLIPADST is an ADST whose output is read back to front, so the flag is
    // exactly "this pass' 1D type is FLIPADST".
    var udFlip = colType == (int)Av1TxType1d.FlipAdst;
    var lrFlip = rowType == (int)Av1TxType1d.FlipAdst;

    // libaom's av1_inv_txfm2d_add_{64x64,64x32,32x64,16x64,64x16}_c expand a 32-wide/tall
    // coefficient block into the full transform and zero the remainder; AV1 7.13.3 codes no
    // coefficient at or beyond 32 in a 64-sample dimension, so clear it rather than trust it.
    if (width == 64)
      for (var r = 0; r < height; ++r)
        coefficients.Slice(r * width + 32, 32).Clear();
    if (height == 64)
      coefficients.Slice(32 * width, (height - 32) * width).Clear();

    // libaom get_rect_tx_log_ratio; only a ratio of exactly 2 gets the 1/sqrt(2) correction.
    var rectLogRatio = Av1StructureTables.TxWidthLog2[sizeIndex] - Av1StructureTables.TxHeightLog2[sizeIndex];
    var rowShift = -_InvShiftRow[sizeIndex];
    var colShift = -_InvShiftColumn[sizeIndex];
    var rowClampBit = bitDepth + 8;
    var colClampBit = Math.Max(bitDepth + 6, 16);

    Span<int> temporaryIn = stackalloc int[Av1Constants.MaxTxSize];
    Span<int> temporaryOut = stackalloc int[Av1Constants.MaxTxSize];

    // Row pass. The left-right flip is folded into the write-back so the column pass can read
    // the buffer straight; libaom instead flips while gathering each column, which is the same
    // permutation but would alias when transforming in place.
    // The spec has no clamp on the row input: 7.12.3 already clips Dequant to +-(1 << (7 + BitDepth)),
    // which is exactly rowClampBit, so libaom's clamp here can only bite on coefficients no
    // conformant bitstream can produce. Keep it, so a malformed stream stays in range.
    for (var r = 0; r < height; ++r) {
      var row = coefficients.Slice(r * width, width);
      if (rectLogRatio is 1 or -1)
        for (var c = 0; c < width; ++c)
          temporaryIn[c] = _ClampValue(_RoundShift((long)row[c] * _NewInvSqrt2, _NewSqrt2Bits), rowClampBit);
      else
        for (var c = 0; c < width; ++c)
          temporaryIn[c] = _ClampValue(row[c], rowClampBit);
      _Transform1d(rowKind, temporaryIn[..width], temporaryOut[..width], _CosBit, rowRangeBit);
      _RoundShiftArray(temporaryOut[..width], rowShift);
      if (lrFlip)
        for (var c = 0; c < width; ++c)
          row[c] = temporaryOut[width - 1 - c];
      else
        temporaryOut[..width].CopyTo(row);
    }

    // Column pass. The clamp on the way in is the spec's "between the row and column transforms"
    // Clip3 to colClampRange, which libaom spells as clamp_buf.
    for (var c = 0; c < width; ++c) {
      for (var r = 0; r < height; ++r)
        temporaryIn[r] = _ClampValue(coefficients[r * width + c], colClampBit);
      _Transform1d(colKind, temporaryIn[..height], temporaryOut[..height], _CosBit, colRangeBit);
      _RoundShiftArray(temporaryOut[..height], colShift);
      if (udFlip)
        for (var r = 0; r < height; ++r)
          coefficients[r * width + c] = temporaryOut[height - 1 - r];
      else
        for (var r = 0; r < height; ++r)
          coefficients[r * width + c] = temporaryOut[r];
    }
  }

  /// <summary>AV1 7.13.3: the lossless 4x4 inverse Walsh-Hadamard transform, in place. Lossless
  /// blocks are always DCT_DCT, so no flipping and no row or column shift applies.</summary>
  /// <param name="coefficients">Dequantised coefficients on entry, row-major 4 rows of 4, and the
  /// spatial residual on exit. Length must be at least 16.</param>
  public static void InverseWalshHadamard4x4(Span<int> coefficients) {
    if (coefficients.Length < 16)
      throw new ArgumentException($"The lossless 4x4 transform needs 16 coefficients, not {coefficients.Length}.", nameof(coefficients));

    // libaom av1_highbd_iwht4x4_16_add_c. The row pass pre-scales by UNIT_QUANT_SHIFT, the column
    // pass does not, matching the spec's inverse WHT invoked with shift 2 then shift 0.
    for (var r = 0; r < 4; ++r) {
      var row = coefficients.Slice(r * 4, 4);
      _InverseWalshHadamard1d(row[0] >> _UnitQuantShift, row[1] >> _UnitQuantShift, row[2] >> _UnitQuantShift, row[3] >> _UnitQuantShift, row, 1);
    }
    for (var c = 0; c < 4; ++c) {
      var column = coefficients[c..];
      _InverseWalshHadamard1d(column[0], column[4], column[8], column[12], column, 4);
    }
  }

  /// <summary>libaom's 4-point inverse Walsh-Hadamard butterfly. The caller passes the inputs in
  /// the a/c/d/b order libaom loads them in and gets a/b/c/d back.</summary>
  private static void _InverseWalshHadamard1d(int a1, int c1, int d1, int b1, Span<int> output, int stride) {
    a1 += c1;
    d1 -= b1;
    var e1 = (a1 - d1) >> 1;
    b1 = e1 - b1;
    c1 = e1 - c1;
    a1 -= b1;
    d1 += c1;
    output[0] = a1;
    output[stride] = b1;
    output[stride * 2] = c1;
    output[stride * 3] = d1;
  }

  /// <summary>The 1D transforms libaom's TXFM_TYPE enumerates, in its order.</summary>
  private enum _TxfmKind {
    Dct4, Dct8, Dct16, Dct32, Dct64, Adst4, Adst8, Adst16,
    Identity4, Identity8, Identity16, Identity32, Invalid,
  }

  /// <summary>libaom <c>INV_COS_BIT</c>: both passes always use 12 fractional cosine bits.</summary>
  private const int _CosBit = 12;

  /// <summary>libaom <c>cos_bit_min</c>: the first row of the cospi/sinpi tables.</summary>
  private const int _CosBitMin = 10;

  /// <summary>libaom <c>NewSqrt2Bits</c>.</summary>
  private const int _NewSqrt2Bits = 12;

  /// <summary>libaom <c>NewSqrt2</c>: 2^12 * sqrt(2).</summary>
  private const int _NewSqrt2 = 5793;

  /// <summary>libaom <c>NewInvSqrt2</c>: 2^12 / sqrt(2), the rectangular-transform correction.</summary>
  private const int _NewInvSqrt2 = 2896;

  /// <summary>libaom <c>UNIT_QUANT_SHIFT</c>: the lossless transform's coefficient pre-scale.</summary>
  private const int _UnitQuantShift = 2;

  /// <summary>libaom <c>av1_txfm_type_ls[MAX_TXWH_IDX][TX_TYPES_1D]</c>: the 1D transform for a
  /// dimension (indexed by log2(size) - 2) and a TX_TYPE_1D. ADST stops at 16 points and DCT at
  /// 64, so the wider dimensions have holes.</summary>
  private static readonly _TxfmKind[][] _TxfmTypeLs = [
    [_TxfmKind.Dct4, _TxfmKind.Adst4, _TxfmKind.Adst4, _TxfmKind.Identity4],
    [_TxfmKind.Dct8, _TxfmKind.Adst8, _TxfmKind.Adst8, _TxfmKind.Identity8],
    [_TxfmKind.Dct16, _TxfmKind.Adst16, _TxfmKind.Adst16, _TxfmKind.Identity16],
    [_TxfmKind.Dct32, _TxfmKind.Invalid, _TxfmKind.Invalid, _TxfmKind.Identity32],
    [_TxfmKind.Dct64, _TxfmKind.Invalid, _TxfmKind.Invalid, _TxfmKind.Invalid],
  ];

  /// <summary>libaom <c>av1_inv_txfm_shift_ls[tx_size][0]</c>: the down-shift applied after the
  /// row pass, indexed by TX_SIZE.</summary>
  private static readonly sbyte[] _InvShiftRow = [
    0, -1, -2, -2, -2, 0, 0, -1, -1, -1, -1, -1, -1, -1, -1, -2, -2, -2, -2,
  ];

  /// <summary>libaom <c>av1_inv_txfm_shift_ls[tx_size][1]</c>: the down-shift applied after the
  /// column pass, indexed by TX_SIZE.</summary>
  private static readonly sbyte[] _InvShiftColumn = [
    -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4,
  ];

  /// <summary>libaom <c>inv_start_range</c>: the sum of the forward shifts for each TX_SIZE. It
  /// only feeds libaom's debug range assertions, so nothing here reads it, but the values belong
  /// with the tables they document.</summary>
  private static readonly sbyte[] _InvStartRange = [
    5, 6, 7, 7, 7, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7,
  ];

  /// <summary>libaom <c>av1_cospi_arr_data[4][64]</c>:
  /// <c>cospi[i][j] = round(cos(PI*j/128) * (1 &lt;&lt; (cos_bit_min + i)))</c>.</summary>
  private static readonly int[][] _CospiArrData = [
    [
      1024, 1024, 1023, 1021, 1019, 1016, 1013, 1009, 1004, 999, 993, 987, 980,
      972, 964, 955, 946, 936, 926, 915, 903, 891, 878, 865, 851, 837,
      822, 807, 792, 775, 759, 742, 724, 706, 688, 669, 650, 630, 610,
      590, 569, 548, 526, 505, 483, 460, 438, 415, 392, 369, 345, 321,
      297, 273, 249, 224, 200, 175, 150, 125, 100, 75, 50, 25,
    ],
    [
      2048, 2047, 2046, 2042, 2038, 2033, 2026, 2018, 2009, 1998, 1987, 1974, 1960,
      1945, 1928, 1911, 1892, 1872, 1851, 1829, 1806, 1782, 1757, 1730, 1703, 1674,
      1645, 1615, 1583, 1551, 1517, 1483, 1448, 1412, 1375, 1338, 1299, 1260, 1220,
      1179, 1138, 1096, 1053, 1009, 965, 921, 876, 830, 784, 737, 690, 642,
      595, 546, 498, 449, 400, 350, 301, 251, 201, 151, 100, 50,
    ],
    [
      4096, 4095, 4091, 4085, 4076, 4065, 4052, 4036, 4017, 3996, 3973, 3948, 3920,
      3889, 3857, 3822, 3784, 3745, 3703, 3659, 3612, 3564, 3513, 3461, 3406, 3349,
      3290, 3229, 3166, 3102, 3035, 2967, 2896, 2824, 2751, 2675, 2598, 2520, 2440,
      2359, 2276, 2191, 2106, 2019, 1931, 1842, 1751, 1660, 1567, 1474, 1380, 1285,
      1189, 1092, 995, 897, 799, 700, 601, 501, 401, 301, 201, 101,
    ],
    [
      8192, 8190, 8182, 8170, 8153, 8130, 8103, 8071, 8035, 7993, 7946, 7895, 7839,
      7779, 7713, 7643, 7568, 7489, 7405, 7317, 7225, 7128, 7027, 6921, 6811, 6698,
      6580, 6458, 6333, 6203, 6070, 5933, 5793, 5649, 5501, 5351, 5197, 5040, 4880,
      4717, 4551, 4383, 4212, 4038, 3862, 3683, 3503, 3320, 3135, 2948, 2760, 2570,
      2378, 2185, 1990, 1795, 1598, 1401, 1202, 1003, 803, 603, 402, 201,
    ],
  ];

  /// <summary>libaom <c>av1_sinpi_arr_data[4][5]</c>:
  /// <c>sinpi[i][j] = round(sqrt(2) * sin(j*PI/9) * 2/3 * (1 &lt;&lt; (cos_bit_min + i)))</c>,
  /// adjusted so that elements 1 and 2 sum to element 4.</summary>
  private static readonly int[][] _SinpiArrData = [
    [
      0, 330, 621, 836, 951,
    ],
    [
      0, 660, 1241, 1672, 1901,
    ],
    [
      0, 1321, 2482, 3344, 3803,
    ],
    [
      0, 2642, 4964, 6689, 7606,
    ],
  ];

  /// <summary>libaom <c>cospi_arr</c>.</summary>
  private static int[] _Cospi(int cosBit) => _CospiArrData[cosBit - _CosBitMin];

  /// <summary>libaom <c>sinpi_arr</c>.</summary>
  private static int[] _Sinpi(int cosBit) => _SinpiArrData[cosBit - _CosBitMin];

  /// <summary>libaom <c>round_shift</c>.</summary>
  private static int _RoundShift(long value, int bit) => (int)((value + (1L << (bit - 1))) >> bit);

  /// <summary>libaom <c>half_btf</c>. The two products are formed in 32 bits exactly as the C
  /// does; for a conformant bitstream neither can overflow, because the inputs are clamped to at
  /// most 20 bits and no cospi weight used here exceeds 4095.</summary>
  private static int _HalfBtf(int w0, int in0, int w1, int in1, int bit) {
    var result = (long)(w0 * in0) + (long)(w1 * in1);
    return (int)((result + (1L << (bit - 1))) >> bit);
  }

  /// <summary>libaom <c>clamp_value</c>.</summary>
  private static int _ClampValue(int value, int bit) {
    if (bit <= 0)
      return value;

    var maxValue = (1L << (bit - 1)) - 1;
    var minValue = -(1L << (bit - 1));
    return (int)Math.Clamp(value, minValue, maxValue);
  }

  /// <summary>libaom <c>av1_round_shift_array_c</c>. Only the non-negative case exists here: the
  /// inverse shift table holds nothing but 0 and negatives, so <c>bit</c> is never negative.</summary>
  private static void _RoundShiftArray(Span<int> values, int bit) {
    if (bit == 0)
      return;

    for (var i = 0; i < values.Length; ++i)
      values[i] = _RoundShift(values[i], bit);
  }

  /// <summary>libaom <c>inv_txfm_type_to_func</c>.</summary>
  private static void _Transform1d(_TxfmKind kind, ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    switch (kind) {
      case _TxfmKind.Dct4: _Idct4(input, output, cosBit, rangeBit); break;
      case _TxfmKind.Dct8: _Idct8(input, output, cosBit, rangeBit); break;
      case _TxfmKind.Dct16: _Idct16(input, output, cosBit, rangeBit); break;
      case _TxfmKind.Dct32: _Idct32(input, output, cosBit, rangeBit); break;
      case _TxfmKind.Dct64: _Idct64(input, output, cosBit, rangeBit); break;
      case _TxfmKind.Adst4: _Iadst4(input, output, cosBit, rangeBit); break;
      case _TxfmKind.Adst8: _Iadst8(input, output, cosBit, rangeBit); break;
      case _TxfmKind.Adst16: _Iadst16(input, output, cosBit, rangeBit); break;
      case _TxfmKind.Identity4: _Iidentity4(input, output); break;
      case _TxfmKind.Identity8: _Iidentity8(input, output); break;
      case _TxfmKind.Identity16: _Iidentity16(input, output); break;
      case _TxfmKind.Identity32: _Iidentity32(input, output); break;
      default: throw new NotSupportedException($"No AV1 inverse 1D transform for {kind}.");
    }
  }

  /// <summary>libaom <c>av1_iadst4</c>: 4-point inverse ADST. Unlike the other sizes this one runs
  /// entirely in 64 bits and clamps nothing, so <paramref name="rangeBit"/> goes unused - libaom's
  /// stage_range only feeds its no-op range_check_value64 here.</summary>
  private static void _Iadst4(ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    var sinpi = _Sinpi(cosBit);
    long x0 = input[0];
    long x1 = input[1];
    long x2 = input[2];
    long x3 = input[3];
    if ((x0 | x1 | x2 | x3) == 0) {
      output[0] = output[1] = output[2] = output[3] = 0;
      return;
    }

    // stage 1
    var s0 = sinpi[1] * x0;
    var s1 = sinpi[2] * x0;
    var s2 = sinpi[3] * x1;
    var s3 = sinpi[4] * x2;
    var s4 = sinpi[1] * x2;
    var s5 = sinpi[2] * x3;
    var s6 = sinpi[4] * x3;

    // stage 2
    var s7 = (x0 - x2) + x3;

    // stage 3
    s0 += s3;
    s1 -= s4;
    s3 = s2;
    s2 = sinpi[3] * s7;

    // stage 4
    s0 += s5;
    s1 -= s6;

    // stage 5
    x0 = s0 + s3;
    x1 = s1 + s3;
    x2 = s2;
    x3 = s0 + s1;

    // stage 6
    x3 -= s3;

    output[0] = _RoundShift(x0, cosBit);
    output[1] = _RoundShift(x1, cosBit);
    output[2] = _RoundShift(x2, cosBit);
    output[3] = _RoundShift(x3, cosBit);
  }

  /// <summary>libaom <c>av1_iidentity4_c</c>: scale by sqrt(2).</summary>
  private static void _Iidentity4(ReadOnlySpan<int> input, Span<int> output) {
    for (var i = 0; i < 4; ++i)
      output[i] = _RoundShift((long)_NewSqrt2 * input[i], _NewSqrt2Bits);
  }

  /// <summary>libaom <c>av1_iidentity8_c</c>: scale by 2.</summary>
  private static void _Iidentity8(ReadOnlySpan<int> input, Span<int> output) {
    for (var i = 0; i < 8; ++i)
      output[i] = (int)((long)input[i] * 2);
  }

  /// <summary>libaom <c>av1_iidentity16_c</c>: scale by 2*sqrt(2).</summary>
  private static void _Iidentity16(ReadOnlySpan<int> input, Span<int> output) {
    for (var i = 0; i < 16; ++i)
      output[i] = _RoundShift((long)_NewSqrt2 * 2 * input[i], _NewSqrt2Bits);
  }

  /// <summary>libaom <c>av1_iidentity32_c</c>: scale by 4.</summary>
  private static void _Iidentity32(ReadOnlySpan<int> input, Span<int> output) {
    for (var i = 0; i < 32; ++i)
      output[i] = (int)((long)input[i] * 4);
  }

  /// <summary>libaom <c>av1_idct4</c>: 4-point inverse DCT.</summary>
  private static void _Idct4(ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    var cospi = _Cospi(cosBit);
    Span<int> step = stackalloc int[4];

    // stage 1
    output[0] = input[0];
    output[1] = input[2];
    output[2] = input[1];
    output[3] = input[3];

    // stage 2
    step[0] = _HalfBtf(cospi[32], output[0], cospi[32], output[1], cosBit);
    step[1] = _HalfBtf(cospi[32], output[0], -cospi[32], output[1], cosBit);
    step[2] = _HalfBtf(cospi[48], output[2], -cospi[16], output[3], cosBit);
    step[3] = _HalfBtf(cospi[16], output[2], cospi[48], output[3], cosBit);

    // stage 3
    output[0] = _ClampValue(step[0] + step[3], rangeBit);
    output[1] = _ClampValue(step[1] + step[2], rangeBit);
    output[2] = _ClampValue(step[1] - step[2], rangeBit);
    output[3] = _ClampValue(step[0] - step[3], rangeBit);
  }

  /// <summary>libaom <c>av1_idct8</c>: 8-point inverse DCT.</summary>
  private static void _Idct8(ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    var cospi = _Cospi(cosBit);
    Span<int> step = stackalloc int[8];

    // stage 1
    output[0] = input[0];
    output[1] = input[4];
    output[2] = input[2];
    output[3] = input[6];
    output[4] = input[1];
    output[5] = input[5];
    output[6] = input[3];
    output[7] = input[7];

    // stage 2
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = _HalfBtf(cospi[56], output[4], -cospi[8], output[7], cosBit);
    step[5] = _HalfBtf(cospi[24], output[5], -cospi[40], output[6], cosBit);
    step[6] = _HalfBtf(cospi[40], output[5], cospi[24], output[6], cosBit);
    step[7] = _HalfBtf(cospi[8], output[4], cospi[56], output[7], cosBit);

    // stage 3
    output[0] = _HalfBtf(cospi[32], step[0], cospi[32], step[1], cosBit);
    output[1] = _HalfBtf(cospi[32], step[0], -cospi[32], step[1], cosBit);
    output[2] = _HalfBtf(cospi[48], step[2], -cospi[16], step[3], cosBit);
    output[3] = _HalfBtf(cospi[16], step[2], cospi[48], step[3], cosBit);
    output[4] = _ClampValue(step[4] + step[5], rangeBit);
    output[5] = _ClampValue(step[4] - step[5], rangeBit);
    output[6] = _ClampValue(-step[6] + step[7], rangeBit);
    output[7] = _ClampValue(step[6] + step[7], rangeBit);

    // stage 4
    step[0] = _ClampValue(output[0] + output[3], rangeBit);
    step[1] = _ClampValue(output[1] + output[2], rangeBit);
    step[2] = _ClampValue(output[1] - output[2], rangeBit);
    step[3] = _ClampValue(output[0] - output[3], rangeBit);
    step[4] = output[4];
    step[5] = _HalfBtf(-cospi[32], output[5], cospi[32], output[6], cosBit);
    step[6] = _HalfBtf(cospi[32], output[5], cospi[32], output[6], cosBit);
    step[7] = output[7];

    // stage 5
    output[0] = _ClampValue(step[0] + step[7], rangeBit);
    output[1] = _ClampValue(step[1] + step[6], rangeBit);
    output[2] = _ClampValue(step[2] + step[5], rangeBit);
    output[3] = _ClampValue(step[3] + step[4], rangeBit);
    output[4] = _ClampValue(step[3] - step[4], rangeBit);
    output[5] = _ClampValue(step[2] - step[5], rangeBit);
    output[6] = _ClampValue(step[1] - step[6], rangeBit);
    output[7] = _ClampValue(step[0] - step[7], rangeBit);
  }

  /// <summary>libaom <c>av1_idct16</c>: 16-point inverse DCT.</summary>
  private static void _Idct16(ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    var cospi = _Cospi(cosBit);
    Span<int> step = stackalloc int[16];

    // stage 1
    output[0] = input[0];
    output[1] = input[8];
    output[2] = input[4];
    output[3] = input[12];
    output[4] = input[2];
    output[5] = input[10];
    output[6] = input[6];
    output[7] = input[14];
    output[8] = input[1];
    output[9] = input[9];
    output[10] = input[5];
    output[11] = input[13];
    output[12] = input[3];
    output[13] = input[11];
    output[14] = input[7];
    output[15] = input[15];

    // stage 2
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = output[4];
    step[5] = output[5];
    step[6] = output[6];
    step[7] = output[7];
    step[8] = _HalfBtf(cospi[60], output[8], -cospi[4], output[15], cosBit);
    step[9] = _HalfBtf(cospi[28], output[9], -cospi[36], output[14], cosBit);
    step[10] = _HalfBtf(cospi[44], output[10], -cospi[20], output[13], cosBit);
    step[11] = _HalfBtf(cospi[12], output[11], -cospi[52], output[12], cosBit);
    step[12] = _HalfBtf(cospi[52], output[11], cospi[12], output[12], cosBit);
    step[13] = _HalfBtf(cospi[20], output[10], cospi[44], output[13], cosBit);
    step[14] = _HalfBtf(cospi[36], output[9], cospi[28], output[14], cosBit);
    step[15] = _HalfBtf(cospi[4], output[8], cospi[60], output[15], cosBit);

    // stage 3
    output[0] = step[0];
    output[1] = step[1];
    output[2] = step[2];
    output[3] = step[3];
    output[4] = _HalfBtf(cospi[56], step[4], -cospi[8], step[7], cosBit);
    output[5] = _HalfBtf(cospi[24], step[5], -cospi[40], step[6], cosBit);
    output[6] = _HalfBtf(cospi[40], step[5], cospi[24], step[6], cosBit);
    output[7] = _HalfBtf(cospi[8], step[4], cospi[56], step[7], cosBit);
    output[8] = _ClampValue(step[8] + step[9], rangeBit);
    output[9] = _ClampValue(step[8] - step[9], rangeBit);
    output[10] = _ClampValue(-step[10] + step[11], rangeBit);
    output[11] = _ClampValue(step[10] + step[11], rangeBit);
    output[12] = _ClampValue(step[12] + step[13], rangeBit);
    output[13] = _ClampValue(step[12] - step[13], rangeBit);
    output[14] = _ClampValue(-step[14] + step[15], rangeBit);
    output[15] = _ClampValue(step[14] + step[15], rangeBit);

    // stage 4
    step[0] = _HalfBtf(cospi[32], output[0], cospi[32], output[1], cosBit);
    step[1] = _HalfBtf(cospi[32], output[0], -cospi[32], output[1], cosBit);
    step[2] = _HalfBtf(cospi[48], output[2], -cospi[16], output[3], cosBit);
    step[3] = _HalfBtf(cospi[16], output[2], cospi[48], output[3], cosBit);
    step[4] = _ClampValue(output[4] + output[5], rangeBit);
    step[5] = _ClampValue(output[4] - output[5], rangeBit);
    step[6] = _ClampValue(-output[6] + output[7], rangeBit);
    step[7] = _ClampValue(output[6] + output[7], rangeBit);
    step[8] = output[8];
    step[9] = _HalfBtf(-cospi[16], output[9], cospi[48], output[14], cosBit);
    step[10] = _HalfBtf(-cospi[48], output[10], -cospi[16], output[13], cosBit);
    step[11] = output[11];
    step[12] = output[12];
    step[13] = _HalfBtf(-cospi[16], output[10], cospi[48], output[13], cosBit);
    step[14] = _HalfBtf(cospi[48], output[9], cospi[16], output[14], cosBit);
    step[15] = output[15];

    // stage 5
    output[0] = _ClampValue(step[0] + step[3], rangeBit);
    output[1] = _ClampValue(step[1] + step[2], rangeBit);
    output[2] = _ClampValue(step[1] - step[2], rangeBit);
    output[3] = _ClampValue(step[0] - step[3], rangeBit);
    output[4] = step[4];
    output[5] = _HalfBtf(-cospi[32], step[5], cospi[32], step[6], cosBit);
    output[6] = _HalfBtf(cospi[32], step[5], cospi[32], step[6], cosBit);
    output[7] = step[7];
    output[8] = _ClampValue(step[8] + step[11], rangeBit);
    output[9] = _ClampValue(step[9] + step[10], rangeBit);
    output[10] = _ClampValue(step[9] - step[10], rangeBit);
    output[11] = _ClampValue(step[8] - step[11], rangeBit);
    output[12] = _ClampValue(-step[12] + step[15], rangeBit);
    output[13] = _ClampValue(-step[13] + step[14], rangeBit);
    output[14] = _ClampValue(step[13] + step[14], rangeBit);
    output[15] = _ClampValue(step[12] + step[15], rangeBit);

    // stage 6
    step[0] = _ClampValue(output[0] + output[7], rangeBit);
    step[1] = _ClampValue(output[1] + output[6], rangeBit);
    step[2] = _ClampValue(output[2] + output[5], rangeBit);
    step[3] = _ClampValue(output[3] + output[4], rangeBit);
    step[4] = _ClampValue(output[3] - output[4], rangeBit);
    step[5] = _ClampValue(output[2] - output[5], rangeBit);
    step[6] = _ClampValue(output[1] - output[6], rangeBit);
    step[7] = _ClampValue(output[0] - output[7], rangeBit);
    step[8] = output[8];
    step[9] = output[9];
    step[10] = _HalfBtf(-cospi[32], output[10], cospi[32], output[13], cosBit);
    step[11] = _HalfBtf(-cospi[32], output[11], cospi[32], output[12], cosBit);
    step[12] = _HalfBtf(cospi[32], output[11], cospi[32], output[12], cosBit);
    step[13] = _HalfBtf(cospi[32], output[10], cospi[32], output[13], cosBit);
    step[14] = output[14];
    step[15] = output[15];

    // stage 7
    output[0] = _ClampValue(step[0] + step[15], rangeBit);
    output[1] = _ClampValue(step[1] + step[14], rangeBit);
    output[2] = _ClampValue(step[2] + step[13], rangeBit);
    output[3] = _ClampValue(step[3] + step[12], rangeBit);
    output[4] = _ClampValue(step[4] + step[11], rangeBit);
    output[5] = _ClampValue(step[5] + step[10], rangeBit);
    output[6] = _ClampValue(step[6] + step[9], rangeBit);
    output[7] = _ClampValue(step[7] + step[8], rangeBit);
    output[8] = _ClampValue(step[7] - step[8], rangeBit);
    output[9] = _ClampValue(step[6] - step[9], rangeBit);
    output[10] = _ClampValue(step[5] - step[10], rangeBit);
    output[11] = _ClampValue(step[4] - step[11], rangeBit);
    output[12] = _ClampValue(step[3] - step[12], rangeBit);
    output[13] = _ClampValue(step[2] - step[13], rangeBit);
    output[14] = _ClampValue(step[1] - step[14], rangeBit);
    output[15] = _ClampValue(step[0] - step[15], rangeBit);
  }

  /// <summary>libaom <c>av1_idct32</c>: 32-point inverse DCT.</summary>
  private static void _Idct32(ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    var cospi = _Cospi(cosBit);
    Span<int> step = stackalloc int[32];

    // stage 1
    output[0] = input[0];
    output[1] = input[16];
    output[2] = input[8];
    output[3] = input[24];
    output[4] = input[4];
    output[5] = input[20];
    output[6] = input[12];
    output[7] = input[28];
    output[8] = input[2];
    output[9] = input[18];
    output[10] = input[10];
    output[11] = input[26];
    output[12] = input[6];
    output[13] = input[22];
    output[14] = input[14];
    output[15] = input[30];
    output[16] = input[1];
    output[17] = input[17];
    output[18] = input[9];
    output[19] = input[25];
    output[20] = input[5];
    output[21] = input[21];
    output[22] = input[13];
    output[23] = input[29];
    output[24] = input[3];
    output[25] = input[19];
    output[26] = input[11];
    output[27] = input[27];
    output[28] = input[7];
    output[29] = input[23];
    output[30] = input[15];
    output[31] = input[31];

    // stage 2
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = output[4];
    step[5] = output[5];
    step[6] = output[6];
    step[7] = output[7];
    step[8] = output[8];
    step[9] = output[9];
    step[10] = output[10];
    step[11] = output[11];
    step[12] = output[12];
    step[13] = output[13];
    step[14] = output[14];
    step[15] = output[15];
    step[16] = _HalfBtf(cospi[62], output[16], -cospi[2], output[31], cosBit);
    step[17] = _HalfBtf(cospi[30], output[17], -cospi[34], output[30], cosBit);
    step[18] = _HalfBtf(cospi[46], output[18], -cospi[18], output[29], cosBit);
    step[19] = _HalfBtf(cospi[14], output[19], -cospi[50], output[28], cosBit);
    step[20] = _HalfBtf(cospi[54], output[20], -cospi[10], output[27], cosBit);
    step[21] = _HalfBtf(cospi[22], output[21], -cospi[42], output[26], cosBit);
    step[22] = _HalfBtf(cospi[38], output[22], -cospi[26], output[25], cosBit);
    step[23] = _HalfBtf(cospi[6], output[23], -cospi[58], output[24], cosBit);
    step[24] = _HalfBtf(cospi[58], output[23], cospi[6], output[24], cosBit);
    step[25] = _HalfBtf(cospi[26], output[22], cospi[38], output[25], cosBit);
    step[26] = _HalfBtf(cospi[42], output[21], cospi[22], output[26], cosBit);
    step[27] = _HalfBtf(cospi[10], output[20], cospi[54], output[27], cosBit);
    step[28] = _HalfBtf(cospi[50], output[19], cospi[14], output[28], cosBit);
    step[29] = _HalfBtf(cospi[18], output[18], cospi[46], output[29], cosBit);
    step[30] = _HalfBtf(cospi[34], output[17], cospi[30], output[30], cosBit);
    step[31] = _HalfBtf(cospi[2], output[16], cospi[62], output[31], cosBit);

    // stage 3
    output[0] = step[0];
    output[1] = step[1];
    output[2] = step[2];
    output[3] = step[3];
    output[4] = step[4];
    output[5] = step[5];
    output[6] = step[6];
    output[7] = step[7];
    output[8] = _HalfBtf(cospi[60], step[8], -cospi[4], step[15], cosBit);
    output[9] = _HalfBtf(cospi[28], step[9], -cospi[36], step[14], cosBit);
    output[10] = _HalfBtf(cospi[44], step[10], -cospi[20], step[13], cosBit);
    output[11] = _HalfBtf(cospi[12], step[11], -cospi[52], step[12], cosBit);
    output[12] = _HalfBtf(cospi[52], step[11], cospi[12], step[12], cosBit);
    output[13] = _HalfBtf(cospi[20], step[10], cospi[44], step[13], cosBit);
    output[14] = _HalfBtf(cospi[36], step[9], cospi[28], step[14], cosBit);
    output[15] = _HalfBtf(cospi[4], step[8], cospi[60], step[15], cosBit);
    output[16] = _ClampValue(step[16] + step[17], rangeBit);
    output[17] = _ClampValue(step[16] - step[17], rangeBit);
    output[18] = _ClampValue(-step[18] + step[19], rangeBit);
    output[19] = _ClampValue(step[18] + step[19], rangeBit);
    output[20] = _ClampValue(step[20] + step[21], rangeBit);
    output[21] = _ClampValue(step[20] - step[21], rangeBit);
    output[22] = _ClampValue(-step[22] + step[23], rangeBit);
    output[23] = _ClampValue(step[22] + step[23], rangeBit);
    output[24] = _ClampValue(step[24] + step[25], rangeBit);
    output[25] = _ClampValue(step[24] - step[25], rangeBit);
    output[26] = _ClampValue(-step[26] + step[27], rangeBit);
    output[27] = _ClampValue(step[26] + step[27], rangeBit);
    output[28] = _ClampValue(step[28] + step[29], rangeBit);
    output[29] = _ClampValue(step[28] - step[29], rangeBit);
    output[30] = _ClampValue(-step[30] + step[31], rangeBit);
    output[31] = _ClampValue(step[30] + step[31], rangeBit);

    // stage 4
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = _HalfBtf(cospi[56], output[4], -cospi[8], output[7], cosBit);
    step[5] = _HalfBtf(cospi[24], output[5], -cospi[40], output[6], cosBit);
    step[6] = _HalfBtf(cospi[40], output[5], cospi[24], output[6], cosBit);
    step[7] = _HalfBtf(cospi[8], output[4], cospi[56], output[7], cosBit);
    step[8] = _ClampValue(output[8] + output[9], rangeBit);
    step[9] = _ClampValue(output[8] - output[9], rangeBit);
    step[10] = _ClampValue(-output[10] + output[11], rangeBit);
    step[11] = _ClampValue(output[10] + output[11], rangeBit);
    step[12] = _ClampValue(output[12] + output[13], rangeBit);
    step[13] = _ClampValue(output[12] - output[13], rangeBit);
    step[14] = _ClampValue(-output[14] + output[15], rangeBit);
    step[15] = _ClampValue(output[14] + output[15], rangeBit);
    step[16] = output[16];
    step[17] = _HalfBtf(-cospi[8], output[17], cospi[56], output[30], cosBit);
    step[18] = _HalfBtf(-cospi[56], output[18], -cospi[8], output[29], cosBit);
    step[19] = output[19];
    step[20] = output[20];
    step[21] = _HalfBtf(-cospi[40], output[21], cospi[24], output[26], cosBit);
    step[22] = _HalfBtf(-cospi[24], output[22], -cospi[40], output[25], cosBit);
    step[23] = output[23];
    step[24] = output[24];
    step[25] = _HalfBtf(-cospi[40], output[22], cospi[24], output[25], cosBit);
    step[26] = _HalfBtf(cospi[24], output[21], cospi[40], output[26], cosBit);
    step[27] = output[27];
    step[28] = output[28];
    step[29] = _HalfBtf(-cospi[8], output[18], cospi[56], output[29], cosBit);
    step[30] = _HalfBtf(cospi[56], output[17], cospi[8], output[30], cosBit);
    step[31] = output[31];

    // stage 5
    output[0] = _HalfBtf(cospi[32], step[0], cospi[32], step[1], cosBit);
    output[1] = _HalfBtf(cospi[32], step[0], -cospi[32], step[1], cosBit);
    output[2] = _HalfBtf(cospi[48], step[2], -cospi[16], step[3], cosBit);
    output[3] = _HalfBtf(cospi[16], step[2], cospi[48], step[3], cosBit);
    output[4] = _ClampValue(step[4] + step[5], rangeBit);
    output[5] = _ClampValue(step[4] - step[5], rangeBit);
    output[6] = _ClampValue(-step[6] + step[7], rangeBit);
    output[7] = _ClampValue(step[6] + step[7], rangeBit);
    output[8] = step[8];
    output[9] = _HalfBtf(-cospi[16], step[9], cospi[48], step[14], cosBit);
    output[10] = _HalfBtf(-cospi[48], step[10], -cospi[16], step[13], cosBit);
    output[11] = step[11];
    output[12] = step[12];
    output[13] = _HalfBtf(-cospi[16], step[10], cospi[48], step[13], cosBit);
    output[14] = _HalfBtf(cospi[48], step[9], cospi[16], step[14], cosBit);
    output[15] = step[15];
    output[16] = _ClampValue(step[16] + step[19], rangeBit);
    output[17] = _ClampValue(step[17] + step[18], rangeBit);
    output[18] = _ClampValue(step[17] - step[18], rangeBit);
    output[19] = _ClampValue(step[16] - step[19], rangeBit);
    output[20] = _ClampValue(-step[20] + step[23], rangeBit);
    output[21] = _ClampValue(-step[21] + step[22], rangeBit);
    output[22] = _ClampValue(step[21] + step[22], rangeBit);
    output[23] = _ClampValue(step[20] + step[23], rangeBit);
    output[24] = _ClampValue(step[24] + step[27], rangeBit);
    output[25] = _ClampValue(step[25] + step[26], rangeBit);
    output[26] = _ClampValue(step[25] - step[26], rangeBit);
    output[27] = _ClampValue(step[24] - step[27], rangeBit);
    output[28] = _ClampValue(-step[28] + step[31], rangeBit);
    output[29] = _ClampValue(-step[29] + step[30], rangeBit);
    output[30] = _ClampValue(step[29] + step[30], rangeBit);
    output[31] = _ClampValue(step[28] + step[31], rangeBit);

    // stage 6
    step[0] = _ClampValue(output[0] + output[3], rangeBit);
    step[1] = _ClampValue(output[1] + output[2], rangeBit);
    step[2] = _ClampValue(output[1] - output[2], rangeBit);
    step[3] = _ClampValue(output[0] - output[3], rangeBit);
    step[4] = output[4];
    step[5] = _HalfBtf(-cospi[32], output[5], cospi[32], output[6], cosBit);
    step[6] = _HalfBtf(cospi[32], output[5], cospi[32], output[6], cosBit);
    step[7] = output[7];
    step[8] = _ClampValue(output[8] + output[11], rangeBit);
    step[9] = _ClampValue(output[9] + output[10], rangeBit);
    step[10] = _ClampValue(output[9] - output[10], rangeBit);
    step[11] = _ClampValue(output[8] - output[11], rangeBit);
    step[12] = _ClampValue(-output[12] + output[15], rangeBit);
    step[13] = _ClampValue(-output[13] + output[14], rangeBit);
    step[14] = _ClampValue(output[13] + output[14], rangeBit);
    step[15] = _ClampValue(output[12] + output[15], rangeBit);
    step[16] = output[16];
    step[17] = output[17];
    step[18] = _HalfBtf(-cospi[16], output[18], cospi[48], output[29], cosBit);
    step[19] = _HalfBtf(-cospi[16], output[19], cospi[48], output[28], cosBit);
    step[20] = _HalfBtf(-cospi[48], output[20], -cospi[16], output[27], cosBit);
    step[21] = _HalfBtf(-cospi[48], output[21], -cospi[16], output[26], cosBit);
    step[22] = output[22];
    step[23] = output[23];
    step[24] = output[24];
    step[25] = output[25];
    step[26] = _HalfBtf(-cospi[16], output[21], cospi[48], output[26], cosBit);
    step[27] = _HalfBtf(-cospi[16], output[20], cospi[48], output[27], cosBit);
    step[28] = _HalfBtf(cospi[48], output[19], cospi[16], output[28], cosBit);
    step[29] = _HalfBtf(cospi[48], output[18], cospi[16], output[29], cosBit);
    step[30] = output[30];
    step[31] = output[31];

    // stage 7
    output[0] = _ClampValue(step[0] + step[7], rangeBit);
    output[1] = _ClampValue(step[1] + step[6], rangeBit);
    output[2] = _ClampValue(step[2] + step[5], rangeBit);
    output[3] = _ClampValue(step[3] + step[4], rangeBit);
    output[4] = _ClampValue(step[3] - step[4], rangeBit);
    output[5] = _ClampValue(step[2] - step[5], rangeBit);
    output[6] = _ClampValue(step[1] - step[6], rangeBit);
    output[7] = _ClampValue(step[0] - step[7], rangeBit);
    output[8] = step[8];
    output[9] = step[9];
    output[10] = _HalfBtf(-cospi[32], step[10], cospi[32], step[13], cosBit);
    output[11] = _HalfBtf(-cospi[32], step[11], cospi[32], step[12], cosBit);
    output[12] = _HalfBtf(cospi[32], step[11], cospi[32], step[12], cosBit);
    output[13] = _HalfBtf(cospi[32], step[10], cospi[32], step[13], cosBit);
    output[14] = step[14];
    output[15] = step[15];
    output[16] = _ClampValue(step[16] + step[23], rangeBit);
    output[17] = _ClampValue(step[17] + step[22], rangeBit);
    output[18] = _ClampValue(step[18] + step[21], rangeBit);
    output[19] = _ClampValue(step[19] + step[20], rangeBit);
    output[20] = _ClampValue(step[19] - step[20], rangeBit);
    output[21] = _ClampValue(step[18] - step[21], rangeBit);
    output[22] = _ClampValue(step[17] - step[22], rangeBit);
    output[23] = _ClampValue(step[16] - step[23], rangeBit);
    output[24] = _ClampValue(-step[24] + step[31], rangeBit);
    output[25] = _ClampValue(-step[25] + step[30], rangeBit);
    output[26] = _ClampValue(-step[26] + step[29], rangeBit);
    output[27] = _ClampValue(-step[27] + step[28], rangeBit);
    output[28] = _ClampValue(step[27] + step[28], rangeBit);
    output[29] = _ClampValue(step[26] + step[29], rangeBit);
    output[30] = _ClampValue(step[25] + step[30], rangeBit);
    output[31] = _ClampValue(step[24] + step[31], rangeBit);

    // stage 8
    step[0] = _ClampValue(output[0] + output[15], rangeBit);
    step[1] = _ClampValue(output[1] + output[14], rangeBit);
    step[2] = _ClampValue(output[2] + output[13], rangeBit);
    step[3] = _ClampValue(output[3] + output[12], rangeBit);
    step[4] = _ClampValue(output[4] + output[11], rangeBit);
    step[5] = _ClampValue(output[5] + output[10], rangeBit);
    step[6] = _ClampValue(output[6] + output[9], rangeBit);
    step[7] = _ClampValue(output[7] + output[8], rangeBit);
    step[8] = _ClampValue(output[7] - output[8], rangeBit);
    step[9] = _ClampValue(output[6] - output[9], rangeBit);
    step[10] = _ClampValue(output[5] - output[10], rangeBit);
    step[11] = _ClampValue(output[4] - output[11], rangeBit);
    step[12] = _ClampValue(output[3] - output[12], rangeBit);
    step[13] = _ClampValue(output[2] - output[13], rangeBit);
    step[14] = _ClampValue(output[1] - output[14], rangeBit);
    step[15] = _ClampValue(output[0] - output[15], rangeBit);
    step[16] = output[16];
    step[17] = output[17];
    step[18] = output[18];
    step[19] = output[19];
    step[20] = _HalfBtf(-cospi[32], output[20], cospi[32], output[27], cosBit);
    step[21] = _HalfBtf(-cospi[32], output[21], cospi[32], output[26], cosBit);
    step[22] = _HalfBtf(-cospi[32], output[22], cospi[32], output[25], cosBit);
    step[23] = _HalfBtf(-cospi[32], output[23], cospi[32], output[24], cosBit);
    step[24] = _HalfBtf(cospi[32], output[23], cospi[32], output[24], cosBit);
    step[25] = _HalfBtf(cospi[32], output[22], cospi[32], output[25], cosBit);
    step[26] = _HalfBtf(cospi[32], output[21], cospi[32], output[26], cosBit);
    step[27] = _HalfBtf(cospi[32], output[20], cospi[32], output[27], cosBit);
    step[28] = output[28];
    step[29] = output[29];
    step[30] = output[30];
    step[31] = output[31];

    // stage 9
    output[0] = _ClampValue(step[0] + step[31], rangeBit);
    output[1] = _ClampValue(step[1] + step[30], rangeBit);
    output[2] = _ClampValue(step[2] + step[29], rangeBit);
    output[3] = _ClampValue(step[3] + step[28], rangeBit);
    output[4] = _ClampValue(step[4] + step[27], rangeBit);
    output[5] = _ClampValue(step[5] + step[26], rangeBit);
    output[6] = _ClampValue(step[6] + step[25], rangeBit);
    output[7] = _ClampValue(step[7] + step[24], rangeBit);
    output[8] = _ClampValue(step[8] + step[23], rangeBit);
    output[9] = _ClampValue(step[9] + step[22], rangeBit);
    output[10] = _ClampValue(step[10] + step[21], rangeBit);
    output[11] = _ClampValue(step[11] + step[20], rangeBit);
    output[12] = _ClampValue(step[12] + step[19], rangeBit);
    output[13] = _ClampValue(step[13] + step[18], rangeBit);
    output[14] = _ClampValue(step[14] + step[17], rangeBit);
    output[15] = _ClampValue(step[15] + step[16], rangeBit);
    output[16] = _ClampValue(step[15] - step[16], rangeBit);
    output[17] = _ClampValue(step[14] - step[17], rangeBit);
    output[18] = _ClampValue(step[13] - step[18], rangeBit);
    output[19] = _ClampValue(step[12] - step[19], rangeBit);
    output[20] = _ClampValue(step[11] - step[20], rangeBit);
    output[21] = _ClampValue(step[10] - step[21], rangeBit);
    output[22] = _ClampValue(step[9] - step[22], rangeBit);
    output[23] = _ClampValue(step[8] - step[23], rangeBit);
    output[24] = _ClampValue(step[7] - step[24], rangeBit);
    output[25] = _ClampValue(step[6] - step[25], rangeBit);
    output[26] = _ClampValue(step[5] - step[26], rangeBit);
    output[27] = _ClampValue(step[4] - step[27], rangeBit);
    output[28] = _ClampValue(step[3] - step[28], rangeBit);
    output[29] = _ClampValue(step[2] - step[29], rangeBit);
    output[30] = _ClampValue(step[1] - step[30], rangeBit);
    output[31] = _ClampValue(step[0] - step[31], rangeBit);
  }

  /// <summary>libaom <c>av1_idct64</c>: 64-point inverse DCT.</summary>
  private static void _Idct64(ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    var cospi = _Cospi(cosBit);
    Span<int> step = stackalloc int[64];

    // stage 1
    output[0] = input[0];
    output[1] = input[32];
    output[2] = input[16];
    output[3] = input[48];
    output[4] = input[8];
    output[5] = input[40];
    output[6] = input[24];
    output[7] = input[56];
    output[8] = input[4];
    output[9] = input[36];
    output[10] = input[20];
    output[11] = input[52];
    output[12] = input[12];
    output[13] = input[44];
    output[14] = input[28];
    output[15] = input[60];
    output[16] = input[2];
    output[17] = input[34];
    output[18] = input[18];
    output[19] = input[50];
    output[20] = input[10];
    output[21] = input[42];
    output[22] = input[26];
    output[23] = input[58];
    output[24] = input[6];
    output[25] = input[38];
    output[26] = input[22];
    output[27] = input[54];
    output[28] = input[14];
    output[29] = input[46];
    output[30] = input[30];
    output[31] = input[62];
    output[32] = input[1];
    output[33] = input[33];
    output[34] = input[17];
    output[35] = input[49];
    output[36] = input[9];
    output[37] = input[41];
    output[38] = input[25];
    output[39] = input[57];
    output[40] = input[5];
    output[41] = input[37];
    output[42] = input[21];
    output[43] = input[53];
    output[44] = input[13];
    output[45] = input[45];
    output[46] = input[29];
    output[47] = input[61];
    output[48] = input[3];
    output[49] = input[35];
    output[50] = input[19];
    output[51] = input[51];
    output[52] = input[11];
    output[53] = input[43];
    output[54] = input[27];
    output[55] = input[59];
    output[56] = input[7];
    output[57] = input[39];
    output[58] = input[23];
    output[59] = input[55];
    output[60] = input[15];
    output[61] = input[47];
    output[62] = input[31];
    output[63] = input[63];

    // stage 2
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = output[4];
    step[5] = output[5];
    step[6] = output[6];
    step[7] = output[7];
    step[8] = output[8];
    step[9] = output[9];
    step[10] = output[10];
    step[11] = output[11];
    step[12] = output[12];
    step[13] = output[13];
    step[14] = output[14];
    step[15] = output[15];
    step[16] = output[16];
    step[17] = output[17];
    step[18] = output[18];
    step[19] = output[19];
    step[20] = output[20];
    step[21] = output[21];
    step[22] = output[22];
    step[23] = output[23];
    step[24] = output[24];
    step[25] = output[25];
    step[26] = output[26];
    step[27] = output[27];
    step[28] = output[28];
    step[29] = output[29];
    step[30] = output[30];
    step[31] = output[31];
    step[32] = _HalfBtf(cospi[63], output[32], -cospi[1], output[63], cosBit);
    step[33] = _HalfBtf(cospi[31], output[33], -cospi[33], output[62], cosBit);
    step[34] = _HalfBtf(cospi[47], output[34], -cospi[17], output[61], cosBit);
    step[35] = _HalfBtf(cospi[15], output[35], -cospi[49], output[60], cosBit);
    step[36] = _HalfBtf(cospi[55], output[36], -cospi[9], output[59], cosBit);
    step[37] = _HalfBtf(cospi[23], output[37], -cospi[41], output[58], cosBit);
    step[38] = _HalfBtf(cospi[39], output[38], -cospi[25], output[57], cosBit);
    step[39] = _HalfBtf(cospi[7], output[39], -cospi[57], output[56], cosBit);
    step[40] = _HalfBtf(cospi[59], output[40], -cospi[5], output[55], cosBit);
    step[41] = _HalfBtf(cospi[27], output[41], -cospi[37], output[54], cosBit);
    step[42] = _HalfBtf(cospi[43], output[42], -cospi[21], output[53], cosBit);
    step[43] = _HalfBtf(cospi[11], output[43], -cospi[53], output[52], cosBit);
    step[44] = _HalfBtf(cospi[51], output[44], -cospi[13], output[51], cosBit);
    step[45] = _HalfBtf(cospi[19], output[45], -cospi[45], output[50], cosBit);
    step[46] = _HalfBtf(cospi[35], output[46], -cospi[29], output[49], cosBit);
    step[47] = _HalfBtf(cospi[3], output[47], -cospi[61], output[48], cosBit);
    step[48] = _HalfBtf(cospi[61], output[47], cospi[3], output[48], cosBit);
    step[49] = _HalfBtf(cospi[29], output[46], cospi[35], output[49], cosBit);
    step[50] = _HalfBtf(cospi[45], output[45], cospi[19], output[50], cosBit);
    step[51] = _HalfBtf(cospi[13], output[44], cospi[51], output[51], cosBit);
    step[52] = _HalfBtf(cospi[53], output[43], cospi[11], output[52], cosBit);
    step[53] = _HalfBtf(cospi[21], output[42], cospi[43], output[53], cosBit);
    step[54] = _HalfBtf(cospi[37], output[41], cospi[27], output[54], cosBit);
    step[55] = _HalfBtf(cospi[5], output[40], cospi[59], output[55], cosBit);
    step[56] = _HalfBtf(cospi[57], output[39], cospi[7], output[56], cosBit);
    step[57] = _HalfBtf(cospi[25], output[38], cospi[39], output[57], cosBit);
    step[58] = _HalfBtf(cospi[41], output[37], cospi[23], output[58], cosBit);
    step[59] = _HalfBtf(cospi[9], output[36], cospi[55], output[59], cosBit);
    step[60] = _HalfBtf(cospi[49], output[35], cospi[15], output[60], cosBit);
    step[61] = _HalfBtf(cospi[17], output[34], cospi[47], output[61], cosBit);
    step[62] = _HalfBtf(cospi[33], output[33], cospi[31], output[62], cosBit);
    step[63] = _HalfBtf(cospi[1], output[32], cospi[63], output[63], cosBit);

    // stage 3
    output[0] = step[0];
    output[1] = step[1];
    output[2] = step[2];
    output[3] = step[3];
    output[4] = step[4];
    output[5] = step[5];
    output[6] = step[6];
    output[7] = step[7];
    output[8] = step[8];
    output[9] = step[9];
    output[10] = step[10];
    output[11] = step[11];
    output[12] = step[12];
    output[13] = step[13];
    output[14] = step[14];
    output[15] = step[15];
    output[16] = _HalfBtf(cospi[62], step[16], -cospi[2], step[31], cosBit);
    output[17] = _HalfBtf(cospi[30], step[17], -cospi[34], step[30], cosBit);
    output[18] = _HalfBtf(cospi[46], step[18], -cospi[18], step[29], cosBit);
    output[19] = _HalfBtf(cospi[14], step[19], -cospi[50], step[28], cosBit);
    output[20] = _HalfBtf(cospi[54], step[20], -cospi[10], step[27], cosBit);
    output[21] = _HalfBtf(cospi[22], step[21], -cospi[42], step[26], cosBit);
    output[22] = _HalfBtf(cospi[38], step[22], -cospi[26], step[25], cosBit);
    output[23] = _HalfBtf(cospi[6], step[23], -cospi[58], step[24], cosBit);
    output[24] = _HalfBtf(cospi[58], step[23], cospi[6], step[24], cosBit);
    output[25] = _HalfBtf(cospi[26], step[22], cospi[38], step[25], cosBit);
    output[26] = _HalfBtf(cospi[42], step[21], cospi[22], step[26], cosBit);
    output[27] = _HalfBtf(cospi[10], step[20], cospi[54], step[27], cosBit);
    output[28] = _HalfBtf(cospi[50], step[19], cospi[14], step[28], cosBit);
    output[29] = _HalfBtf(cospi[18], step[18], cospi[46], step[29], cosBit);
    output[30] = _HalfBtf(cospi[34], step[17], cospi[30], step[30], cosBit);
    output[31] = _HalfBtf(cospi[2], step[16], cospi[62], step[31], cosBit);
    output[32] = _ClampValue(step[32] + step[33], rangeBit);
    output[33] = _ClampValue(step[32] - step[33], rangeBit);
    output[34] = _ClampValue(-step[34] + step[35], rangeBit);
    output[35] = _ClampValue(step[34] + step[35], rangeBit);
    output[36] = _ClampValue(step[36] + step[37], rangeBit);
    output[37] = _ClampValue(step[36] - step[37], rangeBit);
    output[38] = _ClampValue(-step[38] + step[39], rangeBit);
    output[39] = _ClampValue(step[38] + step[39], rangeBit);
    output[40] = _ClampValue(step[40] + step[41], rangeBit);
    output[41] = _ClampValue(step[40] - step[41], rangeBit);
    output[42] = _ClampValue(-step[42] + step[43], rangeBit);
    output[43] = _ClampValue(step[42] + step[43], rangeBit);
    output[44] = _ClampValue(step[44] + step[45], rangeBit);
    output[45] = _ClampValue(step[44] - step[45], rangeBit);
    output[46] = _ClampValue(-step[46] + step[47], rangeBit);
    output[47] = _ClampValue(step[46] + step[47], rangeBit);
    output[48] = _ClampValue(step[48] + step[49], rangeBit);
    output[49] = _ClampValue(step[48] - step[49], rangeBit);
    output[50] = _ClampValue(-step[50] + step[51], rangeBit);
    output[51] = _ClampValue(step[50] + step[51], rangeBit);
    output[52] = _ClampValue(step[52] + step[53], rangeBit);
    output[53] = _ClampValue(step[52] - step[53], rangeBit);
    output[54] = _ClampValue(-step[54] + step[55], rangeBit);
    output[55] = _ClampValue(step[54] + step[55], rangeBit);
    output[56] = _ClampValue(step[56] + step[57], rangeBit);
    output[57] = _ClampValue(step[56] - step[57], rangeBit);
    output[58] = _ClampValue(-step[58] + step[59], rangeBit);
    output[59] = _ClampValue(step[58] + step[59], rangeBit);
    output[60] = _ClampValue(step[60] + step[61], rangeBit);
    output[61] = _ClampValue(step[60] - step[61], rangeBit);
    output[62] = _ClampValue(-step[62] + step[63], rangeBit);
    output[63] = _ClampValue(step[62] + step[63], rangeBit);

    // stage 4
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = output[4];
    step[5] = output[5];
    step[6] = output[6];
    step[7] = output[7];
    step[8] = _HalfBtf(cospi[60], output[8], -cospi[4], output[15], cosBit);
    step[9] = _HalfBtf(cospi[28], output[9], -cospi[36], output[14], cosBit);
    step[10] = _HalfBtf(cospi[44], output[10], -cospi[20], output[13], cosBit);
    step[11] = _HalfBtf(cospi[12], output[11], -cospi[52], output[12], cosBit);
    step[12] = _HalfBtf(cospi[52], output[11], cospi[12], output[12], cosBit);
    step[13] = _HalfBtf(cospi[20], output[10], cospi[44], output[13], cosBit);
    step[14] = _HalfBtf(cospi[36], output[9], cospi[28], output[14], cosBit);
    step[15] = _HalfBtf(cospi[4], output[8], cospi[60], output[15], cosBit);
    step[16] = _ClampValue(output[16] + output[17], rangeBit);
    step[17] = _ClampValue(output[16] - output[17], rangeBit);
    step[18] = _ClampValue(-output[18] + output[19], rangeBit);
    step[19] = _ClampValue(output[18] + output[19], rangeBit);
    step[20] = _ClampValue(output[20] + output[21], rangeBit);
    step[21] = _ClampValue(output[20] - output[21], rangeBit);
    step[22] = _ClampValue(-output[22] + output[23], rangeBit);
    step[23] = _ClampValue(output[22] + output[23], rangeBit);
    step[24] = _ClampValue(output[24] + output[25], rangeBit);
    step[25] = _ClampValue(output[24] - output[25], rangeBit);
    step[26] = _ClampValue(-output[26] + output[27], rangeBit);
    step[27] = _ClampValue(output[26] + output[27], rangeBit);
    step[28] = _ClampValue(output[28] + output[29], rangeBit);
    step[29] = _ClampValue(output[28] - output[29], rangeBit);
    step[30] = _ClampValue(-output[30] + output[31], rangeBit);
    step[31] = _ClampValue(output[30] + output[31], rangeBit);
    step[32] = output[32];
    step[33] = _HalfBtf(-cospi[4], output[33], cospi[60], output[62], cosBit);
    step[34] = _HalfBtf(-cospi[60], output[34], -cospi[4], output[61], cosBit);
    step[35] = output[35];
    step[36] = output[36];
    step[37] = _HalfBtf(-cospi[36], output[37], cospi[28], output[58], cosBit);
    step[38] = _HalfBtf(-cospi[28], output[38], -cospi[36], output[57], cosBit);
    step[39] = output[39];
    step[40] = output[40];
    step[41] = _HalfBtf(-cospi[20], output[41], cospi[44], output[54], cosBit);
    step[42] = _HalfBtf(-cospi[44], output[42], -cospi[20], output[53], cosBit);
    step[43] = output[43];
    step[44] = output[44];
    step[45] = _HalfBtf(-cospi[52], output[45], cospi[12], output[50], cosBit);
    step[46] = _HalfBtf(-cospi[12], output[46], -cospi[52], output[49], cosBit);
    step[47] = output[47];
    step[48] = output[48];
    step[49] = _HalfBtf(-cospi[52], output[46], cospi[12], output[49], cosBit);
    step[50] = _HalfBtf(cospi[12], output[45], cospi[52], output[50], cosBit);
    step[51] = output[51];
    step[52] = output[52];
    step[53] = _HalfBtf(-cospi[20], output[42], cospi[44], output[53], cosBit);
    step[54] = _HalfBtf(cospi[44], output[41], cospi[20], output[54], cosBit);
    step[55] = output[55];
    step[56] = output[56];
    step[57] = _HalfBtf(-cospi[36], output[38], cospi[28], output[57], cosBit);
    step[58] = _HalfBtf(cospi[28], output[37], cospi[36], output[58], cosBit);
    step[59] = output[59];
    step[60] = output[60];
    step[61] = _HalfBtf(-cospi[4], output[34], cospi[60], output[61], cosBit);
    step[62] = _HalfBtf(cospi[60], output[33], cospi[4], output[62], cosBit);
    step[63] = output[63];

    // stage 5
    output[0] = step[0];
    output[1] = step[1];
    output[2] = step[2];
    output[3] = step[3];
    output[4] = _HalfBtf(cospi[56], step[4], -cospi[8], step[7], cosBit);
    output[5] = _HalfBtf(cospi[24], step[5], -cospi[40], step[6], cosBit);
    output[6] = _HalfBtf(cospi[40], step[5], cospi[24], step[6], cosBit);
    output[7] = _HalfBtf(cospi[8], step[4], cospi[56], step[7], cosBit);
    output[8] = _ClampValue(step[8] + step[9], rangeBit);
    output[9] = _ClampValue(step[8] - step[9], rangeBit);
    output[10] = _ClampValue(-step[10] + step[11], rangeBit);
    output[11] = _ClampValue(step[10] + step[11], rangeBit);
    output[12] = _ClampValue(step[12] + step[13], rangeBit);
    output[13] = _ClampValue(step[12] - step[13], rangeBit);
    output[14] = _ClampValue(-step[14] + step[15], rangeBit);
    output[15] = _ClampValue(step[14] + step[15], rangeBit);
    output[16] = step[16];
    output[17] = _HalfBtf(-cospi[8], step[17], cospi[56], step[30], cosBit);
    output[18] = _HalfBtf(-cospi[56], step[18], -cospi[8], step[29], cosBit);
    output[19] = step[19];
    output[20] = step[20];
    output[21] = _HalfBtf(-cospi[40], step[21], cospi[24], step[26], cosBit);
    output[22] = _HalfBtf(-cospi[24], step[22], -cospi[40], step[25], cosBit);
    output[23] = step[23];
    output[24] = step[24];
    output[25] = _HalfBtf(-cospi[40], step[22], cospi[24], step[25], cosBit);
    output[26] = _HalfBtf(cospi[24], step[21], cospi[40], step[26], cosBit);
    output[27] = step[27];
    output[28] = step[28];
    output[29] = _HalfBtf(-cospi[8], step[18], cospi[56], step[29], cosBit);
    output[30] = _HalfBtf(cospi[56], step[17], cospi[8], step[30], cosBit);
    output[31] = step[31];
    output[32] = _ClampValue(step[32] + step[35], rangeBit);
    output[33] = _ClampValue(step[33] + step[34], rangeBit);
    output[34] = _ClampValue(step[33] - step[34], rangeBit);
    output[35] = _ClampValue(step[32] - step[35], rangeBit);
    output[36] = _ClampValue(-step[36] + step[39], rangeBit);
    output[37] = _ClampValue(-step[37] + step[38], rangeBit);
    output[38] = _ClampValue(step[37] + step[38], rangeBit);
    output[39] = _ClampValue(step[36] + step[39], rangeBit);
    output[40] = _ClampValue(step[40] + step[43], rangeBit);
    output[41] = _ClampValue(step[41] + step[42], rangeBit);
    output[42] = _ClampValue(step[41] - step[42], rangeBit);
    output[43] = _ClampValue(step[40] - step[43], rangeBit);
    output[44] = _ClampValue(-step[44] + step[47], rangeBit);
    output[45] = _ClampValue(-step[45] + step[46], rangeBit);
    output[46] = _ClampValue(step[45] + step[46], rangeBit);
    output[47] = _ClampValue(step[44] + step[47], rangeBit);
    output[48] = _ClampValue(step[48] + step[51], rangeBit);
    output[49] = _ClampValue(step[49] + step[50], rangeBit);
    output[50] = _ClampValue(step[49] - step[50], rangeBit);
    output[51] = _ClampValue(step[48] - step[51], rangeBit);
    output[52] = _ClampValue(-step[52] + step[55], rangeBit);
    output[53] = _ClampValue(-step[53] + step[54], rangeBit);
    output[54] = _ClampValue(step[53] + step[54], rangeBit);
    output[55] = _ClampValue(step[52] + step[55], rangeBit);
    output[56] = _ClampValue(step[56] + step[59], rangeBit);
    output[57] = _ClampValue(step[57] + step[58], rangeBit);
    output[58] = _ClampValue(step[57] - step[58], rangeBit);
    output[59] = _ClampValue(step[56] - step[59], rangeBit);
    output[60] = _ClampValue(-step[60] + step[63], rangeBit);
    output[61] = _ClampValue(-step[61] + step[62], rangeBit);
    output[62] = _ClampValue(step[61] + step[62], rangeBit);
    output[63] = _ClampValue(step[60] + step[63], rangeBit);

    // stage 6
    step[0] = _HalfBtf(cospi[32], output[0], cospi[32], output[1], cosBit);
    step[1] = _HalfBtf(cospi[32], output[0], -cospi[32], output[1], cosBit);
    step[2] = _HalfBtf(cospi[48], output[2], -cospi[16], output[3], cosBit);
    step[3] = _HalfBtf(cospi[16], output[2], cospi[48], output[3], cosBit);
    step[4] = _ClampValue(output[4] + output[5], rangeBit);
    step[5] = _ClampValue(output[4] - output[5], rangeBit);
    step[6] = _ClampValue(-output[6] + output[7], rangeBit);
    step[7] = _ClampValue(output[6] + output[7], rangeBit);
    step[8] = output[8];
    step[9] = _HalfBtf(-cospi[16], output[9], cospi[48], output[14], cosBit);
    step[10] = _HalfBtf(-cospi[48], output[10], -cospi[16], output[13], cosBit);
    step[11] = output[11];
    step[12] = output[12];
    step[13] = _HalfBtf(-cospi[16], output[10], cospi[48], output[13], cosBit);
    step[14] = _HalfBtf(cospi[48], output[9], cospi[16], output[14], cosBit);
    step[15] = output[15];
    step[16] = _ClampValue(output[16] + output[19], rangeBit);
    step[17] = _ClampValue(output[17] + output[18], rangeBit);
    step[18] = _ClampValue(output[17] - output[18], rangeBit);
    step[19] = _ClampValue(output[16] - output[19], rangeBit);
    step[20] = _ClampValue(-output[20] + output[23], rangeBit);
    step[21] = _ClampValue(-output[21] + output[22], rangeBit);
    step[22] = _ClampValue(output[21] + output[22], rangeBit);
    step[23] = _ClampValue(output[20] + output[23], rangeBit);
    step[24] = _ClampValue(output[24] + output[27], rangeBit);
    step[25] = _ClampValue(output[25] + output[26], rangeBit);
    step[26] = _ClampValue(output[25] - output[26], rangeBit);
    step[27] = _ClampValue(output[24] - output[27], rangeBit);
    step[28] = _ClampValue(-output[28] + output[31], rangeBit);
    step[29] = _ClampValue(-output[29] + output[30], rangeBit);
    step[30] = _ClampValue(output[29] + output[30], rangeBit);
    step[31] = _ClampValue(output[28] + output[31], rangeBit);
    step[32] = output[32];
    step[33] = output[33];
    step[34] = _HalfBtf(-cospi[8], output[34], cospi[56], output[61], cosBit);
    step[35] = _HalfBtf(-cospi[8], output[35], cospi[56], output[60], cosBit);
    step[36] = _HalfBtf(-cospi[56], output[36], -cospi[8], output[59], cosBit);
    step[37] = _HalfBtf(-cospi[56], output[37], -cospi[8], output[58], cosBit);
    step[38] = output[38];
    step[39] = output[39];
    step[40] = output[40];
    step[41] = output[41];
    step[42] = _HalfBtf(-cospi[40], output[42], cospi[24], output[53], cosBit);
    step[43] = _HalfBtf(-cospi[40], output[43], cospi[24], output[52], cosBit);
    step[44] = _HalfBtf(-cospi[24], output[44], -cospi[40], output[51], cosBit);
    step[45] = _HalfBtf(-cospi[24], output[45], -cospi[40], output[50], cosBit);
    step[46] = output[46];
    step[47] = output[47];
    step[48] = output[48];
    step[49] = output[49];
    step[50] = _HalfBtf(-cospi[40], output[45], cospi[24], output[50], cosBit);
    step[51] = _HalfBtf(-cospi[40], output[44], cospi[24], output[51], cosBit);
    step[52] = _HalfBtf(cospi[24], output[43], cospi[40], output[52], cosBit);
    step[53] = _HalfBtf(cospi[24], output[42], cospi[40], output[53], cosBit);
    step[54] = output[54];
    step[55] = output[55];
    step[56] = output[56];
    step[57] = output[57];
    step[58] = _HalfBtf(-cospi[8], output[37], cospi[56], output[58], cosBit);
    step[59] = _HalfBtf(-cospi[8], output[36], cospi[56], output[59], cosBit);
    step[60] = _HalfBtf(cospi[56], output[35], cospi[8], output[60], cosBit);
    step[61] = _HalfBtf(cospi[56], output[34], cospi[8], output[61], cosBit);
    step[62] = output[62];
    step[63] = output[63];

    // stage 7
    output[0] = _ClampValue(step[0] + step[3], rangeBit);
    output[1] = _ClampValue(step[1] + step[2], rangeBit);
    output[2] = _ClampValue(step[1] - step[2], rangeBit);
    output[3] = _ClampValue(step[0] - step[3], rangeBit);
    output[4] = step[4];
    output[5] = _HalfBtf(-cospi[32], step[5], cospi[32], step[6], cosBit);
    output[6] = _HalfBtf(cospi[32], step[5], cospi[32], step[6], cosBit);
    output[7] = step[7];
    output[8] = _ClampValue(step[8] + step[11], rangeBit);
    output[9] = _ClampValue(step[9] + step[10], rangeBit);
    output[10] = _ClampValue(step[9] - step[10], rangeBit);
    output[11] = _ClampValue(step[8] - step[11], rangeBit);
    output[12] = _ClampValue(-step[12] + step[15], rangeBit);
    output[13] = _ClampValue(-step[13] + step[14], rangeBit);
    output[14] = _ClampValue(step[13] + step[14], rangeBit);
    output[15] = _ClampValue(step[12] + step[15], rangeBit);
    output[16] = step[16];
    output[17] = step[17];
    output[18] = _HalfBtf(-cospi[16], step[18], cospi[48], step[29], cosBit);
    output[19] = _HalfBtf(-cospi[16], step[19], cospi[48], step[28], cosBit);
    output[20] = _HalfBtf(-cospi[48], step[20], -cospi[16], step[27], cosBit);
    output[21] = _HalfBtf(-cospi[48], step[21], -cospi[16], step[26], cosBit);
    output[22] = step[22];
    output[23] = step[23];
    output[24] = step[24];
    output[25] = step[25];
    output[26] = _HalfBtf(-cospi[16], step[21], cospi[48], step[26], cosBit);
    output[27] = _HalfBtf(-cospi[16], step[20], cospi[48], step[27], cosBit);
    output[28] = _HalfBtf(cospi[48], step[19], cospi[16], step[28], cosBit);
    output[29] = _HalfBtf(cospi[48], step[18], cospi[16], step[29], cosBit);
    output[30] = step[30];
    output[31] = step[31];
    output[32] = _ClampValue(step[32] + step[39], rangeBit);
    output[33] = _ClampValue(step[33] + step[38], rangeBit);
    output[34] = _ClampValue(step[34] + step[37], rangeBit);
    output[35] = _ClampValue(step[35] + step[36], rangeBit);
    output[36] = _ClampValue(step[35] - step[36], rangeBit);
    output[37] = _ClampValue(step[34] - step[37], rangeBit);
    output[38] = _ClampValue(step[33] - step[38], rangeBit);
    output[39] = _ClampValue(step[32] - step[39], rangeBit);
    output[40] = _ClampValue(-step[40] + step[47], rangeBit);
    output[41] = _ClampValue(-step[41] + step[46], rangeBit);
    output[42] = _ClampValue(-step[42] + step[45], rangeBit);
    output[43] = _ClampValue(-step[43] + step[44], rangeBit);
    output[44] = _ClampValue(step[43] + step[44], rangeBit);
    output[45] = _ClampValue(step[42] + step[45], rangeBit);
    output[46] = _ClampValue(step[41] + step[46], rangeBit);
    output[47] = _ClampValue(step[40] + step[47], rangeBit);
    output[48] = _ClampValue(step[48] + step[55], rangeBit);
    output[49] = _ClampValue(step[49] + step[54], rangeBit);
    output[50] = _ClampValue(step[50] + step[53], rangeBit);
    output[51] = _ClampValue(step[51] + step[52], rangeBit);
    output[52] = _ClampValue(step[51] - step[52], rangeBit);
    output[53] = _ClampValue(step[50] - step[53], rangeBit);
    output[54] = _ClampValue(step[49] - step[54], rangeBit);
    output[55] = _ClampValue(step[48] - step[55], rangeBit);
    output[56] = _ClampValue(-step[56] + step[63], rangeBit);
    output[57] = _ClampValue(-step[57] + step[62], rangeBit);
    output[58] = _ClampValue(-step[58] + step[61], rangeBit);
    output[59] = _ClampValue(-step[59] + step[60], rangeBit);
    output[60] = _ClampValue(step[59] + step[60], rangeBit);
    output[61] = _ClampValue(step[58] + step[61], rangeBit);
    output[62] = _ClampValue(step[57] + step[62], rangeBit);
    output[63] = _ClampValue(step[56] + step[63], rangeBit);

    // stage 8
    step[0] = _ClampValue(output[0] + output[7], rangeBit);
    step[1] = _ClampValue(output[1] + output[6], rangeBit);
    step[2] = _ClampValue(output[2] + output[5], rangeBit);
    step[3] = _ClampValue(output[3] + output[4], rangeBit);
    step[4] = _ClampValue(output[3] - output[4], rangeBit);
    step[5] = _ClampValue(output[2] - output[5], rangeBit);
    step[6] = _ClampValue(output[1] - output[6], rangeBit);
    step[7] = _ClampValue(output[0] - output[7], rangeBit);
    step[8] = output[8];
    step[9] = output[9];
    step[10] = _HalfBtf(-cospi[32], output[10], cospi[32], output[13], cosBit);
    step[11] = _HalfBtf(-cospi[32], output[11], cospi[32], output[12], cosBit);
    step[12] = _HalfBtf(cospi[32], output[11], cospi[32], output[12], cosBit);
    step[13] = _HalfBtf(cospi[32], output[10], cospi[32], output[13], cosBit);
    step[14] = output[14];
    step[15] = output[15];
    step[16] = _ClampValue(output[16] + output[23], rangeBit);
    step[17] = _ClampValue(output[17] + output[22], rangeBit);
    step[18] = _ClampValue(output[18] + output[21], rangeBit);
    step[19] = _ClampValue(output[19] + output[20], rangeBit);
    step[20] = _ClampValue(output[19] - output[20], rangeBit);
    step[21] = _ClampValue(output[18] - output[21], rangeBit);
    step[22] = _ClampValue(output[17] - output[22], rangeBit);
    step[23] = _ClampValue(output[16] - output[23], rangeBit);
    step[24] = _ClampValue(-output[24] + output[31], rangeBit);
    step[25] = _ClampValue(-output[25] + output[30], rangeBit);
    step[26] = _ClampValue(-output[26] + output[29], rangeBit);
    step[27] = _ClampValue(-output[27] + output[28], rangeBit);
    step[28] = _ClampValue(output[27] + output[28], rangeBit);
    step[29] = _ClampValue(output[26] + output[29], rangeBit);
    step[30] = _ClampValue(output[25] + output[30], rangeBit);
    step[31] = _ClampValue(output[24] + output[31], rangeBit);
    step[32] = output[32];
    step[33] = output[33];
    step[34] = output[34];
    step[35] = output[35];
    step[36] = _HalfBtf(-cospi[16], output[36], cospi[48], output[59], cosBit);
    step[37] = _HalfBtf(-cospi[16], output[37], cospi[48], output[58], cosBit);
    step[38] = _HalfBtf(-cospi[16], output[38], cospi[48], output[57], cosBit);
    step[39] = _HalfBtf(-cospi[16], output[39], cospi[48], output[56], cosBit);
    step[40] = _HalfBtf(-cospi[48], output[40], -cospi[16], output[55], cosBit);
    step[41] = _HalfBtf(-cospi[48], output[41], -cospi[16], output[54], cosBit);
    step[42] = _HalfBtf(-cospi[48], output[42], -cospi[16], output[53], cosBit);
    step[43] = _HalfBtf(-cospi[48], output[43], -cospi[16], output[52], cosBit);
    step[44] = output[44];
    step[45] = output[45];
    step[46] = output[46];
    step[47] = output[47];
    step[48] = output[48];
    step[49] = output[49];
    step[50] = output[50];
    step[51] = output[51];
    step[52] = _HalfBtf(-cospi[16], output[43], cospi[48], output[52], cosBit);
    step[53] = _HalfBtf(-cospi[16], output[42], cospi[48], output[53], cosBit);
    step[54] = _HalfBtf(-cospi[16], output[41], cospi[48], output[54], cosBit);
    step[55] = _HalfBtf(-cospi[16], output[40], cospi[48], output[55], cosBit);
    step[56] = _HalfBtf(cospi[48], output[39], cospi[16], output[56], cosBit);
    step[57] = _HalfBtf(cospi[48], output[38], cospi[16], output[57], cosBit);
    step[58] = _HalfBtf(cospi[48], output[37], cospi[16], output[58], cosBit);
    step[59] = _HalfBtf(cospi[48], output[36], cospi[16], output[59], cosBit);
    step[60] = output[60];
    step[61] = output[61];
    step[62] = output[62];
    step[63] = output[63];

    // stage 9
    output[0] = _ClampValue(step[0] + step[15], rangeBit);
    output[1] = _ClampValue(step[1] + step[14], rangeBit);
    output[2] = _ClampValue(step[2] + step[13], rangeBit);
    output[3] = _ClampValue(step[3] + step[12], rangeBit);
    output[4] = _ClampValue(step[4] + step[11], rangeBit);
    output[5] = _ClampValue(step[5] + step[10], rangeBit);
    output[6] = _ClampValue(step[6] + step[9], rangeBit);
    output[7] = _ClampValue(step[7] + step[8], rangeBit);
    output[8] = _ClampValue(step[7] - step[8], rangeBit);
    output[9] = _ClampValue(step[6] - step[9], rangeBit);
    output[10] = _ClampValue(step[5] - step[10], rangeBit);
    output[11] = _ClampValue(step[4] - step[11], rangeBit);
    output[12] = _ClampValue(step[3] - step[12], rangeBit);
    output[13] = _ClampValue(step[2] - step[13], rangeBit);
    output[14] = _ClampValue(step[1] - step[14], rangeBit);
    output[15] = _ClampValue(step[0] - step[15], rangeBit);
    output[16] = step[16];
    output[17] = step[17];
    output[18] = step[18];
    output[19] = step[19];
    output[20] = _HalfBtf(-cospi[32], step[20], cospi[32], step[27], cosBit);
    output[21] = _HalfBtf(-cospi[32], step[21], cospi[32], step[26], cosBit);
    output[22] = _HalfBtf(-cospi[32], step[22], cospi[32], step[25], cosBit);
    output[23] = _HalfBtf(-cospi[32], step[23], cospi[32], step[24], cosBit);
    output[24] = _HalfBtf(cospi[32], step[23], cospi[32], step[24], cosBit);
    output[25] = _HalfBtf(cospi[32], step[22], cospi[32], step[25], cosBit);
    output[26] = _HalfBtf(cospi[32], step[21], cospi[32], step[26], cosBit);
    output[27] = _HalfBtf(cospi[32], step[20], cospi[32], step[27], cosBit);
    output[28] = step[28];
    output[29] = step[29];
    output[30] = step[30];
    output[31] = step[31];
    output[32] = _ClampValue(step[32] + step[47], rangeBit);
    output[33] = _ClampValue(step[33] + step[46], rangeBit);
    output[34] = _ClampValue(step[34] + step[45], rangeBit);
    output[35] = _ClampValue(step[35] + step[44], rangeBit);
    output[36] = _ClampValue(step[36] + step[43], rangeBit);
    output[37] = _ClampValue(step[37] + step[42], rangeBit);
    output[38] = _ClampValue(step[38] + step[41], rangeBit);
    output[39] = _ClampValue(step[39] + step[40], rangeBit);
    output[40] = _ClampValue(step[39] - step[40], rangeBit);
    output[41] = _ClampValue(step[38] - step[41], rangeBit);
    output[42] = _ClampValue(step[37] - step[42], rangeBit);
    output[43] = _ClampValue(step[36] - step[43], rangeBit);
    output[44] = _ClampValue(step[35] - step[44], rangeBit);
    output[45] = _ClampValue(step[34] - step[45], rangeBit);
    output[46] = _ClampValue(step[33] - step[46], rangeBit);
    output[47] = _ClampValue(step[32] - step[47], rangeBit);
    output[48] = _ClampValue(-step[48] + step[63], rangeBit);
    output[49] = _ClampValue(-step[49] + step[62], rangeBit);
    output[50] = _ClampValue(-step[50] + step[61], rangeBit);
    output[51] = _ClampValue(-step[51] + step[60], rangeBit);
    output[52] = _ClampValue(-step[52] + step[59], rangeBit);
    output[53] = _ClampValue(-step[53] + step[58], rangeBit);
    output[54] = _ClampValue(-step[54] + step[57], rangeBit);
    output[55] = _ClampValue(-step[55] + step[56], rangeBit);
    output[56] = _ClampValue(step[55] + step[56], rangeBit);
    output[57] = _ClampValue(step[54] + step[57], rangeBit);
    output[58] = _ClampValue(step[53] + step[58], rangeBit);
    output[59] = _ClampValue(step[52] + step[59], rangeBit);
    output[60] = _ClampValue(step[51] + step[60], rangeBit);
    output[61] = _ClampValue(step[50] + step[61], rangeBit);
    output[62] = _ClampValue(step[49] + step[62], rangeBit);
    output[63] = _ClampValue(step[48] + step[63], rangeBit);

    // stage 10
    step[0] = _ClampValue(output[0] + output[31], rangeBit);
    step[1] = _ClampValue(output[1] + output[30], rangeBit);
    step[2] = _ClampValue(output[2] + output[29], rangeBit);
    step[3] = _ClampValue(output[3] + output[28], rangeBit);
    step[4] = _ClampValue(output[4] + output[27], rangeBit);
    step[5] = _ClampValue(output[5] + output[26], rangeBit);
    step[6] = _ClampValue(output[6] + output[25], rangeBit);
    step[7] = _ClampValue(output[7] + output[24], rangeBit);
    step[8] = _ClampValue(output[8] + output[23], rangeBit);
    step[9] = _ClampValue(output[9] + output[22], rangeBit);
    step[10] = _ClampValue(output[10] + output[21], rangeBit);
    step[11] = _ClampValue(output[11] + output[20], rangeBit);
    step[12] = _ClampValue(output[12] + output[19], rangeBit);
    step[13] = _ClampValue(output[13] + output[18], rangeBit);
    step[14] = _ClampValue(output[14] + output[17], rangeBit);
    step[15] = _ClampValue(output[15] + output[16], rangeBit);
    step[16] = _ClampValue(output[15] - output[16], rangeBit);
    step[17] = _ClampValue(output[14] - output[17], rangeBit);
    step[18] = _ClampValue(output[13] - output[18], rangeBit);
    step[19] = _ClampValue(output[12] - output[19], rangeBit);
    step[20] = _ClampValue(output[11] - output[20], rangeBit);
    step[21] = _ClampValue(output[10] - output[21], rangeBit);
    step[22] = _ClampValue(output[9] - output[22], rangeBit);
    step[23] = _ClampValue(output[8] - output[23], rangeBit);
    step[24] = _ClampValue(output[7] - output[24], rangeBit);
    step[25] = _ClampValue(output[6] - output[25], rangeBit);
    step[26] = _ClampValue(output[5] - output[26], rangeBit);
    step[27] = _ClampValue(output[4] - output[27], rangeBit);
    step[28] = _ClampValue(output[3] - output[28], rangeBit);
    step[29] = _ClampValue(output[2] - output[29], rangeBit);
    step[30] = _ClampValue(output[1] - output[30], rangeBit);
    step[31] = _ClampValue(output[0] - output[31], rangeBit);
    step[32] = output[32];
    step[33] = output[33];
    step[34] = output[34];
    step[35] = output[35];
    step[36] = output[36];
    step[37] = output[37];
    step[38] = output[38];
    step[39] = output[39];
    step[40] = _HalfBtf(-cospi[32], output[40], cospi[32], output[55], cosBit);
    step[41] = _HalfBtf(-cospi[32], output[41], cospi[32], output[54], cosBit);
    step[42] = _HalfBtf(-cospi[32], output[42], cospi[32], output[53], cosBit);
    step[43] = _HalfBtf(-cospi[32], output[43], cospi[32], output[52], cosBit);
    step[44] = _HalfBtf(-cospi[32], output[44], cospi[32], output[51], cosBit);
    step[45] = _HalfBtf(-cospi[32], output[45], cospi[32], output[50], cosBit);
    step[46] = _HalfBtf(-cospi[32], output[46], cospi[32], output[49], cosBit);
    step[47] = _HalfBtf(-cospi[32], output[47], cospi[32], output[48], cosBit);
    step[48] = _HalfBtf(cospi[32], output[47], cospi[32], output[48], cosBit);
    step[49] = _HalfBtf(cospi[32], output[46], cospi[32], output[49], cosBit);
    step[50] = _HalfBtf(cospi[32], output[45], cospi[32], output[50], cosBit);
    step[51] = _HalfBtf(cospi[32], output[44], cospi[32], output[51], cosBit);
    step[52] = _HalfBtf(cospi[32], output[43], cospi[32], output[52], cosBit);
    step[53] = _HalfBtf(cospi[32], output[42], cospi[32], output[53], cosBit);
    step[54] = _HalfBtf(cospi[32], output[41], cospi[32], output[54], cosBit);
    step[55] = _HalfBtf(cospi[32], output[40], cospi[32], output[55], cosBit);
    step[56] = output[56];
    step[57] = output[57];
    step[58] = output[58];
    step[59] = output[59];
    step[60] = output[60];
    step[61] = output[61];
    step[62] = output[62];
    step[63] = output[63];

    // stage 11
    output[0] = _ClampValue(step[0] + step[63], rangeBit);
    output[1] = _ClampValue(step[1] + step[62], rangeBit);
    output[2] = _ClampValue(step[2] + step[61], rangeBit);
    output[3] = _ClampValue(step[3] + step[60], rangeBit);
    output[4] = _ClampValue(step[4] + step[59], rangeBit);
    output[5] = _ClampValue(step[5] + step[58], rangeBit);
    output[6] = _ClampValue(step[6] + step[57], rangeBit);
    output[7] = _ClampValue(step[7] + step[56], rangeBit);
    output[8] = _ClampValue(step[8] + step[55], rangeBit);
    output[9] = _ClampValue(step[9] + step[54], rangeBit);
    output[10] = _ClampValue(step[10] + step[53], rangeBit);
    output[11] = _ClampValue(step[11] + step[52], rangeBit);
    output[12] = _ClampValue(step[12] + step[51], rangeBit);
    output[13] = _ClampValue(step[13] + step[50], rangeBit);
    output[14] = _ClampValue(step[14] + step[49], rangeBit);
    output[15] = _ClampValue(step[15] + step[48], rangeBit);
    output[16] = _ClampValue(step[16] + step[47], rangeBit);
    output[17] = _ClampValue(step[17] + step[46], rangeBit);
    output[18] = _ClampValue(step[18] + step[45], rangeBit);
    output[19] = _ClampValue(step[19] + step[44], rangeBit);
    output[20] = _ClampValue(step[20] + step[43], rangeBit);
    output[21] = _ClampValue(step[21] + step[42], rangeBit);
    output[22] = _ClampValue(step[22] + step[41], rangeBit);
    output[23] = _ClampValue(step[23] + step[40], rangeBit);
    output[24] = _ClampValue(step[24] + step[39], rangeBit);
    output[25] = _ClampValue(step[25] + step[38], rangeBit);
    output[26] = _ClampValue(step[26] + step[37], rangeBit);
    output[27] = _ClampValue(step[27] + step[36], rangeBit);
    output[28] = _ClampValue(step[28] + step[35], rangeBit);
    output[29] = _ClampValue(step[29] + step[34], rangeBit);
    output[30] = _ClampValue(step[30] + step[33], rangeBit);
    output[31] = _ClampValue(step[31] + step[32], rangeBit);
    output[32] = _ClampValue(step[31] - step[32], rangeBit);
    output[33] = _ClampValue(step[30] - step[33], rangeBit);
    output[34] = _ClampValue(step[29] - step[34], rangeBit);
    output[35] = _ClampValue(step[28] - step[35], rangeBit);
    output[36] = _ClampValue(step[27] - step[36], rangeBit);
    output[37] = _ClampValue(step[26] - step[37], rangeBit);
    output[38] = _ClampValue(step[25] - step[38], rangeBit);
    output[39] = _ClampValue(step[24] - step[39], rangeBit);
    output[40] = _ClampValue(step[23] - step[40], rangeBit);
    output[41] = _ClampValue(step[22] - step[41], rangeBit);
    output[42] = _ClampValue(step[21] - step[42], rangeBit);
    output[43] = _ClampValue(step[20] - step[43], rangeBit);
    output[44] = _ClampValue(step[19] - step[44], rangeBit);
    output[45] = _ClampValue(step[18] - step[45], rangeBit);
    output[46] = _ClampValue(step[17] - step[46], rangeBit);
    output[47] = _ClampValue(step[16] - step[47], rangeBit);
    output[48] = _ClampValue(step[15] - step[48], rangeBit);
    output[49] = _ClampValue(step[14] - step[49], rangeBit);
    output[50] = _ClampValue(step[13] - step[50], rangeBit);
    output[51] = _ClampValue(step[12] - step[51], rangeBit);
    output[52] = _ClampValue(step[11] - step[52], rangeBit);
    output[53] = _ClampValue(step[10] - step[53], rangeBit);
    output[54] = _ClampValue(step[9] - step[54], rangeBit);
    output[55] = _ClampValue(step[8] - step[55], rangeBit);
    output[56] = _ClampValue(step[7] - step[56], rangeBit);
    output[57] = _ClampValue(step[6] - step[57], rangeBit);
    output[58] = _ClampValue(step[5] - step[58], rangeBit);
    output[59] = _ClampValue(step[4] - step[59], rangeBit);
    output[60] = _ClampValue(step[3] - step[60], rangeBit);
    output[61] = _ClampValue(step[2] - step[61], rangeBit);
    output[62] = _ClampValue(step[1] - step[62], rangeBit);
    output[63] = _ClampValue(step[0] - step[63], rangeBit);
  }

  /// <summary>libaom <c>av1_iadst8</c>: 8-point inverse ADST.</summary>
  private static void _Iadst8(ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    var cospi = _Cospi(cosBit);
    Span<int> step = stackalloc int[8];

    // stage 1
    output[0] = input[7];
    output[1] = input[0];
    output[2] = input[5];
    output[3] = input[2];
    output[4] = input[3];
    output[5] = input[4];
    output[6] = input[1];
    output[7] = input[6];

    // stage 2
    step[0] = _HalfBtf(cospi[4], output[0], cospi[60], output[1], cosBit);
    step[1] = _HalfBtf(cospi[60], output[0], -cospi[4], output[1], cosBit);
    step[2] = _HalfBtf(cospi[20], output[2], cospi[44], output[3], cosBit);
    step[3] = _HalfBtf(cospi[44], output[2], -cospi[20], output[3], cosBit);
    step[4] = _HalfBtf(cospi[36], output[4], cospi[28], output[5], cosBit);
    step[5] = _HalfBtf(cospi[28], output[4], -cospi[36], output[5], cosBit);
    step[6] = _HalfBtf(cospi[52], output[6], cospi[12], output[7], cosBit);
    step[7] = _HalfBtf(cospi[12], output[6], -cospi[52], output[7], cosBit);

    // stage 3
    output[0] = _ClampValue(step[0] + step[4], rangeBit);
    output[1] = _ClampValue(step[1] + step[5], rangeBit);
    output[2] = _ClampValue(step[2] + step[6], rangeBit);
    output[3] = _ClampValue(step[3] + step[7], rangeBit);
    output[4] = _ClampValue(step[0] - step[4], rangeBit);
    output[5] = _ClampValue(step[1] - step[5], rangeBit);
    output[6] = _ClampValue(step[2] - step[6], rangeBit);
    output[7] = _ClampValue(step[3] - step[7], rangeBit);

    // stage 4
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = _HalfBtf(cospi[16], output[4], cospi[48], output[5], cosBit);
    step[5] = _HalfBtf(cospi[48], output[4], -cospi[16], output[5], cosBit);
    step[6] = _HalfBtf(-cospi[48], output[6], cospi[16], output[7], cosBit);
    step[7] = _HalfBtf(cospi[16], output[6], cospi[48], output[7], cosBit);

    // stage 5
    output[0] = _ClampValue(step[0] + step[2], rangeBit);
    output[1] = _ClampValue(step[1] + step[3], rangeBit);
    output[2] = _ClampValue(step[0] - step[2], rangeBit);
    output[3] = _ClampValue(step[1] - step[3], rangeBit);
    output[4] = _ClampValue(step[4] + step[6], rangeBit);
    output[5] = _ClampValue(step[5] + step[7], rangeBit);
    output[6] = _ClampValue(step[4] - step[6], rangeBit);
    output[7] = _ClampValue(step[5] - step[7], rangeBit);

    // stage 6
    step[0] = output[0];
    step[1] = output[1];
    step[2] = _HalfBtf(cospi[32], output[2], cospi[32], output[3], cosBit);
    step[3] = _HalfBtf(cospi[32], output[2], -cospi[32], output[3], cosBit);
    step[4] = output[4];
    step[5] = output[5];
    step[6] = _HalfBtf(cospi[32], output[6], cospi[32], output[7], cosBit);
    step[7] = _HalfBtf(cospi[32], output[6], -cospi[32], output[7], cosBit);
    output[0] = step[0];
    output[1] = -step[4];
    output[2] = step[6];
    output[3] = -step[2];
    output[4] = step[3];
    output[5] = -step[7];
    output[6] = step[5];
    output[7] = -step[1];
  }

  /// <summary>libaom <c>av1_iadst16</c>: 16-point inverse ADST.</summary>
  private static void _Iadst16(ReadOnlySpan<int> input, Span<int> output, int cosBit, int rangeBit) {
    var cospi = _Cospi(cosBit);
    Span<int> step = stackalloc int[16];

    // stage 1
    output[0] = input[15];
    output[1] = input[0];
    output[2] = input[13];
    output[3] = input[2];
    output[4] = input[11];
    output[5] = input[4];
    output[6] = input[9];
    output[7] = input[6];
    output[8] = input[7];
    output[9] = input[8];
    output[10] = input[5];
    output[11] = input[10];
    output[12] = input[3];
    output[13] = input[12];
    output[14] = input[1];
    output[15] = input[14];

    // stage 2
    step[0] = _HalfBtf(cospi[2], output[0], cospi[62], output[1], cosBit);
    step[1] = _HalfBtf(cospi[62], output[0], -cospi[2], output[1], cosBit);
    step[2] = _HalfBtf(cospi[10], output[2], cospi[54], output[3], cosBit);
    step[3] = _HalfBtf(cospi[54], output[2], -cospi[10], output[3], cosBit);
    step[4] = _HalfBtf(cospi[18], output[4], cospi[46], output[5], cosBit);
    step[5] = _HalfBtf(cospi[46], output[4], -cospi[18], output[5], cosBit);
    step[6] = _HalfBtf(cospi[26], output[6], cospi[38], output[7], cosBit);
    step[7] = _HalfBtf(cospi[38], output[6], -cospi[26], output[7], cosBit);
    step[8] = _HalfBtf(cospi[34], output[8], cospi[30], output[9], cosBit);
    step[9] = _HalfBtf(cospi[30], output[8], -cospi[34], output[9], cosBit);
    step[10] = _HalfBtf(cospi[42], output[10], cospi[22], output[11], cosBit);
    step[11] = _HalfBtf(cospi[22], output[10], -cospi[42], output[11], cosBit);
    step[12] = _HalfBtf(cospi[50], output[12], cospi[14], output[13], cosBit);
    step[13] = _HalfBtf(cospi[14], output[12], -cospi[50], output[13], cosBit);
    step[14] = _HalfBtf(cospi[58], output[14], cospi[6], output[15], cosBit);
    step[15] = _HalfBtf(cospi[6], output[14], -cospi[58], output[15], cosBit);

    // stage 3
    output[0] = _ClampValue(step[0] + step[8], rangeBit);
    output[1] = _ClampValue(step[1] + step[9], rangeBit);
    output[2] = _ClampValue(step[2] + step[10], rangeBit);
    output[3] = _ClampValue(step[3] + step[11], rangeBit);
    output[4] = _ClampValue(step[4] + step[12], rangeBit);
    output[5] = _ClampValue(step[5] + step[13], rangeBit);
    output[6] = _ClampValue(step[6] + step[14], rangeBit);
    output[7] = _ClampValue(step[7] + step[15], rangeBit);
    output[8] = _ClampValue(step[0] - step[8], rangeBit);
    output[9] = _ClampValue(step[1] - step[9], rangeBit);
    output[10] = _ClampValue(step[2] - step[10], rangeBit);
    output[11] = _ClampValue(step[3] - step[11], rangeBit);
    output[12] = _ClampValue(step[4] - step[12], rangeBit);
    output[13] = _ClampValue(step[5] - step[13], rangeBit);
    output[14] = _ClampValue(step[6] - step[14], rangeBit);
    output[15] = _ClampValue(step[7] - step[15], rangeBit);

    // stage 4
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = output[4];
    step[5] = output[5];
    step[6] = output[6];
    step[7] = output[7];
    step[8] = _HalfBtf(cospi[8], output[8], cospi[56], output[9], cosBit);
    step[9] = _HalfBtf(cospi[56], output[8], -cospi[8], output[9], cosBit);
    step[10] = _HalfBtf(cospi[40], output[10], cospi[24], output[11], cosBit);
    step[11] = _HalfBtf(cospi[24], output[10], -cospi[40], output[11], cosBit);
    step[12] = _HalfBtf(-cospi[56], output[12], cospi[8], output[13], cosBit);
    step[13] = _HalfBtf(cospi[8], output[12], cospi[56], output[13], cosBit);
    step[14] = _HalfBtf(-cospi[24], output[14], cospi[40], output[15], cosBit);
    step[15] = _HalfBtf(cospi[40], output[14], cospi[24], output[15], cosBit);

    // stage 5
    output[0] = _ClampValue(step[0] + step[4], rangeBit);
    output[1] = _ClampValue(step[1] + step[5], rangeBit);
    output[2] = _ClampValue(step[2] + step[6], rangeBit);
    output[3] = _ClampValue(step[3] + step[7], rangeBit);
    output[4] = _ClampValue(step[0] - step[4], rangeBit);
    output[5] = _ClampValue(step[1] - step[5], rangeBit);
    output[6] = _ClampValue(step[2] - step[6], rangeBit);
    output[7] = _ClampValue(step[3] - step[7], rangeBit);
    output[8] = _ClampValue(step[8] + step[12], rangeBit);
    output[9] = _ClampValue(step[9] + step[13], rangeBit);
    output[10] = _ClampValue(step[10] + step[14], rangeBit);
    output[11] = _ClampValue(step[11] + step[15], rangeBit);
    output[12] = _ClampValue(step[8] - step[12], rangeBit);
    output[13] = _ClampValue(step[9] - step[13], rangeBit);
    output[14] = _ClampValue(step[10] - step[14], rangeBit);
    output[15] = _ClampValue(step[11] - step[15], rangeBit);

    // stage 6
    step[0] = output[0];
    step[1] = output[1];
    step[2] = output[2];
    step[3] = output[3];
    step[4] = _HalfBtf(cospi[16], output[4], cospi[48], output[5], cosBit);
    step[5] = _HalfBtf(cospi[48], output[4], -cospi[16], output[5], cosBit);
    step[6] = _HalfBtf(-cospi[48], output[6], cospi[16], output[7], cosBit);
    step[7] = _HalfBtf(cospi[16], output[6], cospi[48], output[7], cosBit);
    step[8] = output[8];
    step[9] = output[9];
    step[10] = output[10];
    step[11] = output[11];
    step[12] = _HalfBtf(cospi[16], output[12], cospi[48], output[13], cosBit);
    step[13] = _HalfBtf(cospi[48], output[12], -cospi[16], output[13], cosBit);
    step[14] = _HalfBtf(-cospi[48], output[14], cospi[16], output[15], cosBit);
    step[15] = _HalfBtf(cospi[16], output[14], cospi[48], output[15], cosBit);

    // stage 7
    output[0] = _ClampValue(step[0] + step[2], rangeBit);
    output[1] = _ClampValue(step[1] + step[3], rangeBit);
    output[2] = _ClampValue(step[0] - step[2], rangeBit);
    output[3] = _ClampValue(step[1] - step[3], rangeBit);
    output[4] = _ClampValue(step[4] + step[6], rangeBit);
    output[5] = _ClampValue(step[5] + step[7], rangeBit);
    output[6] = _ClampValue(step[4] - step[6], rangeBit);
    output[7] = _ClampValue(step[5] - step[7], rangeBit);
    output[8] = _ClampValue(step[8] + step[10], rangeBit);
    output[9] = _ClampValue(step[9] + step[11], rangeBit);
    output[10] = _ClampValue(step[8] - step[10], rangeBit);
    output[11] = _ClampValue(step[9] - step[11], rangeBit);
    output[12] = _ClampValue(step[12] + step[14], rangeBit);
    output[13] = _ClampValue(step[13] + step[15], rangeBit);
    output[14] = _ClampValue(step[12] - step[14], rangeBit);
    output[15] = _ClampValue(step[13] - step[15], rangeBit);

    // stage 8
    step[0] = output[0];
    step[1] = output[1];
    step[2] = _HalfBtf(cospi[32], output[2], cospi[32], output[3], cosBit);
    step[3] = _HalfBtf(cospi[32], output[2], -cospi[32], output[3], cosBit);
    step[4] = output[4];
    step[5] = output[5];
    step[6] = _HalfBtf(cospi[32], output[6], cospi[32], output[7], cosBit);
    step[7] = _HalfBtf(cospi[32], output[6], -cospi[32], output[7], cosBit);
    step[8] = output[8];
    step[9] = output[9];
    step[10] = _HalfBtf(cospi[32], output[10], cospi[32], output[11], cosBit);
    step[11] = _HalfBtf(cospi[32], output[10], -cospi[32], output[11], cosBit);
    step[12] = output[12];
    step[13] = output[13];
    step[14] = _HalfBtf(cospi[32], output[14], cospi[32], output[15], cosBit);
    step[15] = _HalfBtf(cospi[32], output[14], -cospi[32], output[15], cosBit);
    output[0] = step[0];
    output[1] = -step[8];
    output[2] = step[12];
    output[3] = -step[4];
    output[4] = step[6];
    output[5] = -step[14];
    output[6] = step[10];
    output[7] = -step[2];
    output[8] = step[3];
    output[9] = -step[11];
    output[10] = step[15];
    output[11] = -step[7];
    output[12] = step[5];
    output[13] = -step[13];
    output[14] = step[9];
    output[15] = -step[1];
  }
}
