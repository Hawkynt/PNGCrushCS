using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Coefficient parsing, AV1 5.11.39 coeffs(). This is a transcription of libaom's
/// <c>read_coeffs_txb</c> in <c>av1/decoder/decodetxb.c</c> together with the context functions in
/// <c>av1/common/txb_common.h</c>: the two are one algorithm and splitting them apart is how a
/// context ends up off by one.
/// </summary>
internal sealed partial class Av1TileDecoder {

  private const int _TX_PAD_HOR = 4;
  private const int _TX_PAD_HOR_LOG2 = 2;
  private const int _COEFF_CONTEXT_BITS = 3;
  private const int _COEFF_CONTEXT_MASK = (1 << _COEFF_CONTEXT_BITS) - 1;
  private const int _MAX_BASE_BR_RANGE = Av1Constants.CoeffBaseRange + Av1Constants.NumBaseLevels + 1;
  private const int _NZ_MAP_CTX_0 = 26;

  /// <summary>libaom <c>nz_map_ctx_offset_1d</c>: the 1D transform classes only distinguish the
  /// first three positions along the coded direction.</summary>
  private static readonly int[] _NZ_MAP_CTX_OFFSET_1D = [
    _NZ_MAP_CTX_0, _NZ_MAP_CTX_0 + 5, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10,
    _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10,
    _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10,
    _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10,
    _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10,
    _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10,
    _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10,
    _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10, _NZ_MAP_CTX_0 + 10,
  ];

  /// <summary>libaom <c>kTxbCtxSigns</c>: a stored context of 1 means a negative DC, 2 a positive one.</summary>
  private static readonly int[] _DC_SIGN_VALUE = [0, -1, 1];

  /// <summary>libaom <c>kTxbCtxSkipContexts[top][left]</c>.</summary>
  private static readonly byte[] _SKIP_CONTEXTS = [
    1, 2, 2, 2, 3,
    2, 4, 4, 4, 5,
    2, 4, 4, 4, 5,
    2, 4, 4, 4, 5,
    3, 5, 5, 5, 6,
  ];

  private readonly byte[] _levels = new byte[(32 + _TX_PAD_HOR) * (32 + 4) + 16];

  /// <summary>Reads one transform block's coefficients, dequantising them into
  /// <see cref="_coefficients"/> in row-major order, and returns the end-of-block position.</summary>
  private int _ReadCoefficients(
    int plane, int startX, int startY, int subBlockMiRow, int subBlockMiCol,
    Av1TxSize txSize, out Av1TxType txType
  ) {
    txType = Av1TxType.DctDct;

    var subX = this._frame.SubX[plane];
    var subY = this._frame.SubY[plane];
    var planeType = plane > 0 ? 1 : 0;
    var txSizeContext = Av1Structure.TxSizeEntropyContext(txSize);
    var adjusted = Av1Structure.AdjustedTxSize(txSize);
    var width = Av1StructureTables.TxWidth[(int)adjusted];
    var height = Av1StructureTables.TxHeight[(int)adjusted];
    var bhl = Av1StructureTables.TxHeightLog2[(int)adjusted];
    var txWidthUnit = Av1StructureTables.TxWidthUnit[(int)txSize];
    var txHeightUnit = Av1StructureTables.TxHeightUnit[(int)txSize];
    var planeBlockSize = Av1Structure.PlaneResidualSize(this._miSize, subX, subY);

    var aboveContext = this._aboveEntropyContext[plane];
    var leftContext = this._leftEntropyContext[plane];
    var aboveIndex = startX >> _MI_SIZE_LOG2;
    var leftIndex = (startY >> _MI_SIZE_LOG2) & (this._sbMask >> subY);

    var (skipContext, dcSignContext) = this._GetTransformBlockContext(
      plane, planeBlockSize, txSize, aboveContext, aboveIndex, leftContext, leftIndex,
      txWidthUnit, txHeightUnit);

    Array.Clear(this._coefficients, 0,
      Av1StructureTables.TxWidth[(int)txSize] * Av1StructureTables.TxHeight[(int)txSize]);

    var allZero = this._reader.ReadSymbol(this._cdf.TxbSkip, (txSizeContext * 13 + skipContext) * 3, 2);
    if (allZero != 0) {
      this._SetEntropyContexts(plane, planeBlockSize, txSize, 0, aboveContext, aboveIndex, leftContext, leftIndex,
        txWidthUnit, txHeightUnit, startX, startY);
      return 0;
    }

    txType = plane == 0
      ? this._ReadLumaTxType(txSize)
      : this._ChromaTxType(txSize);

    var txClass = Av1Structure.TxClassOf(txType);
    var scan = Av1ScanTables.GetScan((int)txSize, (int)txType);
    var eob = this._ReadEndOfBlock(txSizeContext, planeType, txSize, txClass);

    Array.Clear(this._levels);

    // The last coded coefficient uses its own CDF: its level is known to be non-zero.
    {
      var c = eob - 1;
      var position = scan[c];
      var context = _LowerLevelsContextEob(bhl, width, c);
      var level = 1 + this._reader.ReadSymbol(
        this._cdf.CoeffBaseEob,
        ((txSizeContext * 2 + planeType) * Av1Constants.SigCoefContextsEob + context) * 4,
        3);

      if (level > Av1Constants.NumBaseLevels)
        level += this._ReadCoefficientRange(_BrContextEob(position, bhl, txClass), txSizeContext, planeType);

      this._levels[_PaddedIndex(position, bhl)] = (byte)level;
    }

    if (eob > 1) {
      if (txClass == Av1Structure.TxClass2d) {
        this._ReadCoefficientsReverse2d(1, eob - 2, scan, bhl, txSize, txSizeContext, planeType);
        this._ReadCoefficientsReverse(0, 0, scan, bhl, txSize, txClass, txSizeContext, planeType);
      } else
        this._ReadCoefficientsReverse(0, eob - 2, scan, bhl, txSize, txClass, txSizeContext, planeType);
    }

    var (dcQuant, acQuant) = this._GetDequantizers(plane);
    var inverseQm = this._GetInverseQuantizerMatrix(plane, txSize, txType);
    var shift = Av1Structure.TxScale(txSize);
    var maxValue = (1 << (7 + this._bitDepth)) - 1;
    var minValue = -(1 << (7 + this._bitDepth));
    var txWidthFull = Av1StructureTables.TxWidth[(int)txSize];
    var culLevel = 0;
    var dcValue = 0;

    for (var c = 0; c < eob; ++c) {
      var position = scan[c];
      long level = this._levels[_PaddedIndex(position, bhl)];
      if (level == 0)
        continue;

      int sign;
      if (c == 0)
        sign = this._reader.ReadSymbol(this._cdf.DcSign, (planeType * Av1Constants.DcSignContexts + dcSignContext) * 3, 2);
      else
        sign = this._reader.ReadLiteralBit();

      if (level >= _MAX_BASE_BR_RANGE)
        level += this._reader.ReadGolomb();

      if (c == 0)
        dcValue = sign != 0 ? -(int)level : (int)level;

      // libaom masks rather than clamps: the valid level range is at most 20 bits.
      level &= 0xFFFFF;
      culLevel += (int)level;

      var quantizer = position != 0 ? acQuant : dcQuant;
      if (!inverseQm.IsEmpty)
        quantizer = (inverseQm[position] * quantizer + (1 << (Av1QuantizerMatrixTables.WeightBits - 1)))
          >> Av1QuantizerMatrixTables.WeightBits;

      var dequantized = (int)(level * quantizer & 0xFFFFFF) >> shift;
      if (sign != 0)
        dequantized = -dequantized;

      var col = position >> bhl;
      var rowInBlock = position - (col << bhl);
      this._coefficients[rowInBlock * txWidthFull + col] = Math.Clamp(dequantized, minValue, maxValue);
    }

    culLevel = Math.Min(_COEFF_CONTEXT_MASK, culLevel);
    if (dcValue < 0)
      culLevel |= 1 << _COEFF_CONTEXT_BITS;
    else if (dcValue > 0)
      culLevel += 2 << _COEFF_CONTEXT_BITS;

    this._SetEntropyContexts(plane, planeBlockSize, txSize, culLevel, aboveContext, aboveIndex, leftContext, leftIndex,
      txWidthUnit, txHeightUnit, startX, startY);
    return eob;
  }

  private Av1TxType _ReadLumaTxType(Av1TxSize txSize) {
    if (this._IsLossless() || Av1StructureTables.TxSizeSquareUp[(int)txSize] > (byte)Av1TxSize.Tx32x32)
      return Av1TxType.DctDct;
    if (this._currentQIndex == 0)
      return Av1TxType.DctDct;

    var setType = Av1Structure.IntraExtTxSetType(txSize, this._fh.ReducedTxSet);
    if (Av1Structure.ExtTxSetSize(setType) <= 1)
      return Av1TxType.DctDct;

    var mode = this._useFilterIntra
      ? Av1Structure.FilterIntraAsIntraMode(this._filterIntraMode)
      : this._yMode;
    var setIndex = Av1Structure.IntraExtTxSetIndex(setType);
    var squareTxSize = Av1StructureTables.TxSizeSquare[(int)txSize];
    var offset = ((setIndex * 4 + squareTxSize) * Av1Constants.IntraModes + (int)mode) * Av1CdfContext.IntraExtTxStride;
    var symbol = this._reader.ReadSymbol(this._cdf.IntraExtTx, offset, Av1Structure.ExtTxSetSize(setType));
    return Av1Structure.ExtTxInverse(setType, symbol);
  }

  private Av1TxType _ChromaTxType(Av1TxSize txSize) {
    if (this._IsLossless() || Av1StructureTables.TxSizeSquareUp[(int)txSize] > (byte)Av1TxSize.Tx32x32)
      return Av1TxType.DctDct;

    // Chroma has its own prediction mode, so it derives its own default transform type rather than
    // borrowing the one coded for luma (libaom av1_get_tx_type, PLANE_TYPE_UV).
    var mode = Av1Structure.UvModeAsIntraMode(this._uvMode);
    var txType = (Av1TxType)Av1StructureTables.IntraModeToTxType[(int)mode];
    var setType = Av1Structure.IntraExtTxSetType(txSize, this._fh.ReducedTxSet);
    return Av1Structure.ExtTxUsed(setType, txType) ? txType : Av1TxType.DctDct;
  }

  private int _ReadEndOfBlock(int txSizeContext, int planeType, Av1TxSize txSize, int txClass) {
    var multiSize = Av1StructureTables.TxSizeLog2Minus4[(int)txSize];
    var multiContext = txClass == Av1Structure.TxClass2d ? 0 : 1;
    var symbols = Av1CdfContext.EobPtSymbols[multiSize];
    var stride = Av1CdfContext.EobPtStrides[multiSize];
    var eobPt = 1 + this._reader.ReadSymbol(this._cdf.EobPt[multiSize], (planeType * 2 + multiContext) * stride, symbols);

    var offsetBits = Av1CoefficientContextTables.EobOffsetBits[eobPt];
    var extra = 0;
    if (offsetBits > 0) {
      var eobContext = eobPt - 3;
      var bit = this._reader.ReadSymbol(
        this._cdf.EobExtra,
        ((txSizeContext * 2 + planeType) * Av1Constants.EobCoefContexts + eobContext) * 3, 2);
      if (bit != 0)
        extra += 1 << (offsetBits - 1);

      for (var i = 1; i < offsetBits; ++i)
        if (this._reader.ReadLiteralBit() != 0)
          extra += 1 << (offsetBits - 1 - i);
    }

    var eob = Av1CoefficientContextTables.EobGroupStart[eobPt];
    return eob > 2 ? eob + extra : eob;
  }

  private void _ReadCoefficientsReverse2d(
    int startScan, int endScan, ReadOnlySpan<ushort> scan, int bhl,
    Av1TxSize txSize, int txSizeContext, int planeType
  ) {
    var offsets = Av1CoefficientContextTables.GetNzMapCtxOffset((int)txSize);
    for (var c = endScan; c >= startScan; --c) {
      var position = scan[c];
      var context = this._LowerLevelsContext2d(position, bhl, offsets);
      var level = this._reader.ReadSymbol(
        this._cdf.CoeffBase,
        ((txSizeContext * 2 + planeType) * Av1Constants.SigCoefContexts + context) * 5, 4);

      if (level > Av1Constants.NumBaseLevels)
        level += this._ReadCoefficientRange(this._BrContext2d(position, bhl), txSizeContext, planeType);

      this._levels[_PaddedIndex(position, bhl)] = (byte)level;
    }
  }

  private void _ReadCoefficientsReverse(
    int startScan, int endScan, ReadOnlySpan<ushort> scan, int bhl,
    Av1TxSize txSize, int txClass, int txSizeContext, int planeType
  ) {
    var offsets = Av1CoefficientContextTables.GetNzMapCtxOffset((int)txSize);
    for (var c = endScan; c >= startScan; --c) {
      var position = scan[c];
      var context = this._LowerLevelsContext(position, bhl, txClass, offsets);
      var level = this._reader.ReadSymbol(
        this._cdf.CoeffBase,
        ((txSizeContext * 2 + planeType) * Av1Constants.SigCoefContexts + context) * 5, 4);

      if (level > Av1Constants.NumBaseLevels)
        level += this._ReadCoefficientRange(this._BrContext(position, bhl, txClass), txSizeContext, planeType);

      this._levels[_PaddedIndex(position, bhl)] = (byte)level;
    }
  }

  private int _ReadCoefficientRange(int brContext, int txSizeContext, int planeType) {
    // The base-range CDF set saturates at 32x32: larger transforms reuse it (libaom AOMMIN).
    var sizeIndex = Math.Min(txSizeContext, (int)Av1TxSize.Tx32x32);
    var offset = ((sizeIndex * 2 + planeType) * Av1Constants.LevelContexts + brContext) * 5;

    var total = 0;
    for (var index = 0; index < Av1Constants.CoeffBaseRange; index += Av1Constants.BrCdfSize - 1) {
      var k = this._reader.ReadSymbol(this._cdf.CoeffBr, offset, Av1Constants.BrCdfSize);
      total += k;
      if (k < Av1Constants.BrCdfSize - 1)
        break;
    }
    return total;
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
      + Math.Min((int)levels[baseIndex + (2 << bhl) + (2 << _TX_PAD_HOR_LOG2)], 3)
      + Math.Min((int)levels[baseIndex + 2], 3);

    return Math.Min((magnitude + 1) >> 1, 4) + offsets[position];
  }

  private int _LowerLevelsContext(int position, int bhl, int txClass, ReadOnlySpan<sbyte> offsets) {
    var levels = this._levels;
    var baseIndex = _PaddedIndex(position, bhl);
    var stride = (1 << bhl) + _TX_PAD_HOR;

    var magnitude = Math.Min((int)levels[baseIndex + stride], 3) + Math.Min((int)levels[baseIndex + 1], 3);
    if (txClass == Av1Structure.TxClass2d) {
      magnitude += Math.Min((int)levels[baseIndex + stride + 1], 3)
        + Math.Min((int)levels[baseIndex + (2 << bhl) + (2 << _TX_PAD_HOR_LOG2)], 3)
        + Math.Min((int)levels[baseIndex + 2], 3);
    } else if (txClass == Av1Structure.TxClassVertical) {
      magnitude += Math.Min((int)levels[baseIndex + 2], 3)
        + Math.Min((int)levels[baseIndex + 3], 3)
        + Math.Min((int)levels[baseIndex + 4], 3);
    } else {
      magnitude += Math.Min((int)levels[baseIndex + (2 << bhl) + (2 << _TX_PAD_HOR_LOG2)], 3)
        + Math.Min((int)levels[baseIndex + (3 << bhl) + (3 << _TX_PAD_HOR_LOG2)], 3)
        + Math.Min((int)levels[baseIndex + (4 << bhl) + (4 << _TX_PAD_HOR_LOG2)], 3);
    }

    if ((txClass | position) == 0)
      return 0;

    var context = Math.Min((magnitude + 1) >> 1, 4);
    if (txClass == Av1Structure.TxClass2d)
      return context + offsets[position];

    var column = position >> bhl;
    if (txClass == Av1Structure.TxClassHorizontal)
      return context + _NZ_MAP_CTX_OFFSET_1D[column];

    return context + _NZ_MAP_CTX_OFFSET_1D[position - (column << bhl)];
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

  private int _BrContext(int position, int bhl, int txClass) {
    var levels = this._levels;
    var column = position >> bhl;
    var row = position - (column << bhl);
    var stride = (1 << bhl) + _TX_PAD_HOR;
    var index = column * stride + row;

    var magnitude = levels[index + 1] + levels[index + stride];
    switch (txClass) {
      case Av1Structure.TxClass2d:
        magnitude += levels[index + stride + 1];
        magnitude = Math.Min((magnitude + 1) >> 1, 6);
        if (position == 0)
          return magnitude;
        if (row < 2 && column < 2)
          return magnitude + 7;
        break;
      case Av1Structure.TxClassHorizontal:
        magnitude += levels[index + (stride << 1)];
        magnitude = Math.Min((magnitude + 1) >> 1, 6);
        if (position == 0)
          return magnitude;
        if (column == 0)
          return magnitude + 7;
        break;
      default:
        magnitude += levels[index + 2];
        magnitude = Math.Min((magnitude + 1) >> 1, 6);
        if (position == 0)
          return magnitude;
        if (row == 0)
          return magnitude + 7;
        break;
    }

    return magnitude + 14;
  }

  private static int _BrContextEob(int position, int bhl, int txClass) {
    if (position == 0)
      return 0;

    var column = position >> bhl;
    var row = position - (column << bhl);
    if ((txClass == Av1Structure.TxClass2d && row < 2 && column < 2)
        || (txClass == Av1Structure.TxClassHorizontal && column == 0)
        || (txClass == Av1Structure.TxClassVertical && row == 0))
      return 7;
    return 14;
  }

  private (int SkipContext, int DcSignContext) _GetTransformBlockContext(
    int plane, Av1BlockSize planeBlockSize, Av1TxSize txSize,
    byte[] aboveContext, int aboveIndex, byte[] leftContext, int leftIndex,
    int txWidthUnit, int txHeightUnit
  ) {
    var dcSign = 0;
    for (var k = 0; k < txWidthUnit; ++k)
      dcSign += _DC_SIGN_VALUE[aboveContext[aboveIndex + k] >> _COEFF_CONTEXT_BITS];
    for (var k = 0; k < txHeightUnit; ++k)
      dcSign += _DC_SIGN_VALUE[leftContext[leftIndex + k] >> _COEFF_CONTEXT_BITS];

    // libaom kTxbCtxDcSignContexts: negative totals pick context 1, positive 2, zero 0.
    var dcSignContext = dcSign < 0 ? 1 : dcSign > 0 ? 2 : 0;

    int skipContext;
    if (plane == 0) {
      if (planeBlockSize == (Av1BlockSize)Av1StructureTables.TxSizeToBlockSize[(int)txSize])
        skipContext = 0;
      else {
        var top = 0;
        var left = 0;
        for (var k = 0; k < txWidthUnit; ++k)
          top |= aboveContext[aboveIndex + k];
        for (var k = 0; k < txHeightUnit; ++k)
          left |= leftContext[leftIndex + k];

        top = Math.Min(top & _COEFF_CONTEXT_MASK, 4);
        left = Math.Min(left & _COEFF_CONTEXT_MASK, 4);
        skipContext = _SKIP_CONTEXTS[top * 5 + left];
      }
    } else {
      var above = 0;
      var left = 0;
      for (var k = 0; k < txWidthUnit; ++k)
        above |= aboveContext[aboveIndex + k];
      for (var k = 0; k < txHeightUnit; ++k)
        left |= leftContext[leftIndex + k];

      var contextBase = (above != 0 ? 1 : 0) + (left != 0 ? 1 : 0);
      var contextOffset = Av1StructureTables.NumPelsLog2[(int)planeBlockSize]
        > Av1StructureTables.NumPelsLog2[Av1StructureTables.TxSizeToBlockSize[(int)txSize]] ? 10 : 7;
      skipContext = contextBase + contextOffset;
    }

    return (skipContext, dcSignContext);
  }

  private void _SetEntropyContexts(
    int plane, Av1BlockSize planeBlockSize, Av1TxSize txSize, int culLevel,
    byte[] aboveContext, int aboveIndex, byte[] leftContext, int leftIndex,
    int txWidthUnit, int txHeightUnit, int startX, int startY
  ) {
    // AV1 clears the contexts of the part of a transform block that falls outside the frame, so a
    // block hanging over the edge does not advertise coefficients its neighbours cannot see.
    var subX = this._frame.SubX[plane];
    var subY = this._frame.SubY[plane];
    var blockOriginX = (this._miCol >> subX) * _MI_SIZE;
    var blockOriginY = (this._miRow >> subY) * _MI_SIZE;
    var blocksWide = this._MaxBlockWide(planeBlockSize, subX);
    var blocksHigh = this._MaxBlockHigh(planeBlockSize, subY);
    var offsetX = (startX - blockOriginX) >> _MI_SIZE_LOG2;
    var offsetY = (startY - blockOriginY) >> _MI_SIZE_LOG2;

    var aboveCount = culLevel != 0 ? Math.Clamp(blocksWide - offsetX, 0, txWidthUnit) : txWidthUnit;
    for (var k = 0; k < txWidthUnit; ++k)
      aboveContext[aboveIndex + k] = (byte)(k < aboveCount ? culLevel : 0);

    var leftCount = culLevel != 0 ? Math.Clamp(blocksHigh - offsetY, 0, txHeightUnit) : txHeightUnit;
    for (var k = 0; k < txHeightUnit; ++k)
      leftContext[leftIndex + k] = (byte)(k < leftCount ? culLevel : 0);
  }

  private int _MaxBlockWide(Av1BlockSize planeBlockSize, int subX) {
    int wide = Av1StructureTables.BlockWidth[(int)planeBlockSize];
    var toRight = (this._miCols - Av1StructureTables.MiSizeWide[(int)this._miSize] - this._miCol) * _MI_SIZE;
    if (toRight < 0)
      wide += toRight >> subX;
    return wide >> _MI_SIZE_LOG2;
  }

  private int _MaxBlockHigh(Av1BlockSize planeBlockSize, int subY) {
    int high = Av1StructureTables.BlockHeight[(int)planeBlockSize];
    var toBottom = (this._miRows - Av1StructureTables.MiSizeHigh[(int)this._miSize] - this._miRow) * _MI_SIZE;
    if (toBottom < 0)
      high += toBottom >> subY;
    return high >> _MI_SIZE_LOG2;
  }

  /// <summary>
  /// AV1 7.12.2: the per-position weights a frame with <c>using_qmatrix</c> folds into the
  /// dequantiser. An empty span means no weighting, which is what the specification calls a flat
  /// matrix — level 15, a lossless frame, or a transform that is not two-dimensional.
  /// </summary>
  private ReadOnlySpan<byte> _GetInverseQuantizerMatrix(int plane, Av1TxSize txSize, Av1TxType txType) {
    if (!this._fh.UsingQMatrix || this._IsLossless() || txType >= Av1TxType.IdtxIdtx)
      return default;

    var level = plane switch { 0 => this._fh.QmY, 1 => this._fh.QmU, _ => this._fh.QmV };
    if (level >= Av1QuantizerMatrixTables.Levels)
      return default;

    return Av1QuantizerMatrixTables.GetInverse(level, plane, Av1Structure.AdjustedTxSize(txSize));
  }

  private (int Dc, int Ac) _GetDequantizers(int plane) {
    var qIndex = this._currentQIndex;
    var (dcDelta, acDelta) = plane switch {
      0 => (this._fh.DeltaQYDc, 0),
      1 => (this._fh.DeltaQUDc, this._fh.DeltaQUAc),
      _ => (this._fh.DeltaQVDc, this._fh.DeltaQVAc),
    };

    return (Av1Quantizer.DcQ(qIndex + dcDelta, this._bitDepth), Av1Quantizer.AcQ(qIndex + acDelta, this._bitDepth));
  }
}
