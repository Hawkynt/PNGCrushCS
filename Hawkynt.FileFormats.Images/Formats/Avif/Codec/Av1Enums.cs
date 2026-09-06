namespace FileFormat.Avif.Codec;

/// <summary>AV1 block sizes (BLOCK_SIZE), in the enumeration order the specification and libaom
/// share. The tables in <see cref="Av1StructureTables"/> are indexed by these values.</summary>
internal enum Av1BlockSize {
  Block4x4 = 0, Block4x8, Block8x4, Block8x8, Block8x16, Block16x8, Block16x16, Block16x32,
  Block32x16, Block32x32, Block32x64, Block64x32, Block64x64, Block64x128, Block128x64,
  Block128x128, Block4x16, Block16x4, Block8x32, Block32x8, Block16x64, Block64x16,
  Invalid = 255,
}

/// <summary>AV1 transform sizes (TX_SIZE).</summary>
internal enum Av1TxSize {
  Tx4x4 = 0, Tx8x8, Tx16x16, Tx32x32, Tx64x64, Tx4x8, Tx8x4, Tx8x16, Tx16x8, Tx16x32, Tx32x16,
  Tx32x64, Tx64x32, Tx4x16, Tx16x4, Tx8x32, Tx32x8, Tx16x64, Tx64x16,
}

/// <summary>AV1 transform types (TX_TYPE). The vertical component is listed first, matching the
/// specification's naming: <c>AdstDct</c> applies ADST down the columns and DCT along the rows.</summary>
internal enum Av1TxType {
  DctDct = 0, AdstDct, DctAdst, AdstAdst, FlipAdstDct, DctFlipAdst, FlipAdstFlipAdst,
  AdstFlipAdst, FlipAdstAdst, IdtxIdtx, DctIdtx, IdtxDct, AdstIdtx, IdtxAdst, FlipAdstIdtx,
  IdtxFlipAdst,
}

/// <summary>The 1D transforms a <see cref="Av1TxType"/> decomposes into.</summary>
internal enum Av1TxType1d { Dct = 0, Adst = 1, FlipAdst = 2, Identity = 3 }

/// <summary>AV1 intra prediction modes. <c>UvCfl</c> is a chroma-only mode.</summary>
internal enum Av1PredictionMode {
  DcPred = 0, VPred, HPred, D45Pred, D135Pred, D113Pred, D157Pred, D203Pred, D67Pred,
  SmoothPred, SmoothVPred, SmoothHPred, PaethPred, UvCflPred,
}

/// <summary>AV1 partition types for one level of the superblock quadtree.</summary>
internal enum Av1PartitionType {
  None = 0, Horizontal, Vertical, Split, HorizontalA, HorizontalB, VerticalA, VerticalB,
  Horizontal4, Vertical4,
}

/// <summary>AV1 recursive intra filter modes (FILTER_INTRA_MODE).</summary>
internal enum Av1FilterIntraMode { DcPred = 0, VPred, HPred, D157Pred, Paeth }

/// <summary>AV1 loop restoration types, in the order the frame header signals them.</summary>
internal enum Av1RestorationType { None = 0, Switchable, Wiener, SgrProj }

/// <summary>Constants shared across the AV1 codec, named after the specification.</summary>
internal static class Av1Constants {

  public const int MaxTxDepth = 2;
  public const int MaxTxSize = 64;
  public const int MaxSbSize = 128;
  public const int MiSizeLog2 = 2;
  public const int IntraModes = 13;
  public const int UvIntraModes = 14;
  public const int TxTypes = 16;
  public const int TxSizesAll = 19;
  public const int BlockSizesAll = 22;

  /// <summary>Number of quantiser-indexed coefficient CDF sets (TOKEN_CDF_Q_CTXS).</summary>
  public const int TokenCdfQContexts = 4;

  public const int SigCoefContexts = 42;
  public const int SigCoefContextsEob = 4;
  public const int LevelContexts = 21;
  public const int TxbSkipContexts = 13;
  public const int EobCoefContexts = 9;
  public const int DcSignContexts = 3;
  public const int NumBaseLevels = 2;
  public const int BrCdfSize = 4;
  public const int CoeffBaseRange = 12;
  public const int MaxBaseRangeExtent = 4;
  public const int SigRefDiffOffsetNum = 5;

  /// <summary>Maximum absolute coefficient level after the Golomb suffix (spec 5.11.39).</summary>
  public const int MaxTxbLevel = 1 << 20;

  public const int MaxTileWidth = 4096;
  public const int MaxTileArea = 4096 * 2304;
  public const int MaxTileRows = 64;
  public const int MaxTileCols = 64;

  public const int RestorationTileSizeMax = 256;
  public const int SgrProjParams = 16;
  public const int SgrProjSgrBits = 4;
  public const int SgrProjPrecBits = 7;
  public const int SgrProjBits = 4;
  public const int WienerCoeffs = 3;

  public const int CdefVeryLarge = 0x4000;
}
