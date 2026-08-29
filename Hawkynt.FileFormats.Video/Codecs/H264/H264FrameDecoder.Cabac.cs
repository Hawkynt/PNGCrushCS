using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>CABAC slice-data syntax connected to the shared H.264 reconstruction engine.</summary>
internal sealed partial class H264FrameDecoder {
  private bool[]? _cabacSkipped;
  private bool[]? _cabacBDirect;
  private bool[]? _cabacDirectBlock;
  private byte[]? _cabacCbpLuma;
  private byte[]? _cabacCbpChroma;
  private sbyte[]? _cabacIntraChromaMode;
  private bool[]? _cabacTransform8x8;
  private sbyte[]? _cabacRefIdx0;
  private sbyte[]? _cabacRefIdx1;
  private short[]? _cabacMvdX0;
  private short[]? _cabacMvdY0;
  private short[]? _cabacMvdX1;
  private short[]? _cabacMvdY1;
  private bool[]? _cabacLumaCbf;
  private bool[]? _cabacChromaCbf;
  private bool[]? _cabacLumaDcCbf;
  private bool[]? _cabacChromaDcCbf;

  /// <summary>Decodes one progressive 8-bit 4:2:0 CABAC I/P/B slice.</summary>
  internal void DecodeCabacSlice(
    ref H264BitReader reader,
    H264SliceHeader header,
    H264Picture[] referenceList0,
    H264Picture[] referenceList1) {
    if (!header.Pps.EntropyCodingModeFlag)
      throw new ArgumentException("DecodeCabacSlice requires entropy_coding_mode_flag=1.", nameof(header));

    this._EnsureCabacState();
    if (header.IsB)
      this._EnsureList1State();
    this._header = header;
    this._referenceList = referenceList0;
    this._referenceList1 = header.IsB ? referenceList1 : [];
    this._scalingLists = header.Pps.ResolveScalingLists(header.Sps);
    this._currentSliceId = this._nextSliceId++;
    this._qpRunning = header.SliceQpY;

    var contexts = new H264CabacContexts(header);
    var decoder = new H264CabacDecoder(reader);
    var lastQpDelta = 0;
    var mbAddr = header.FirstMbInSlice;
    if (mbAddr >= this._mbCount)
      throw new InvalidDataException(
        $"An H.264 CABAC slice states first_mb_in_slice {mbAddr}, beyond {this._mbCount} macroblocks.");

    while (true) {
      this._RefuseAddressPastPicture(mbAddr, "a CABAC macroblock");
      var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
      var skipped = !header.IsIntra
        && H264CabacSyntax.DecodeSkipFlag(ref decoder, contexts, header.IsB, left, above);
      if (skipped) {
        this._CabacPrepareMacroblock(mbAddr, skipped: true, direct: header.IsB);
        if (header.IsB)
          this._DecodeBSkipped(mbAddr);
        else
          this._DecodeSkipped(mbAddr);
        // The CAVLC skip helpers call _BeginMacroblock themselves; restore the CABAC metadata that
        // _BeginMacroblock intentionally knows nothing about.
        this._CabacMarkSkipped(mbAddr, header.IsB);
        lastQpDelta = 0;
      } else {
        this._BeginMacroblock(mbAddr);
        if (header.IsB)
          this._ClearList1Motion(mbAddr);
        this._CabacPrepareMacroblock(mbAddr, skipped: false, direct: false);
        this._DecodeCabacMacroblock(ref decoder, contexts, mbAddr, ref lastQpDelta);
      }

      if (decoder.DecodeTerminate() != 0) {
        reader = decoder.SnapshotReader();
        return;
      }
      ++mbAddr;
    }
  }

  private void _DecodeCabacMacroblock(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    ref int lastQpDelta) {
    var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
    var mbType = this._header.IsIntra
      ? H264CabacSyntax.DecodeMbTypeI(ref decoder, contexts, left, above)
      : this._header.IsB
        ? H264CabacSyntax.DecodeMbTypeB(ref decoder, contexts, left, above)
        : H264CabacSyntax.DecodeMbTypeP(ref decoder, contexts);

    var intraOffset = this._header.IsIntra ? 0 : this._header.IsB ? 23 : 5;
    if (mbType >= intraOffset && (this._header.IsIntra || mbType >= intraOffset)) {
      var intraType = mbType - intraOffset;
      if (intraType is >= 0 and <= 25) {
        this._DecodeCabacIntraMacroblock(ref decoder, contexts, mbAddr, intraType, ref lastQpDelta);
        return;
      }
    }

    if (this._header.IsB)
      this._DecodeCabacBInter(ref decoder, contexts, mbAddr, mbType, ref lastQpDelta);
    else
      this._DecodeCabacPInter(ref decoder, contexts, mbAddr, mbType, ref lastQpDelta);
  }

  private void _DecodeCabacIntraMacroblock(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    int intraType,
    ref int lastQpDelta) {
    if ((uint)intraType > 25)
      throw new InvalidDataException($"An H.264 CABAC intra mb_type decoded {intraType}, outside Table 9-36.");

    if (intraType == _PCM_MB_TYPE_I) {
      this._CabacDecodePcm(ref decoder, mbAddr);
      this._cabacCbpLuma![mbAddr] = 15;
      this._cabacCbpChroma![mbAddr] = 2;
      this._cabacIntraChromaMode![mbAddr] = 0;
      this._cabacTransform8x8![mbAddr] = false;
      this._cabacLumaDcCbf![mbAddr] = true;
      this._cabacChromaDcCbf![mbAddr * 2] = true;
      this._cabacChromaDcCbf[mbAddr * 2 + 1] = true;
      lastQpDelta = 0;
      return;
    }

    if (intraType == 0) {
      var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
      var transform8x8 = this._header.Pps.Transform8x8ModeFlag
        && H264CabacSyntax.DecodeTransform8x8Flag(ref decoder, contexts, left, above);
      this._cabacTransform8x8![mbAddr] = transform8x8;
      if (transform8x8)
        this._DecodeCabacIntra8x8(ref decoder, contexts, mbAddr, ref lastQpDelta);
      else
        this._DecodeCabacIntra4x4(ref decoder, contexts, mbAddr, ref lastQpDelta);
      return;
    }

    this._DecodeCabacIntra16x16(ref decoder, contexts, mbAddr, intraType, ref lastQpDelta);
  }

  private void _DecodeCabacIntra4x4(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    ref int lastQpDelta) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    this._kind[mbAddr] = H264MacroblockKind.Intra4x4;

    for (var blkIdx = 0; blkIdx < 16; ++blkIdx) {
      var (bx, by) = _BlockPosition(blkIdx);
      var blockX = mbX * 4 + (bx >> 2);
      var blockY = mbY * 4 + (by >> 2);
      var predicted = this._PredictIntraMode(blockX, blockY);
      this._intra4x4Mode[blockY * this._blockWidth + blockX] = (sbyte)
        H264CabacSyntax.DecodeIntraPredictionMode(ref decoder, contexts, predicted);
    }

    var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
    var chromaMode = H264CabacSyntax.DecodeIntraChromaPredMode(ref decoder, contexts, left, above);
    this._cabacIntraChromaMode![mbAddr] = (sbyte)chromaMode;
    var cbp = H264CabacSyntax.DecodeCodedBlockPattern(ref decoder, contexts, left, above);
    this._ReadCabacResidualAndQp(
      ref decoder, contexts, mbAddr, cbp & 15, cbp >> 4, intra16x16: false, transform8x8: false, ref lastQpDelta);

    var qp = this._qpY[mbAddr];
    var scaling = this._scalingLists.FourByFour(0);
    Span<byte> pred = stackalloc byte[16];
    Span<int> residual = stackalloc int[16];
    Span<byte> top = stackalloc byte[8];
    Span<byte> leftSamples = stackalloc byte[4];
    for (var blkIdx = 0; blkIdx < 16; ++blkIdx) {
      var (bx, by) = _BlockPosition(blkIdx);
      var x = mbX * 16 + bx;
      var y = mbY * 16 + by;
      var blockX = mbX * 4 + (bx >> 2);
      var blockY = mbY * 4 + (by >> 2);
      var neighbours = this._GatherLuma4x4Neighbours(mbAddr, x, y, blkIdx, top, leftSamples, out var topLeft);
      H264IntraPrediction.Predict4x4(
        this._intra4x4Mode[blockY * this._blockWidth + blockX], top, leftSamples, topLeft,
        neighbours.Top, neighbours.Left, neighbours.TopLeft, pred);
      if (this._lumaCoeffCount[blockY * this._blockWidth + blockX] > 0) {
        H264Transform.DecodeBlock(
          this._lumaLevels.AsSpan(blkIdx * 16, 16), qp, hasSeparateDc: false, 0, scaling, residual);
        _AddResidual(this.Picture.Luma, this.Picture.LumaWidth, x, y, 4, pred, residual);
      } else
        _CopyPrediction(this.Picture.Luma, this.Picture.LumaWidth, x, y, 4, pred);
      this._blockReconstructed[blockY * this._blockWidth + blockX] = true;
    }
    this._ReconstructChroma(mbAddr, chromaMode, intra: true);
  }

  private void _DecodeCabacIntra8x8(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    ref int lastQpDelta) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    this._kind[mbAddr] = H264MacroblockKind.Intra8x8;
    for (var i8x8 = 0; i8x8 < 4; ++i8x8) {
      var (bx, by) = _BlockPosition(i8x8 * 4);
      var blockX = mbX * 4 + (bx >> 2);
      var blockY = mbY * 4 + (by >> 2);
      var predicted = this._PredictIntraMode(blockX, blockY);
      var mode = H264CabacSyntax.DecodeIntraPredictionMode(ref decoder, contexts, predicted);
      for (var dy = 0; dy < 2; ++dy)
        for (var dx = 0; dx < 2; ++dx)
          this._intra4x4Mode[(blockY + dy) * this._blockWidth + blockX + dx] = (sbyte)mode;
    }

    var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
    var chromaMode = H264CabacSyntax.DecodeIntraChromaPredMode(ref decoder, contexts, left, above);
    this._cabacIntraChromaMode![mbAddr] = (sbyte)chromaMode;
    var cbp = H264CabacSyntax.DecodeCodedBlockPattern(ref decoder, contexts, left, above);
    this._ReadCabacResidualAndQp(
      ref decoder, contexts, mbAddr, cbp & 15, cbp >> 4, intra16x16: false, transform8x8: true, ref lastQpDelta);

    var qp = this._qpY[mbAddr];
    var scaling = this._scalingLists.EightByEight(intra: true);
    Span<byte> top = stackalloc byte[16];
    Span<byte> leftSamples = stackalloc byte[8];
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
      var neighbours = this._GatherLuma8x8Neighbours(mbAddr, x, y, top, leftSamples, out var topLeft);
      H264Intra8x8Prediction.Predict(
        mode, top, leftSamples, topLeft,
        neighbours.Top, neighbours.TopRight, neighbours.Left, neighbours.TopLeft, prediction);
      if ((cbp & (1 << i8x8)) != 0) {
        residual.Clear();
        H264Transform8x8.DecodeBlock(this._lumaLevels.AsSpan(i8x8 * 64, 64), qp, scaling, residual);
        _AddResidual(this.Picture.Luma, this.Picture.LumaWidth, x, y, 8, prediction, residual);
      } else
        _CopyPrediction(this.Picture.Luma, this.Picture.LumaWidth, x, y, 8, prediction);
      for (var dy = 0; dy < 2; ++dy)
        for (var dx = 0; dx < 2; ++dx)
          this._blockReconstructed[(blockY + dy) * this._blockWidth + blockX + dx] = true;
    }
    this._ReconstructChroma(mbAddr, chromaMode, intra: true);
  }

  private void _DecodeCabacIntra16x16(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    int intraType,
    ref int lastQpDelta) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    this._kind[mbAddr] = H264MacroblockKind.Intra16x16;
    var index = intraType - 1;
    var lumaMode = index % 4;
    var cbpChroma = index / 4 % 3;
    var cbpLuma = index >= 12 ? 15 : 0;
    var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
    var chromaMode = H264CabacSyntax.DecodeIntraChromaPredMode(ref decoder, contexts, left, above);
    this._cabacIntraChromaMode![mbAddr] = (sbyte)chromaMode;
    this._ReadCabacResidualAndQp(
      ref decoder, contexts, mbAddr, cbpLuma, cbpChroma, intra16x16: true, transform8x8: false, ref lastQpDelta);

    var qp = this._qpY[mbAddr];
    var scaling = this._scalingLists.FourByFour(0);
    Span<byte> pred = stackalloc byte[256];
    Span<byte> top = stackalloc byte[16];
    Span<byte> leftSamples = stackalloc byte[16];
    var neighbours = this._GatherLumaMacroblockNeighbours(mbAddr, top, leftSamples, out var topLeft);
    H264IntraPrediction.Predict16x16(
      lumaMode, top, leftSamples, topLeft, neighbours.Top, neighbours.Left, neighbours.TopLeft, pred);
    Span<int> dc = stackalloc int[16];
    H264Transform.DecodeLumaDc(this._lumaDcLevels, qp, scaling, dc);
    Span<int> residual = stackalloc int[16];
    Span<byte> blockPrediction = stackalloc byte[16];
    for (var blkIdx = 0; blkIdx < 16; ++blkIdx) {
      var (bx, by) = _BlockPosition(blkIdx);
      for (var row = 0; row < 4; ++row)
        for (var column = 0; column < 4; ++column)
          blockPrediction[(row << 2) + column] = pred[((by + row) << 4) + bx + column];
      var dcValue = dc[(by >> 2) * 4 + (bx >> 2)];
      H264Transform.DecodeBlock(
        this._lumaLevels.AsSpan(blkIdx * 16, 16), qp, hasSeparateDc: true, dcValue, scaling, residual);
      var blockX = mbX * 4 + (bx >> 2);
      var blockY = mbY * 4 + (by >> 2);
      _AddResidual(this.Picture.Luma, this.Picture.LumaWidth, mbX * 16 + bx, mbY * 16 + by, 4, blockPrediction, residual);
      this._blockReconstructed[blockY * this._blockWidth + blockX] = true;
    }
    this._ReconstructChroma(mbAddr, chromaMode, intra: true);
  }

  private void _DecodeCabacPInter(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    int mbType,
    ref int lastQpDelta) {
    if ((uint)mbType > 3)
      throw new InvalidDataException($"An H.264 CABAC P mb_type decoded unsupported inter type {mbType}.");
    this._kind[mbAddr] = H264MacroblockKind.Inter;
    this._cabacIntraChromaMode![mbAddr] = 0;
    if (mbType == 3) {
      this._DecodeCabacP8x8(ref decoder, contexts, mbAddr, ref lastQpDelta);
      return;
    }

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var (partWidth, partHeight, partCount) = mbType switch {
      0 => (16, 16, 1),
      1 => (16, 8, 2),
      _ => (8, 16, 2),
    };
    Span<int> refs = stackalloc int[2];
    for (var part = 0; part < partCount; ++part) {
      var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);
      refs[part] = this._DecodeCabacReferenceIndex(ref decoder, contexts, 0, mbAddr, x, y);
      this._CabacFillReference(0, x, y, partWidth, partHeight, refs[part]);
    }

    Span<int> mvXs = stackalloc int[2];
    Span<int> mvYs = stackalloc int[2];
    for (var part = 0; part < partCount; ++part) {
      var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);
      var (predX, predY) = this._PredictMotion(mbAddr, x, y, partWidth, partHeight, refs[part], part, partCount);
      var mvdX = this._DecodeCabacMvd(ref decoder, contexts, 0, mbAddr, x, y, vertical: false);
      var mvdY = this._DecodeCabacMvd(ref decoder, contexts, 0, mbAddr, x, y, vertical: true);
      mvXs[part] = predX + mvdX;
      mvYs[part] = predY + mvdY;
      this._CabacFillMvd(0, x, y, partWidth, partHeight, mvdX, mvdY);
      this._AssignMotion(x >> 2, y >> 2, partWidth >> 2, partHeight >> 2, refs[part], mvXs[part], mvYs[part]);
    }

    var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
    var cbp = H264CabacSyntax.DecodeCodedBlockPattern(ref decoder, contexts, left, above);
    var transform8x8 = this._header.Pps.Transform8x8ModeFlag && (cbp & 15) != 0
      && H264CabacSyntax.DecodeTransform8x8Flag(ref decoder, contexts, left, above);
    this._cabacTransform8x8![mbAddr] = transform8x8;
    this._ReadCabacResidualAndQp(
      ref decoder, contexts, mbAddr, cbp & 15, cbp >> 4, intra16x16: false, transform8x8, ref lastQpDelta);
    for (var part = 0; part < partCount; ++part) {
      var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);
      this._Predict(mbAddr, refs[part], x, y, partWidth, partHeight, mvXs[part], mvYs[part], addResidual: !transform8x8);
    }
    if (transform8x8)
      this._AddInter8x8Residuals(mbAddr, cbp & 15);
    this._MarkReconstructed(mbX, mbY);
    this._ReconstructChroma(mbAddr, 0, intra: false);
  }

  private void _DecodeCabacP8x8(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    ref int lastQpDelta) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    Span<int> subType = stackalloc int[4];
    var canTransform8x8 = true;
    for (var part = 0; part < 4; ++part) {
      subType[part] = H264CabacSyntax.DecodeSubMbTypeP(ref decoder, contexts);
      canTransform8x8 &= subType[part] == 0;
    }

    Span<int> refs = stackalloc int[4];
    for (var part = 0; part < 4; ++part) {
      var x = mbX * 16 + (part & 1) * 8;
      var y = mbY * 16 + (part >> 1) * 8;
      refs[part] = this._DecodeCabacReferenceIndex(ref decoder, contexts, 0, mbAddr, x, y);
      this._CabacFillReference(0, x, y, 8, 8, refs[part]);
    }

    for (var part = 0; part < 4; ++part) {
      var (subWidth, subHeight, subCount) = subType[part] switch {
        0 => (8, 8, 1),
        1 => (8, 4, 2),
        2 => (4, 8, 2),
        _ => (4, 4, 4),
      };
      var partX = mbX * 16 + (part & 1) * 8;
      var partY = mbY * 16 + (part >> 1) * 8;
      for (var sub = 0; sub < subCount; ++sub) {
        var x = partX + (subWidth == 4 ? (sub & 1) * 4 : 0);
        var y = partY + (subHeight == 4 ? (subCount == 4 ? (sub >> 1) * 4 : sub * 4) : 0);
        var (predX, predY) = this._PredictMotion(mbAddr, x, y, subWidth, subHeight, refs[part], part, 4);
        var mvdX = this._DecodeCabacMvd(ref decoder, contexts, 0, mbAddr, x, y, vertical: false);
        var mvdY = this._DecodeCabacMvd(ref decoder, contexts, 0, mbAddr, x, y, vertical: true);
        this._CabacFillMvd(0, x, y, subWidth, subHeight, mvdX, mvdY);
        this._AssignMotion(x >> 2, y >> 2, subWidth >> 2, subHeight >> 2, refs[part], predX + mvdX, predY + mvdY);
      }
    }

    var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
    var cbp = H264CabacSyntax.DecodeCodedBlockPattern(ref decoder, contexts, left, above);
    var transform8x8 = this._header.Pps.Transform8x8ModeFlag && canTransform8x8 && (cbp & 15) != 0
      && H264CabacSyntax.DecodeTransform8x8Flag(ref decoder, contexts, left, above);
    this._cabacTransform8x8![mbAddr] = transform8x8;
    this._ReadCabacResidualAndQp(
      ref decoder, contexts, mbAddr, cbp & 15, cbp >> 4, intra16x16: false, transform8x8, ref lastQpDelta);
    for (var by = 0; by < 4; ++by)
      for (var bx = 0; bx < 4; ++bx) {
        var at = (mbY * 4 + by) * this._blockWidth + mbX * 4 + bx;
        this._Predict(
          mbAddr, this._refIdx[at], mbX * 16 + bx * 4, mbY * 16 + by * 4, 4, 4,
          this._mvX[at], this._mvY[at], addResidual: !transform8x8);
      }
    if (transform8x8)
      this._AddInter8x8Residuals(mbAddr, cbp & 15);
    this._MarkReconstructed(mbX, mbY);
    this._ReconstructChroma(mbAddr, 0, intra: false);
  }

  private void _DecodeCabacBInter(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    int mbType,
    ref int lastQpDelta) {
    if ((uint)mbType > 22)
      throw new InvalidDataException($"An H.264 CABAC B mb_type decoded inter type {mbType} outside 0..22.");
    this._kind[mbAddr] = H264MacroblockKind.Inter;
    this._cabacIntraChromaMode![mbAddr] = 0;
    if (mbType == 22) {
      this._DecodeCabacB8x8(ref decoder, contexts, mbAddr, ref lastQpDelta);
      return;
    }

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    if (mbType == 0) {
      this._cabacBDirect![mbAddr] = true;
      this._CabacFillDirect(mbX * 16, mbY * 16, 16, 16, true);
      this._DeriveDirect(mbAddr, mbX * 16, mbY * 16, 16, 16);
      this._FinishCabacBInter(
        ref decoder, contexts, mbAddr, canTransform8x8: this._header.Sps.Direct8x8InferenceFlag, ref lastQpDelta);
      return;
    }

    var (partWidth, partHeight, firstMode, secondMode) = _BMacroblockLayout(mbType);
    var partCount = partWidth == 16 && partHeight == 16 ? 1 : 2;
    Span<BPredMode> modes = stackalloc BPredMode[2];
    modes[0] = firstMode;
    modes[1] = secondMode;
    Span<int> ref0 = stackalloc int[2];
    Span<int> ref1 = stackalloc int[2];
    ref0.Fill(-1);
    ref1.Fill(-1);

    for (var part = 0; part < partCount; ++part) {
      var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);
      if (_UsesList0(modes[part])) {
        ref0[part] = this._DecodeCabacReferenceIndex(ref decoder, contexts, 0, mbAddr, x, y);
        this._CabacFillReference(0, x, y, partWidth, partHeight, ref0[part]);
      }
    }
    for (var part = 0; part < partCount; ++part) {
      var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);
      if (_UsesList1(modes[part])) {
        ref1[part] = this._DecodeCabacReferenceIndex(ref decoder, contexts, 1, mbAddr, x, y);
        this._CabacFillReference(1, x, y, partWidth, partHeight, ref1[part]);
      }
    }

    for (var list = 0; list < 2; ++list)
      for (var part = 0; part < partCount; ++part) {
        if (list == 0 && !_UsesList0(modes[part]) || list == 1 && !_UsesList1(modes[part]))
          continue;
        var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
        var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);
        var reference = list == 0 ? ref0[part] : ref1[part];
        var (predX, predY) = this._PredictMotionForList(list, mbAddr, x, y, partWidth, partHeight, reference, part, partCount);
        var mvdX = this._DecodeCabacMvd(ref decoder, contexts, list, mbAddr, x, y, vertical: false);
        var mvdY = this._DecodeCabacMvd(ref decoder, contexts, list, mbAddr, x, y, vertical: true);
        this._CabacFillMvd(list, x, y, partWidth, partHeight, mvdX, mvdY);
        this._AssignMotionList(
          list, x >> 2, y >> 2, partWidth >> 2, partHeight >> 2, reference, predX + mvdX, predY + mvdY);
      }

    this._FinishCabacBInter(ref decoder, contexts, mbAddr, canTransform8x8: true, ref lastQpDelta);
  }

  private void _DecodeCabacB8x8(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    ref int lastQpDelta) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    Span<int> subType = stackalloc int[4];
    Span<BPredMode> mode = stackalloc BPredMode[4];
    var canTransform8x8 = true;
    for (var part = 0; part < 4; ++part) {
      subType[part] = H264CabacSyntax.DecodeSubMbTypeB(ref decoder, contexts);
      mode[part] = _BSubMode(subType[part]);
      var directSmall = subType[part] == 0 && !this._header.Sps.Direct8x8InferenceFlag;
      canTransform8x8 &= subType[part] <= 3 && !directSmall;
    }

    Span<int> ref0 = stackalloc int[4];
    Span<int> ref1 = stackalloc int[4];
    ref0.Fill(-1);
    ref1.Fill(-1);
    for (var part = 0; part < 4; ++part) {
      var x = mbX * 16 + (part & 1) * 8;
      var y = mbY * 16 + (part >> 1) * 8;
      if (_UsesList0(mode[part])) {
        ref0[part] = this._DecodeCabacReferenceIndex(ref decoder, contexts, 0, mbAddr, x, y);
        this._CabacFillReference(0, x, y, 8, 8, ref0[part]);
      }
    }
    for (var part = 0; part < 4; ++part) {
      var x = mbX * 16 + (part & 1) * 8;
      var y = mbY * 16 + (part >> 1) * 8;
      if (_UsesList1(mode[part])) {
        ref1[part] = this._DecodeCabacReferenceIndex(ref decoder, contexts, 1, mbAddr, x, y);
        this._CabacFillReference(1, x, y, 8, 8, ref1[part]);
      }
    }

    for (var list = 0; list < 2; ++list)
      for (var part = 0; part < 4; ++part) {
        if (mode[part] == BPredMode.Direct)
          continue;
        if (list == 0 && !_UsesList0(mode[part]) || list == 1 && !_UsesList1(mode[part]))
          continue;
        var (subWidth, subHeight, subCount) = _BSubGeometry(subType[part], this._header.Sps.Direct8x8InferenceFlag);
        var baseX = mbX * 16 + (part & 1) * 8;
        var baseY = mbY * 16 + (part >> 1) * 8;
        var reference = list == 0 ? ref0[part] : ref1[part];
        for (var sub = 0; sub < subCount; ++sub) {
          var x = baseX + (subWidth == 4 ? (sub & 1) * 4 : 0);
          var y = baseY + (subHeight == 4 ? (subCount == 4 ? (sub >> 1) * 4 : sub * 4) : 0);
          var (predX, predY) = this._PredictMotionForList(list, mbAddr, x, y, subWidth, subHeight, reference, part, 4);
          var mvdX = this._DecodeCabacMvd(ref decoder, contexts, list, mbAddr, x, y, vertical: false);
          var mvdY = this._DecodeCabacMvd(ref decoder, contexts, list, mbAddr, x, y, vertical: true);
          this._CabacFillMvd(list, x, y, subWidth, subHeight, mvdX, mvdY);
          this._AssignMotionList(
            list, x >> 2, y >> 2, subWidth >> 2, subHeight >> 2, reference, predX + mvdX, predY + mvdY);
        }
      }

    for (var part = 0; part < 4; ++part)
      if (mode[part] == BPredMode.Direct) {
        var baseX = mbX * 16 + (part & 1) * 8;
        var baseY = mbY * 16 + (part >> 1) * 8;
        this._CabacFillDirect(baseX, baseY, 8, 8, true);
        if (this._header.Sps.Direct8x8InferenceFlag)
          this._DeriveDirect(mbAddr, baseX, baseY, 8, 8);
        else
          for (var by = 0; by < 2; ++by)
            for (var bx = 0; bx < 2; ++bx)
              this._DeriveDirect(mbAddr, baseX + bx * 4, baseY + by * 4, 4, 4);
      }

    this._FinishCabacBInter(ref decoder, contexts, mbAddr, canTransform8x8, ref lastQpDelta);
  }

  private void _FinishCabacBInter(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    bool canTransform8x8,
    ref int lastQpDelta) {
    var (left, above) = this._CabacMacroblockNeighbours(mbAddr);
    var cbp = H264CabacSyntax.DecodeCodedBlockPattern(ref decoder, contexts, left, above);
    var transform8x8 = this._header.Pps.Transform8x8ModeFlag && canTransform8x8 && (cbp & 15) != 0
      && H264CabacSyntax.DecodeTransform8x8Flag(ref decoder, contexts, left, above);
    this._cabacTransform8x8![mbAddr] = transform8x8;
    this._ReadCabacResidualAndQp(
      ref decoder, contexts, mbAddr, cbp & 15, cbp >> 4, intra16x16: false, transform8x8, ref lastQpDelta);

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    for (var by = 0; by < 4; ++by)
      for (var bx = 0; bx < 4; ++bx)
        this._PredictBStored(
          mbAddr, mbX * 16 + bx * 4, mbY * 16 + by * 4, 4, 4, addResidual: !transform8x8);
    if (transform8x8)
      this._AddInter8x8Residuals(mbAddr, cbp & 15);
    this._MarkReconstructed(mbX, mbY);
    this._ReconstructChromaB(mbAddr);
  }

  private void _ReadCabacResidualAndQp(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    int cbpLuma,
    int cbpChroma,
    bool intra16x16,
    bool transform8x8,
    ref int lastQpDelta) {
    this._cabacCbpLuma![mbAddr] = (byte)cbpLuma;
    this._cabacCbpChroma![mbAddr] = (byte)cbpChroma;
    this._cabacTransform8x8![mbAddr] = transform8x8;
    if (cbpLuma == 0 && cbpChroma == 0 && !intra16x16) {
      this._qpY[mbAddr] = (sbyte)this._qpRunning;
      lastQpDelta = 0;
      return;
    }

    var delta = H264CabacSyntax.DecodeMbQpDelta(ref decoder, contexts, lastQpDelta != 0);
    if (delta is < -26 or > 25)
      throw new InvalidDataException(
        $"An H.264 CABAC macroblock states mb_qp_delta {delta}; 8-bit AVC confines it to -26..25.");
    lastQpDelta = delta;
    this._qpRunning = (this._qpRunning + delta + 52) % 52;
    this._qpY[mbAddr] = (sbyte)this._qpRunning;
    this._ReadCabacResidual(ref decoder, contexts, mbAddr, cbpLuma, cbpChroma, intra16x16, transform8x8);
  }

  private void _ReadCabacResidual(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int mbAddr,
    int cbpLuma,
    int cbpChroma,
    bool intra16x16,
    bool transform8x8) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var currentIntra = this._kind[mbAddr] != H264MacroblockKind.Inter;

    if (intra16x16) {
      var leftCondition = this._CabacLumaDcCondition(mbAddr - 1, mbAddr, currentIntra, left: true);
      var aboveCondition = this._CabacLumaDcCondition(mbAddr - this._mbWidth, mbAddr, currentIntra, left: false);
      var coded = H264CabacSyntax.DecodeCodedBlockFlag(
        ref decoder, contexts, H264CabacBlockType.Luma16x16Dc, leftCondition, aboveCondition);
      this._cabacLumaDcCbf![mbAddr] = coded;
      if (coded)
        H264CabacSyntax.DecodeResidualBlock(
          ref decoder, contexts, H264CabacBlockType.Luma16x16Dc, this._lumaDcLevels, 0, 15);
    }

    if (transform8x8) {
      for (var i8x8 = 0; i8x8 < 4; ++i8x8) {
        if ((cbpLuma & (1 << i8x8)) == 0)
          continue;
        var coefficients = this._lumaLevels.AsSpan(i8x8 * 64, 64);
        var count = H264CabacSyntax.DecodeResidualBlock(
          ref decoder, contexts, H264CabacBlockType.Luma8x8, coefficients, 0, 63);
        for (var i4x4 = 0; i4x4 < 4; ++i4x4) {
          var blkIdx = i8x8 * 4 + i4x4;
          var (bx, by) = _BlockPosition(blkIdx);
          var blockX = mbX * 4 + (bx >> 2);
          var blockY = mbY * 4 + (by >> 2);
          var at = blockY * this._blockWidth + blockX;
          this._lumaCoeffCount[at] = (byte)count;
          this._cabacLumaCbf![at] = count != 0;
        }
      }
    } else {
      for (var i8x8 = 0; i8x8 < 4; ++i8x8)
        for (var i4x4 = 0; i4x4 < 4; ++i4x4) {
          var blkIdx = i8x8 * 4 + i4x4;
          var (bx, by) = _BlockPosition(blkIdx);
          var blockX = mbX * 4 + (bx >> 2);
          var blockY = mbY * 4 + (by >> 2);
          if ((cbpLuma & (1 << i8x8)) == 0)
            continue;
          var type = intra16x16 ? H264CabacBlockType.Luma16x16Ac : H264CabacBlockType.Luma4x4;
          var leftCondition = this._CabacLumaBlockCondition(blockX - 1, blockY, mbAddr, currentIntra);
          var aboveCondition = this._CabacLumaBlockCondition(blockX, blockY - 1, mbAddr, currentIntra);
          var coded = H264CabacSyntax.DecodeCodedBlockFlag(
            ref decoder, contexts, type, leftCondition, aboveCondition);
          var at = blockY * this._blockWidth + blockX;
          this._cabacLumaCbf![at] = coded;
          if (!coded)
            continue;
          var coefficients = intra16x16
            ? this._lumaLevels.AsSpan(blkIdx * 16 + 1, 15)
            : this._lumaLevels.AsSpan(blkIdx * 16, 16);
          var count = H264CabacSyntax.DecodeResidualBlock(
            ref decoder, contexts, type, coefficients, 0, coefficients.Length - 1);
          this._lumaCoeffCount[at] = (byte)count;
        }
    }

    if (cbpChroma == 0)
      return;
    for (var component = 0; component < 2; ++component) {
      var leftCondition = this._CabacChromaDcCondition(mbAddr - 1, mbAddr, component, currentIntra, left: true);
      var aboveCondition = this._CabacChromaDcCondition(mbAddr - this._mbWidth, mbAddr, component, currentIntra, left: false);
      var coded = H264CabacSyntax.DecodeCodedBlockFlag(
        ref decoder, contexts, H264CabacBlockType.ChromaDc, leftCondition, aboveCondition);
      this._cabacChromaDcCbf![mbAddr * 2 + component] = coded;
      if (coded)
        H264CabacSyntax.DecodeResidualBlock(
          ref decoder, contexts, H264CabacBlockType.ChromaDc,
          this._chromaDcLevels.AsSpan(component * 4, 4), 0, 3);
    }
    if (cbpChroma < 2)
      return;

    for (var component = 0; component < 2; ++component)
      for (var blkIdx = 0; blkIdx < 4; ++blkIdx) {
        var blockX = mbX * 2 + (blkIdx & 1);
        var blockY = mbY * 2 + (blkIdx >> 1);
        var leftCondition = this._CabacChromaBlockCondition(component, blockX - 1, blockY, mbAddr, currentIntra);
        var aboveCondition = this._CabacChromaBlockCondition(component, blockX, blockY - 1, mbAddr, currentIntra);
        var coded = H264CabacSyntax.DecodeCodedBlockFlag(
          ref decoder, contexts, H264CabacBlockType.ChromaAc, leftCondition, aboveCondition);
        var at = this._ChromaBlockBase(component, blockX, blockY);
        this._cabacChromaCbf![at] = coded;
        if (!coded)
          continue;
        var coefficients = this._chromaLevels.AsSpan((component * 4 + blkIdx) * 16 + 1, 15);
        var count = H264CabacSyntax.DecodeResidualBlock(
          ref decoder, contexts, H264CabacBlockType.ChromaAc, coefficients, 0, 14);
        this._chromaCoeffCount[at] = (byte)count;
      }
  }

  private int _DecodeCabacReferenceIndex(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int list,
    int mbAddr,
    int x,
    int y) {
    var active = list == 0 ? this._header.NumRefIdxL0Active : this._header.NumRefIdxL1Active;
    var references = list == 0 ? this._referenceList : this._referenceList1;
    var left = this._CabacReferenceGreaterThanZero(list, mbAddr, x - 1, y);
    var above = this._CabacReferenceGreaterThanZero(list, mbAddr, x, y - 1);
    var index = H264CabacSyntax.DecodeReferenceIndex(ref decoder, contexts, left, above, active);
    if ((uint)index >= (uint)references.Length)
      throw new InvalidDataException(
        $"An H.264 CABAC macroblock names list-{list} reference {index}, but the decoded list has {references.Length} picture(s).");
    return index;
  }

  private int _DecodeCabacMvd(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int list,
    int mbAddr,
    int x,
    int y,
    bool vertical) {
    var sum = this._CabacNeighbourMvdAbs(list, mbAddr, x - 1, y, vertical)
      + this._CabacNeighbourMvdAbs(list, mbAddr, x, y - 1, vertical);
    return H264CabacSyntax.DecodeMotionVectorDifference(ref decoder, contexts, vertical, sum);
  }

  private (H264CabacMbNeighbour Left, H264CabacMbNeighbour Above) _CabacMacroblockNeighbours(int mbAddr) {
    var x = mbAddr % this._mbWidth;
    var y = mbAddr / this._mbWidth;
    return (
      this._CabacMacroblockNeighbour(x > 0 ? mbAddr - 1 : -1),
      this._CabacMacroblockNeighbour(y > 0 ? mbAddr - this._mbWidth : -1));
  }

  private H264CabacMbNeighbour _CabacMacroblockNeighbour(int address) {
    if (address < 0 || address >= this._mbCount || this._sliceId[address] != this._currentSliceId)
      return default;
    var kind = this._kind[address];
    return new(
      Available: true,
      Skipped: this._cabacSkipped![address],
      IsPcm: kind == H264MacroblockKind.Pcm,
      IsINxN: kind is H264MacroblockKind.Intra4x4 or H264MacroblockKind.Intra8x8,
      IsBDirect: this._cabacBDirect![address],
      IsInter: kind == H264MacroblockKind.Inter,
      CbpLuma: this._cabacCbpLuma![address],
      CbpChroma: this._cabacCbpChroma![address],
      IntraChromaMode: this._cabacIntraChromaMode![address],
      Transform8x8: this._cabacTransform8x8![address]);
  }

  private bool _CabacReferenceGreaterThanZero(int list, int mbAddr, int x, int y) {
    if (!this._CabacBlockAvailable(mbAddr, x, y, out var at))
      return false;
    if (this._cabacDirectBlock![at])
      return false;
    var value = list == 0 ? this._cabacRefIdx0![at] : this._cabacRefIdx1![at];
    return value > 0;
  }

  private int _CabacNeighbourMvdAbs(int list, int mbAddr, int x, int y, bool vertical) {
    if (!this._CabacBlockAvailable(mbAddr, x, y, out var at) || this._cabacDirectBlock![at])
      return 0;
    var value = (list, vertical) switch {
      (0, false) => this._cabacMvdX0![at],
      (0, true) => this._cabacMvdY0![at],
      (1, false) => this._cabacMvdX1![at],
      _ => this._cabacMvdY1![at],
    };
    return Math.Abs((int)value);
  }

  private bool _CabacBlockAvailable(int mbAddr, int x, int y, out int at) {
    at = 0;
    if (x < 0 || y < 0 || x >= this._mbWidth * 16 || y >= this._mbHeight * 16)
      return false;
    var neighbourMb = y / 16 * this._mbWidth + x / 16;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return false;
    at = (y >> 2) * this._blockWidth + (x >> 2);
    if (neighbourMb == mbAddr && !this._motionAssigned[at] && this._cabacRefIdx0![at] < 0 && this._cabacRefIdx1![at] < 0)
      return false;
    return true;
  }

  private bool _CabacLumaBlockCondition(int blockX, int blockY, int mbAddr, bool currentIntra) {
    if (blockX < 0 || blockY < 0 || blockX >= this._blockWidth || blockY >= this._mbHeight * 4)
      return currentIntra;
    var neighbourMb = blockY / 4 * this._mbWidth + blockX / 4;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return currentIntra;
    if (this._kind[neighbourMb] == H264MacroblockKind.Pcm)
      return true;
    return this._cabacLumaCbf![blockY * this._blockWidth + blockX];
  }

  private bool _CabacChromaBlockCondition(
    int component, int blockX, int blockY, int mbAddr, bool currentIntra) {
    if (blockX < 0 || blockY < 0 || blockX >= this._chromaBlockWidth || blockY >= this._mbHeight * 2)
      return currentIntra;
    var neighbourMb = blockY / 2 * this._mbWidth + blockX / 2;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return currentIntra;
    if (this._kind[neighbourMb] == H264MacroblockKind.Pcm)
      return true;
    return this._cabacChromaCbf![this._ChromaBlockBase(component, blockX, blockY)];
  }

  private bool _CabacLumaDcCondition(int neighbour, int current, bool currentIntra, bool left) {
    if (neighbour < 0 || neighbour >= this._mbCount)
      return currentIntra;
    if (left && current % this._mbWidth == 0 || !left && current < this._mbWidth)
      return currentIntra;
    if (this._sliceId[neighbour] != this._currentSliceId)
      return currentIntra;
    if (this._kind[neighbour] == H264MacroblockKind.Pcm)
      return true;
    return this._cabacLumaDcCbf![neighbour];
  }

  private bool _CabacChromaDcCondition(int neighbour, int current, int component, bool currentIntra, bool left) {
    if (neighbour < 0 || neighbour >= this._mbCount)
      return currentIntra;
    if (left && current % this._mbWidth == 0 || !left && current < this._mbWidth)
      return currentIntra;
    if (this._sliceId[neighbour] != this._currentSliceId)
      return currentIntra;
    if (this._kind[neighbour] == H264MacroblockKind.Pcm)
      return true;
    return this._cabacChromaDcCbf![neighbour * 2 + component];
  }

  private void _CabacFillReference(int list, int x, int y, int width, int height, int reference) {
    var target = list == 0 ? this._cabacRefIdx0! : this._cabacRefIdx1!;
    for (var by = 0; by < height >> 2; ++by) {
      var row = ((y >> 2) + by) * this._blockWidth + (x >> 2);
      for (var bx = 0; bx < width >> 2; ++bx)
        target[row + bx] = (sbyte)reference;
    }
  }

  private void _CabacFillMvd(int list, int x, int y, int width, int height, int mvdX, int mvdY) {
    var targetX = list == 0 ? this._cabacMvdX0! : this._cabacMvdX1!;
    var targetY = list == 0 ? this._cabacMvdY0! : this._cabacMvdY1!;
    for (var by = 0; by < height >> 2; ++by) {
      var row = ((y >> 2) + by) * this._blockWidth + (x >> 2);
      for (var bx = 0; bx < width >> 2; ++bx) {
        targetX[row + bx] = (short)mvdX;
        targetY[row + bx] = (short)mvdY;
      }
    }
  }

  private void _CabacFillDirect(int x, int y, int width, int height, bool direct) {
    for (var by = 0; by < height >> 2; ++by) {
      var row = ((y >> 2) + by) * this._blockWidth + (x >> 2);
      for (var bx = 0; bx < width >> 2; ++bx)
        this._cabacDirectBlock![row + bx] = direct;
    }
  }

  private void _CabacDecodePcm(ref H264CabacDecoder decoder, int mbAddr) {
    var raw = decoder.SnapshotReader();
    while ((raw.BitPosition & 7) != 0)
      if (raw.ReadBit() != 0)
        throw new InvalidDataException("An H.264 I_PCM macroblock contains a one pcm_alignment_zero_bit.");
    this._DecodePcm(ref raw, mbAddr);
    decoder = new H264CabacDecoder(raw);
  }

  private void _CabacPrepareMacroblock(int mbAddr, bool skipped, bool direct) {
    this._cabacSkipped![mbAddr] = skipped;
    this._cabacBDirect![mbAddr] = direct;
    this._cabacCbpLuma![mbAddr] = 0;
    this._cabacCbpChroma![mbAddr] = 0;
    this._cabacIntraChromaMode![mbAddr] = 0;
    this._cabacTransform8x8![mbAddr] = false;
    this._cabacLumaDcCbf![mbAddr] = false;
    this._cabacChromaDcCbf![mbAddr * 2] = false;
    this._cabacChromaDcCbf[mbAddr * 2 + 1] = false;
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    for (var by = 0; by < 4; ++by) {
      var row = (mbY * 4 + by) * this._blockWidth + mbX * 4;
      for (var bx = 0; bx < 4; ++bx) {
        var at = row + bx;
        this._cabacDirectBlock![at] = direct;
        this._cabacRefIdx0![at] = -1;
        this._cabacRefIdx1![at] = -1;
        this._cabacMvdX0![at] = 0;
        this._cabacMvdY0![at] = 0;
        this._cabacMvdX1![at] = 0;
        this._cabacMvdY1![at] = 0;
        this._cabacLumaCbf![at] = false;
      }
    }
    for (var component = 0; component < 2; ++component)
      for (var by = 0; by < 2; ++by) {
        var row = this._ChromaBlockBase(component, mbX * 2, mbY * 2 + by);
        for (var bx = 0; bx < 2; ++bx)
          this._cabacChromaCbf![row + bx] = false;
      }
  }

  private void _CabacMarkSkipped(int mbAddr, bool isB) {
    this._cabacSkipped![mbAddr] = true;
    this._cabacBDirect![mbAddr] = isB;
    this._cabacCbpLuma![mbAddr] = 0;
    this._cabacCbpChroma![mbAddr] = 0;
    this._cabacTransform8x8![mbAddr] = false;
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    for (var by = 0; by < 4; ++by)
      for (var bx = 0; bx < 4; ++bx) {
        var at = (mbY * 4 + by) * this._blockWidth + mbX * 4 + bx;
        this._cabacDirectBlock![at] = isB;
        this._cabacRefIdx0![at] = this._refIdx[at];
        this._cabacRefIdx1![at] = isB ? this._refIdx1![at] : (sbyte)-1;
        this._cabacMvdX0![at] = 0;
        this._cabacMvdY0![at] = 0;
        this._cabacMvdX1![at] = 0;
        this._cabacMvdY1![at] = 0;
      }
  }

  private void _EnsureCabacState() {
    if (this._cabacSkipped != null)
      return;
    var blocks = this._lumaCoeffCount.Length;
    this._cabacSkipped = new bool[this._mbCount];
    this._cabacBDirect = new bool[this._mbCount];
    this._cabacDirectBlock = new bool[blocks];
    this._cabacCbpLuma = new byte[this._mbCount];
    this._cabacCbpChroma = new byte[this._mbCount];
    this._cabacIntraChromaMode = new sbyte[this._mbCount];
    this._cabacTransform8x8 = new bool[this._mbCount];
    this._cabacRefIdx0 = new sbyte[blocks];
    this._cabacRefIdx0.AsSpan().Fill(-1);
    this._cabacRefIdx1 = new sbyte[blocks];
    this._cabacRefIdx1.AsSpan().Fill(-1);
    this._cabacMvdX0 = new short[blocks];
    this._cabacMvdY0 = new short[blocks];
    this._cabacMvdX1 = new short[blocks];
    this._cabacMvdY1 = new short[blocks];
    this._cabacLumaCbf = new bool[blocks];
    this._cabacChromaCbf = new bool[this._chromaCoeffCount.Length];
    this._cabacLumaDcCbf = new bool[this._mbCount];
    this._cabacChromaDcCbf = new bool[this._mbCount * 2];
  }
}
