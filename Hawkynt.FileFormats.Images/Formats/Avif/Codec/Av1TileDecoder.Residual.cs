using System;

namespace FileFormat.Avif.Codec;

/// <summary>Residual traversal and reconstruction: AV1 5.11.34 residual(), 5.11.35 transform_block()
/// and 7.12.3 reconstruct().</summary>
internal sealed partial class Av1TileDecoder {

  private readonly int[] _coefficients = new int[64 * 64];
  private readonly int[] _cflLuma = new int[32 * 32];

  private void _DecodeResidual() {
    var widthChunks = Math.Max(1, Av1StructureTables.BlockWidth[(int)this._miSize] >> 6);
    var heightChunks = Math.Max(1, Av1StructureTables.BlockHeight[(int)this._miSize] >> 6);
    var chunkSize = widthChunks > 1 || heightChunks > 1 ? Av1BlockSize.Block64x64 : this._miSize;
    var planeCount = 1 + (this._hasChroma ? 2 : 0);

    for (var chunkY = 0; chunkY < heightChunks; ++chunkY)
      for (var chunkX = 0; chunkX < widthChunks; ++chunkX)
        for (var plane = 0; plane < planeCount; ++plane) {
          var txSize = this._IsLossless() ? Av1TxSize.Tx4x4 : this._GetTxSize(plane);
          var stepX = Av1StructureTables.TxWidth[(int)txSize] >> _MI_SIZE_LOG2;
          var stepY = Av1StructureTables.TxHeight[(int)txSize] >> _MI_SIZE_LOG2;
          var subX = this._frame.SubX[plane];
          var subY = this._frame.SubY[plane];
          var planeSize = Av1Structure.PlaneResidualSize(chunkSize, subX, subY);
          var num4x4W = Av1StructureTables.MiSizeWide[(int)planeSize];
          var num4x4H = Av1StructureTables.MiSizeHigh[(int)planeSize];
          var baseX = (this._miCol >> subX) * _MI_SIZE;
          var baseY = (this._miRow >> subY) * _MI_SIZE;

          for (var y = 0; y < num4x4H; y += stepY)
            for (var x = 0; x < num4x4W; x += stepX)
              this._TransformBlock(
                plane, baseX, baseY, txSize,
                x + ((chunkX << 4) >> subX),
                y + ((chunkY << 4) >> subY));
        }
  }

  /// <summary>AV1 5.11.15 get_tx_size(): chroma follows the luma choice but never exceeds 32x32.</summary>
  private Av1TxSize _GetTxSize(int plane) {
    if (plane == 0)
      return this._txSize;

    var planeSize = Av1Structure.PlaneResidualSize(this._miSize, this._frame.SubX[plane], this._frame.SubY[plane]);
    var uvTx = (Av1TxSize)Av1StructureTables.MaxTxSizeRect[(int)planeSize];
    if (Av1StructureTables.TxWidth[(int)uvTx] != 64 && Av1StructureTables.TxHeight[(int)uvTx] != 64)
      return uvTx;

    if (Av1StructureTables.TxWidth[(int)uvTx] == 16)
      return Av1TxSize.Tx16x32;
    if (Av1StructureTables.TxHeight[(int)uvTx] == 16)
      return Av1TxSize.Tx32x16;
    return Av1TxSize.Tx32x32;
  }

  private void _TransformBlock(int plane, int baseX, int baseY, Av1TxSize txSize, int x, int y) {
    var startX = baseX + _MI_SIZE * x;
    var startY = baseY + _MI_SIZE * y;
    var subX = this._frame.SubX[plane];
    var subY = this._frame.SubY[plane];
    var row = (startY << subY) >> _MI_SIZE_LOG2;
    var col = (startX << subX) >> _MI_SIZE_LOG2;
    var subBlockMiRow = row & this._sbMask;
    var subBlockMiCol = col & this._sbMask;
    var stepX = Av1StructureTables.TxWidth[(int)txSize] >> _MI_SIZE_LOG2;
    var stepY = Av1StructureTables.TxHeight[(int)txSize] >> _MI_SIZE_LOG2;
    var maxX = (this._miCols * _MI_SIZE) >> subX;
    var maxY = (this._miRows * _MI_SIZE) >> subY;

    if (startX >= maxX || startY >= maxY)
      return;

    var txWidth = Av1StructureTables.TxWidth[(int)txSize];
    var txHeight = Av1StructureTables.TxHeight[(int)txSize];
    var stride = this._frame.Strides[plane];
    var samples = this._frame.Planes[plane];

    var haveLeft = (plane == 0 ? this._availL : this._availLChroma) || x > 0;
    var haveAbove = (plane == 0 ? this._availU : this._availUChroma) || y > 0;
    var flags = this._blockDecoded[plane];
    var haveAboveRight = flags[this._BlockDecodedIndex((subBlockMiRow >> subY) - 1, (subBlockMiCol >> subX) + stepX)];
    var haveBelowLeft = flags[this._BlockDecodedIndex((subBlockMiRow >> subY) + stepY, (subBlockMiCol >> subX) - 1)];

    var isCfl = plane > 0 && this._uvMode == Av1PredictionMode.UvCflPred;
    var mode = plane == 0 ? this._yMode : isCfl ? Av1PredictionMode.DcPred : this._uvMode;
    var angleDelta = plane == 0 ? this._angleDeltaY : this._angleDeltaUv;

    // libaom clamps the neighbour counts to what actually exists inside the frame; the predictor
    // replicates the last real sample beyond that.
    var xRight = maxX - startX - txWidth;
    var yBelow = maxY - startY - txHeight;
    var topPixels = haveAbove ? Math.Min(txWidth, xRight + txWidth) : 0;
    var leftPixels = haveLeft ? Math.Min(txHeight, yBelow + txHeight) : 0;
    var topRightPixels = haveAboveRight ? Math.Max(0, Math.Min(txWidth, xRight)) : 0;
    var bottomLeftPixels = haveBelowLeft ? Math.Max(0, Math.Min(txHeight, yBelow)) : 0;

    Av1IntraPrediction.Predict(
      samples, stride, startX, startY, txSize,
      mode, angleDelta,
      topPixels, topRightPixels, leftPixels, bottomLeftPixels,
      plane == 0 && this._useFilterIntra, this._filterIntraMode,
      this._seq.EnableIntraEdgeFilter, this._bitDepth,
      this._GetIntraEdgeFilterType(plane));

    if (isCfl)
      this._PredictChromaFromLuma(plane, startX, startY, txWidth, txHeight);

    if (plane == 0) {
      this._maxLumaWidth = startX + stepX * _MI_SIZE;
      this._maxLumaHeight = startY + stepY * _MI_SIZE;
    }

    if (!this._skip) {
      var eob = this._ReadCoefficients(plane, startX, startY, subBlockMiRow, subBlockMiCol, txSize, out var txType);
      if (eob > 0)
        this._Reconstruct(plane, startX, startY, txSize, txType);
    }

    for (var i = 0; i < stepY; ++i)
      for (var j = 0; j < stepX; ++j) {
        var loopRow = (row >> subY) + i;
        var loopCol = (col >> subX) + j;
        if (loopRow < this._miRows && loopCol < this._miCols)
          this._frame.TxSizes[plane][loopRow * this._miCols + loopCol] = (byte)txSize;
        flags[this._BlockDecodedIndex((subBlockMiRow >> subY) + i, (subBlockMiCol >> subX) + j)] = true;
      }
  }

  /// <summary>
  /// libaom <c>get_intra_edge_filter_type</c>: a directional block next to a smooth one filters its
  /// reference edge harder, so the neighbours' modes have to be consulted rather than assumed.
  /// </summary>
  private int _GetIntraEdgeFilterType(int plane) {
    if (plane == 0) {
      var above = this._availU && _IsSmoothMode((Av1PredictionMode)this._frame.YModes[(this._miRow - 1) * this._miCols + this._miCol]);
      var left = this._availL && _IsSmoothMode((Av1PredictionMode)this._frame.YModes[this._miRow * this._miCols + this._miCol - 1]);
      return above || left ? 1 : 0;
    }

    // Chroma looks at the block covering the top-left luma position of its own area.
    var subX = this._frame.SubX[plane];
    var subY = this._frame.SubY[plane];
    var baseRow = this._miRow - (this._miRow & subY);
    var baseCol = this._miCol - (this._miCol & subX);

    var chromaAbove = this._availUChroma
      && _IsSmoothMode((Av1PredictionMode)this._frame.UvModes[(baseRow - 1) * this._miCols + baseCol]);
    var chromaLeft = this._availLChroma
      && _IsSmoothMode((Av1PredictionMode)this._frame.UvModes[baseRow * this._miCols + baseCol - 1]);
    return chromaAbove || chromaLeft ? 1 : 0;
  }

  private static bool _IsSmoothMode(Av1PredictionMode mode) =>
    mode is Av1PredictionMode.SmoothPred or Av1PredictionMode.SmoothVPred or Av1PredictionMode.SmoothHPred;

  /// <summary>
  /// AV1 7.11.5: adds the scaled luma AC signal to the DC prediction already in place. The luma the
  /// chroma block draws on is bounded by the last luma transform block that was inside the frame,
  /// which is what <see cref="_maxLumaWidth"/> and <see cref="_maxLumaHeight"/> record.
  /// </summary>
  private void _PredictChromaFromLuma(int plane, int startX, int startY, int width, int height) {
    var subX = this._frame.SubX[plane];
    var subY = this._frame.SubY[plane];
    var lumaX = startX << subX;
    var lumaY = startY << subY;

    Av1IntraPrediction.BuildCflAcBuffer(
      this._frame.Planes[0], this._frame.Strides[0], lumaX, lumaY, width, height, subX, subY,
      this._maxLumaWidth - lumaX, this._maxLumaHeight - lumaY, this._cflLuma);

    Av1IntraPrediction.PredictCfl(
      this._frame.Planes[plane], this._frame.Strides[plane], startX, startY, width, height,
      this._cflLuma, plane == 1 ? this._cflAlphaU : this._cflAlphaV, this._bitDepth);
  }

  private void _Reconstruct(int plane, int startX, int startY, Av1TxSize txSize, Av1TxType txType) {
    var txWidth = Av1StructureTables.TxWidth[(int)txSize];
    var txHeight = Av1StructureTables.TxHeight[(int)txSize];
    var residual = this._coefficients.AsSpan(0, txWidth * txHeight);

    if (this._IsLossless())
      Av1InverseTransform.InverseWalshHadamard4x4(residual);
    else
      Av1InverseTransform.Inverse(residual, txSize, txType, this._bitDepth);

    var samples = this._frame.Planes[plane];
    var stride = this._frame.Strides[plane];
    var maximum = (1 << this._bitDepth) - 1;

    // AV1 7.12.3 step 3 writes the whole transform block, including the part past the last visible
    // column or row. Those samples are never output, but the next block's intra prediction and any
    // chroma-from-luma reference read them, so clipping the write here changes the picture.
    for (var i = 0; i < txHeight; ++i) {
      var rowBase = (startY + i) * stride + startX;
      for (var j = 0; j < txWidth; ++j)
        samples[rowBase + j] = (short)Math.Clamp(samples[rowBase + j] + residual[i * txWidth + j], 0, maximum);
    }
  }
}
