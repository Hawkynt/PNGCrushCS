using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Encodes one tile of the lossless still picture: the partition tree, the mode information, and
/// the residual of every 4x4 transform block.
/// </summary>
/// <remarks>
/// The encoder deliberately makes the same decisions everywhere — DC prediction, the largest
/// partition that fits, one transform size — because the point of this path is a stream other AV1
/// decoders accept, not a small one. Every symbol here has a counterpart in
/// <see cref="Av1TileDecoder"/>, and the two read the same CDF tables and context rules; where they
/// disagree, the picture comes back wrong rather than failing, so they are kept side by side.
/// </remarks>
internal sealed class Av1TileEncoder {

  private const int _MI_SIZE = 4;
  private const int _MI_SIZE_LOG2 = 2;
  private const int _TX_PAD_HOR = 4;
  private const int _TX_PAD_HOR_LOG2 = 2;
  private const int _COEFF_CONTEXT_BITS = 3;
  private const int _COEFF_CONTEXT_MASK = (1 << _COEFF_CONTEXT_BITS) - 1;
  private const int _MAX_BASE_BR_RANGE = Av1Constants.CoeffBaseRange + Av1Constants.NumBaseLevels + 1;

  private static readonly byte[] _SKIP_CONTEXTS = [
    1, 2, 2, 2, 3,
    2, 4, 4, 4, 5,
    2, 4, 4, 4, 5,
    2, 4, 4, 4, 5,
    3, 5, 5, 5, 6,
  ];

  private readonly Av1EncodedPlanes _source;
  private readonly Av1DecodedFrame _reconstruction;
  private readonly Av1CdfContext _frameCdf;
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
  private readonly bool[][] _blockDecoded;
  private readonly int _blockDecodedStride;
  private readonly byte[] _levels = new byte[(4 + _TX_PAD_HOR) * (4 + 4) + 16];
  private readonly int[] _residual = new int[16];
  private readonly int[] _quantized = new int[16];

  private Av1SymbolEncoder _writer = null!;
  private Av1CdfContext _cdf = null!;
  private int _tileMiRowStart;
  private int _tileMiRowEnd;
  private int _tileMiColStart;
  private int _tileMiColEnd;
  private int _miRow;
  private int _miCol;
  private Av1BlockSize _miSize;
  private bool _availU;
  private bool _availL;

  public Av1TileEncoder(Av1EncodedPlanes source, Av1DecodedFrame reconstruction, Av1CdfContext frameCdf) {
    this._source = source;
    this._reconstruction = reconstruction;
    this._frameCdf = frameCdf;
    this._numPlanes = reconstruction.NumPlanes;
    this._bitDepth = reconstruction.BitDepth;
    this._miCols = reconstruction.MiCols;
    this._miRows = reconstruction.MiRows;
    this._sbSize = Av1BlockSize.Block64x64;
    this._sbSize4 = Av1StructureTables.MiSizeWide[(int)this._sbSize];
    this._sbMask = this._sbSize4 - 1;

    this._abovePartitionContext = new byte[this._miCols + 32];
    this._leftPartitionContext = new byte[this._sbSize4 + 32];
    this._aboveEntropyContext = new byte[this._numPlanes][];
    this._leftEntropyContext = new byte[this._numPlanes][];
    for (var plane = 0; plane < this._numPlanes; ++plane) {
      this._aboveEntropyContext[plane] = new byte[this._miCols + 32];
      this._leftEntropyContext[plane] = new byte[this._sbSize4 + 32];
    }

    this._blockDecodedStride = this._sbSize4 + 3;
    this._blockDecoded = new bool[this._numPlanes][];
    for (var plane = 0; plane < this._numPlanes; ++plane)
      this._blockDecoded[plane] = new bool[this._blockDecodedStride * this._blockDecodedStride];
  }

  /// <summary>Encodes one tile and returns its arithmetic partition.</summary>
  public byte[] EncodeTile(int[] colStartsSb, int[] rowStartsSb, int tileCol, int tileRow, bool disableCdfUpdate) {
    this._writer = new(disableCdfUpdate);
    this._cdf = this._frameCdf.Clone();

    this._tileMiColStart = colStartsSb[tileCol] * this._sbSize4;
    this._tileMiColEnd = Math.Min(this._miCols, colStartsSb[tileCol + 1] * this._sbSize4);
    this._tileMiRowStart = rowStartsSb[tileRow] * this._sbSize4;
    this._tileMiRowEnd = Math.Min(this._miRows, rowStartsSb[tileRow + 1] * this._sbSize4);

    Array.Clear(this._abovePartitionContext);
    foreach (var plane in this._aboveEntropyContext)
      Array.Clear(plane);

    for (var miRow = this._tileMiRowStart; miRow < this._tileMiRowEnd; miRow += this._sbSize4) {
      Array.Clear(this._leftPartitionContext);
      foreach (var plane in this._leftEntropyContext)
        Array.Clear(plane);

      for (var miCol = this._tileMiColStart; miCol < this._tileMiColEnd; miCol += this._sbSize4) {
        this._ClearBlockDecodedFlags(miRow, miCol);
        this._EncodePartition(miRow, miCol, this._sbSize);
      }
    }

    return this._writer.Finish();
  }

  private void _ClearBlockDecodedFlags(int miRow, int miCol) {
    for (var plane = 0; plane < this._numPlanes; ++plane) {
      var subX = this._reconstruction.SubX[plane];
      var subY = this._reconstruction.SubY[plane];
      var flags = this._blockDecoded[plane];
      Array.Clear(flags);

      var sbWidth4 = (this._tileMiColEnd - miCol) >> subX;
      var sbHeight4 = (this._tileMiRowEnd - miRow) >> subY;
      var lastX = this._sbSize4 >> subX;
      var lastY = this._sbSize4 >> subY;

      for (var y = -1; y <= lastY; ++y)
        for (var x = -1; x <= lastX; ++x)
          flags[this._BlockDecodedIndex(y, x)] = (y < 0 && x < sbWidth4) || (x < 0 && y < sbHeight4);

      flags[this._BlockDecodedIndex(lastY, -1)] = false;
    }
  }

  private int _BlockDecodedIndex(int y, int x) => (y + 1) * this._blockDecodedStride + (x + 1);

  private void _EncodePartition(int miRow, int miCol, Av1BlockSize blockSize) {
    if (miRow >= this._miRows || miCol >= this._miCols)
      return;

    var num4x4 = Av1StructureTables.MiSizeWide[(int)blockSize];
    var half = num4x4 >> 1;
    var hasRows = miRow + half < this._miRows;
    var hasCols = miCol + half < this._miCols;
    var splitSize = _PartitionSubsize(Av1PartitionType.Split, blockSize);

    Av1PartitionType partition;
    if (blockSize < Av1BlockSize.Block8x8)
      partition = Av1PartitionType.None;
    else if (hasRows && hasCols) {
      // The whole block is inside the frame, so the cheapest choice is one block at this size.
      partition = Av1PartitionType.None;
      this._WritePartition(miRow, miCol, blockSize, partition, hasRows, hasCols);
    } else if (hasCols) {
      // Only the top half exists; a horizontal split codes it as a single block.
      partition = Av1PartitionType.Horizontal;
      this._WritePartition(miRow, miCol, blockSize, partition, hasRows, hasCols);
    } else if (hasRows) {
      partition = Av1PartitionType.Vertical;
      this._WritePartition(miRow, miCol, blockSize, partition, hasRows, hasCols);
    } else
      // Neither half is inside the frame, so the split is implied and nothing is coded.
      partition = Av1PartitionType.Split;

    var subSize = _PartitionSubsize(partition, blockSize);
    if (partition == Av1PartitionType.Split) {
      this._EncodePartition(miRow, miCol, subSize);
      this._EncodePartition(miRow, miCol + half, subSize);
      this._EncodePartition(miRow + half, miCol, subSize);
      this._EncodePartition(miRow + half, miCol + half, subSize);
    } else
      this._EncodeBlock(miRow, miCol, subSize);

    this._UpdatePartitionContext(miRow, miCol, blockSize, subSize, partition);
  }

  private void _WritePartition(
    int miRow, int miCol, Av1BlockSize blockSize, Av1PartitionType partition, bool hasRows, bool hasCols
  ) {
    var bsl = Av1StructureTables.MiSizeWideLog2[(int)blockSize] - Av1StructureTables.MiSizeWideLog2[(int)Av1BlockSize.Block8x8];
    var above = (this._abovePartitionContext[miCol] >> bsl) & 1;
    var left = (this._leftPartitionContext[miRow & this._sbMask] >> bsl) & 1;
    var offset = (left * 2 + above + bsl * 4) * Av1CdfContext.PartitionStride;

    if (hasRows && hasCols) {
      var symbols = blockSize <= Av1BlockSize.Block8x8 ? 4 : blockSize == Av1BlockSize.Block128x128 ? 8 : 10;
      this._writer.WriteSymbol(this._cdf.Partition, offset, symbols, (int)partition);
      return;
    }

    // The folded two-symbol CDF the decoder builds for a partly-visible block; symbol 0 is the
    // rectangular partition, symbol 1 a split. It is not adapted on either side.
    const int CDF_TOP = 1 << 15;
    var full = this._cdf.Partition.AsSpan(offset, Av1CdfContext.PartitionStride);
    var is128 = blockSize == Av1BlockSize.Block128x128;
    int splitLike;
    if (hasCols)
      splitLike = _ElementProbability(full, (int)Av1PartitionType.Vertical)
        + _ElementProbability(full, (int)Av1PartitionType.Split)
        + _ElementProbability(full, (int)Av1PartitionType.HorizontalA)
        + _ElementProbability(full, (int)Av1PartitionType.VerticalA)
        + _ElementProbability(full, (int)Av1PartitionType.VerticalB)
        + (is128 ? 0 : _ElementProbability(full, (int)Av1PartitionType.Vertical4));
    else
      splitLike = _ElementProbability(full, (int)Av1PartitionType.Horizontal)
        + _ElementProbability(full, (int)Av1PartitionType.Split)
        + _ElementProbability(full, (int)Av1PartitionType.HorizontalA)
        + _ElementProbability(full, (int)Av1PartitionType.HorizontalB)
        + _ElementProbability(full, (int)Av1PartitionType.VerticalA)
        + (is128 ? 0 : _ElementProbability(full, (int)Av1PartitionType.Horizontal4));

    Span<ushort> folded = [(ushort)(CDF_TOP - splitLike), CDF_TOP];
    var symbol = partition == Av1PartitionType.Split ? 1 : 0;
    this._writer.WriteSymbolNoUpdate(folded, symbol);
  }

  private static int _ElementProbability(ReadOnlySpan<ushort> cdf, int symbol) =>
    symbol == 0 ? cdf[0] : cdf[symbol] - cdf[symbol - 1];

  private static Av1BlockSize _PartitionSubsize(Av1PartitionType partition, Av1BlockSize blockSize) =>
    (Av1BlockSize)Av1StructureTables.PartitionSubsize[(int)partition * 6 + (int)blockSize / 3];

  private void _UpdatePartitionContext(
    int miRow, int miCol, Av1BlockSize blockSize, Av1BlockSize subSize, Av1PartitionType partition
  ) {
    if (blockSize < Av1BlockSize.Block8x8)
      return;
    if (partition == Av1PartitionType.Split && blockSize != Av1BlockSize.Block8x8)
      return;

    var bw = Av1StructureTables.MiSizeWide[(int)blockSize];
    var bh = Av1StructureTables.MiSizeHigh[(int)blockSize];
    var above = Av1StructureTables.PartitionContextAbove[(int)subSize];
    var left = Av1StructureTables.PartitionContextLeft[(int)subSize];

    for (var i = 0; i < bw && miCol + i < this._abovePartitionContext.Length; ++i)
      this._abovePartitionContext[miCol + i] = above;
    for (var i = 0; i < bh; ++i)
      this._leftPartitionContext[(miRow & this._sbMask) + i] = left;
  }

  private void _EncodeBlock(int miRow, int miCol, Av1BlockSize blockSize) {
    this._miRow = miRow;
    this._miCol = miCol;
    this._miSize = blockSize;
    this._availU = miRow > this._tileMiRowStart;
    this._availL = miCol > this._tileMiColStart;

    // skip = 0: this encoder always codes a residual, even when it is all zero, because the
    // per-transform-block all_zero flag already covers that case at a lower cost.
    var skipContext = 0;
    if (this._availU && this._reconstruction.Skips[(miRow - 1) * this._miCols + miCol])
      ++skipContext;
    if (this._availL && this._reconstruction.Skips[miRow * this._miCols + miCol - 1])
      ++skipContext;
    this._writer.WriteSymbol(this._cdf.Skip, skipContext * 3, 2, 0);

    // intra_frame_y_mode, using the above and left modes as context. Everything is DC_PRED here.
    var aboveMode = this._availU
      ? (Av1PredictionMode)this._reconstruction.YModes[(miRow - 1) * this._miCols + miCol]
      : Av1PredictionMode.DcPred;
    var leftMode = this._availL
      ? (Av1PredictionMode)this._reconstruction.YModes[miRow * this._miCols + miCol - 1]
      : Av1PredictionMode.DcPred;
    var modeOffset = (Av1StructureTables.IntraModeContext[(int)aboveMode] * 5
      + Av1StructureTables.IntraModeContext[(int)leftMode]) * Av1CdfContext.KeyFrameYModeStride;
    this._writer.WriteSymbol(this._cdf.KeyFrameYMode, modeOffset, Av1Constants.IntraModes, (int)Av1PredictionMode.DcPred);

    if (this._numPlanes > 1) {
      // Chroma-from-luma needs a 4x4 chroma block in a lossless frame, which never happens here, so
      // the CDF is the one without the extra CFL symbol.
      var uvOffset = (int)Av1PredictionMode.DcPred * Av1CdfContext.UvModeStride;
      this._writer.WriteSymbol(this._cdf.UvMode, uvOffset, Av1Constants.IntraModes, (int)Av1PredictionMode.DcPred);
    }

    this._RecordModeInfo();
    this._EncodeResidual();
  }

  private void _RecordModeInfo() {
    var bw4 = Av1StructureTables.MiSizeWide[(int)this._miSize];
    var bh4 = Av1StructureTables.MiSizeHigh[(int)this._miSize];
    var rowEnd = Math.Min(this._miRows, this._miRow + bh4);
    var colEnd = Math.Min(this._miCols, this._miCol + bw4);

    for (var row = this._miRow; row < rowEnd; ++row)
      for (var col = this._miCol; col < colEnd; ++col) {
        var index = row * this._miCols + col;
        this._reconstruction.BlockSizes[index] = (byte)this._miSize;
        this._reconstruction.YModes[index] = (byte)Av1PredictionMode.DcPred;
        this._reconstruction.UvModes[index] = (byte)Av1PredictionMode.DcPred;
        this._reconstruction.Skips[index] = false;
      }
  }

  private void _EncodeResidual() {
    var widthChunks = Math.Max(1, Av1StructureTables.BlockWidth[(int)this._miSize] >> 6);
    var heightChunks = Math.Max(1, Av1StructureTables.BlockHeight[(int)this._miSize] >> 6);
    var chunkSize = widthChunks > 1 || heightChunks > 1 ? Av1BlockSize.Block64x64 : this._miSize;

    for (var chunkY = 0; chunkY < heightChunks; ++chunkY)
      for (var chunkX = 0; chunkX < widthChunks; ++chunkX)
        for (var plane = 0; plane < this._numPlanes; ++plane) {
          var subX = this._reconstruction.SubX[plane];
          var subY = this._reconstruction.SubY[plane];
          var planeSize = Av1Structure.PlaneResidualSize(chunkSize, subX, subY);
          var num4x4W = Av1StructureTables.MiSizeWide[(int)planeSize];
          var num4x4H = Av1StructureTables.MiSizeHigh[(int)planeSize];
          var baseX = (this._miCol >> subX) * _MI_SIZE;
          var baseY = (this._miRow >> subY) * _MI_SIZE;

          for (var y = 0; y < num4x4H; ++y)
            for (var x = 0; x < num4x4W; ++x)
              this._EncodeTransformBlock(
                plane, baseX, baseY,
                x + ((chunkX << 4) >> subX),
                y + ((chunkY << 4) >> subY));
        }
  }

  private void _EncodeTransformBlock(int plane, int baseX, int baseY, int x, int y) {
    var startX = baseX + _MI_SIZE * x;
    var startY = baseY + _MI_SIZE * y;
    var subX = this._reconstruction.SubX[plane];
    var subY = this._reconstruction.SubY[plane];
    var row = (startY << subY) >> _MI_SIZE_LOG2;
    var col = (startX << subX) >> _MI_SIZE_LOG2;
    var subBlockMiRow = row & this._sbMask;
    var subBlockMiCol = col & this._sbMask;
    var maxX = (this._miCols * _MI_SIZE) >> subX;
    var maxY = (this._miRows * _MI_SIZE) >> subY;

    if (startX >= maxX || startY >= maxY)
      return;

    var stride = this._reconstruction.Strides[plane];
    var samples = this._reconstruction.Planes[plane];
    var flags = this._blockDecoded[plane];

    var haveLeft = this._availL || x > 0;
    var haveAbove = this._availU || y > 0;
    var haveAboveRight = flags[this._BlockDecodedIndex((subBlockMiRow >> subY) - 1, (subBlockMiCol >> subX) + 1)];
    var haveBelowLeft = flags[this._BlockDecodedIndex((subBlockMiRow >> subY) + 1, (subBlockMiCol >> subX) - 1)];

    var xRight = maxX - startX - 4;
    var yBelow = maxY - startY - 4;
    var topPixels = haveAbove ? Math.Min(4, xRight + 4) : 0;
    var leftPixels = haveLeft ? Math.Min(4, yBelow + 4) : 0;
    var topRightPixels = haveAboveRight ? Math.Max(0, Math.Min(4, xRight)) : 0;
    var bottomLeftPixels = haveBelowLeft ? Math.Max(0, Math.Min(4, yBelow)) : 0;

    Av1IntraPrediction.Predict(
      samples, stride, startX, startY, Av1TxSize.Tx4x4,
      Av1PredictionMode.DcPred, 0,
      topPixels, topRightPixels, leftPixels, bottomLeftPixels,
      false, Av1FilterIntraMode.DcPred, false, this._bitDepth, 0);

    // The residual is the difference from the padded source; because the transform is reversible
    // and the quantiser step is one, the decoder rebuilds the source exactly.
    var sourcePlane = this._source.Planes[plane];
    var sourceStride = this._source.Strides[plane];
    for (var i = 0; i < 4; ++i)
      for (var j = 0; j < 4; ++j)
        this._residual[i * 4 + j] = sourcePlane[(startY + i) * sourceStride + startX + j]
          - samples[(startY + i) * stride + startX + j];

    Av1ForwardTransform.WalshHadamard4x4(this._residual);

    // The scan and the context tables index coefficients as column * height + row.
    for (var position = 0; position < 16; ++position)
      this._quantized[position] = this._residual[(position & 3) * 4 + (position >> 2)];

    this._EncodeCoefficients(plane, startX, startY);

    for (var i = 0; i < 4; ++i)
      for (var j = 0; j < 4; ++j)
        samples[(startY + i) * stride + startX + j] = sourcePlane[(startY + i) * sourceStride + startX + j];

    var loopRow = row >> subY;
    var loopCol = col >> subX;
    if (loopRow < this._miRows && loopCol < this._miCols)
      this._reconstruction.TxSizes[plane][loopRow * this._miCols + loopCol] = (byte)Av1TxSize.Tx4x4;
    flags[this._BlockDecodedIndex(subBlockMiRow >> subY, subBlockMiCol >> subX)] = true;
  }

  private void _EncodeCoefficients(int plane, int startX, int startY) {
    var subX = this._reconstruction.SubX[plane];
    var subY = this._reconstruction.SubY[plane];
    var planeType = plane > 0 ? 1 : 0;
    var planeBlockSize = Av1Structure.PlaneResidualSize(this._miSize, subX, subY);
    var aboveContext = this._aboveEntropyContext[plane];
    var leftContext = this._leftEntropyContext[plane];
    var aboveIndex = startX >> _MI_SIZE_LOG2;
    var leftIndex = (startY >> _MI_SIZE_LOG2) & (this._sbMask >> subY);

    var (skipContext, dcSignContext) = this._GetTransformBlockContext(
      plane, planeBlockSize, aboveContext, aboveIndex, leftContext, leftIndex);

    var scan = Av1ScanTables.GetScan((int)Av1TxSize.Tx4x4, (int)Av1TxType.DctDct);
    var eob = 0;
    for (var c = 15; c >= 0; --c)
      if (this._quantized[scan[c]] != 0) {
        eob = c + 1;
        break;
      }

    // The transform block's own skip flag: TX_4X4 in a 4x4 plane block has skip context 0.
    this._writer.WriteSymbol(this._cdf.TxbSkip, skipContext * 3, 2, eob == 0 ? 1 : 0);
    if (eob == 0) {
      this._SetEntropyContexts(plane, planeBlockSize, 0, aboveContext, aboveIndex, leftContext, leftIndex, startX, startY);
      return;
    }

    // Lossless frames never code a transform type: it is fixed at DCT_DCT, which for a 4x4 block
    // means the default scan and the two-dimensional coefficient contexts.
    this._WriteEndOfBlock(planeType, eob);

    Array.Clear(this._levels);
    const int BHL = 2;
    var offsets = Av1CoefficientContextTables.GetNzMapCtxOffset((int)Av1TxSize.Tx4x4);

    {
      var c = eob - 1;
      var position = scan[c];
      var level = Math.Min(Math.Abs(this._quantized[position]), _MAX_BASE_BR_RANGE);
      var context = _LowerLevelsContextEob(BHL, 4, c);
      this._writer.WriteSymbol(
        this._cdf.CoeffBaseEob,
        ((0 * 2 + planeType) * Av1Constants.SigCoefContextsEob + context) * 4,
        3,
        Math.Min(level, 3) - 1);

      if (level > Av1Constants.NumBaseLevels)
        this._WriteCoefficientRange(_BrContextEob(position, BHL), planeType, level - 3);

      this._levels[_PaddedIndex(position, BHL)] = (byte)level;
    }

    if (eob > 1) {
      for (var c = eob - 2; c >= 1; --c)
        this._WriteBaseLevel(scan[c], BHL, planeType, offsets);

      // The DC coefficient is coded last and out of the loop above: its map context is fixed at
      // zero for a two-dimensional transform, and its range context has no positional offset.
      this._WriteDcBaseLevel(BHL, planeType);
    }

    var culLevel = 0;
    var dcValue = 0;
    for (var c = 0; c < eob; ++c) {
      var position = scan[c];
      var value = this._quantized[position];
      var level = Math.Abs(value);
      if (level == 0)
        continue;

      var sign = value < 0 ? 1 : 0;
      if (c == 0)
        this._writer.WriteSymbol(
          this._cdf.DcSign, (planeType * Av1Constants.DcSignContexts + dcSignContext) * 3, 2, sign);
      else
        this._writer.WriteLiteralBit(sign);

      if (level >= _MAX_BASE_BR_RANGE)
        this._writer.WriteGolomb(level - _MAX_BASE_BR_RANGE);

      if (c == 0)
        dcValue = value;
      culLevel += level;
    }

    culLevel = Math.Min(_COEFF_CONTEXT_MASK, culLevel);
    if (dcValue < 0)
      culLevel |= 1 << _COEFF_CONTEXT_BITS;
    else if (dcValue > 0)
      culLevel += 2 << _COEFF_CONTEXT_BITS;

    this._SetEntropyContexts(plane, planeBlockSize, culLevel, aboveContext, aboveIndex, leftContext, leftIndex, startX, startY);
  }

  private void _WriteBaseLevel(int position, int bhl, int planeType, ReadOnlySpan<sbyte> offsets) {
    var level = Math.Min(Math.Abs(this._quantized[position]), _MAX_BASE_BR_RANGE);
    var context = this._LowerLevelsContext2d(position, bhl, offsets);
    this._writer.WriteSymbol(
      this._cdf.CoeffBase,
      ((0 * 2 + planeType) * Av1Constants.SigCoefContexts + context) * 5,
      4,
      Math.Min(level, 3));

    if (level > Av1Constants.NumBaseLevels)
      this._WriteCoefficientRange(this._BrContext2d(position, bhl), planeType, level - 3);

    this._levels[_PaddedIndex(position, bhl)] = (byte)level;
  }

  private void _WriteDcBaseLevel(int bhl, int planeType) {
    var level = Math.Min(Math.Abs(this._quantized[0]), _MAX_BASE_BR_RANGE);
    this._writer.WriteSymbol(
      this._cdf.CoeffBase,
      ((0 * 2 + planeType) * Av1Constants.SigCoefContexts + 0) * 5,
      4,
      Math.Min(level, 3));

    if (level > Av1Constants.NumBaseLevels) {
      var stride = (1 << bhl) + _TX_PAD_HOR;
      var magnitude = this._levels[1] + this._levels[stride] + this._levels[stride + 1];
      this._WriteCoefficientRange(Math.Min((magnitude + 1) >> 1, 6), planeType, level - 3);
    }

    this._levels[0] = (byte)level;
  }

  private void _WriteCoefficientRange(int brContext, int planeType, int remaining) {
    var offset = ((0 * 2 + planeType) * Av1Constants.LevelContexts + brContext) * 5;
    for (var index = 0; index < Av1Constants.CoeffBaseRange; index += Av1Constants.BrCdfSize - 1) {
      var k = Math.Min(remaining, Av1Constants.BrCdfSize - 1);
      this._writer.WriteSymbol(this._cdf.CoeffBr, offset, Av1Constants.BrCdfSize, k);
      remaining -= k;
      if (k < Av1Constants.BrCdfSize - 1)
        break;
    }
  }

  private void _WriteEndOfBlock(int planeType, int eob) {
    // A 4x4 transform uses the 16-coefficient end-of-block CDF, five symbols wide.
    var eobPt = 1;
    while (eobPt < 11 && Av1CoefficientContextTables.EobGroupStart[eobPt + 1] <= eob)
      ++eobPt;

    var stride = Av1CdfContext.EobPtStrides[0];
    this._writer.WriteSymbol(this._cdf.EobPt[0], (planeType * 2 + 0) * stride, Av1CdfContext.EobPtSymbols[0], eobPt - 1);

    var offsetBits = Av1CoefficientContextTables.EobOffsetBits[eobPt];
    if (offsetBits <= 0)
      return;

    var extra = eob - Av1CoefficientContextTables.EobGroupStart[eobPt];
    var eobContext = eobPt - 3;
    var topBit = (extra >> (offsetBits - 1)) & 1;
    this._writer.WriteSymbol(
      this._cdf.EobExtra, ((0 * 2 + planeType) * Av1Constants.EobCoefContexts + eobContext) * 3, 2, topBit);

    for (var i = 1; i < offsetBits; ++i)
      this._writer.WriteLiteralBit((extra >> (offsetBits - 1 - i)) & 1);
  }

  private static int _PaddedIndex(int index, int bhl) => index + ((index >> bhl) << _TX_PAD_HOR_LOG2);

  private static int _LowerLevelsContextEob(int bhl, int width, int scanIndex) {
    if (scanIndex == 0)
      return 0;
    if (scanIndex <= (width << bhl) / 8)
      return 1;
    if (scanIndex <= (width << bhl) / 4)
      return 2;
    return 3;
  }

  private int _LowerLevelsContext2d(int position, int bhl, ReadOnlySpan<sbyte> offsets) {
    var levels = this._levels;
    var baseIndex = _PaddedIndex(position, bhl);
    var stride = (1 << bhl) + _TX_PAD_HOR;

    var magnitude = Math.Min((int)levels[baseIndex + stride], 3)
      + Math.Min((int)levels[baseIndex + 1], 3)
      + Math.Min((int)levels[baseIndex + stride + 1], 3)
      + Math.Min((int)levels[baseIndex + 2 * stride], 3)
      + Math.Min((int)levels[baseIndex + 2], 3);

    return Math.Min((magnitude + 1) >> 1, 4) + offsets[position];
  }

  private int _BrContext2d(int position, int bhl) {
    var levels = this._levels;
    var column = position >> bhl;
    var row = position - (column << bhl);
    var stride = (1 << bhl) + _TX_PAD_HOR;
    var index = column * stride + row;

    var magnitude = Math.Min((int)levels[index + 1], _MAX_BASE_BR_RANGE)
      + Math.Min((int)levels[index + stride], _MAX_BASE_BR_RANGE)
      + Math.Min((int)levels[index + 1 + stride], _MAX_BASE_BR_RANGE);
    magnitude = Math.Min((magnitude + 1) >> 1, 6);
    return (row | column) < 2 ? magnitude + 7 : magnitude + 14;
  }

  private static int _BrContextEob(int position, int bhl) {
    if (position == 0)
      return 0;

    var column = position >> bhl;
    var row = position - (column << bhl);
    return row < 2 && column < 2 ? 7 : 14;
  }

  private (int SkipContext, int DcSignContext) _GetTransformBlockContext(
    int plane, Av1BlockSize planeBlockSize,
    byte[] aboveContext, int aboveIndex, byte[] leftContext, int leftIndex
  ) {
    var dcSign = _DcSignOf(aboveContext[aboveIndex]) + _DcSignOf(leftContext[leftIndex]);
    var dcSignContext = dcSign < 0 ? 1 : dcSign > 0 ? 2 : 0;

    int skipContext;
    if (plane == 0) {
      if (planeBlockSize == Av1BlockSize.Block4x4)
        skipContext = 0;
      else {
        var top = Math.Min(aboveContext[aboveIndex] & _COEFF_CONTEXT_MASK, 4);
        var left = Math.Min(leftContext[leftIndex] & _COEFF_CONTEXT_MASK, 4);
        skipContext = _SKIP_CONTEXTS[top * 5 + left];
      }
    } else {
      var contextBase = (aboveContext[aboveIndex] != 0 ? 1 : 0) + (leftContext[leftIndex] != 0 ? 1 : 0);
      var contextOffset = Av1StructureTables.NumPelsLog2[(int)planeBlockSize]
        > Av1StructureTables.NumPelsLog2[(int)Av1BlockSize.Block4x4] ? 10 : 7;
      skipContext = contextBase + contextOffset;
    }

    return (skipContext, dcSignContext);
  }

  private static int _DcSignOf(byte context) => (context >> _COEFF_CONTEXT_BITS) switch {
    1 => -1,
    2 => 1,
    _ => 0,
  };

  private void _SetEntropyContexts(
    int plane, Av1BlockSize planeBlockSize, int culLevel,
    byte[] aboveContext, int aboveIndex, byte[] leftContext, int leftIndex, int startX, int startY
  ) {
    var subX = this._reconstruction.SubX[plane];
    var subY = this._reconstruction.SubY[plane];
    var blockOriginX = (this._miCol >> subX) * _MI_SIZE;
    var blockOriginY = (this._miRow >> subY) * _MI_SIZE;
    var offsetX = (startX - blockOriginX) >> _MI_SIZE_LOG2;
    var offsetY = (startY - blockOriginY) >> _MI_SIZE_LOG2;

    int wide = Av1StructureTables.BlockWidth[(int)planeBlockSize];
    var toRight = (this._miCols - Av1StructureTables.MiSizeWide[(int)this._miSize] - this._miCol) * _MI_SIZE;
    if (toRight < 0)
      wide += toRight >> subX;

    int high = Av1StructureTables.BlockHeight[(int)planeBlockSize];
    var toBottom = (this._miRows - Av1StructureTables.MiSizeHigh[(int)this._miSize] - this._miRow) * _MI_SIZE;
    if (toBottom < 0)
      high += toBottom >> subY;

    var aboveInside = culLevel == 0 || offsetX < (wide >> _MI_SIZE_LOG2);
    var leftInside = culLevel == 0 || offsetY < (high >> _MI_SIZE_LOG2);
    aboveContext[aboveIndex] = (byte)(aboveInside ? culLevel : 0);
    leftContext[leftIndex] = (byte)(leftInside ? culLevel : 0);
  }
}
