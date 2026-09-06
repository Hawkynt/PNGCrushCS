using System;

namespace FileFormat.Avif.Codec;

/// <summary>Block-level syntax and reconstruction: AV1 5.11.5 decode_block() and what it calls.</summary>
internal sealed partial class Av1TileDecoder {

  private void _DecodeBlock(int miRow, int miCol, Av1BlockSize blockSize) {
    this._miRow = miRow;
    this._miCol = miCol;
    this._miSize = blockSize;

    var bw4 = Av1StructureTables.MiSizeWide[(int)blockSize];
    var bh4 = Av1StructureTables.MiSizeHigh[(int)blockSize];
    var subX = this._numPlanes > 1 ? this._seq.SubsamplingX : 0;
    var subY = this._numPlanes > 1 ? this._seq.SubsamplingY : 0;

    this._hasChroma = this._numPlanes > 1
      && !(bh4 == 1 && subY != 0 && (miRow & 1) == 0)
      && !(bw4 == 1 && subX != 0 && (miCol & 1) == 0);

    this._availU = miRow > this._tileMiRowStart;
    this._availL = miCol > this._tileMiColStart;
    this._availUChroma = this._availU;
    this._availLChroma = this._availL;
    if (this._hasChroma) {
      if (subY != 0 && bh4 == 1)
        this._availUChroma = miRow - 2 >= this._tileMiRowStart;
      if (subX != 0 && bw4 == 1)
        this._availLChroma = miCol - 2 >= this._tileMiColStart;
    } else {
      this._availUChroma = false;
      this._availLChroma = false;
    }

    this._ReadModeInfo();
    this._ReadBlockTxSize();

    // The mode information has to be visible to later blocks before the residual is decoded,
    // because the coefficient contexts of this block's own transform blocks read it back.
    this._RecordModeInfo();

    this._DecodeResidual();
  }

  private void _RecordModeInfo() {
    var bw4 = Av1StructureTables.MiSizeWide[(int)this._miSize];
    var bh4 = Av1StructureTables.MiSizeHigh[(int)this._miSize];
    var rowEnd = Math.Min(this._miRows, this._miRow + bh4);
    var colEnd = Math.Min(this._miCols, this._miCol + bw4);

    for (var row = this._miRow; row < rowEnd; ++row)
      for (var col = this._miCol; col < colEnd; ++col) {
        var index = row * this._miCols + col;
        this._frame.BlockSizes[index] = (byte)this._miSize;
        this._frame.YModes[index] = (byte)this._yMode;
        this._frame.UvModes[index] = (byte)this._uvMode;
        this._frame.Skips[index] = this._skip;
        this._frame.SegmentIds[index] = (byte)this._segmentId;
        for (var i = 0; i < _FRAME_LF_COUNT; ++i)
          this._frame.DeltaLf[index * 4 + i] = (sbyte)this._deltaLf[i];
      }
  }

  private void _ReadModeInfo() {
    this._segmentId = 0;
    if (this._fh.SegmentationEnabled)
      throw new NotSupportedException("AV1: segmentation_enabled is not supported by this still-image decoder.");

    this._skip = this._ReadSkip();
    this._ReadCdef();
    this._ReadDeltaQIndex();
    this._ReadDeltaLoopFilter();
    this._readDeltas = false;

    if (this._fh.AllowIntraBc)
      throw new NotSupportedException("AV1: intra block copy is not supported by this still-image decoder.");

    this._yMode = this._ReadIntraFrameYMode();
    this._angleDeltaY = this._ReadAngleDelta(this._yMode);

    if (this._hasChroma) {
      this._uvMode = this._ReadUvMode();
      if (this._uvMode == Av1PredictionMode.UvCflPred)
        this._ReadCflAlphas();
      this._angleDeltaUv = this._ReadAngleDelta(Av1Structure.UvModeAsIntraMode(this._uvMode));
    } else {
      this._uvMode = Av1PredictionMode.DcPred;
      this._angleDeltaUv = 0;
      this._cflAlphaU = 0;
      this._cflAlphaV = 0;
    }

    this._ReadPaletteModeInfo();
    this._ReadFilterIntraModeInfo();
  }

  private bool _ReadSkip() {
    var context = 0;
    if (this._availU && this._frame.Skips[(this._miRow - 1) * this._miCols + this._miCol])
      ++context;
    if (this._availL && this._frame.Skips[this._miRow * this._miCols + this._miCol - 1])
      ++context;
    return this._reader.ReadSymbol(this._cdf.Skip, context * 3, 2) != 0;
  }

  private void _ReadCdef() {
    // AV1 5.11.56: one index per 64x64 unit, coded at the first non-skipped block that reaches it.
    if (this._skip || this._fh.CodedLossless || !this._seq.EnableCdef || this._fh.AllowIntraBc)
      return;

    const int CDEF_SIZE4 = 16;
    var row = this._miRow & ~(CDEF_SIZE4 - 1);
    var col = this._miCol & ~(CDEF_SIZE4 - 1);
    var cdefIndex = (row >> 4) * this._frame.Cdef64Cols + (col >> 4);
    if (this._frame.CdefIndices[cdefIndex] >= 0)
      return;

    var value = (sbyte)this._reader.ReadLiteral(this._fh.CdefBits);
    var w4 = Av1StructureTables.MiSizeWide[(int)this._miSize];
    var h4 = Av1StructureTables.MiSizeHigh[(int)this._miSize];
    for (var i = row; i < row + h4; i += CDEF_SIZE4)
      for (var j = col; j < col + w4; j += CDEF_SIZE4) {
        var index = (i >> 4) * this._frame.Cdef64Cols + (j >> 4);
        if (index >= 0 && index < this._frame.CdefIndices.Length)
          this._frame.CdefIndices[index] = value;
      }
  }

  private void _ReadDeltaQIndex() {
    if (this._miSize == this._sbSize && this._skip)
      return;
    if (!this._readDeltas)
      return;

    var absolute = this._reader.ReadSymbol(this._cdf.DeltaQ, 0, _DELTA_Q_SMALL + 1);
    if (absolute == _DELTA_Q_SMALL) {
      var extraBits = (int)this._reader.ReadLiteral(3) + 1;
      absolute = (int)this._reader.ReadLiteral(extraBits) + (1 << extraBits) + 1;
    }

    if (absolute == 0)
      return;

    var sign = this._reader.ReadLiteralBit();
    var delta = sign != 0 ? -absolute : absolute;
    this._currentQIndex = Math.Clamp(this._currentQIndex + (delta << this._fh.DeltaQRes), 1, 255);
  }

  private void _ReadDeltaLoopFilter() {
    if (this._miSize == this._sbSize && this._skip)
      return;
    if (!this._readDeltas || !this._fh.DeltaLfPresent)
      return;

    var count = this._fh.DeltaLfMulti ? (this._numPlanes > 1 ? _FRAME_LF_COUNT : _FRAME_LF_COUNT - 2) : 1;
    for (var i = 0; i < count; ++i) {
      var absolute = this._fh.DeltaLfMulti
        ? this._reader.ReadSymbol(this._cdf.DeltaLfMulti, i * 5, _DELTA_LF_SMALL + 1)
        : this._reader.ReadSymbol(this._cdf.DeltaLf, 0, _DELTA_LF_SMALL + 1);

      if (absolute == _DELTA_LF_SMALL) {
        var extraBits = (int)this._reader.ReadLiteral(3) + 1;
        absolute = (int)this._reader.ReadLiteral(extraBits) + (1 << extraBits) + 1;
      }

      if (absolute == 0)
        continue;

      var sign = this._reader.ReadLiteralBit();
      var delta = sign != 0 ? -absolute : absolute;
      if (this._fh.DeltaLfMulti)
        this._deltaLf[i] = Math.Clamp(this._deltaLf[i] + (delta << this._fh.DeltaLfRes), -63, 63);
      else
        for (var j = 0; j < _FRAME_LF_COUNT; ++j)
          this._deltaLf[j] = Math.Clamp(this._deltaLf[j] + (delta << this._fh.DeltaLfRes), -63, 63);
    }
  }

  private Av1PredictionMode _ReadIntraFrameYMode() {
    var above = this._availU
      ? (Av1PredictionMode)this._frame.YModes[(this._miRow - 1) * this._miCols + this._miCol]
      : Av1PredictionMode.DcPred;
    var left = this._availL
      ? (Av1PredictionMode)this._frame.YModes[this._miRow * this._miCols + this._miCol - 1]
      : Av1PredictionMode.DcPred;

    var aboveContext = Av1StructureTables.IntraModeContext[(int)above];
    var leftContext = Av1StructureTables.IntraModeContext[(int)left];
    var offset = (aboveContext * 5 + leftContext) * Av1CdfContext.KeyFrameYModeStride;
    return (Av1PredictionMode)this._reader.ReadSymbol(this._cdf.KeyFrameYMode, offset, Av1Constants.IntraModes);
  }

  private int _ReadAngleDelta(Av1PredictionMode mode) {
    if (this._miSize < Av1BlockSize.Block8x8 || !Av1Structure.IsDirectionalMode(mode))
      return 0;

    var offset = ((int)mode - (int)Av1PredictionMode.VPred) * Av1CdfContext.AngleDeltaStride;
    var symbol = this._reader.ReadSymbol(this._cdf.AngleDelta, offset, 2 * _MAX_ANGLE_DELTA + 1);
    return symbol - _MAX_ANGLE_DELTA;
  }

  private Av1PredictionMode _ReadUvMode() {
    var cflAllowed = this._IsCflAllowed();
    var symbols = cflAllowed ? Av1Constants.UvIntraModes : Av1Constants.IntraModes;
    var offset = ((cflAllowed ? 1 : 0) * Av1Constants.IntraModes + (int)this._yMode) * Av1CdfContext.UvModeStride;
    return (Av1PredictionMode)this._reader.ReadSymbol(this._cdf.UvMode, offset, symbols);
  }

  private bool _IsCflAllowed() {
    if (this._IsLossless()) {
      // In lossless mode the transform is always 4x4, so chroma-from-luma only fits when the
      // chroma block is 4x4 as well (libaom is_cfl_allowed).
      var planeSize = Av1Structure.PlaneResidualSize(this._miSize, this._seq.SubsamplingX, this._seq.SubsamplingY);
      return planeSize == Av1BlockSize.Block4x4;
    }

    return Av1StructureTables.BlockWidth[(int)this._miSize] <= 32
      && Av1StructureTables.BlockHeight[(int)this._miSize] <= 32;
  }

  private void _ReadCflAlphas() {
    var jointSign = this._reader.ReadSymbol(this._cdf.CflSign, 0, 8);
    var signU = (jointSign + 1) / 3;
    var signV = (jointSign + 1) % 3;

    this._cflAlphaU = 0;
    this._cflAlphaV = 0;

    if (signU != 0) {
      var context = jointSign - 2;
      var magnitude = 1 + this._reader.ReadSymbol(this._cdf.CflAlpha, context * Av1CdfContext.CflAlphaStride, 16);
      this._cflAlphaU = signU == 1 ? -magnitude : magnitude;
    }

    if (signV != 0) {
      var context = signV * 3 + signU - 3;
      var magnitude = 1 + this._reader.ReadSymbol(this._cdf.CflAlpha, context * Av1CdfContext.CflAlphaStride, 16);
      this._cflAlphaV = signV == 1 ? -magnitude : magnitude;
    }
  }

  private void _ReadPaletteModeInfo() {
    // AV1 5.11.46: only offered when the frame enables screen-content tools.
    if (!this._fh.AllowScreenContentTools)
      return;
    if (this._miSize < Av1BlockSize.Block8x8
        || Av1StructureTables.BlockWidth[(int)this._miSize] > 64
        || Av1StructureTables.BlockHeight[(int)this._miSize] > 64)
      return;

    var bsizeContext = Av1StructureTables.MiSizeWideLog2[(int)this._miSize]
      + Av1StructureTables.MiSizeHighLog2[(int)this._miSize] - 2;

    if (this._yMode == Av1PredictionMode.DcPred) {
      var context = 0;
      if (this._availU && this._frame.PaletteSizes[((this._miRow - 1) * this._miCols + this._miCol) * 2] > 0)
        ++context;
      if (this._availL && this._frame.PaletteSizes[(this._miRow * this._miCols + this._miCol - 1) * 2] > 0)
        ++context;

      var offset = (bsizeContext * 3 + context) * 3;
      if (this._reader.ReadSymbol(this._cdf.PaletteYMode, offset, 2) != 0)
        throw new NotSupportedException("AV1: palette prediction is not supported by this still-image decoder.");
    }

    if (this._hasChroma && this._uvMode == Av1PredictionMode.DcPred) {
      var context = this._frame.PaletteSizes[(this._miRow * this._miCols + this._miCol) * 2] > 0 ? 1 : 0;
      if (this._reader.ReadSymbol(this._cdf.PaletteUvMode, context * 3, 2) != 0)
        throw new NotSupportedException("AV1: palette prediction is not supported by this still-image decoder.");
    }
  }

  private void _ReadFilterIntraModeInfo() {
    this._useFilterIntra = false;
    this._filterIntraMode = Av1FilterIntraMode.DcPred;

    if (!this._seq.EnableFilterIntra
        || this._yMode != Av1PredictionMode.DcPred
        || Av1StructureTables.BlockWidth[(int)this._miSize] > 32
        || Av1StructureTables.BlockHeight[(int)this._miSize] > 32)
      return;

    this._useFilterIntra = this._reader.ReadSymbol(this._cdf.FilterIntra, (int)this._miSize * 3, 2) != 0;
    if (this._useFilterIntra)
      this._filterIntraMode = (Av1FilterIntraMode)this._reader.ReadSymbol(this._cdf.FilterIntraMode, 0, 5);
  }

  private bool _IsLossless() => this._fh.CodedLossless;

  private void _ReadBlockTxSize() {
    if (this._IsLossless()) {
      this._txSize = Av1TxSize.Tx4x4;
      this._SetTxfmContexts(Av1TxSize.Tx4x4, true);
      return;
    }

    if (!Av1Structure.BlockSignalsTxSize(this._miSize)) {
      this._txSize = (Av1TxSize)Av1StructureTables.MaxTxSizeRect[(int)this._miSize];
      this._SetTxfmContexts(this._txSize, false);
      return;
    }

    if (this._fh.TxMode != Av1TxMode.Select) {
      this._txSize = Av1Structure.TxSizeFromTxMode(this._miSize, this._fh.TxMode);
      this._SetTxfmContexts(this._txSize, false);
      return;
    }

    var category = Av1Structure.TxSizeCategory(this._miSize);
    var maxDepth = Av1Structure.MaxTxDepth(this._miSize);
    var context = this._GetTxSizeContext();
    var offset = (category * 3 + context) * Av1CdfContext.TxSizeStride;
    var depth = this._reader.ReadSymbol(this._cdf.TxSize, offset, maxDepth + 1);
    this._txSize = Av1Structure.DepthToTxSize(depth, this._miSize);
    this._SetTxfmContexts(this._txSize, false);
  }

  private int _GetTxSizeContext() {
    var maxTxSize = (Av1TxSize)Av1StructureTables.MaxTxSizeRect[(int)this._miSize];
    var maxWide = Av1StructureTables.TxWidth[(int)maxTxSize];
    var maxHigh = Av1StructureTables.TxHeight[(int)maxTxSize];

    // Intra neighbours always report their transform dimension, so the inter special case in
    // libaom's get_tx_size_context cannot apply here.
    var above = this._aboveTxfmContext[this._miCol] >= maxWide ? 1 : 0;
    var left = this._leftTxfmContext[this._miRow & this._sbMask] >= maxHigh ? 1 : 0;

    if (this._availU && this._availL)
      return above + left;
    if (this._availU)
      return above;
    if (this._availL)
      return left;
    return 0;
  }

  private void _SetTxfmContexts(Av1TxSize txSize, bool lossless) {
    var bw4 = Av1StructureTables.MiSizeWide[(int)this._miSize];
    var bh4 = Av1StructureTables.MiSizeHigh[(int)this._miSize];
    var wide = (byte)Av1StructureTables.TxWidth[(int)txSize];
    var high = (byte)Av1StructureTables.TxHeight[(int)txSize];

    for (var i = 0; i < bw4 && this._miCol + i < this._aboveTxfmContext.Length; ++i)
      this._aboveTxfmContext[this._miCol + i] = wide;
    for (var i = 0; i < bh4; ++i)
      this._leftTxfmContext[(this._miRow & this._sbMask) + i] = high;
  }
}
