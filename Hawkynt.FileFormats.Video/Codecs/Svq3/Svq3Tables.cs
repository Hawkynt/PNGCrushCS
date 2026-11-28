using System;

namespace FileFormat.Codecs.Svq3;

/// <summary>
/// SVQ3's constant tables and the H.264 ones it borrows, transcribed mechanically from FFmpeg's
/// <c>libavcodec/svq3.c</c>, <c>h264data.c</c> and <c>mathtables.c</c> (LGPL-2.1-or-later) by a script
/// that parsed the C initialisers. Multi-dimensional tables are flattened in C order.
/// </summary>
internal static class Svq3Tables {

  /// <summary><c>svq3_scan</c>: the dual scan of an intra 4x4 block at fine quantisers.</summary>
  internal static ReadOnlySpan<byte> Svq3Scan => [0, 1, 2, 6, 10, 3, 7, 11, 4, 8, 5, 9, 12, 13, 14, 15];

  /// <summary><c>luma_dc_zigzag_scan</c>.</summary>
  internal static ReadOnlySpan<byte> LumaDcZigzagScan => [0, 16, 32, 128, 48, 64, 80, 96, 144, 160, 176, 192, 112, 208, 224, 240];

  /// <summary><c>svq3_pred_0[25][2]</c>: a 4x4 mode code as two ranks.</summary>
  internal static readonly byte[] Prediction0 = [
    0, 0, 1, 0, 0, 1, 0, 2, 1, 1, 2, 0, 3, 0, 2, 1,
    1, 2, 0, 3, 0, 4, 1, 3, 2, 2, 3, 1, 4, 0, 4, 1,
    3, 2, 2, 3, 1, 4, 2, 4, 3, 3, 4, 2, 4, 3, 3, 4,
    4, 4,
  ];

  /// <summary><c>svq3_pred_1[6][6][5]</c>: rank to mode, by the modes above and to the left.</summary>
  internal static readonly sbyte[] Prediction1 = [
    2, -1, -1, -1, -1, 2, 1, -1, -1, -1, 1, 2, -1, -1, -1, 2,
    1, -1, -1, -1, 1, 2, -1, -1, -1, 1, 2, -1, -1, -1, 0, 2,
    -1, -1, -1, 0, 2, 1, 4, 3, 0, 1, 2, 4, 3, 0, 2, 1,
    4, 3, 2, 0, 1, 3, 4, 0, 4, 2, 1, 3, 2, 0, -1, -1,
    -1, 2, 1, 0, 4, 3, 1, 2, 4, 0, 3, 2, 1, 0, 4, 3,
    2, 1, 4, 3, 0, 1, 2, 4, 0, 3, 2, 0, -1, -1, -1, 2,
    0, 1, 4, 3, 1, 2, 0, 4, 3, 2, 1, 0, 4, 3, 2, 1,
    3, 4, 0, 2, 4, 1, 0, 3, 0, 2, -1, -1, -1, 0, 2, 1,
    3, 4, 1, 2, 3, 0, 4, 2, 0, 1, 3, 4, 2, 1, 3, 0,
    4, 2, 0, 4, 3, 1, 0, 2, -1, -1, -1, 0, 2, 4, 1, 3,
    1, 4, 2, 0, 3, 4, 2, 0, 1, 3, 2, 0, 1, 4, 3, 4,
    2, 1, 0, 3,
  ];

  /// <summary><c>svq3_dct_tables[2][16]</c>'s runs.</summary>
  internal static readonly byte[] DctRun = [
    0, 0, 1, 2, 0, 3, 4, 5, 0, 1, 2, 6, 7, 8, 9, 0,
    0, 0, 1, 0, 2, 0, 0, 0, 3, 4, 1, 1, 0, 0, 0, 0,
  ];

  /// <summary><c>svq3_dct_tables[2][16]</c>'s levels.</summary>
  internal static readonly byte[] DctLevel = [
    0, 1, 1, 1, 2, 1, 1, 1, 3, 2, 2, 1, 1, 1, 1, 4,
    0, 1, 1, 2, 1, 3, 4, 5, 1, 1, 2, 3, 6, 7, 8, 9,
  ];

  /// <summary><c>svq3_dequant_coeff</c>.</summary>
  internal static readonly uint[] DequantCoeff = [
    3881, 4351, 4890, 5481, 6154, 6914, 7761, 8718, 9781, 10987, 12339, 13828, 15523, 17435, 19561, 21873,
    24552, 27656, 30847, 34870, 38807, 43747, 49103, 54683, 61694, 68745, 77615, 89113, 100253, 109366, 126635, 141533,
  ];

  /// <summary><c>ff_h264_golomb_to_intra4x4_cbp</c>.</summary>
  internal static readonly byte[] GolombToIntra4x4Cbp = [
    47, 31, 15, 0, 23, 27, 29, 30, 7, 11, 13, 14, 39, 43, 45, 46,
    16, 3, 5, 10, 12, 19, 21, 26, 28, 35, 37, 42, 44, 1, 2, 4,
    8, 17, 18, 20, 24, 6, 9, 22, 25, 32, 33, 34, 36, 40, 38, 41,
  ];

  /// <summary><c>ff_h264_golomb_to_inter_cbp</c>.</summary>
  internal static readonly byte[] GolombToInterCbp = [
    0, 16, 1, 2, 4, 8, 32, 3, 5, 10, 12, 15, 47, 7, 11, 13,
    14, 6, 9, 31, 35, 37, 42, 44, 33, 34, 36, 40, 39, 43, 45, 46,
    17, 18, 20, 24, 19, 21, 26, 28, 23, 27, 29, 30, 22, 25, 38, 41,
  ];

  /// <summary><c>ff_h264_chroma_dc_scan</c>.</summary>
  internal static ReadOnlySpan<byte> ChromaDcScan => [0, 16, 32, 48];

  /// <summary><c>ff_h264_i_mb_type_info</c>'s prediction modes.</summary>
  internal static readonly sbyte[] IntraMacroblockPredictionMode = [
    -1, 2, 1, 0, 3, 2, 1, 0, 3, 2, 1, 0, 3, 2, 1, 0,
    3, 2, 1, 0, 3, 2, 1, 0, 3, -1,
  ];

  /// <summary><c>ff_h264_i_mb_type_info</c>'s coded block patterns.</summary>
  internal static readonly sbyte[] IntraMacroblockCbp = [
    -1, 0, 0, 0, 0, 16, 16, 16, 16, 32, 32, 32, 32, 15, 15, 15,
    15, 31, 31, 31, 31, 47, 47, 47, 47, -1,
  ];

  /// <summary><c>ff_h264_dequant4_coeff_init[6][3]</c>.</summary>
  internal static readonly byte[] Dequant4CoeffInit = [
    10, 13, 16, 11, 14, 18, 13, 16, 20, 14, 18, 23, 16, 20, 25, 18,
    23, 29,
  ];

  /// <summary><c>ff_h264_quant_rem6</c>, the eight-bit range.</summary>
  internal static readonly byte[] QuantRem6 = [
    0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0, 1, 2, 3,
    4, 5, 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0, 1,
    2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5,
    0, 1, 2, 3,
  ];

  /// <summary><c>ff_h264_quant_div6</c>, the eight-bit range.</summary>
  internal static readonly byte[] QuantDiv6 = [
    0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2,
    2, 2, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 5, 5,
    5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7,
    8, 8, 8, 8,
  ];

  /// <summary><c>ff_h264_chroma_qp[0]</c>: <c>CHROMA_QP_TABLE_END(8)</c>.</summary>
  internal static readonly byte[] ChromaQp = [
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 29, 30,
    31, 32, 32, 33, 34, 34, 35, 35, 36, 36, 37, 37, 37, 38, 38, 38,
    39, 39, 39, 39,
  ];

  /// <summary><c>ff_zigzag_scan</c>: H.264's 4x4 zigzag.</summary>
  internal static ReadOnlySpan<byte> ZigzagScan => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
}
