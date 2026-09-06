using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Decodes one AV1 tile of a key frame: the superblock partition tree, intra mode information,
/// residual coefficients, and the reconstruction that follows each transform block.
/// </summary>
/// <remarks>
/// Only intra frames reach this class. AVIF still pictures are always key frames, so the inter
/// paths of AV1 5.11 have no counterpart here and anything that would need them is refused by name
/// rather than approximated.
/// </remarks>
internal sealed partial class Av1TileDecoder {

  private const int _MI_SIZE = 4;
  private const int _MI_SIZE_LOG2 = 2;
  private const int _MAX_ANGLE_DELTA = 3;
  private const int _ANGLE_STEP = 3;
  private const int _DELTA_Q_SMALL = 3;
  private const int _DELTA_LF_SMALL = 3;
  private const int _FRAME_LF_COUNT = 4;

  private readonly Av1SequenceHeader _seq;
  private readonly Av1FrameHeader _fh;
  private readonly Av1DecodedFrame _frame;
  private readonly Av1CdfContext _frameCdf;
  private readonly Av1LoopRestorationUnits? _restoration;
  private readonly int _numPlanes;
  private readonly int _bitDepth;
  private readonly int _miCols;
  private readonly int _miRows;
  private readonly int _sbSize4;
  private readonly int _sbMask;
  private readonly Av1BlockSize _sbSize;

  private readonly byte[] _abovePartitionContext;
  private readonly byte[] _leftPartitionContext;
  private readonly byte[][] _aboveEntropyContext;
  private readonly byte[][] _leftEntropyContext;
  private readonly byte[] _aboveTxfmContext;
  private readonly byte[] _leftTxfmContext;
  private readonly bool[][] _blockDecoded;
  private readonly int _blockDecodedStride;

  // Loop-restoration references carried across units within a tile (AV1 5.11.57).
  private readonly int[][] _refLrWiener;
  private readonly int[][] _refSgrXqd;

  private Av1SymbolDecoder _reader = null!;
  private Av1CdfContext _cdf = null!;
  private int _tileMiRowStart;
  private int _tileMiRowEnd;
  private int _tileMiColStart;
  private int _tileMiColEnd;

  // Block-scoped mode information, mirroring the spec's frame-level variables.
  private int _miRow;
  private int _miCol;
  private Av1BlockSize _miSize;
  private bool _skip;
  private bool _hasChroma;
  private bool _availU;
  private bool _availL;
  private bool _availUChroma;
  private bool _availLChroma;
  private Av1PredictionMode _yMode;
  private Av1PredictionMode _uvMode;
  private int _angleDeltaY;
  private int _angleDeltaUv;
  private bool _useFilterIntra;
  private Av1FilterIntraMode _filterIntraMode;
  private int _cflAlphaU;
  private int _cflAlphaV;
  private Av1TxSize _txSize;
  private int _segmentId;
  private int _currentQIndex;
  private bool _readDeltas;
  private readonly int[] _deltaLf = new int[_FRAME_LF_COUNT];
  private int _maxLumaWidth;
  private int _maxLumaHeight;

  public Av1TileDecoder(
    Av1SequenceHeader seq,
    Av1FrameHeader fh,
    Av1DecodedFrame frame,
    Av1CdfContext frameCdf,
    Av1LoopRestorationUnits? restoration
  ) {
    this._seq = seq;
    this._fh = fh;
    this._frame = frame;
    this._frameCdf = frameCdf;
    this._restoration = restoration;
    this._numPlanes = frame.NumPlanes;
    this._bitDepth = seq.BitDepth;
    this._miCols = frame.MiCols;
    this._miRows = frame.MiRows;
    this._sbSize = seq.Use128x128Superblock ? Av1BlockSize.Block128x128 : Av1BlockSize.Block64x64;
    this._sbSize4 = Av1StructureTables.MiSizeWide[(int)this._sbSize];
    this._sbMask = this._sbSize4 - 1;

    this._abovePartitionContext = new byte[this._miCols + 32];
    this._leftPartitionContext = new byte[this._sbSize4 + 32];
    this._aboveTxfmContext = new byte[this._miCols + 32];
    this._leftTxfmContext = new byte[this._sbSize4 + 32];

    this._aboveEntropyContext = new byte[this._numPlanes][];
    this._leftEntropyContext = new byte[this._numPlanes][];
    for (var plane = 0; plane < this._numPlanes; ++plane) {
      this._aboveEntropyContext[plane] = new byte[this._miCols + 32];
      this._leftEntropyContext[plane] = new byte[this._sbSize4 + 32];
    }

    // BlockDecoded carries a one-sample border on the top and left, and one extra column past the
    // right edge, because the intra predictor asks about the block above-right and below-left.
    this._blockDecodedStride = this._sbSize4 + 3;
    this._blockDecoded = new bool[this._numPlanes][];
    for (var plane = 0; plane < this._numPlanes; ++plane)
      this._blockDecoded[plane] = new bool[this._blockDecodedStride * this._blockDecodedStride];

    // One reference per plane, per pass, per coefficient (AV1 5.11.2 RefLrWiener).
    this._refLrWiener = [new int[6], new int[6], new int[6]];
    this._refSgrXqd = [new int[2], new int[2], new int[2]];
  }

  /// <summary>The CDF state the tile finished with, which a frame-end update would adopt.</summary>
  public Av1CdfContext TileCdf => this._cdf;

  /// <summary>AV1 5.11.2 decode_tile(): decodes one tile into the frame buffers.</summary>
  public void DecodeTile(byte[] data, int offset, int length, int tileCol, int tileRow) {
    this._reader = new(data, offset, length, this._fh.DisableCdfUpdate);
    this._cdf = this._frameCdf.Clone();

    var colStartSb = this._fh.TileColStarts[tileCol];
    var colEndSb = this._fh.TileColStarts[tileCol + 1];
    var rowStartSb = this._fh.TileRowStarts[tileRow];
    var rowEndSb = this._fh.TileRowStarts[tileRow + 1];

    this._tileMiColStart = colStartSb * this._sbSize4;
    this._tileMiColEnd = Math.Min(this._miCols, colEndSb * this._sbSize4);
    this._tileMiRowStart = rowStartSb * this._sbSize4;
    this._tileMiRowEnd = Math.Min(this._miRows, rowEndSb * this._sbSize4);

    // AV1 5.11.2 clear_above_context(): the above contexts are per tile, not per frame.
    Array.Clear(this._abovePartitionContext);
    Array.Clear(this._aboveTxfmContext);
    foreach (var plane in this._aboveEntropyContext)
      Array.Clear(plane);

    Array.Clear(this._deltaLf);
    this._currentQIndex = this._fh.BaseQIndex;
    for (var plane = 0; plane < 3; ++plane) {
      this._refSgrXqd[plane][0] = _SGR_XQD_MID[0];
      this._refSgrXqd[plane][1] = _SGR_XQD_MID[1];
      for (var pass = 0; pass < 2; ++pass)
        for (var j = 0; j < Av1Constants.WienerCoeffs; ++j)
          this._refLrWiener[plane][pass * Av1Constants.WienerCoeffs + j] = _WIENER_TAPS_MID[j];
    }

    for (var miRow = this._tileMiRowStart; miRow < this._tileMiRowEnd; miRow += this._sbSize4) {
      Array.Clear(this._leftPartitionContext);
      Array.Clear(this._leftTxfmContext);
      foreach (var plane in this._leftEntropyContext)
        Array.Clear(plane);

      for (var miCol = this._tileMiColStart; miCol < this._tileMiColEnd; miCol += this._sbSize4) {
        this._readDeltas = this._fh.DeltaQPresent;
        this._ClearBlockDecodedFlags(miRow, miCol);
        this._ReadLoopRestoration(miRow, miCol, this._sbSize);
        this._DecodePartition(miRow, miCol, this._sbSize);
      }
    }
  }

  private void _ClearBlockDecodedFlags(int miRow, int miCol) {
    // AV1 5.11.3: samples above and to the left of the superblock count as decoded when they lie
    // inside the frame, which is what lets the first block of a superblock look upwards.
    for (var plane = 0; plane < this._numPlanes; ++plane) {
      var subX = this._frame.SubX[plane];
      var subY = this._frame.SubY[plane];
      var flags = this._blockDecoded[plane];
      Array.Clear(flags);

      var sbWidth4 = (this._tileMiColEnd - miCol) >> subX;
      var sbHeight4 = (this._tileMiRowEnd - miRow) >> subY;
      var lastX = this._sbSize4 >> subX;
      var lastY = this._sbSize4 >> subY;

      for (var y = -1; y <= lastY; ++y)
        for (var x = -1; x <= lastX; ++x) {
          var value = (y < 0 && x < sbWidth4) || (x < 0 && y < sbHeight4);
          flags[this._BlockDecodedIndex(y, x)] = value;
        }

      // The sample below the superblock's bottom-left corner belongs to the superblock row that
      // has not been decoded yet, whatever the tile bounds say.
      flags[this._BlockDecodedIndex(lastY, -1)] = false;
    }
  }

  private int _BlockDecodedIndex(int y, int x) => (y + 1) * this._blockDecodedStride + (x + 1);

  private void _DecodePartition(int miRow, int miCol, Av1BlockSize blockSize) {
    if (miRow >= this._miRows || miCol >= this._miCols)
      return;

    var num4x4 = Av1StructureTables.MiSizeWide[(int)blockSize];
    var halfBlock4x4 = num4x4 >> 1;
    var quarterBlock4x4 = halfBlock4x4 >> 1;
    var hasRows = miRow + halfBlock4x4 < this._miRows;
    var hasCols = miCol + halfBlock4x4 < this._miCols;

    var partition = blockSize < Av1BlockSize.Block8x8
      ? Av1PartitionType.None
      : this._ReadPartition(miRow, miCol, blockSize, hasRows, hasCols);

    var subSize = _PartitionSubsize(partition, blockSize);
    var splitSize = _PartitionSubsize(Av1PartitionType.Split, blockSize);

    switch (partition) {
      case Av1PartitionType.None:
        this._DecodeBlock(miRow, miCol, subSize);
        break;
      case Av1PartitionType.Horizontal:
        this._DecodeBlock(miRow, miCol, subSize);
        if (hasRows)
          this._DecodeBlock(miRow + halfBlock4x4, miCol, subSize);
        break;
      case Av1PartitionType.Vertical:
        this._DecodeBlock(miRow, miCol, subSize);
        if (hasCols)
          this._DecodeBlock(miRow, miCol + halfBlock4x4, subSize);
        break;
      case Av1PartitionType.Split:
        this._DecodePartition(miRow, miCol, subSize);
        this._DecodePartition(miRow, miCol + halfBlock4x4, subSize);
        this._DecodePartition(miRow + halfBlock4x4, miCol, subSize);
        this._DecodePartition(miRow + halfBlock4x4, miCol + halfBlock4x4, subSize);
        break;
      case Av1PartitionType.HorizontalA:
        this._DecodeBlock(miRow, miCol, splitSize);
        this._DecodeBlock(miRow, miCol + halfBlock4x4, splitSize);
        this._DecodeBlock(miRow + halfBlock4x4, miCol, subSize);
        break;
      case Av1PartitionType.HorizontalB:
        this._DecodeBlock(miRow, miCol, subSize);
        this._DecodeBlock(miRow + halfBlock4x4, miCol, splitSize);
        this._DecodeBlock(miRow + halfBlock4x4, miCol + halfBlock4x4, splitSize);
        break;
      case Av1PartitionType.VerticalA:
        this._DecodeBlock(miRow, miCol, splitSize);
        this._DecodeBlock(miRow + halfBlock4x4, miCol, splitSize);
        this._DecodeBlock(miRow, miCol + halfBlock4x4, subSize);
        break;
      case Av1PartitionType.VerticalB:
        this._DecodeBlock(miRow, miCol, subSize);
        this._DecodeBlock(miRow, miCol + halfBlock4x4, splitSize);
        this._DecodeBlock(miRow + halfBlock4x4, miCol + halfBlock4x4, splitSize);
        break;
      case Av1PartitionType.Horizontal4:
        for (var i = 0; i < 4; ++i) {
          var row = miRow + quarterBlock4x4 * i;
          if (i > 0 && row >= this._miRows)
            break;
          this._DecodeBlock(row, miCol, subSize);
        }
        break;
      case Av1PartitionType.Vertical4:
        for (var i = 0; i < 4; ++i) {
          var col = miCol + quarterBlock4x4 * i;
          if (i > 0 && col >= this._miCols)
            break;
          this._DecodeBlock(miRow, col, subSize);
        }
        break;
      default:
        throw new NotSupportedException($"AV1: partition type {partition} is not defined.");
    }

    this._UpdatePartitionContext(miRow, miCol, blockSize, subSize, splitSize, partition);
  }

  private static Av1BlockSize _PartitionSubsize(Av1PartitionType partition, Av1BlockSize blockSize) {
    // libaom subsize_lookup is indexed by the six square block sizes, whose BLOCK_SIZE values are
    // every third entry starting at BLOCK_4X4.
    var squareIndex = (int)blockSize / 3;
    return (Av1BlockSize)Av1StructureTables.PartitionSubsize[(int)partition * 6 + squareIndex];
  }

  private Av1PartitionType _ReadPartition(int miRow, int miCol, Av1BlockSize blockSize, bool hasRows, bool hasCols) {
    if (!hasRows && !hasCols)
      return Av1PartitionType.Split;

    var bsl = Av1StructureTables.MiSizeWideLog2[(int)blockSize] - Av1StructureTables.MiSizeWideLog2[(int)Av1BlockSize.Block8x8];
    var above = (this._abovePartitionContext[miCol] >> bsl) & 1;
    var left = (this._leftPartitionContext[miRow & this._sbMask] >> bsl) & 1;
    var context = (left * 2 + above) + bsl * 4;
    var offset = context * Av1CdfContext.PartitionStride;

    if (hasRows && hasCols) {
      var symbols = blockSize <= Av1BlockSize.Block8x8 ? 4 : blockSize == Av1BlockSize.Block128x128 ? 8 : 10;
      return (Av1PartitionType)this._reader.ReadSymbol(this._cdf.Partition, offset, symbols);
    }

    // Only one of the two halves is inside the frame, so the choice narrows to a split or the one
    // rectangular partition that codes the visible half. libaom folds every partition that would
    // still divide the block along the missing direction into a single "split" symbol
    // (partition_gather_vert_alike / partition_gather_horz_alike); the folded CDF is never adapted
    // because there is nothing persistent behind it.
    const int CDF_TOP = 1 << 15;
    Span<ushort> folded = stackalloc ushort[2];
    folded[1] = CDF_TOP;
    var full = this._cdf.Partition.AsSpan(offset, Av1CdfContext.PartitionStride);
    var is128 = blockSize == Av1BlockSize.Block128x128;

    if (hasCols) {
      var splitLike = _ElementProbability(full, (int)Av1PartitionType.Vertical)
        + _ElementProbability(full, (int)Av1PartitionType.Split)
        + _ElementProbability(full, (int)Av1PartitionType.HorizontalA)
        + _ElementProbability(full, (int)Av1PartitionType.VerticalA)
        + _ElementProbability(full, (int)Av1PartitionType.VerticalB)
        + (is128 ? 0 : _ElementProbability(full, (int)Av1PartitionType.Vertical4));
      folded[0] = (ushort)(CDF_TOP - splitLike);
      return this._reader.ReadSymbolNoUpdate(folded, 2) != 0 ? Av1PartitionType.Split : Av1PartitionType.Horizontal;
    }

    var horizontalSplitLike = _ElementProbability(full, (int)Av1PartitionType.Horizontal)
      + _ElementProbability(full, (int)Av1PartitionType.Split)
      + _ElementProbability(full, (int)Av1PartitionType.HorizontalA)
      + _ElementProbability(full, (int)Av1PartitionType.HorizontalB)
      + _ElementProbability(full, (int)Av1PartitionType.VerticalA)
      + (is128 ? 0 : _ElementProbability(full, (int)Av1PartitionType.Horizontal4));
    folded[0] = (ushort)(CDF_TOP - horizontalSplitLike);
    return this._reader.ReadSymbolNoUpdate(folded, 2) != 0 ? Av1PartitionType.Split : Av1PartitionType.Vertical;
  }

  private static int _ElementProbability(ReadOnlySpan<ushort> cdf, int symbol) =>
    symbol == 0 ? cdf[0] : cdf[symbol] - cdf[symbol - 1];

  private void _UpdatePartitionContext(
    int miRow, int miCol, Av1BlockSize blockSize,
    Av1BlockSize subSize, Av1BlockSize splitSize, Av1PartitionType partition
  ) {
    if (blockSize < Av1BlockSize.Block8x8)
      return;

    var half = Av1StructureTables.MiSizeWide[(int)blockSize] >> 1;
    switch (partition) {
      case Av1PartitionType.Split when blockSize != Av1BlockSize.Block8x8:
        // A split above 8x8 has already had its context written by the four sub-partitions.
        break;
      case Av1PartitionType.Split:
      case Av1PartitionType.None:
      case Av1PartitionType.Horizontal:
      case Av1PartitionType.Vertical:
      case Av1PartitionType.Horizontal4:
      case Av1PartitionType.Vertical4:
        this._SetPartitionContext(miRow, miCol, subSize, blockSize);
        break;
      case Av1PartitionType.HorizontalA:
        this._SetPartitionContext(miRow, miCol, splitSize, subSize);
        this._SetPartitionContext(miRow + half, miCol, subSize, subSize);
        break;
      case Av1PartitionType.HorizontalB:
        this._SetPartitionContext(miRow, miCol, subSize, subSize);
        this._SetPartitionContext(miRow + half, miCol, splitSize, subSize);
        break;
      case Av1PartitionType.VerticalA:
        this._SetPartitionContext(miRow, miCol, splitSize, subSize);
        this._SetPartitionContext(miRow, miCol + half, subSize, subSize);
        break;
      case Av1PartitionType.VerticalB:
        this._SetPartitionContext(miRow, miCol, subSize, subSize);
        this._SetPartitionContext(miRow, miCol + half, splitSize, subSize);
        break;
    }
  }

  private void _SetPartitionContext(int miRow, int miCol, Av1BlockSize subSize, Av1BlockSize blockSize) {
    var bw = Av1StructureTables.MiSizeWide[(int)blockSize];
    var bh = Av1StructureTables.MiSizeHigh[(int)blockSize];
    var above = Av1StructureTables.PartitionContextAbove[(int)subSize];
    var left = Av1StructureTables.PartitionContextLeft[(int)subSize];

    for (var i = 0; i < bw && miCol + i < this._abovePartitionContext.Length; ++i)
      this._abovePartitionContext[miCol + i] = above;
    for (var i = 0; i < bh; ++i)
      this._leftPartitionContext[(miRow & this._sbMask) + i] = left;
  }
}
