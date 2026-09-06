using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Derived block and transform relations from the AV1 specification, kept beside the raw lookup
/// tables in <see cref="Av1StructureTables"/>. These are libaom's small inline helpers: the tables
/// alone do not say, for instance, which transform-type set a transform size may signal.
/// </summary>
internal static class Av1Structure {

  /// <summary>Transform-type classes (TX_CLASS): 2D, vertical-only or horizontal-only.</summary>
  public const int TxClass2d = 0;
  public const int TxClassHorizontal = 1;
  public const int TxClassVertical = 2;

  /// <summary>libaom <c>tx_type_to_class</c>: the V_* types are vertical, the H_* types horizontal.</summary>
  private static readonly byte[] _TX_TYPE_TO_CLASS = [
    TxClass2d, TxClass2d, TxClass2d, TxClass2d, TxClass2d, TxClass2d, TxClass2d, TxClass2d,
    TxClass2d, TxClass2d, TxClassVertical, TxClassHorizontal, TxClassVertical, TxClassHorizontal,
    TxClassVertical, TxClassHorizontal,
  ];

  /// <summary>libaom <c>av1_num_ext_tx_set</c>: transform types in each set.</summary>
  private static readonly int[] _EXT_TX_SET_SIZE = [1, 2, 5, 7, 12, 16];

  /// <summary>libaom <c>av1_ext_tx_inv</c>: coded index to TX_TYPE, per set type.</summary>
  private static readonly byte[][] _EXT_TX_INVERSE = [
    [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    [9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    [9, 0, 3, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    [9, 0, 10, 11, 3, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    [9, 10, 11, 0, 1, 2, 4, 5, 3, 6, 7, 8, 0, 0, 0, 0],
    [9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 4, 5, 3, 6, 7, 8],
  ];

  /// <summary>libaom <c>av1_ext_tx_used</c>: which transform types a set type admits.</summary>
  private static readonly byte[][] _EXT_TX_USED = [
    [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    [1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0],
    [1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0],
    [1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0],
    [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0],
    [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
  ];

  /// <summary>libaom <c>ext_tx_set_index</c>, intra row: set type to the CDF's set index.</summary>
  private static readonly int[] _INTRA_EXT_TX_SET_INDEX = [0, -1, 2, 1, -1, -1];

  /// <summary>libaom <c>fimode_to_intradir</c>: the intra direction a filter-intra mode stands in for.</summary>
  private static readonly byte[] _FILTER_INTRA_TO_DIRECTION = [
    (byte)Av1PredictionMode.DcPred, (byte)Av1PredictionMode.VPred, (byte)Av1PredictionMode.HPred,
    (byte)Av1PredictionMode.D157Pred, (byte)Av1PredictionMode.DcPred,
  ];

  /// <summary>libaom <c>bsize_to_tx_size_depth_table</c> minus one: the tx_size CDF category.</summary>
  private static readonly byte[] _TX_SIZE_CATEGORY_PLUS_ONE = [
    0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 4, 4, 4, 2, 2, 3, 3, 4, 4,
  ];

  /// <summary>libaom <c>tx_mode_to_biggest_tx_size</c>.</summary>
  private static readonly byte[] _TX_MODE_TO_BIGGEST = [
    (byte)Av1TxSize.Tx4x4, (byte)Av1TxSize.Tx64x64, (byte)Av1TxSize.Tx64x64,
  ];

  public static int TxClassOf(Av1TxType txType) => _TX_TYPE_TO_CLASS[(int)txType];

  /// <summary>The intra direction a filter-intra mode stands in for when choosing a transform type.</summary>
  public static Av1PredictionMode FilterIntraAsIntraMode(Av1FilterIntraMode mode) =>
    (Av1PredictionMode)_FILTER_INTRA_TO_DIRECTION[(int)mode];

  /// <summary>libaom <c>av1_get_adjusted_tx_size</c>: only 32 coefficients per dimension are coded.</summary>
  public static Av1TxSize AdjustedTxSize(Av1TxSize txSize) => txSize switch {
    Av1TxSize.Tx64x64 or Av1TxSize.Tx64x32 or Av1TxSize.Tx32x64 => Av1TxSize.Tx32x32,
    Av1TxSize.Tx64x16 => Av1TxSize.Tx32x16,
    Av1TxSize.Tx16x64 => Av1TxSize.Tx16x32,
    _ => txSize,
  };

  /// <summary>libaom <c>get_txsize_entropy_ctx</c>: which of the five coefficient CDF sets to use.</summary>
  public static int TxSizeEntropyContext(Av1TxSize txSize) =>
    (Av1StructureTables.TxSizeSquare[(int)txSize] + Av1StructureTables.TxSizeSquareUp[(int)txSize] + 1) >> 1;

  /// <summary>libaom <c>av1_get_tx_scale</c>: the extra right shift large transforms carry.</summary>
  public static int TxScale(Av1TxSize txSize) {
    var pixels = Av1StructureTables.TxWidth[(int)txSize] * Av1StructureTables.TxHeight[(int)txSize];
    return (pixels > 256 ? 1 : 0) + (pixels > 1024 ? 1 : 0);
  }

  /// <summary>libaom <c>av1_get_ext_tx_set_type</c> for an intra block.</summary>
  public static int IntraExtTxSetType(Av1TxSize txSize, bool reducedTxSet) {
    var sqrUp = (Av1TxSize)Av1StructureTables.TxSizeSquareUp[(int)txSize];
    if (sqrUp > Av1TxSize.Tx32x32)
      return 0; // EXT_TX_SET_DCTONLY
    if (sqrUp == Av1TxSize.Tx32x32)
      return 0;
    if (reducedTxSet)
      return 2; // EXT_TX_SET_DTT4_IDTX

    // libaom av1_ext_tx_set_lookup[0]: 16x16 drops the 1D-DCT members.
    return Av1StructureTables.TxSizeSquare[(int)txSize] == (byte)Av1TxSize.Tx16x16 ? 2 : 3;
  }

  public static int ExtTxSetSize(int setType) => _EXT_TX_SET_SIZE[setType];

  public static int IntraExtTxSetIndex(int setType) => _INTRA_EXT_TX_SET_INDEX[setType];

  public static bool ExtTxUsed(int setType, Av1TxType txType) => _EXT_TX_USED[setType][(int)txType] != 0;

  public static Av1TxType ExtTxInverse(int setType, int codedIndex) =>
    (Av1TxType)_EXT_TX_INVERSE[setType][codedIndex];

  /// <summary>libaom <c>bsize_to_tx_size_cat</c>.</summary>
  public static int TxSizeCategory(Av1BlockSize blockSize) => _TX_SIZE_CATEGORY_PLUS_ONE[(int)blockSize] - 1;

  /// <summary>libaom <c>bsize_to_max_depth</c>: how far the transform tree may descend.</summary>
  public static int MaxTxDepth(Av1BlockSize blockSize) {
    var txSize = (Av1TxSize)Av1StructureTables.MaxTxSizeRect[(int)blockSize];
    var depth = 0;
    while (depth < Av1Constants.MaxTxDepth && txSize != Av1TxSize.Tx4x4) {
      ++depth;
      txSize = (Av1TxSize)Av1StructureTables.SubTxSize[(int)txSize];
    }
    return depth;
  }

  /// <summary>libaom <c>depth_to_tx_size</c>.</summary>
  public static Av1TxSize DepthToTxSize(int depth, Av1BlockSize blockSize) {
    var txSize = (Av1TxSize)Av1StructureTables.MaxTxSizeRect[(int)blockSize];
    for (var d = 0; d < depth; ++d)
      txSize = (Av1TxSize)Av1StructureTables.SubTxSize[(int)txSize];
    return txSize;
  }

  /// <summary>libaom <c>tx_size_from_tx_mode</c>, used when the frame does not signal per-block sizes.</summary>
  public static Av1TxSize TxSizeFromTxMode(Av1BlockSize blockSize, Av1TxMode txMode) {
    var largest = (Av1TxSize)_TX_MODE_TO_BIGGEST[(int)txMode];
    var maxRect = (Av1TxSize)Av1StructureTables.MaxTxSizeRect[(int)blockSize];
    if (blockSize == Av1BlockSize.Block4x4)
      return (Av1TxSize)Math.Min((int)maxRect, (int)largest);

    return (Av1TxSize)Av1StructureTables.TxSizeSquareUp[(int)maxRect] <= largest ? maxRect : largest;
  }

  /// <summary>libaom <c>block_signals_txsize</c>: only 4x4 has nothing to choose.</summary>
  public static bool BlockSignalsTxSize(Av1BlockSize blockSize) => blockSize > Av1BlockSize.Block4x4;

  /// <summary>AV1 5.11.38 get_plane_residual_size: the block size a plane sees after subsampling.</summary>
  public static Av1BlockSize PlaneResidualSize(Av1BlockSize blockSize, int subX, int subY) =>
    (Av1BlockSize)Av1StructureTables.SubsampledSize[((int)blockSize * 2 + subX) * 2 + subY];

  /// <summary>AV1 5.11.5 is_chroma_reference: whether this block carries the chroma for its area.</summary>
  public static bool IsChromaReference(int miRow, int miCol, Av1BlockSize blockSize, int subX, int subY) {
    var bw = Av1StructureTables.MiSizeWide[(int)blockSize];
    var bh = Av1StructureTables.MiSizeHigh[(int)blockSize];
    var rowOk = (miRow & 1) != 0 || (bh & 1) == 0 || subY == 0;
    var colOk = (miCol & 1) != 0 || (bw & 1) == 0 || subX == 0;
    return rowOk && colOk;
  }

  /// <summary>AV1 7.11.2: whether a mode extrapolates along an angle.</summary>
  public static bool IsDirectionalMode(Av1PredictionMode mode) =>
    mode >= Av1PredictionMode.VPred && mode <= Av1PredictionMode.D67Pred;

  /// <summary>libaom <c>get_uv_mode</c>: CFL predicts DC and then adds the luma contribution.</summary>
  public static Av1PredictionMode UvModeAsIntraMode(Av1PredictionMode uvMode) =>
    uvMode == Av1PredictionMode.UvCflPred ? Av1PredictionMode.DcPred : uvMode;
}
