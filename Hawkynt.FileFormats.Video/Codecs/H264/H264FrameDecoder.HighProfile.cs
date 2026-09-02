using System;

namespace FileFormat.Codecs.H264;

internal sealed partial class H264FrameDecoder {
  private bool[]? _transform8x8;

  internal bool Transform8x8Of(int mbAddr)
    => (this._transform8x8 is not null && this._transform8x8[mbAddr])
       || (this._cabacTransform8x8 is not null && this._cabacTransform8x8[mbAddr]);

  private void _MarkTransform8x8(int mbAddr)
    => (this._transform8x8 ??= new bool[this._mbCount])[mbAddr] = true;

  private void _DecodeIntra8x8(ref H264BitReader reader, int mbAddr) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    this._kind[mbAddr] = H264MacroblockKind.Intra8x8;
    this._MarkTransform8x8(mbAddr);

    // Four 8x8 prediction modes are coded in raster order. Store each mode in the four 4x4 state
    // entries it covers so neighbouring 4x4/8x8 units share the same mode-derivation machinery.
    for (var i8x8 = 0; i8x8 < 4; ++i8x8) {
      var (bx, by) = _BlockPosition(i8x8 * 4);
      var blockX = mbX * 4 + (bx >> 2);
      var blockY = mbY * 4 + (by >> 2);
      var predicted = this._PredictIntraMode(blockX, blockY);
      var mode = predicted;
      if (reader.ReadBit() == 0) {
        var remaining = reader.ReadBits(3);
        mode = remaining < predicted ? remaining : remaining + 1;
      }

      for (var dy = 0; dy < 2; ++dy)
        for (var dx = 0; dx < 2; ++dx)
          this._intra4x4Mode[(blockY + dy) * this._blockWidth + blockX + dx] = (sbyte)mode;
    }

    var chromaMode = reader.ReadUnsignedExpGolomb();
    var cbp = H264CavlcTables.ReadCodedBlockPattern(ref reader, intra: true);
    this._ReadResidualAndQp(
      ref reader, mbAddr, cbp & 15, cbp >> 4, intra16x16: false, transform8x8: true);

    var qp = this._qpY[mbAddr];
    var scaling = this._scalingLists.EightByEight(intra: true);
    Span<byte> top = stackalloc byte[16];
    Span<byte> left = stackalloc byte[8];
    Span<byte> prediction = stackalloc byte[64];
    Span<int> residual = stackalloc int[64];

    for (var i8x8 = 0; i8x8 < 4; ++i8x8) {
      var localX = (i8x8 & 1) * 8;
      var localY = (i8x8 >> 1) * 8;
      var x = mbX * 16 + localX;
      var y = mbY * 16 + localY;
      var blockX = mbX * 4 + (localX >> 2);
      var blockY = mbY * 4 + (localY >> 2);
      var mode = this._intra4x4Mode[blockY * this._blockWidth + blockX];

      var neighbours = this._GatherLuma8x8Neighbours(
        mbAddr, x, y, top, left, out var topLeft);
      H264Intra8x8Prediction.Predict(
        mode, top, left, topLeft,
        neighbours.Top, neighbours.TopRight, neighbours.Left, neighbours.TopLeft,
        prediction);

      var hasResidual = (cbp & (1 << i8x8)) != 0;
      if (hasResidual) {
        residual.Clear();
        H264Transform8x8.DecodeBlock(this._lumaLevels.AsSpan(i8x8 * 64, 64), qp, scaling, residual);
        _AddResidual(this.Picture.Luma, this.Picture.LumaWidth, x, y, 8, prediction, residual);
      } else {
        _CopyPrediction(this.Picture.Luma, this.Picture.LumaWidth, x, y, 8, prediction);
      }

      for (var dy = 0; dy < 2; ++dy)
        for (var dx = 0; dx < 2; ++dx)
          this._blockReconstructed[(blockY + dy) * this._blockWidth + blockX + dx] = true;
    }

    this._ReconstructChroma(mbAddr, chromaMode, intra: true);
  }

  private void _AddInter8x8Residuals(int mbAddr, int cbpLuma) {
    this._MarkTransform8x8(mbAddr);
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var scaling = this._scalingLists.EightByEight(intra: false);
    var qp = this._qpY[mbAddr];
    Span<int> residual = stackalloc int[64];

    for (var i8x8 = 0; i8x8 < 4; ++i8x8) {
      if ((cbpLuma & (1 << i8x8)) == 0)
        continue;

      residual.Clear();
      H264Transform8x8.DecodeBlock(this._lumaLevels.AsSpan(i8x8 * 64, 64), qp, scaling, residual);
      _AddResidualInPlace(
        this.Picture.Luma,
        this.Picture.LumaWidth,
        mbX * 16 + (i8x8 & 1) * 8,
        mbY * 16 + (i8x8 >> 1) * 8,
        8,
        residual);
    }
  }

  private (bool Top, bool TopRight, bool Left, bool TopLeft) _GatherLuma8x8Neighbours(
    int mbAddr,
    int x,
    int y,
    Span<byte> top,
    Span<byte> left,
    out byte topLeft) {
    var plane = this.Picture.Luma;
    var stride = this.Picture.LumaWidth;

    var topAvailable = true;
    for (var i = 0; i < 8; ++i)
      topAvailable &= this._ReconstructedSampleAvailable(mbAddr, x + i, y - 1);

    var topRightAvailable = true;
    for (var i = 8; i < 16; ++i)
      topRightAvailable &= this._ReconstructedSampleAvailable(mbAddr, x + i, y - 1);

    var leftAvailable = true;
    for (var i = 0; i < 8; ++i)
      leftAvailable &= this._ReconstructedSampleAvailable(mbAddr, x - 1, y + i);

    var topLeftAvailable = this._ReconstructedSampleAvailable(mbAddr, x - 1, y - 1);

    for (var i = 0; i < 8; ++i) {
      top[i] = topAvailable ? plane[(y - 1) * stride + x + i] : (byte)0;
      left[i] = leftAvailable ? plane[(y + i) * stride + x - 1] : (byte)0;
    }
    for (var i = 8; i < 16; ++i)
      top[i] = topRightAvailable ? plane[(y - 1) * stride + x + i] : (byte)0;

    topLeft = topLeftAvailable ? plane[(y - 1) * stride + x - 1] : (byte)0;
    return (topAvailable, topRightAvailable, leftAvailable, topLeftAvailable);
  }

  private bool _ReconstructedSampleAvailable(int mbAddr, int x, int y) {
    if (x < 0 || y < 0 || x >= this._mbWidth * 16 || y >= this._mbHeight * 16)
      return false;

    var neighbourMb = y / 16 * this._mbWidth + x / 16;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return false;
    if (this._kind[neighbourMb] == H264MacroblockKind.Inter && this._header.Pps.ConstrainedIntraPredFlag)
      return false;
    if (neighbourMb != mbAddr)
      return true;

    var at = (y >> 2) * this._blockWidth + (x >> 2);
    return this._blockReconstructed[at];
  }
}
