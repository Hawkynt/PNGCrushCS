// AV1 constant tables transcribed from the libaom reference implementation (BSD 2-Clause,
// Alliance for Open Media). See THIRD_PARTY_NOTICES.md. Generated values are copied verbatim:
// a re-derived table is simply a wrong one.

using System;

namespace FileFormat.Avif.Codec;

/// <summary>Structural lookup tables shared by the AV1 syntax and reconstruction paths, transcribed
/// from libaom's <c>av1/common/common_data.h</c>, <c>av1/common/blockd.h</c> and
/// <c>av1/common/blockd.c</c>. Block sizes are indexed by BLOCK_SIZE (22 values) and transform sizes
/// by TX_SIZE (19 values), in libaom's enumeration order.</summary>
internal static class Av1StructureTables {

  /// <summary>libaom <c>mi_size_wide</c>: block width in 4x4 units.</summary>
  internal static readonly byte[] MiSizeWide = [
    1, 1, 2, 2, 2, 4, 4, 4, 8, 8, 8, 16, 16, 16, 32, 32, 1, 4, 2, 8, 4, 16,
  ];

  /// <summary>libaom <c>mi_size_high</c>: block height in 4x4 units.</summary>
  internal static readonly byte[] MiSizeHigh = [
    1, 2, 1, 2, 4, 2, 4, 8, 4, 8, 16, 8, 16, 32, 16, 32, 4, 1, 8, 2, 16, 4,
  ];

  /// <summary>libaom <c>mi_size_wide_log2</c>: log2 of the block width in 4x4 units.</summary>
  internal static readonly byte[] MiSizeWideLog2 = [
    0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 0, 2, 1, 3, 2, 4,
  ];

  /// <summary>libaom <c>mi_size_high_log2</c>: log2 of the block height in 4x4 units.</summary>
  internal static readonly byte[] MiSizeHighLog2 = [
    0, 1, 0, 1, 2, 1, 2, 3, 2, 3, 4, 3, 4, 5, 4, 5, 2, 0, 3, 1, 4, 2,
  ];

  /// <summary>libaom <c>block_size_wide</c>: block width in luma samples.</summary>
  internal static readonly byte[] BlockWidth = [
    4, 4, 8, 8, 8, 16, 16, 16, 32, 32, 32, 64, 64, 64, 128, 128, 4, 16, 8, 32, 16, 64,
  ];

  /// <summary>libaom <c>block_size_high</c>: block height in luma samples.</summary>
  internal static readonly byte[] BlockHeight = [
    4, 8, 4, 8, 16, 8, 16, 32, 16, 32, 64, 32, 64, 128, 64, 128, 16, 4, 32, 8, 64, 16,
  ];

  /// <summary>libaom <c>size_group_lookup</c>: BLOCK_SIZE_GROUPS index of a block size.</summary>
  internal static readonly byte[] SizeGroup = [
    0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 0, 0, 1, 1, 2, 2,
  ];

  /// <summary>libaom <c>num_pels_log2_lookup</c>: log2 of the block area.</summary>
  internal static readonly byte[] NumPelsLog2 = [
    4, 5, 5, 6, 7, 7, 8, 9, 9, 10, 11, 11, 12, 13, 13, 14, 6, 6, 8, 8, 10, 10,
  ];

  /// <summary>libaom <c>subsize_lookup</c>: subsize_lookup[EXT_PARTITION_TYPES=10][SQR_BLOCK_SIZES=6].</summary>
  internal static readonly byte[] PartitionSubsize = [
    0, 3, 6, 9, 12, 15, 255, 2, 5, 8, 11, 14, 255, 1, 4, 7, 10, 13, 255, 0, 3, 6,
    9, 12, 255, 255, 5, 8, 11, 14, 255, 255, 5, 8, 11, 14, 255, 255, 4, 7, 10, 13, 255, 255,
    4, 7, 10, 13, 255, 255, 17, 19, 21, 255, 255, 255, 16, 18, 20, 255,
  ];

  /// <summary>libaom <c>max_txsize_lookup</c>: largest square transform fitting a block size.</summary>
  internal static readonly byte[] MaxTxSizeSquare = [
    0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 4, 0, 0, 1, 1, 2, 2,
  ];

  /// <summary>libaom <c>max_txsize_rect_lookup</c>: largest rectangular transform fitting a block size.</summary>
  internal static readonly byte[] MaxTxSizeRect = [
    0, 5, 6, 1, 7, 8, 2, 9, 10, 3, 11, 12, 4, 4, 4, 4, 13, 14, 15, 16, 17, 18,
  ];

  /// <summary>libaom <c>sub_tx_size_map</c>: one transform-split step.</summary>
  internal static readonly byte[] SubTxSize = [
    0, 0, 1, 2, 3, 0, 0, 1, 1, 2, 2, 3, 3, 5, 6, 7, 8, 9, 10,
  ];

  /// <summary>libaom <c>txsize_horz_map</c>: square transform matching the width.</summary>
  internal static readonly byte[] TxSizeHorzMap = [
    0, 1, 2, 3, 4, 0, 1, 1, 2, 2, 3, 3, 4, 0, 2, 1, 3, 2, 4,
  ];

  /// <summary>libaom <c>txsize_vert_map</c>: square transform matching the height.</summary>
  internal static readonly byte[] TxSizeVertMap = [
    0, 1, 2, 3, 4, 1, 0, 2, 1, 3, 2, 4, 3, 2, 0, 3, 1, 4, 2,
  ];

  /// <summary>libaom <c>tx_size_wide</c>: transform width in samples.</summary>
  internal static readonly int[] TxWidth = [
    4, 8, 16, 32, 64, 4, 8, 8, 16, 16, 32, 32, 64, 4, 16, 8, 32, 16, 64,
  ];

  /// <summary>libaom <c>tx_size_high</c>: transform height in samples.</summary>
  internal static readonly int[] TxHeight = [
    4, 8, 16, 32, 64, 8, 4, 16, 8, 32, 16, 64, 32, 16, 4, 32, 8, 64, 16,
  ];

  /// <summary>libaom <c>tx_size_wide_log2</c>: log2 of the transform width.</summary>
  internal static readonly int[] TxWidthLog2 = [
    2, 3, 4, 5, 6, 2, 3, 3, 4, 4, 5, 5, 6, 2, 4, 3, 5, 4, 6,
  ];

  /// <summary>libaom <c>tx_size_high_log2</c>: log2 of the transform height.</summary>
  internal static readonly int[] TxHeightLog2 = [
    2, 3, 4, 5, 6, 3, 2, 4, 3, 5, 4, 6, 5, 4, 2, 5, 3, 6, 4,
  ];

  /// <summary>libaom <c>tx_size_wide_unit</c>: transform width in 4x4 units.</summary>
  internal static readonly int[] TxWidthUnit = [
    1, 2, 4, 8, 16, 1, 2, 2, 4, 4, 8, 8, 16, 1, 4, 2, 8, 4, 16,
  ];

  /// <summary>libaom <c>tx_size_high_unit</c>: transform height in 4x4 units.</summary>
  internal static readonly int[] TxHeightUnit = [
    1, 2, 4, 8, 16, 2, 1, 4, 2, 8, 4, 16, 8, 4, 1, 8, 2, 16, 4,
  ];

  /// <summary>libaom <c>txsize_to_bsize</c>: block size covering a transform size.</summary>
  internal static readonly byte[] TxSizeToBlockSize = [
    0, 3, 6, 9, 12, 1, 2, 4, 5, 7, 8, 10, 11, 16, 17, 18, 19, 20, 21,
  ];

  /// <summary>libaom <c>txsize_sqr_map</c>: square transform of the smaller dimension.</summary>
  internal static readonly byte[] TxSizeSquare = [
    0, 1, 2, 3, 4, 0, 0, 1, 1, 2, 2, 3, 3, 0, 0, 1, 1, 2, 2,
  ];

  /// <summary>libaom <c>txsize_sqr_up_map</c>: square transform of the larger dimension.</summary>
  internal static readonly byte[] TxSizeSquareUp = [
    0, 1, 2, 3, 4, 1, 1, 2, 2, 3, 3, 4, 4, 2, 2, 3, 3, 4, 4,
  ];

  /// <summary>libaom <c>txsize_log2_minus4</c>: log2(area)/2 - 4, the coefficient CDF size index.</summary>
  internal static readonly sbyte[] TxSizeLog2Minus4 = [
    0, 2, 4, 6, 6, 1, 1, 3, 3, 5, 5, 6, 6, 2, 2, 4, 4, 5, 5,
  ];

  /// <summary>libaom <c>intra_mode_context</c>: above/left context class of an intra mode.</summary>
  internal static readonly byte[] IntraModeContext = [
    0, 1, 2, 3, 4, 4, 4, 4, 3, 0, 1, 2, 0,
  ];

  /// <summary>libaom <c>vtx_tab</c>: vertical 1D transform of each TX_TYPE.</summary>
  internal static readonly byte[] TxTypeVertical = [
    0, 1, 0, 1, 2, 0, 2, 1, 2, 3, 0, 3, 1, 3, 2, 3,
  ];

  /// <summary>libaom <c>htx_tab</c>: horizontal 1D transform of each TX_TYPE.</summary>
  internal static readonly byte[] TxTypeHorizontal = [
    0, 0, 1, 1, 0, 2, 2, 2, 1, 3, 3, 0, 3, 1, 3, 2,
  ];

  /// <summary>libaom <c>av1_ss_size_lookup</c>: av1_ss_size_lookup[BLOCK_SIZES_ALL][2][2].</summary>
  internal static readonly byte[] SubsampledSize = [
    0, 0, 0, 0, 1, 0, 255, 0, 2, 255, 0, 0, 3, 2, 1, 0, 4, 3, 255, 1, 5, 255,
    3, 2, 6, 5, 4, 3, 7, 6, 255, 4, 8, 255, 6, 5, 9, 8, 7, 6, 10, 9, 255, 7,
    11, 255, 9, 8, 12, 11, 10, 9, 13, 12, 255, 10, 14, 255, 12, 11, 15, 14, 13, 12, 16, 1,
    255, 1, 17, 255, 2, 2, 18, 4, 255, 16, 19, 255, 5, 17, 20, 7, 255, 18, 21, 255, 8, 19,
  ];

  /// <summary>libaom <c>_intra_mode_to_tx_type</c>: default transform type of an intra mode.</summary>
  internal static readonly byte[] IntraModeToTxType = [
    0, 1, 2, 0, 3, 1, 2, 2, 1, 3, 1, 2, 3,
  ];

  /// <summary>libaom <c>mode_to_angle_map</c>: base angle of each intra mode, in degrees.</summary>
  internal static readonly byte[] ModeToAngleMap = [
    0, 90, 180, 45, 135, 113, 157, 203, 67, 0, 0, 0, 0,
  ];

  /// <summary>libaom <c>partition_context_lookup[].above</c>.</summary>
  internal static readonly byte[] PartitionContextAbove = [
    31, 31, 30, 30, 30, 28, 28, 28, 24, 24, 24, 16, 16, 16, 0, 0, 31, 28, 30, 24, 28, 16,
  ];

  /// <summary>libaom <c>partition_context_lookup[].left</c>.</summary>
  internal static readonly byte[] PartitionContextLeft = [
    31, 30, 31, 30, 28, 30, 28, 24, 28, 24, 16, 24, 16, 0, 16, 0, 28, 31, 24, 30, 16, 28,
  ];
}
