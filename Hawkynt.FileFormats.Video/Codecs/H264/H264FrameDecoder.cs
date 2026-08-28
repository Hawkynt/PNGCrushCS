using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>What a macroblock is, as far as anything decoded after it needs to know.</summary>
internal enum H264MacroblockKind : byte {

  /// <summary>No slice has covered this address yet.</summary>
  Absent = 0,

  /// <summary>Intra, predicted a 4x4 block at a time (<c>I_NxN</c> with the 8x8 transform off).</summary>
  Intra4x4,

  /// <summary>Intra, predicted as one 16x16 block.</summary>
  Intra16x16,

  /// <summary>Samples carried literally (<c>I_PCM</c>).</summary>
  Pcm,

  /// <summary>Predicted from a reference picture.</summary>
  Inter,
}

/// <summary>
/// Decodes the slices of one picture into it, and then filters its block edges.
/// </summary>
/// <remarks>
/// One of these exists per coded picture, not per slice, because a picture's slices share a great
/// deal that neither of them owns. A macroblock's intra prediction reads its neighbours' reconstructed
/// samples; its <c>nC</c> reads their coefficient counts; its motion vector predictor reads their
/// motion vectors; and the deblocking filter runs across all of it once every slice has arrived. All
/// of that lives here, indexed by position in the picture, and a slice contributes to it rather than
/// owning any of it.
/// <para/>
/// <b>What is in scope.</b> I and P slices, CAVLC, 4:2:0, 8-bit samples, progressive frames, one
/// slice group, including explicit weighted P prediction. Everything outside that is refused by name
/// before this type is reached — by <see cref="H264SequenceParameterSet.RefuseUnsupported"/>,
/// <see cref="H264PictureParameterSet.RefuseUnsupported"/> and <see cref="H264SliceHeader"/> — so
/// nothing here silently produces a picture for an unsupported coding tool.
/// </remarks>
internal sealed class H264FrameDecoder {

  private const int _PCM_MB_TYPE_I = 25;
  private const int _INTRA_MB_TYPE_OFFSET_P = 5;

  private readonly int _mbWidth;
  private readonly int _mbHeight;
  private readonly int _mbCount;

  /// <summary>Width of the picture-wide grid of 4x4 luma blocks.</summary>
  private readonly int _blockWidth;

  /// <summary>Width of the picture-wide grid of 4x4 chroma blocks, per component.</summary>
  private readonly int _chromaBlockWidth;

  // --- per macroblock ---
  private readonly int[] _sliceId;
  private readonly H264MacroblockKind[] _kind;
  private readonly sbyte[] _qpY;
  private readonly byte[] _disableDeblockingFilterIdc;
  private readonly sbyte[] _filterOffsetA;
  private readonly sbyte[] _filterOffsetB;
  private readonly sbyte[] _chromaQpOffsetCb;
  private readonly sbyte[] _chromaQpOffsetCr;

  // --- per 4x4 luma block ---
  private readonly byte[] _lumaCoeffCount;
  private readonly sbyte[] _intra4x4Mode;
  private readonly short[] _mvX;
  private readonly short[] _mvY;
  private readonly sbyte[] _refIdx;
  private readonly long[] _refSerial;
  private readonly bool[] _motionAssigned;
  private readonly bool[] _blockReconstructed;

  // --- per 4x4 chroma block, both components ---
  private readonly byte[] _chromaCoeffCount;

  // --- the residual of the macroblock being decoded, in scan order per block ---
  private readonly int[] _lumaLevels = new int[16 * 16];
  private readonly int[] _lumaDcLevels = new int[16];
  private readonly int[] _chromaLevels = new int[2 * 4 * 16];
  private readonly int[] _chromaDcLevels = new int[2 * 4];

  // --- state for the slice being decoded ---
  private H264SliceHeader _header = null!;
  private H264Picture[] _referenceList = [];
  private int _currentSliceId;
  private int _qpRunning;
  private int _nextSliceId;

  internal H264FrameDecoder(H264SequenceParameterSet sps, long serial) {
    this._mbWidth = sps.PicWidthInMbs;
    this._mbHeight = sps.FrameHeightInMbs;
    this._mbCount = this._mbWidth * this._mbHeight;
    this._blockWidth = this._mbWidth * 4;
    this._chromaBlockWidth = this._mbWidth * 2;

    this.Picture = new(sps.CodedWidth, sps.CodedHeight, serial);

    this._sliceId = new int[this._mbCount];
    this._sliceId.AsSpan().Fill(-1);
    this._kind = new H264MacroblockKind[this._mbCount];
    this._qpY = new sbyte[this._mbCount];
    this._disableDeblockingFilterIdc = new byte[this._mbCount];
    this._filterOffsetA = new sbyte[this._mbCount];
    this._filterOffsetB = new sbyte[this._mbCount];
    this._chromaQpOffsetCb = new sbyte[this._mbCount];
    this._chromaQpOffsetCr = new sbyte[this._mbCount];

    var lumaBlocks = this._blockWidth * this._mbHeight * 4;
    this._lumaCoeffCount = new byte[lumaBlocks];
    this._intra4x4Mode = new sbyte[lumaBlocks];
    this._mvX = new short[lumaBlocks];
    this._mvY = new short[lumaBlocks];
    this._refIdx = new sbyte[lumaBlocks];
    this._refIdx.AsSpan().Fill(-1);
    this._refSerial = new long[lumaBlocks];
    this._motionAssigned = new bool[lumaBlocks];
    this._blockReconstructed = new bool[lumaBlocks];

    this._chromaCoeffCount = new byte[2 * this._chromaBlockWidth * this._mbHeight * 2];
  }

  internal H264Picture Picture { get; }

  internal int MacroblockWidth => this._mbWidth;

  internal int MacroblockHeight => this._mbHeight;

  internal H264MacroblockKind KindOf(int mbAddr) => this._kind[mbAddr];

  internal int SliceOf(int mbAddr) => this._sliceId[mbAddr];

  internal int QpOf(int mbAddr) => this._qpY[mbAddr];

  internal int DeblockingIdcOf(int mbAddr) => this._disableDeblockingFilterIdc[mbAddr];

  internal int FilterOffsetAOf(int mbAddr) => this._filterOffsetA[mbAddr];

  internal int FilterOffsetBOf(int mbAddr) => this._filterOffsetB[mbAddr];

  internal int ChromaQpOffsetOf(int mbAddr, int component)
    => component == 0 ? this._chromaQpOffsetCb[mbAddr] : this._chromaQpOffsetCr[mbAddr];

  internal bool BlockHasCoefficients(int blockX, int blockY)
    => this._lumaCoeffCount[blockY * this._blockWidth + blockX] > 0;

  internal (int X, int Y, long Reference, bool Predicted) BlockMotion(int blockX, int blockY) {
    var at = blockY * this._blockWidth + blockX;
    return (this._mvX[at], this._mvY[at], this._refSerial[at], this._refIdx[at] >= 0);
  }

  /// <summary>
  /// Refuses a picture whose slices did not between them cover every macroblock.
  /// </summary>
  /// <remarks>
  /// A picture with a hole in it is a lost or truncated slice, and the hole holds whatever the buffer
  /// was initialised to. Nothing downstream can tell that from a decode, and a later picture
  /// predicting from it spreads the hole; so a picture that is not whole is not handed out.
  /// </remarks>
  internal void RefuseIfIncomplete() {
    for (var mbAddr = 0; mbAddr < this._mbCount; ++mbAddr) {
      if (this._sliceId[mbAddr] >= 0)
        continue;

      throw new InvalidDataException(
        $"Macroblock {mbAddr} of {this._mbCount} ({mbAddr % this._mbWidth}, {mbAddr / this._mbWidth}) was covered by "
        + "no slice of this H.264 picture, so part of the picture was never coded. The access unit is incomplete.");
    }
  }

  // ============================================================================================
  // The slice layer — clause 7.3.4
  // ============================================================================================

  /// <summary>Decodes one slice's macroblocks into this picture.</summary>
  /// <param name="reader">Positioned just after the slice header.</param>
  /// <param name="header">That header.</param>
  /// <param name="referenceList">Reference picture list 0, already built and modified for this slice.</param>
  internal void DecodeSlice(ref H264BitReader reader, H264SliceHeader header, H264Picture[] referenceList) {
    this._header = header;
    this._referenceList = referenceList;
    this._currentSliceId = this._nextSliceId++;
    this._qpRunning = header.SliceQpY;

    var mbAddr = header.FirstMbInSlice;
    if (mbAddr >= this._mbCount)
      throw new InvalidDataException(
        $"An H.264 slice states first_mb_in_slice {mbAddr}, and this picture has {this._mbCount} macroblocks.");

    var moreData = true;
    while (true) {
      if (!header.IsIntra) {
        var skipRun = reader.ReadUnsignedExpGolomb();
        for (var i = 0; i < skipRun; ++i) {
          this._RefuseAddressPastPicture(mbAddr, "a run of skipped macroblocks");
          this._DecodeSkipped(mbAddr);
          ++mbAddr;
        }

        if (skipRun > 0)
          moreData = reader.MoreRbspData;
      }

      if (moreData) {
        this._RefuseAddressPastPicture(mbAddr, "a coded macroblock");
        this._DecodeMacroblock(ref reader, mbAddr);
      }

      moreData = reader.MoreRbspData;
      ++mbAddr;

      if (!moreData)
        return;
    }
  }

  private void _RefuseAddressPastPicture(int mbAddr, string what) {
    if (mbAddr < this._mbCount)
      return;

    throw new InvalidDataException(
      $"An H.264 slice reached macroblock address {mbAddr} decoding {what}, and this picture has "
      + $"{this._mbCount} macroblocks. The slice runs past the end of the picture.");
  }

  // ============================================================================================
  // The macroblock layer — clause 7.3.5
  // ============================================================================================

  private void _DecodeMacroblock(ref H264BitReader reader, int mbAddr) {
    this._BeginMacroblock(mbAddr);

    var mbType = reader.ReadUnsignedExpGolomb();

    // In a P slice the first five values are the inter partitionings and everything above them is an
    // intra macroblock with its type shifted up by five (clause 7.4.5, Tables 7-13 and 7-11).
    if (!this._header.IsIntra && mbType < _INTRA_MB_TYPE_OFFSET_P) {
      this._DecodeInter(ref reader, mbAddr, mbType);
      return;
    }

    var intraType = this._header.IsIntra ? mbType : mbType - _INTRA_MB_TYPE_OFFSET_P;
    if (intraType > _PCM_MB_TYPE_I)
      throw new InvalidDataException(
        $"An H.264 {(this._header.IsIntra ? "I" : "P")} slice states mb_type {mbType}, which is beyond the "
        + $"{(this._header.IsIntra ? "25" : "30")} that H.264 Table 7-11 defines.");

    if (intraType == _PCM_MB_TYPE_I) {
      this._DecodePcm(ref reader, mbAddr);
      return;
    }

    if (intraType == 0)
      this._DecodeIntra4x4(ref reader, mbAddr);
    else
      this._DecodeIntra16x16(ref reader, mbAddr, intraType);
  }

  /// <summary>Clears the per-block state of a macroblock about to be decoded.</summary>
  private void _BeginMacroblock(int mbAddr) {
    this._sliceId[mbAddr] = this._currentSliceId;
    this._disableDeblockingFilterIdc[mbAddr] = (byte)this._header.DisableDeblockingFilterIdc;
    this._filterOffsetA[mbAddr] = (sbyte)this._header.FilterOffsetA;
    this._filterOffsetB[mbAddr] = (sbyte)this._header.FilterOffsetB;
    this._chromaQpOffsetCb[mbAddr] = (sbyte)this._header.Pps.ChromaQpIndexOffset;
    this._chromaQpOffsetCr[mbAddr] = (sbyte)this._header.Pps.SecondChromaQpIndexOffset;

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;

    for (var by = 0; by < 4; ++by) {
      var row = (mbY * 4 + by) * this._blockWidth + mbX * 4;
      for (var bx = 0; bx < 4; ++bx) {
        this._lumaCoeffCount[row + bx] = 0;
        this._motionAssigned[row + bx] = false;
        this._blockReconstructed[row + bx] = false;
        this._refIdx[row + bx] = -1;
        this._refSerial[row + bx] = 0;
        this._mvX[row + bx] = 0;
        this._mvY[row + bx] = 0;
      }
    }

    for (var component = 0; component < 2; ++component)
      for (var by = 0; by < 2; ++by) {
        var row = this._ChromaBlockBase(component, mbX * 2, mbY * 2 + by);
        for (var bx = 0; bx < 2; ++bx)
          this._chromaCoeffCount[row + bx] = 0;
      }

    Array.Clear(this._lumaLevels);
    Array.Clear(this._lumaDcLevels);
    Array.Clear(this._chromaLevels);
    Array.Clear(this._chromaDcLevels);
  }

  private int _ChromaBlockBase(int component, int blockX, int blockY)
    => component * this._chromaBlockWidth * this._mbHeight * 2 + blockY * this._chromaBlockWidth + blockX;

  // ============================================================================================
  // I_PCM — clause 7.3.5, and 8.5.14 for what it does to the reconstruction
  // ============================================================================================

  private void _DecodePcm(ref H264BitReader reader, int mbAddr) {
    reader.AlignToByte();

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var picture = this.Picture;

    for (var y = 0; y < 16; ++y)
      for (var x = 0; x < 16; ++x)
        picture.Luma[(mbY * 16 + y) * picture.LumaWidth + mbX * 16 + x] = (byte)reader.ReadBits(8);

    for (var component = 0; component < 2; ++component) {
      var plane = picture.Chroma(component);
      for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 8; ++x)
          plane[(mbY * 8 + y) * picture.ChromaWidth + mbX * 8 + x] = (byte)reader.ReadBits(8);
    }

    this._kind[mbAddr] = H264MacroblockKind.Pcm;

    // Clause 7.4.5 sets QPY to 0 for an I_PCM macroblock, and clause 9.2.1 makes its blocks count as
    // sixteen coefficients for the neighbouring blocks' nC — the largest a block can hold, because a
    // macroblock carrying its samples literally is as far from flat as a macroblock gets.
    this._qpY[mbAddr] = 0;
    this._qpRunning = 0;

    for (var by = 0; by < 4; ++by) {
      var row = (mbY * 4 + by) * this._blockWidth + mbX * 4;
      for (var bx = 0; bx < 4; ++bx) {
        this._lumaCoeffCount[row + bx] = 16;
        this._blockReconstructed[row + bx] = true;
      }
    }

    for (var component = 0; component < 2; ++component)
      for (var by = 0; by < 2; ++by) {
        var row = this._ChromaBlockBase(component, mbX * 2, mbY * 2 + by);
        for (var bx = 0; bx < 2; ++bx)
          this._chromaCoeffCount[row + bx] = 16;
      }
  }

  // ============================================================================================
  // Intra macroblocks — clauses 7.3.5.1 and 8.3
  // ============================================================================================

  private void _DecodeIntra4x4(ref H264BitReader reader, int mbAddr) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    this._kind[mbAddr] = H264MacroblockKind.Intra4x4;

    // The modes first, all sixteen of them, because that is the order the syntax is in — and each
    // one's predictor is the two blocks beside it, so they have to be resolved as they are read
    // rather than afterwards.
    for (var blkIdx = 0; blkIdx < 16; ++blkIdx) {
      var (bx, by) = _BlockPosition(blkIdx);
      var blockX = mbX * 4 + (bx >> 2);
      var blockY = mbY * 4 + (by >> 2);

      var predicted = this._PredictIntra4x4Mode(mbAddr, blockX, blockY);
      var mode = predicted;
      if (reader.ReadBit() == 0) {
        var remaining = reader.ReadBits(3);
        mode = remaining < predicted ? remaining : remaining + 1;
      }

      this._intra4x4Mode[blockY * this._blockWidth + blockX] = (sbyte)mode;
    }

    var chromaMode = reader.ReadUnsignedExpGolomb();
    var cbp = H264CavlcTables.ReadCodedBlockPattern(ref reader, intra: true);
    this._ReadResidualAndQp(ref reader, mbAddr, cbp & 15, cbp >> 4, intra16x16: false);

    // Then the reconstruction, one block at a time and in coding order, because each block predicts
    // from the reconstructed samples of the ones before it (clause 8.3.1.2, NOTE).
    var qp = this._qpY[mbAddr];
    Span<byte> pred = stackalloc byte[16];
    Span<int> residual = stackalloc int[16];
    Span<byte> top = stackalloc byte[8];
    Span<byte> left = stackalloc byte[4];

    for (var blkIdx = 0; blkIdx < 16; ++blkIdx) {
      var (bx, by) = _BlockPosition(blkIdx);
      var x = mbX * 16 + bx;
      var y = mbY * 16 + by;
      var blockX = mbX * 4 + (bx >> 2);
      var blockY = mbY * 4 + (by >> 2);

      var neighbours = this._GatherLuma4x4Neighbours(mbAddr, x, y, blkIdx, top, left, out var topLeft);
      H264IntraPrediction.Predict4x4(
        this._intra4x4Mode[blockY * this._blockWidth + blockX], top, left, topLeft,
        neighbours.Top, neighbours.Left, neighbours.TopLeft, pred);

      var levels = this._lumaLevels.AsSpan(blkIdx * 16, 16);
      if (this._lumaCoeffCount[blockY * this._blockWidth + blockX] > 0) {
        H264Transform.DecodeBlock(levels, qp, hasSeparateDc: false, 0, residual);
        _AddResidual(this.Picture.Luma, this.Picture.LumaWidth, x, y, 4, pred, residual);
      } else {
        _CopyPrediction(this.Picture.Luma, this.Picture.LumaWidth, x, y, 4, pred);
      }

      this._blockReconstructed[blockY * this._blockWidth + blockX] = true;
    }

    this._ReconstructChroma(mbAddr, chromaMode, intra: true);
  }

  private void _DecodeIntra16x16(ref H264BitReader reader, int mbAddr, int intraType) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    this._kind[mbAddr] = H264MacroblockKind.Intra16x16;

    // Table 7-11: mb_type 1 to 24 spell out the prediction mode and both halves of the coded block
    // pattern, in that order, four modes to a chroma pattern and twelve to a luma one.
    var index = intraType - 1;
    var lumaMode = index % 4;
    var cbpChroma = index / 4 % 3;
    var cbpLuma = index >= 12 ? 15 : 0;

    var chromaMode = reader.ReadUnsignedExpGolomb();
    this._ReadResidualAndQp(ref reader, mbAddr, cbpLuma, cbpChroma, intra16x16: true);

    var qp = this._qpY[mbAddr];
    Span<byte> pred = stackalloc byte[256];
    Span<byte> top = stackalloc byte[16];
    Span<byte> left = stackalloc byte[16];

    var neighbours = this._GatherLumaMacroblockNeighbours(mbAddr, top, left, out var topLeft);
    H264IntraPrediction.Predict16x16(
      lumaMode, top, left, topLeft, neighbours.Top, neighbours.Left, neighbours.TopLeft, pred);

    Span<int> dc = stackalloc int[16];
    H264Transform.DecodeLumaDc(this._lumaDcLevels, qp, dc);

    Span<int> residual = stackalloc int[16];
    Span<byte> blockPrediction = stackalloc byte[16];

    for (var blkIdx = 0; blkIdx < 16; ++blkIdx) {
      var (bx, by) = _BlockPosition(blkIdx);
      for (var row = 0; row < 4; ++row)
        for (var column = 0; column < 4; ++column)
          blockPrediction[(row << 2) + column] = pred[((by + row) << 4) + bx + column];

      // Figure 8-6: the DC values are in raster order over the macroblock's sixteen 4x4 blocks, and
      // the block indices are in the Z order of clause 6.4.3 — so the two are not the same walk.
      var dcValue = dc[(by >> 2) * 4 + (bx >> 2)];
      H264Transform.DecodeBlock(this._lumaLevels.AsSpan(blkIdx * 16, 16), qp, hasSeparateDc: true, dcValue, residual);

      var blockX = mbX * 4 + (bx >> 2);
      var blockY = mbY * 4 + (by >> 2);
      _AddResidual(this.Picture.Luma, this.Picture.LumaWidth, mbX * 16 + bx, mbY * 16 + by, 4, blockPrediction, residual);
      this._blockReconstructed[blockY * this._blockWidth + blockX] = true;
    }

    this._ReconstructChroma(mbAddr, chromaMode, intra: true);
  }

  /// <summary>The Intra_4x4 prediction mode predicted from the two neighbouring blocks — clause 8.3.1.1.</summary>
  private int _PredictIntra4x4Mode(int mbAddr, int blockX, int blockY) {
    var modeA = this._NeighbourIntraMode(mbAddr, blockX - 1, blockY, out var haveA);
    var modeB = this._NeighbourIntraMode(mbAddr, blockX, blockY - 1, out var haveB);

    // Either neighbour missing, or unusable because it is inter-coded in a stream that forbids intra
    // prediction from inter samples, makes both predictors DC.
    if (!haveA || !haveB)
      return H264IntraPrediction.DC_4X4;

    return Math.Min(modeA, modeB);
  }

  private int _NeighbourIntraMode(int mbAddr, int blockX, int blockY, out bool usable) {
    usable = false;
    if (blockX < 0 || blockY < 0 || blockX >= this._blockWidth || blockY >= this._mbHeight * 4)
      return H264IntraPrediction.DC_4X4;

    var neighbourMb = blockY / 4 * this._mbWidth + blockX / 4;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return H264IntraPrediction.DC_4X4;

    if (this._kind[neighbourMb] == H264MacroblockKind.Inter && this._header.Pps.ConstrainedIntraPredFlag)
      return H264IntraPrediction.DC_4X4;

    usable = true;

    // A neighbour that is not itself predicted a 4x4 block at a time has no mode to lend, and the
    // standard says to treat it as DC rather than to treat it as absent (clause 8.3.1.1, step 3).
    return this._kind[neighbourMb] == H264MacroblockKind.Intra4x4
      ? this._intra4x4Mode[blockY * this._blockWidth + blockX]
      : H264IntraPrediction.DC_4X4;
  }

  // ============================================================================================
  // Inter macroblocks — clauses 7.3.5.1, 7.3.5.2 and 8.4
  // ============================================================================================

  private void _DecodeSkipped(int mbAddr) {
    this._BeginMacroblock(mbAddr);
    this._kind[mbAddr] = H264MacroblockKind.Inter;
    this._qpY[mbAddr] = (sbyte)this._qpRunning;

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var (mvX, mvY) = this._PredictSkipMotion(mbAddr, mbX * 16, mbY * 16);

    this._AssignMotion(mbX * 4, mbY * 4, 4, 4, 0, mvX, mvY);
    this._Predict(mbAddr, 0, mbX * 16, mbY * 16, 16, 16, mvX, mvY, addResidual: false);
    this._MarkReconstructed(mbX, mbY);
    this._ReconstructChroma(mbAddr, 0, intra: false);
  }

  private void _DecodeInter(ref H264BitReader reader, int mbAddr, int mbType) {
    this._kind[mbAddr] = H264MacroblockKind.Inter;
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;

    if (mbType is 3 or 4) {
      this._DecodeInter8x8(ref reader, mbAddr, mbType == 4);
      return;
    }

    // Table 7-13: one 16x16 partition, two 16x8 or two 8x16.
    var (partWidth, partHeight, partCount) = mbType switch {
      0 => (16, 16, 1),
      1 => (16, 8, 2),
      _ => (8, 16, 2),
    };

    Span<int> refIdx = stackalloc int[2];
    for (var part = 0; part < partCount; ++part)
      refIdx[part] = this._ReadReferenceIndex(ref reader);

    Span<int> mvXs = stackalloc int[2];
    Span<int> mvYs = stackalloc int[2];
    for (var part = 0; part < partCount; ++part) {
      var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);

      var (predX, predY) = this._PredictMotion(
        mbAddr, x, y, partWidth, partHeight, refIdx[part], part, partCount);

      mvXs[part] = predX + reader.ReadSignedExpGolomb();
      mvYs[part] = predY + reader.ReadSignedExpGolomb();

      // Assigned as each partition's vector is known, because the next partition's predictor reads
      // it (clause 6.4.11.7, step 7c makes an undecoded partition unavailable rather than zero).
      this._AssignMotion(x >> 2, y >> 2, partWidth >> 2, partHeight >> 2, refIdx[part], mvXs[part], mvYs[part]);
    }

    var cbp = H264CavlcTables.ReadCodedBlockPattern(ref reader, intra: false);
    this._ReadResidualAndQp(ref reader, mbAddr, cbp & 15, cbp >> 4, intra16x16: false);

    for (var part = 0; part < partCount; ++part) {
      var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);
      this._Predict(mbAddr, refIdx[part], x, y, partWidth, partHeight, mvXs[part], mvYs[part], addResidual: true);
    }

    this._MarkReconstructed(mbX, mbY);
    this._ReconstructChroma(mbAddr, 0, intra: false);
  }

  /// <summary>The four-partition case, whose sub-macroblock types are read first — clause 7.3.5.2.</summary>
  private void _DecodeInter8x8(ref H264BitReader reader, int mbAddr, bool refZero) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;

    Span<int> subType = stackalloc int[4];
    for (var part = 0; part < 4; ++part) {
      subType[part] = reader.ReadUnsignedExpGolomb();
      if (subType[part] > 3)
        throw new InvalidDataException(
          $"An H.264 P macroblock states sub_mb_type {subType[part]}. H.264, Table 7-17 defines 0 to 3 only.");
    }

    Span<int> refIdx = stackalloc int[4];
    for (var part = 0; part < 4; ++part)
      refIdx[part] = refZero ? 0 : this._ReadReferenceIndex(ref reader);

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

        var (predX, predY) = this._PredictMotion(
          mbAddr, x, y, subWidth, subHeight, refIdx[part], part, 4);

        var mvX = predX + reader.ReadSignedExpGolomb();
        var mvY = predY + reader.ReadSignedExpGolomb();
        this._AssignMotion(x >> 2, y >> 2, subWidth >> 2, subHeight >> 2, refIdx[part], mvX, mvY);
      }
    }

    var cbp = H264CavlcTables.ReadCodedBlockPattern(ref reader, intra: false);
    this._ReadResidualAndQp(ref reader, mbAddr, cbp & 15, cbp >> 4, intra16x16: false);

    // Prediction happens after the residual is read because the residual comes after the motion data
    // in the bitstream; the vectors are all known by now, so the whole macroblock is predicted 4x4
    // block by 4x4 block from what was stored.
    for (var by = 0; by < 4; ++by)
      for (var bx = 0; bx < 4; ++bx) {
        var at = (mbY * 4 + by) * this._blockWidth + mbX * 4 + bx;
        this._Predict(
          mbAddr, this._refIdx[at], mbX * 16 + bx * 4, mbY * 16 + by * 4, 4, 4,
          this._mvX[at], this._mvY[at], addResidual: true);
      }

    this._MarkReconstructed(mbX, mbY);
    this._ReconstructChroma(mbAddr, 0, intra: false);
  }

  private int _ReadReferenceIndex(ref H264BitReader reader) {
    if (this._header.NumRefIdxL0Active <= 1)
      return 0;

    var index = reader.ReadTruncatedExpGolomb(this._header.NumRefIdxL0Active - 1);
    if (index >= this._referenceList.Length)
      throw new InvalidDataException(
        $"An H.264 macroblock names reference index {index} of a list holding {this._referenceList.Length} "
        + "picture(s). The stream refers to a picture that was never decoded — decoding did not begin at an IDR.");

    return index;
  }

  private void _AssignMotion(int blockX, int blockY, int blocksWide, int blocksHigh, int refIdx, int mvX, int mvY) {
    var serial = this._referenceList.Length > refIdx ? this._referenceList[refIdx].Serial : 0;

    for (var y = 0; y < blocksHigh; ++y) {
      var row = (blockY + y) * this._blockWidth + blockX;
      for (var x = 0; x < blocksWide; ++x) {
        this._mvX[row + x] = (short)mvX;
        this._mvY[row + x] = (short)mvY;
        this._refIdx[row + x] = (sbyte)refIdx;
        this._refSerial[row + x] = serial;
        this._motionAssigned[row + x] = true;
      }
    }
  }

  private void _MarkReconstructed(int mbX, int mbY) {
    for (var by = 0; by < 4; ++by) {
      var row = (mbY * 4 + by) * this._blockWidth + mbX * 4;
      for (var bx = 0; bx < 4; ++bx)
        this._blockReconstructed[row + bx] = true;
    }
  }

  /// <summary>
  /// Predicts one partition from its reference picture, applies the slice's explicit weight after
  /// interpolation, and adds its residual where it has one.
  /// </summary>
  private void _Predict(
    int mbAddr, int refIdx, int x, int y, int width, int height, int mvX, int mvY, bool addResidual) {
    if (refIdx < 0 || refIdx >= this._referenceList.Length)
      throw new InvalidDataException(
        $"An H.264 inter macroblock names reference index {refIdx} of a list holding "
        + $"{this._referenceList.Length} picture(s).");

    var reference = this._referenceList[refIdx];
    var picture = this.Picture;

    Span<byte> pred = stackalloc byte[256];
    H264MotionCompensation.PredictLuma(
      reference.Luma, reference.LumaWidth, reference.LumaHeight, x, y, mvX, mvY, width, height, pred);
    this._header.PredictionWeights?.ApplyLuma(refIdx, pred[..(width * height)]);

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var qp = this._qpY[mbAddr];

    Span<int> residual = stackalloc int[16];
    Span<byte> blockPrediction = stackalloc byte[16];

    for (var blockRow = 0; blockRow < height >> 2; ++blockRow)
      for (var blockColumn = 0; blockColumn < width >> 2; ++blockColumn) {
        for (var row = 0; row < 4; ++row)
          for (var column = 0; column < 4; ++column)
            blockPrediction[(row << 2) + column] = pred[(blockRow * 4 + row) * width + blockColumn * 4 + column];

        var blockX = (x >> 2) + blockColumn;
        var blockY = (y >> 2) + blockRow;
        var blkIdx = _BlockIndex(blockX - mbX * 4, blockY - mbY * 4);

        if (addResidual && this._lumaCoeffCount[blockY * this._blockWidth + blockX] > 0) {
          H264Transform.DecodeBlock(this._lumaLevels.AsSpan(blkIdx * 16, 16), qp, hasSeparateDc: false, 0, residual);
          _AddResidual(picture.Luma, picture.LumaWidth, blockX * 4, blockY * 4, 4, blockPrediction, residual);
          continue;
        }

        _CopyPrediction(picture.Luma, picture.LumaWidth, blockX * 4, blockY * 4, 4, blockPrediction);
      }
  }

  /// <summary>The motion vector predictor of clause 8.4.1.3.</summary>
  private (int X, int Y) _PredictMotion(
    int mbAddr, int x, int y, int partWidth, int partHeight, int refIdx, int partIdx, int partCount) {
    var a = this._NeighbourMotion(mbAddr, x - 1, y);
    var b = this._NeighbourMotion(mbAddr, x, y - 1);
    var c = this._NeighbourMotion(mbAddr, x + partWidth, y - 1);
    if (!c.Available)
      c = this._NeighbourMotion(mbAddr, x - 1, y - 1);

    // The directional cases: a partition that is half a macroblock tall or wide takes its predictor
    // straight from the neighbour along the split, when that neighbour uses the same reference
    // picture (equations 8-203 to 8-206). The median would blur two motions that the encoder split
    // the macroblock precisely in order to keep apart.
    if (partCount == 2 && partWidth == 16 && partHeight == 8) {
      if (partIdx == 0 && b.RefIdx == refIdx)
        return (b.MvX, b.MvY);

      if (partIdx == 1 && a.RefIdx == refIdx)
        return (a.MvX, a.MvY);
    } else if (partCount == 2 && partWidth == 8 && partHeight == 16) {
      if (partIdx == 0 && a.RefIdx == refIdx)
        return (a.MvX, a.MvY);

      if (partIdx == 1 && c.RefIdx == refIdx)
        return (c.MvX, c.MvY);
    }

    return _Median(a, b, c, refIdx);
  }

  /// <summary>Clause 8.4.1.3.1.</summary>
  private static (int X, int Y) _Median(
    (bool Available, int MvX, int MvY, int RefIdx) a,
    (bool Available, int MvX, int MvY, int RefIdx) b,
    (bool Available, int MvX, int MvY, int RefIdx) c,
    int refIdx) {
    // A macroblock at the top of a picture has no B or C, and the standard says to use A for all
    // three rather than to median a vector against two zeroes (equations 8-207 to 8-210).
    if (!b.Available && !c.Available && a.Available) {
      b = a;
      c = a;
    }

    var matches = (a.RefIdx == refIdx ? 1 : 0) + (b.RefIdx == refIdx ? 1 : 0) + (c.RefIdx == refIdx ? 1 : 0);
    if (matches == 1)
      return a.RefIdx == refIdx ? (a.MvX, a.MvY)
        : b.RefIdx == refIdx ? (b.MvX, b.MvY)
        : (c.MvX, c.MvY);

    return (_MedianOf(a.MvX, b.MvX, c.MvX), _MedianOf(a.MvY, b.MvY, c.MvY));
  }

  private static int _MedianOf(int first, int second, int third)
    => first + second + third - Math.Min(first, Math.Min(second, third)) - Math.Max(first, Math.Max(second, third));

  /// <summary>The motion of one neighbouring partition — clause 8.4.1.3.2.</summary>
  private (bool Available, int MvX, int MvY, int RefIdx) _NeighbourMotion(int mbAddr, int x, int y) {
    if (x < 0 || y < 0 || x >= this._mbWidth * 16 || y >= this._mbHeight * 16)
      return (false, 0, 0, -1);

    var neighbourMb = y / 16 * this._mbWidth + x / 16;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return (false, 0, 0, -1);

    var at = (y >> 2) * this._blockWidth + (x >> 2);

    // Inside the macroblock being decoded, a partition whose vector has not been read yet counts as
    // absent rather than as zero (clause 6.4.11.7, step 7c).
    if (neighbourMb == mbAddr && !this._motionAssigned[at])
      return (false, 0, 0, -1);

    // An intra neighbour is available as a partition but lends nothing, which is not the same as
    // being absent: it still counts towards the "B and C both absent" test above.
    if (this._kind[neighbourMb] != H264MacroblockKind.Inter)
      return (true, 0, 0, -1);

    return (true, this._mvX[at], this._mvY[at], this._refIdx[at]);
  }

  /// <summary>The motion vector of a skipped macroblock — clause 8.4.1.1.</summary>
  private (int X, int Y) _PredictSkipMotion(int mbAddr, int x, int y) {
    var a = this._NeighbourMotion(mbAddr, x - 1, y);
    var b = this._NeighbourMotion(mbAddr, x, y - 1);

    // A skipped macroblock beside a picture edge, or beside one that is itself not moving, is not
    // moving either. Everywhere else it takes the ordinary predictor and no correction at all, which
    // is what makes a skip a skip: the motion was predictable and the residual was nothing.
    if (!a.Available || !b.Available
        || (a.RefIdx == 0 && a.MvX == 0 && a.MvY == 0)
        || (b.RefIdx == 0 && b.MvX == 0 && b.MvY == 0))
      return (0, 0);

    return this._PredictMotion(mbAddr, x, y, 16, 16, 0, 0, 1);
  }

  // ============================================================================================
  // Chroma reconstruction — clauses 8.3.4 and 8.5.4
  // ============================================================================================

  private void _ReconstructChroma(int mbAddr, int chromaMode, bool intra) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var picture = this.Picture;
    var qpY = this._qpY[mbAddr];

    Span<byte> pred = stackalloc byte[64];
    Span<byte> top = stackalloc byte[8];
    Span<byte> left = stackalloc byte[8];
    Span<int> dc = stackalloc int[4];
    Span<int> residual = stackalloc int[16];
    Span<byte> blockPrediction = stackalloc byte[16];

    for (var component = 0; component < 2; ++component) {
      var plane = picture.Chroma(component);
      var qp = H264Transform.ChromaQp(Math.Clamp(qpY + this.ChromaQpOffsetOf(mbAddr, component), 0, 51));

      if (intra) {
        var neighbours = this._GatherChromaNeighbours(mbAddr, component, top, left, out var topLeft);
        H264IntraPrediction.PredictChroma8x8(
          chromaMode, top, left, topLeft, neighbours.Top, neighbours.Left, neighbours.TopLeft, pred);
      } else {
        this._PredictChromaInter(mbAddr, component, pred);
      }

      H264Transform.DecodeChromaDc(this._chromaDcLevels.AsSpan(component * 4, 4), qp, dc);

      for (var blkIdx = 0; blkIdx < 4; ++blkIdx) {
        var bx = (blkIdx & 1) * 4;
        var by = (blkIdx >> 1) * 4;

        for (var row = 0; row < 4; ++row)
          for (var column = 0; column < 4; ++column)
            blockPrediction[(row << 2) + column] = pred[((by + row) << 3) + bx + column];

        var levels = this._chromaLevels.AsSpan((component * 4 + blkIdx) * 16, 16);
        H264Transform.DecodeBlock(levels, qp, hasSeparateDc: true, dc[blkIdx], residual);
        _AddResidual(plane, picture.ChromaWidth, mbX * 8 + bx, mbY * 8 + by, 4, blockPrediction, residual);
      }
    }
  }

  /// <summary>
  /// Predicts a macroblock's chroma from the reference picture, one 4x4 luma block's motion at a
  /// time — clause 8.4.1.4. Explicit weights are applied to each interpolated block before it is
  /// copied into the macroblock prediction.
  /// </summary>
  /// <remarks>
  /// Block by block rather than partition by partition because chroma is half the size in each
  /// direction: a 4x4 luma partition covers 2x2 chroma samples, and the four sub-partitions of an
  /// 8x8 may each have their own vector. Walking the 4x4 grid covers every partitioning there is
  /// without a case for each.
  /// </remarks>
  private void _PredictChromaInter(int mbAddr, int component, Span<byte> pred) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;

    Span<byte> block = stackalloc byte[4];

    for (var by = 0; by < 4; ++by)
      for (var bx = 0; bx < 4; ++bx) {
        var at = (mbY * 4 + by) * this._blockWidth + mbX * 4 + bx;
        var refIdx = this._refIdx[at];
        if (refIdx < 0 || refIdx >= this._referenceList.Length)
          throw new InvalidDataException(
            $"An H.264 inter macroblock left 4x4 block ({bx}, {by}) with no reference index, so its chroma cannot "
            + "be predicted. The macroblock's motion data is incomplete.");

        var reference = this._referenceList[refIdx];
        H264MotionCompensation.PredictChroma(
          reference.Chroma(component), reference.ChromaWidth, reference.ChromaHeight,
          mbX * 8 + bx * 2, mbY * 8 + by * 2, this._mvX[at], this._mvY[at], 2, 2, block);
        this._header.PredictionWeights?.ApplyChroma(refIdx, component, block);

        for (var row = 0; row < 2; ++row)
          for (var column = 0; column < 2; ++column)
            pred[((by * 2 + row) << 3) + bx * 2 + column] = block[(row << 1) + column];
      }
  }

  // ============================================================================================
  // Residual — clauses 7.3.5.3 and 9.2
  // ============================================================================================

  private void _ReadResidualAndQp(
    ref H264BitReader reader, int mbAddr, int cbpLuma, int cbpChroma, bool intra16x16) {
    if (cbpLuma == 0 && cbpChroma == 0 && !intra16x16) {
      // No mb_qp_delta is coded, and clause 7.4.5 infers it to be zero — so the quantiser carries
      // over from the previous macroblock of this slice rather than reverting to the slice's.
      this._qpY[mbAddr] = (sbyte)this._qpRunning;
      return;
    }

    var delta = reader.ReadSignedExpGolomb();
    if (delta is < -26 or > 25)
      throw new InvalidDataException(
        $"An H.264 macroblock states mb_qp_delta {delta}. H.264, clause 7.4.5 confines it to -26..25 for 8-bit "
        + "samples.");

    // Equation 7-37, with QpBdOffsetY zero for 8-bit samples: the quantiser wraps rather than
    // saturating, so a stream may step from 51 to 0 in one macroblock with a delta of one.
    this._qpRunning = (this._qpRunning + delta + 52) % 52;
    this._qpY[mbAddr] = (sbyte)this._qpRunning;

    this._ReadResidual(ref reader, mbAddr, cbpLuma, cbpChroma, intra16x16);
  }

  private void _ReadResidual(ref H264BitReader reader, int mbAddr, int cbpLuma, int cbpChroma, bool intra16x16) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;

    if (intra16x16) {
      // The DC block's nC comes from the neighbours of block zero (clause 9.2.1, step 1), not from
      // anything of the DC block's own.
      var nC = this._LumaNc(mbX * 4, mbY * 4);
      H264Residual.ReadBlock(ref reader, this._lumaDcLevels, nC, chromaDc: false);
    }

    for (var i8x8 = 0; i8x8 < 4; ++i8x8)
      for (var i4x4 = 0; i4x4 < 4; ++i4x4) {
        var blkIdx = i8x8 * 4 + i4x4;
        var (bx, by) = _BlockPosition(blkIdx);
        var blockX = mbX * 4 + (bx >> 2);
        var blockY = mbY * 4 + (by >> 2);

        if ((cbpLuma & (1 << i8x8)) == 0)
          continue;

        var nC = this._LumaNc(blockX, blockY);
        var count = intra16x16
          ? H264Residual.ReadBlock(ref reader, this._lumaLevels.AsSpan(blkIdx * 16 + 1, 15), nC, chromaDc: false)
          : H264Residual.ReadBlock(ref reader, this._lumaLevels.AsSpan(blkIdx * 16, 16), nC, chromaDc: false);

        this._lumaCoeffCount[blockY * this._blockWidth + blockX] = (byte)count;
      }

    if (cbpChroma == 0)
      return;

    // Chroma is coded DC first for both components and then AC for both, rather than each component
    // whole (clause 7.3.5.3): the DC blocks are small and putting them together costs fewer bits.
    for (var component = 0; component < 2; ++component)
      H264Residual.ReadBlock(ref reader, this._chromaDcLevels.AsSpan(component * 4, 4), -1, chromaDc: true);

    if (cbpChroma < 2)
      return;

    for (var component = 0; component < 2; ++component)
      for (var blkIdx = 0; blkIdx < 4; ++blkIdx) {
        var blockX = mbX * 2 + (blkIdx & 1);
        var blockY = mbY * 2 + (blkIdx >> 1);
        var nC = this._ChromaNc(component, blockX, blockY);
        var count = H264Residual.ReadBlock(
          ref reader, this._chromaLevels.AsSpan((component * 4 + blkIdx) * 16 + 1, 15), nC, chromaDc: false);

        this._chromaCoeffCount[this._ChromaBlockBase(component, blockX, blockY)] = (byte)count;
      }
  }

  /// <summary>The table selector for a luma block, from its two decoded neighbours — clause 9.2.1.</summary>
  private int _LumaNc(int blockX, int blockY) {
    var a = this._LumaNeighbourCount(blockX - 1, blockY, out var haveA);
    var b = this._LumaNeighbourCount(blockX, blockY - 1, out var haveB);

    return haveA && haveB ? (a + b + 1) >> 1 : haveA ? a : haveB ? b : 0;
  }

  private int _LumaNeighbourCount(int blockX, int blockY, out bool available) {
    available = false;
    if (blockX < 0 || blockY < 0 || blockX >= this._blockWidth || blockY >= this._mbHeight * 4)
      return 0;

    var neighbourMb = blockY / 4 * this._mbWidth + blockX / 4;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return 0;

    available = true;
    return this._lumaCoeffCount[blockY * this._blockWidth + blockX];
  }

  private int _ChromaNc(int component, int blockX, int blockY) {
    var a = this._ChromaNeighbourCount(component, blockX - 1, blockY, out var haveA);
    var b = this._ChromaNeighbourCount(component, blockX, blockY - 1, out var haveB);

    return haveA && haveB ? (a + b + 1) >> 1 : haveA ? a : haveB ? b : 0;
  }

  private int _ChromaNeighbourCount(int component, int blockX, int blockY, out bool available) {
    available = false;
    if (blockX < 0 || blockY < 0 || blockX >= this._chromaBlockWidth || blockY >= this._mbHeight * 2)
      return 0;

    var neighbourMb = blockY / 2 * this._mbWidth + blockX / 2;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return 0;

    available = true;
    return this._chromaCoeffCount[this._ChromaBlockBase(component, blockX, blockY)];
  }

  // ============================================================================================
  // Neighbouring samples — clauses 8.3.1.2, 8.3.3 and 8.3.4
  // ============================================================================================

  private (bool Top, bool Left, bool TopLeft) _GatherLuma4x4Neighbours(
    int mbAddr, int x, int y, int blkIdx, Span<byte> top, Span<byte> left, out byte topLeft) {
    var plane = this.Picture.Luma;
    var width = this.Picture.LumaWidth;

    var topAvailable = this._SampleAvailable(mbAddr, x, y - 1, blkIdx);
    var leftAvailable = this._SampleAvailable(mbAddr, x - 1, y, blkIdx);
    var topLeftAvailable = this._SampleAvailable(mbAddr, x - 1, y - 1, blkIdx);

    for (var i = 0; i < 4; ++i) {
      top[i] = topAvailable ? plane[(y - 1) * width + x + i] : (byte)0;
      left[i] = leftAvailable ? plane[(y + i) * width + x - 1] : (byte)0;
    }

    // p[4..7,−1]: the four samples above and to the right, which for two of the sixteen blocks belong
    // to a block that has not been decoded yet. Where they are missing and p[3,−1] is not, the
    // standard repeats p[3,−1] across them rather than marking the whole row unavailable — so the
    // diagonal modes stay usable at the right-hand edge of a macroblock (clause 8.3.1.2).
    var topRightAvailable = this._SampleAvailable(mbAddr, x + 4, y - 1, blkIdx);
    for (var i = 4; i < 8; ++i)
      top[i] = topRightAvailable ? plane[(y - 1) * width + x + i]
        : topAvailable ? top[3]
        : (byte)0;

    topLeft = topLeftAvailable ? plane[(y - 1) * width + x - 1] : (byte)0;
    return (topAvailable, leftAvailable, topLeftAvailable);
  }

  private (bool Top, bool Left, bool TopLeft) _GatherLumaMacroblockNeighbours(
    int mbAddr, Span<byte> top, Span<byte> left, out byte topLeft) {
    var plane = this.Picture.Luma;
    var width = this.Picture.LumaWidth;
    var x = mbAddr % this._mbWidth * 16;
    var y = mbAddr / this._mbWidth * 16;

    var topAvailable = this._MacroblockUsable(x, y - 1);
    var leftAvailable = this._MacroblockUsable(x - 1, y);
    var topLeftAvailable = this._MacroblockUsable(x - 1, y - 1);

    for (var i = 0; i < 16; ++i) {
      top[i] = topAvailable ? plane[(y - 1) * width + x + i] : (byte)0;
      left[i] = leftAvailable ? plane[(y + i) * width + x - 1] : (byte)0;
    }

    topLeft = topLeftAvailable ? plane[(y - 1) * width + x - 1] : (byte)0;
    return (topAvailable, leftAvailable, topLeftAvailable);
  }

  private (bool Top, bool Left, bool TopLeft) _GatherChromaNeighbours(
    int mbAddr, int component, Span<byte> top, Span<byte> left, out byte topLeft) {
    var plane = this.Picture.Chroma(component);
    var width = this.Picture.ChromaWidth;
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var x = mbX * 8;
    var y = mbY * 8;

    var topAvailable = this._MacroblockUsable(mbX * 16, mbY * 16 - 1);
    var leftAvailable = this._MacroblockUsable(mbX * 16 - 1, mbY * 16);
    var topLeftAvailable = this._MacroblockUsable(mbX * 16 - 1, mbY * 16 - 1);

    for (var i = 0; i < 8; ++i) {
      top[i] = topAvailable ? plane[(y - 1) * width + x + i] : (byte)0;
      left[i] = leftAvailable ? plane[(y + i) * width + x - 1] : (byte)0;
    }

    topLeft = topLeftAvailable ? plane[(y - 1) * width + x - 1] : (byte)0;
    return (topAvailable, leftAvailable, topLeftAvailable);
  }

  /// <summary>
  /// Whether a luma sample position may be read for intra prediction — clause 8.3.1.2, step 3.
  /// </summary>
  /// <param name="blkIdx">
  /// The index of the block being predicted, which decides whether a position inside the current
  /// macroblock has been reconstructed yet.
  /// </param>
  private bool _SampleAvailable(int mbAddr, int x, int y, int blkIdx) {
    if (!this._MacroblockUsable(x, y))
      return false;

    var neighbourMb = y / 16 * this._mbWidth + x / 16;
    if (neighbourMb != mbAddr)
      return true;

    // Inside the macroblock being decoded, the sample exists only if its own block has already been
    // reconstructed. Working it out from the block order rather than from a rule about which indices
    // are affected is the same answer — it is exactly blocks 3 and 11 whose above-right neighbour is
    // still to come, which is the case the standard names.
    var neighbour = _BlockIndex((x & 15) >> 2, (y & 15) >> 2);
    return neighbour < blkIdx;
  }

  /// <summary>Whether the macroblock holding a sample position may be read from at all.</summary>
  private bool _MacroblockUsable(int x, int y) {
    if (x < 0 || y < 0 || x >= this._mbWidth * 16 || y >= this._mbHeight * 16)
      return false;

    var neighbourMb = y / 16 * this._mbWidth + x / 16;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return false;

    return this._kind[neighbourMb] != H264MacroblockKind.Inter || !this._header.Pps.ConstrainedIntraPredFlag;
  }

  // ============================================================================================
  // Block geometry — clause 6.4.3
  // ============================================================================================

  /// <summary>The position of a 4x4 luma block inside its macroblock, in samples.</summary>
  /// <remarks>
  /// The Z order of clause 6.4.3, which is neither raster nor anything simpler: the macroblock is
  /// four 8x8 quadrants in raster order and each quadrant is four 4x4 blocks in raster order.
  /// </remarks>
  private static (int X, int Y) _BlockPosition(int blkIdx) {
    var quadrant = blkIdx >> 2;
    var within = blkIdx & 3;
    return (((quadrant & 1) << 3) + ((within & 1) << 2), ((quadrant >> 1) << 3) + ((within >> 1) << 2));
  }

  /// <summary>
  /// The inverse of <see cref="_BlockPosition"/>: which block index a 4x4 block of a macroblock is.
  /// </summary>
  /// <param name="blockX">The block's column within the macroblock, 0 to 3 — not its sample column.</param>
  /// <param name="blockY">Its row within the macroblock, likewise.</param>
  private static int _BlockIndex(int blockX, int blockY) {
    var quadrant = ((blockY >> 1) << 1) + (blockX >> 1);
    var within = ((blockY & 1) << 1) + (blockX & 1);
    return (quadrant << 2) + within;
  }

  private static void _AddResidual(
    byte[] plane, int planeWidth, int x, int y, int size, ReadOnlySpan<byte> pred, ReadOnlySpan<int> residual) {
    for (var row = 0; row < size; ++row) {
      var target = (y + row) * planeWidth + x;
      for (var column = 0; column < size; ++column) {
        var value = pred[row * size + column] + residual[row * size + column];
        plane[target + column] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  private static void _CopyPrediction(
    byte[] plane, int planeWidth, int x, int y, int size, ReadOnlySpan<byte> pred) {
    for (var row = 0; row < size; ++row) {
      var target = (y + row) * planeWidth + x;
      for (var column = 0; column < size; ++column)
        plane[target + column] = pred[row * size + column];
    }
  }
}
