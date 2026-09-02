using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>B-slice motion/list1 reconstruction layered onto the shared CAVLC frame decoder.</summary>
internal sealed partial class H264FrameDecoder {
  private enum BPredMode : byte { Direct, L0, L1, Bi }

  private H264Picture[] _referenceList1 = [];
  private short[]? _mvX1;
  private short[]? _mvY1;
  private sbyte[]? _refIdx1;
  private long[]? _refSerial1;

  /// <summary>Decodes one CAVLC B slice with two reference lists.</summary>
  internal void DecodeBSlice(
    ref H264BitReader reader,
    H264SliceHeader header,
    H264Picture[] referenceList0,
    H264Picture[] referenceList1) {
    if (!header.IsB)
      throw new ArgumentException("DecodeBSlice requires a B slice header.", nameof(header));
    if (header.Pps.EntropyCodingModeFlag)
      throw new InvalidOperationException("CABAC B slices must enter the CABAC slice-data path.");

    this._EnsureList1State();
    this._header = header;
    this._referenceList = referenceList0;
    this._referenceList1 = referenceList1;
    this._scalingLists = header.Pps.ResolveScalingLists(header.Sps);
    this._currentSliceId = this._nextSliceId++;
    this._qpRunning = header.SliceQpY;

    var mbAddr = header.FirstMbInSlice;
    if (mbAddr >= this._mbCount)
      throw new InvalidDataException(
        $"An H.264 B slice states first_mb_in_slice {mbAddr}, beyond {this._mbCount} macroblocks.");

    var moreData = true;
    while (true) {
      var skipRun = reader.ReadUnsignedExpGolomb();
      for (var i = 0; i < skipRun; ++i) {
        this._RefuseAddressPastPicture(mbAddr, "a run of skipped B macroblocks");
        this._DecodeBSkipped(mbAddr++);
      }
      if (skipRun > 0)
        moreData = reader.MoreRbspData;

      if (moreData) {
        this._RefuseAddressPastPicture(mbAddr, "a coded B macroblock");
        this._DecodeBMacroblock(ref reader, mbAddr);
      }

      moreData = reader.MoreRbspData;
      ++mbAddr;
      if (!moreData)
        return;
    }
  }

  /// <summary>Copies both reference-list motion fields into a picture that may later be co-located.</summary>
  internal H264MotionField ExportMotionField() {
    var field = new H264MotionField(this._blockWidth, this._mbHeight * 4);
    Array.Copy(this._mvX, field.MvX0, this._mvX.Length);
    Array.Copy(this._mvY, field.MvY0, this._mvY.Length);
    Array.Copy(this._refIdx, field.RefIdx0, this._refIdx.Length);
    Array.Copy(this._refSerial, field.RefSerial0, this._refSerial.Length);
    if (this._mvX1 != null) {
      Array.Copy(this._mvX1, field.MvX1, this._mvX1.Length);
      Array.Copy(this._mvY1!, field.MvY1, this._mvY1!.Length);
      Array.Copy(this._refIdx1!, field.RefIdx1, this._refIdx1!.Length);
      Array.Copy(this._refSerial1!, field.RefSerial1, this._refSerial1!.Length);
    }
    return field;
  }

  /// <summary>Both list motions for deblocking's B-picture boundary-strength comparison.</summary>
  internal (
    int X0, int Y0, long Reference0, bool Predicted0,
    int X1, int Y1, long Reference1, bool Predicted1) BlockMotionPair(int blockX, int blockY) {
    var at = blockY * this._blockWidth + blockX;
    var predicted1 = this._refIdx1 != null && this._refIdx1[at] >= 0;
    return (
      this._mvX[at], this._mvY[at], this._refSerial[at], this._refIdx[at] >= 0,
      this._mvX1?[at] ?? 0, this._mvY1?[at] ?? 0, this._refSerial1?[at] ?? 0, predicted1);
  }

  private void _DecodeBMacroblock(ref H264BitReader reader, int mbAddr) {
    this._BeginMacroblock(mbAddr);
    this._ClearList1Motion(mbAddr);
    var mbType = reader.ReadUnsignedExpGolomb();
    if (mbType > 48)
      throw new InvalidDataException(
        $"An H.264 B slice states mb_type {mbType}; Table 7-14 defines 0 through 48.");

    if (mbType >= 23) {
      var intraType = mbType - 23;
      if (intraType == _PCM_MB_TYPE_I) {
        this._DecodePcm(ref reader, mbAddr);
        return;
      }
      if (intraType == 0) {
        var transform8x8 = this._header.Pps.Transform8x8ModeFlag && reader.ReadBit() != 0;
        if (transform8x8)
          this._DecodeIntra8x8(ref reader, mbAddr);
        else
          this._DecodeIntra4x4(ref reader, mbAddr);
        return;
      }
      this._DecodeIntra16x16(ref reader, mbAddr, intraType);
      return;
    }

    this._kind[mbAddr] = H264MacroblockKind.Inter;
    if (mbType == 22) {
      this._DecodeB8x8(ref reader, mbAddr);
      return;
    }

    if (mbType == 0) {
      this._RankMotionPartitions(mbAddr, 16, 16, 1);
      this._DeriveDirect(mbAddr, mbAddr % this._mbWidth * 16, mbAddr / this._mbWidth * 16, 16, 16);
      this._FinishBInterMacroblock(ref reader, mbAddr, canTransform8x8: this._header.Sps.Direct8x8InferenceFlag);
      return;
    }

    var (partWidth, partHeight, firstMode, secondMode) = _BMacroblockLayout(mbType);
    var partCount = partWidth == 16 && partHeight == 16 ? 1 : 2;
    this._RankMotionPartitions(mbAddr, partWidth, partHeight, partCount);
    Span<BPredMode> modes = stackalloc BPredMode[2];
    modes[0] = firstMode;
    modes[1] = secondMode;
    Span<int> ref0 = stackalloc int[2];
    Span<int> ref1 = stackalloc int[2];
    ref0.Fill(-1);
    ref1.Fill(-1);

    for (var part = 0; part < partCount; ++part)
      if (_UsesList0(modes[part]))
        ref0[part] = this._ReadBReferenceIndex(ref reader, 0);
    for (var part = 0; part < partCount; ++part)
      if (_UsesList1(modes[part]))
        ref1[part] = this._ReadBReferenceIndex(ref reader, 1);

    for (var part = 0; part < partCount; ++part) {
      var x = mbAddr % this._mbWidth * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbAddr / this._mbWidth * 16 + (partHeight == 8 ? part * 8 : 0);
      if (_UsesList0(modes[part])) {
        var (px, py) = this._PredictMotionForList(
          0, mbAddr, x, y, partWidth, partHeight, ref0[part], part, partCount, _MotionPartitionRankOf(part, 0));
        this._AssignMotionList(0, x >> 2, y >> 2, partWidth >> 2, partHeight >> 2, ref0[part],
          px + reader.ReadSignedExpGolomb(), py + reader.ReadSignedExpGolomb());
      }
    }
    for (var part = 0; part < partCount; ++part) {
      var x = mbAddr % this._mbWidth * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbAddr / this._mbWidth * 16 + (partHeight == 8 ? part * 8 : 0);
      if (_UsesList1(modes[part])) {
        var (px, py) = this._PredictMotionForList(
          1, mbAddr, x, y, partWidth, partHeight, ref1[part], part, partCount, _MotionPartitionRankOf(part, 0));
        this._AssignMotionList(1, x >> 2, y >> 2, partWidth >> 2, partHeight >> 2, ref1[part],
          px + reader.ReadSignedExpGolomb(), py + reader.ReadSignedExpGolomb());
      }
    }

    this._FinishBInterMacroblock(ref reader, mbAddr, canTransform8x8: true);
  }

  private void _DecodeBSkipped(int mbAddr) {
    this._BeginMacroblock(mbAddr);
    this._ClearList1Motion(mbAddr);
    this._kind[mbAddr] = H264MacroblockKind.Inter;
    this._qpY[mbAddr] = (sbyte)this._qpRunning;
    var x = mbAddr % this._mbWidth * 16;
    var y = mbAddr / this._mbWidth * 16;
    this._RankMotionPartitions(mbAddr, 16, 16, 1);
    this._DeriveDirect(mbAddr, x, y, 16, 16);
    this._PredictBStored(mbAddr, x, y, 16, 16, addResidual: false);
    this._MarkReconstructed(mbAddr % this._mbWidth, mbAddr / this._mbWidth);
    this._ReconstructChromaB(mbAddr);
  }

  private void _FinishBInterMacroblock(ref H264BitReader reader, int mbAddr, bool canTransform8x8) {
    var cbp = H264CavlcTables.ReadCodedBlockPattern(ref reader, intra: false);
    var transform8x8 = this._header.Pps.Transform8x8ModeFlag
      && canTransform8x8
      && (cbp & 15) != 0
      && reader.ReadBit() != 0;
    this._ReadResidualAndQp(ref reader, mbAddr, cbp & 15, cbp >> 4, intra16x16: false, transform8x8);

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    for (var by = 0; by < 4; ++by)
      for (var bx = 0; bx < 4; ++bx)
        this._PredictBStored(
          mbAddr, mbX * 16 + bx * 4, mbY * 16 + by * 4, 4, 4,
          addResidual: !transform8x8);
    if (transform8x8)
      this._AddInter8x8Residuals(mbAddr, cbp & 15);
    this._MarkReconstructed(mbX, mbY);
    this._ReconstructChromaB(mbAddr);
  }

  private void _DecodeB8x8(ref H264BitReader reader, int mbAddr) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    Span<int> subType = stackalloc int[4];
    Span<BPredMode> mode = stackalloc BPredMode[4];
    var canTransform8x8 = true;
    for (var part = 0; part < 4; ++part) {
      subType[part] = reader.ReadUnsignedExpGolomb();
      if (subType[part] > 12)
        throw new InvalidDataException(
          $"An H.264 B_8x8 macroblock states sub_mb_type {subType[part]}; Table 7-18 defines 0 through 12.");
      mode[part] = _BSubMode(subType[part]);
      var directSmall = subType[part] == 0 && !this._header.Sps.Direct8x8InferenceFlag;
      canTransform8x8 &= subType[part] <= 3 && !directSmall;
    }

    this._RankB8x8MotionPartitions(mbAddr, subType);
    this._DeriveB8x8DirectPartitions(mbAddr, mode, cabac: false);

    Span<int> ref0 = stackalloc int[4];
    Span<int> ref1 = stackalloc int[4];
    ref0.Fill(-1);
    ref1.Fill(-1);
    for (var part = 0; part < 4; ++part)
      if (_UsesList0(mode[part]))
        ref0[part] = this._ReadBReferenceIndex(ref reader, 0);
    for (var part = 0; part < 4; ++part)
      if (_UsesList1(mode[part]))
        ref1[part] = this._ReadBReferenceIndex(ref reader, 1);

    for (var list = 0; list < 2; ++list)
      for (var part = 0; part < 4; ++part) {
        if (mode[part] == BPredMode.Direct)
          continue;
        if (list == 0 && !_UsesList0(mode[part]) || list == 1 && !_UsesList1(mode[part]))
          continue;

        var (subWidth, subHeight, subCount) = _BSubGeometry(subType[part], this._header.Sps.Direct8x8InferenceFlag);
        var baseX = mbX * 16 + (part & 1) * 8;
        var baseY = mbY * 16 + (part >> 1) * 8;
        var referenceIndex = list == 0 ? ref0[part] : ref1[part];
        for (var sub = 0; sub < subCount; ++sub) {
          var x = baseX + (subWidth == 4 ? (sub & 1) * 4 : 0);
          var y = baseY + (subHeight == 4 ? (subCount == 4 ? (sub >> 1) * 4 : sub * 4) : 0);
          var (px, py) = this._PredictMotionForList(
            list, mbAddr, x, y, subWidth, subHeight, referenceIndex, part, 4, _MotionPartitionRankOf(part, sub));
          this._AssignMotionList(list, x >> 2, y >> 2, subWidth >> 2, subHeight >> 2, referenceIndex,
            px + reader.ReadSignedExpGolomb(), py + reader.ReadSignedExpGolomb());
        }
      }

    var cbp = H264CavlcTables.ReadCodedBlockPattern(ref reader, intra: false);
    var transform8x8 = this._header.Pps.Transform8x8ModeFlag
      && canTransform8x8
      && (cbp & 15) != 0
      && reader.ReadBit() != 0;
    this._ReadResidualAndQp(ref reader, mbAddr, cbp & 15, cbp >> 4, intra16x16: false, transform8x8);

    for (var by = 0; by < 4; ++by)
      for (var bx = 0; bx < 4; ++bx)
        this._PredictBStored(
          mbAddr, mbX * 16 + bx * 4, mbY * 16 + by * 4, 4, 4,
          addResidual: !transform8x8);
    if (transform8x8)
      this._AddInter8x8Residuals(mbAddr, cbp & 15);
    this._MarkReconstructed(mbX, mbY);
    this._ReconstructChromaB(mbAddr);
  }

  /// <summary>
  /// Derives the motion of every B_Direct_8x8 sub-macroblock of a B_8x8 macroblock.
  /// </summary>
  /// <remarks>
  /// Clause 8.4.1 walks the sub-macroblock partitions in index order and resolves direct partitions
  /// through 8.4.1.2 in place, so a direct partition already carries motion when a later partition
  /// runs its own neighbour-based prediction. Deriving them after the coded vectors would hide them
  /// from clause 8.4.1.3 and silently change the predictors of the partitions that follow.
  /// </remarks>
  private void _DeriveB8x8DirectPartitions(int mbAddr, ReadOnlySpan<BPredMode> mode, bool cabac) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    for (var part = 0; part < 4; ++part) {
      if (mode[part] != BPredMode.Direct)
        continue;
      var baseX = mbX * 16 + (part & 1) * 8;
      var baseY = mbY * 16 + (part >> 1) * 8;
      if (cabac)
        this._CabacFillDirect(baseX, baseY, 8, 8, true);
      if (this._header.Sps.Direct8x8InferenceFlag)
        this._DeriveDirect(mbAddr, baseX, baseY, 8, 8);
      else
        for (var by = 0; by < 2; ++by)
          for (var bx = 0; bx < 2; ++bx)
            this._DeriveDirect(mbAddr, baseX + bx * 4, baseY + by * 4, 4, 4);
    }
  }

  private static (int Width, int Height, BPredMode First, BPredMode Second) _BMacroblockLayout(int mbType)
    => mbType switch {
      1 => (16, 16, BPredMode.L0, BPredMode.L0),
      2 => (16, 16, BPredMode.L1, BPredMode.L1),
      3 => (16, 16, BPredMode.Bi, BPredMode.Bi),
      4 => (16, 8, BPredMode.L0, BPredMode.L0),
      5 => (8, 16, BPredMode.L0, BPredMode.L0),
      6 => (16, 8, BPredMode.L1, BPredMode.L1),
      7 => (8, 16, BPredMode.L1, BPredMode.L1),
      8 => (16, 8, BPredMode.L0, BPredMode.L1),
      9 => (8, 16, BPredMode.L0, BPredMode.L1),
      10 => (16, 8, BPredMode.L1, BPredMode.L0),
      11 => (8, 16, BPredMode.L1, BPredMode.L0),
      12 => (16, 8, BPredMode.L0, BPredMode.Bi),
      13 => (8, 16, BPredMode.L0, BPredMode.Bi),
      14 => (16, 8, BPredMode.L1, BPredMode.Bi),
      15 => (8, 16, BPredMode.L1, BPredMode.Bi),
      16 => (16, 8, BPredMode.Bi, BPredMode.L0),
      17 => (8, 16, BPredMode.Bi, BPredMode.L0),
      18 => (16, 8, BPredMode.Bi, BPredMode.L1),
      19 => (8, 16, BPredMode.Bi, BPredMode.L1),
      20 => (16, 8, BPredMode.Bi, BPredMode.Bi),
      21 => (8, 16, BPredMode.Bi, BPredMode.Bi),
      _ => throw new InvalidDataException($"H.264 B mb_type {mbType} has no partition layout."),
    };

  private static BPredMode _BSubMode(int subType) => subType switch {
    0 => BPredMode.Direct,
    1 or 4 or 5 or 10 => BPredMode.L0,
    2 or 6 or 7 or 11 => BPredMode.L1,
    3 or 8 or 9 or 12 => BPredMode.Bi,
    _ => throw new InvalidDataException($"H.264 B sub_mb_type {subType} has no prediction mode."),
  };

  private static (int Width, int Height, int Count) _BSubGeometry(int subType, bool direct8x8Inference)
    => subType switch {
      0 => direct8x8Inference ? (8, 8, 1) : (4, 4, 4),
      1 or 2 or 3 => (8, 8, 1),
      4 or 6 or 8 => (8, 4, 2),
      5 or 7 or 9 => (4, 8, 2),
      10 or 11 or 12 => (4, 4, 4),
      _ => throw new InvalidDataException($"H.264 B sub_mb_type {subType} has no sub-partition geometry."),
    };

  private static bool _UsesList0(BPredMode mode) => mode is BPredMode.L0 or BPredMode.Bi;
  private static bool _UsesList1(BPredMode mode) => mode is BPredMode.L1 or BPredMode.Bi;

  private int _ReadBReferenceIndex(ref H264BitReader reader, int list) {
    var active = list == 0 ? this._header.NumRefIdxL0Active : this._header.NumRefIdxL1Active;
    var references = list == 0 ? this._referenceList : this._referenceList1;
    var index = active <= 1 ? 0 : reader.ReadTruncatedExpGolomb(active - 1);
    if ((uint)index >= (uint)references.Length)
      throw new InvalidDataException(
        $"An H.264 B macroblock names list-{list} reference {index}, but that list holds {references.Length} picture(s).");
    return index;
  }

  /// <summary>Ranks whole-macroblock partitions so clause 6.4.11.7 can order them.</summary>
  private void _RankMotionPartitions(int mbAddr, int partWidth, int partHeight, int partCount) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    for (var part = 0; part < partCount; ++part) {
      var x = mbX * 16 + (partWidth == 8 ? part * 8 : 0);
      var y = mbY * 16 + (partHeight == 8 ? part * 8 : 0);
      this._RankMotionPartition(x, y, partWidth, partHeight, _MotionPartitionRankOf(part, 0));
    }
  }

  /// <summary>Ranks the sub-macroblock partitions of a B_8x8 macroblock.</summary>
  private void _RankB8x8MotionPartitions(int mbAddr, ReadOnlySpan<int> subType) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    for (var part = 0; part < 4; ++part) {
      var (subWidth, subHeight, subCount) = _BSubGeometry(subType[part], this._header.Sps.Direct8x8InferenceFlag);
      var baseX = mbX * 16 + (part & 1) * 8;
      var baseY = mbY * 16 + (part >> 1) * 8;
      for (var sub = 0; sub < subCount; ++sub) {
        var x = baseX + (subWidth == 4 ? (sub & 1) * 4 : 0);
        var y = baseY + (subHeight == 4 ? (subCount == 4 ? (sub >> 1) * 4 : sub * 4) : 0);
        this._RankMotionPartition(x, y, subWidth, subHeight, _MotionPartitionRankOf(part, sub));
      }
    }
  }

  private static byte _MotionPartitionRankOf(int partIdx, int subPartIdx) => (byte)(partIdx * 4 + subPartIdx);

  private void _RankMotionPartition(int x, int y, int width, int height, byte rank) {
    for (var by = 0; by < height >> 2; ++by) {
      var row = ((y >> 2) + by) * this._blockWidth + (x >> 2);
      for (var bx = 0; bx < width >> 2; ++bx)
        this._motionPartitionRank[row + bx] = rank;
    }
  }

  private void _EnsureList1State() {
    if (this._mvX1 != null)
      return;
    var count = this._lumaCoeffCount.Length;
    this._mvX1 = new short[count];
    this._mvY1 = new short[count];
    this._refIdx1 = new sbyte[count];
    this._refIdx1.AsSpan().Fill(-1);
    this._refSerial1 = new long[count];
  }

  private void _ClearList1Motion(int mbAddr) {
    this._EnsureList1State();
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    for (var by = 0; by < 4; ++by) {
      var row = (mbY * 4 + by) * this._blockWidth + mbX * 4;
      for (var bx = 0; bx < 4; ++bx) {
        this._mvX1![row + bx] = 0;
        this._mvY1![row + bx] = 0;
        this._refIdx1![row + bx] = -1;
        this._refSerial1![row + bx] = 0;
      }
    }
  }

  private void _AssignMotionList(
    int list, int blockX, int blockY, int blocksWide, int blocksHigh, int refIdx, int mvX, int mvY) {
    var references = list == 0 ? this._referenceList : this._referenceList1;
    if ((uint)refIdx >= (uint)references.Length)
      throw new InvalidDataException(
        $"An H.264 B motion vector names list-{list} reference {refIdx}, but that list holds {references.Length} picture(s).");
    var serial = references[refIdx].Serial;
    for (var y = 0; y < blocksHigh; ++y) {
      var row = (blockY + y) * this._blockWidth + blockX;
      for (var x = 0; x < blocksWide; ++x) {
        var at = row + x;
        if (list == 0) {
          this._mvX[at] = (short)mvX;
          this._mvY[at] = (short)mvY;
          this._refIdx[at] = (sbyte)refIdx;
          this._refSerial[at] = serial;
        } else {
          this._mvX1![at] = (short)mvX;
          this._mvY1![at] = (short)mvY;
          this._refIdx1![at] = (sbyte)refIdx;
          this._refSerial1![at] = serial;
        }
        this._motionAssigned[at] = true;
      }
    }
  }

  private (int X, int Y) _PredictMotionForList(
    int list, int mbAddr, int x, int y, int partWidth, int partHeight, int refIdx, int partIdx, int partCount,
    int partitionRank) {
    var a = this._NeighbourMotionForList(list, mbAddr, x - 1, y, partitionRank);
    var b = this._NeighbourMotionForList(list, mbAddr, x, y - 1, partitionRank);
    var c = this._NeighbourMotionForList(list, mbAddr, x + partWidth, y - 1, partitionRank);
    if (!c.Available)
      c = this._NeighbourMotionForList(list, mbAddr, x - 1, y - 1, partitionRank);

    if (partCount == 2 && partWidth == 16 && partHeight == 8) {
      if (partIdx == 0 && b.RefIdx == refIdx) return (b.MvX, b.MvY);
      if (partIdx == 1 && a.RefIdx == refIdx) return (a.MvX, a.MvY);
    } else if (partCount == 2 && partWidth == 8 && partHeight == 16) {
      if (partIdx == 0 && a.RefIdx == refIdx) return (a.MvX, a.MvY);
      if (partIdx == 1 && c.RefIdx == refIdx) return (c.MvX, c.MvY);
    }
    return _Median(a, b, c, refIdx);
  }

  /// <summary>
  /// Reads one neighbouring partition's list-<paramref name="list"/> motion for clause 8.4.1.3.
  /// </summary>
  /// <remarks>
  /// Clause 6.4.11.7 hides a partition of the current macroblock while it is not yet decoded, and
  /// "decoded" is a property of the partition, not of a single reference list: a partition that only
  /// predicts from the other list still counts as decoded once its turn has passed, and then supplies
  /// refIdxLX -1 with a zero vector rather than triggering the mbAddrC-to-mbAddrD substitution.
  /// </remarks>
  private (bool Available, int MvX, int MvY, int RefIdx) _NeighbourMotionForList(
    int list, int mbAddr, int x, int y, int partitionRank) {
    if (x < 0 || y < 0 || x >= this._mbWidth * 16 || y >= this._mbHeight * 16)
      return (false, 0, 0, -1);
    var neighbourMb = y / 16 * this._mbWidth + x / 16;
    if (this._sliceId[neighbourMb] != this._currentSliceId)
      return (false, 0, 0, -1);
    var at = (y >> 2) * this._blockWidth + (x >> 2);
    if (neighbourMb == mbAddr && this._motionPartitionRank[at] >= partitionRank)
      return (false, 0, 0, -1);
    if (this._kind[neighbourMb] != H264MacroblockKind.Inter)
      return (true, 0, 0, -1);
    return list == 0
      ? (true, this._mvX[at], this._mvY[at], this._refIdx[at])
      : (true, this._mvX1![at], this._mvY1![at], this._refIdx1![at]);
  }

  private void _DeriveDirect(int mbAddr, int x, int y, int width, int height) {
    if (width == 16 && height == 16) {
      var size = this._header.Sps.Direct8x8InferenceFlag ? 8 : 4;
      for (var dy = 0; dy < 16; dy += size)
        for (var dx = 0; dx < 16; dx += size)
          this._DeriveDirect(mbAddr, x + dx, y + dy, size, size);
      return;
    }

    if (this._header.DirectSpatialMvPredFlag)
      this._DeriveSpatialDirect(mbAddr, x, y, width, height);
    else
      this._DeriveTemporalDirect(x, y, width, height);
  }

  private void _DeriveSpatialDirect(int mbAddr, int x, int y, int width, int height) {
    var ref0 = this._DirectSpatialReferenceIndex(0, mbAddr);
    var ref1 = this._DirectSpatialReferenceIndex(1, mbAddr);
    var directZeroPrediction = ref0 < 0 && ref1 < 0;
    if (directZeroPrediction)
      ref0 = ref1 = 0;

    var colZero = this._DirectColZeroFlag(x, y);
    if (ref0 >= 0) {
      var mv = directZeroPrediction || ref0 == 0 && colZero
        ? (X: 0, Y: 0)
        : this._PredictSpatialDirectMotion(0, mbAddr, ref0);
      this._AssignMotionList(0, x >> 2, y >> 2, width >> 2, height >> 2, ref0, mv.X, mv.Y);
    }
    if (ref1 >= 0) {
      var mv = directZeroPrediction || ref1 == 0 && colZero
        ? (X: 0, Y: 0)
        : this._PredictSpatialDirectMotion(1, mbAddr, ref1);
      this._AssignMotionList(1, x >> 2, y >> 2, width >> 2, height >> 2, ref1, mv.X, mv.Y);
    }
  }

  private int _DirectSpatialReferenceIndex(int list, int mbAddr) {
    var x = mbAddr % this._mbWidth * 16;
    var y = mbAddr / this._mbWidth * 16;
    var a = this._NeighbourMotionForList(list, mbAddr, x - 1, y, 0);
    var b = this._NeighbourMotionForList(list, mbAddr, x, y - 1, 0);
    var c = this._NeighbourMotionForList(list, mbAddr, x + 16, y - 1, 0);
    if (!c.Available)
      c = this._NeighbourMotionForList(list, mbAddr, x - 1, y - 1, 0);
    var result = int.MaxValue;
    if (a.Available && a.RefIdx >= 0) result = Math.Min(result, a.RefIdx);
    if (b.Available && b.RefIdx >= 0) result = Math.Min(result, b.RefIdx);
    if (c.Available && c.RefIdx >= 0) result = Math.Min(result, c.RefIdx);
    return result == int.MaxValue ? -1 : result;
  }

  private (int X, int Y) _PredictSpatialDirectMotion(int list, int mbAddr, int refIdx) {
    var x = mbAddr % this._mbWidth * 16;
    var y = mbAddr / this._mbWidth * 16;
    return this._PredictMotionForList(list, mbAddr, x, y, 16, 16, refIdx, 0, 1, 0);
  }

  private (int X, int Y) _DirectCoLocatedPosition(int x, int y) {
    if (!this._header.Sps.Direct8x8InferenceFlag)
      return (x, y);

    var mbPartIdx = ((y & 15) >> 3) * 2 + ((x & 15) >> 3);
    var (offsetX, offsetY) = _BlockPosition(5 * mbPartIdx);
    return ((x & ~15) + offsetX, (y & ~15) + offsetY);
  }

  private bool _DirectColZeroFlag(int x, int y) {
    if (this._referenceList1.Length == 0 || this._referenceList1[0].IsLongTerm)
      return false;
    var col = this._referenceList1[0].Motion;
    var (colX, colY) = this._DirectCoLocatedPosition(x, y);
    if (col == null || !col.TryGet(colX >> 2, colY >> 2, out var motion))
      return false;
    var refIdx = motion.HasList0 ? motion.RefIdx0 : motion.RefIdx1;
    var mvX = motion.HasList0 ? motion.MvX0 : motion.MvX1;
    var mvY = motion.HasList0 ? motion.MvY0 : motion.MvY1;
    return refIdx == 0 && Math.Abs(mvX) <= 1 && Math.Abs(mvY) <= 1;
  }

  private void _DeriveTemporalDirect(int x, int y, int width, int height) {
    if (this._referenceList.Length == 0 || this._referenceList1.Length == 0)
      throw new InvalidDataException("H.264 temporal direct prediction requires both reference lists.");

    var colPic = this._referenceList1[0];
    var colField = colPic.Motion;
    var (colX, colY) = this._DirectCoLocatedPosition(x, y);
    if (colField == null || !colField.TryGet(colX >> 2, colY >> 2, out var colMotion)
        || (!colMotion.HasList0 && !colMotion.HasList1)) {
      this._AssignMotionList(0, x >> 2, y >> 2, width >> 2, height >> 2, 0, 0, 0);
      this._AssignMotionList(1, x >> 2, y >> 2, width >> 2, height >> 2, 0, 0, 0);
      return;
    }

    var colRefSerial = colMotion.HasList0 ? colMotion.RefSerial0 : colMotion.RefSerial1;
    var mvColX = colMotion.HasList0 ? colMotion.MvX0 : colMotion.MvX1;
    var mvColY = colMotion.HasList0 ? colMotion.MvY0 : colMotion.MvY1;
    var refIdx0 = Array.FindIndex(this._referenceList, picture => picture.Serial == colRefSerial);
    if (refIdx0 < 0)
      throw new InvalidDataException(
        "H.264 temporal direct prediction could not map the co-located picture's reference into current list 0.");

    var reference0 = this._referenceList[refIdx0];
    var scale = _TemporalDistanceScale(this.Picture.PicOrderCnt, reference0, colPic);
    var mv0X = (scale * mvColX + 128) >> 8;
    var mv0Y = (scale * mvColY + 128) >> 8;
    var mv1X = mv0X - mvColX;
    var mv1Y = mv0Y - mvColY;
    this._AssignMotionList(0, x >> 2, y >> 2, width >> 2, height >> 2, refIdx0, mv0X, mv0Y);
    this._AssignMotionList(1, x >> 2, y >> 2, width >> 2, height >> 2, 0, mv1X, mv1Y);
  }

  private static int _TemporalDistanceScale(int currentPoc, H264Picture reference0, H264Picture colPic) {
    if (reference0.IsLongTerm)
      return 256;
    var td = Math.Clamp(colPic.PicOrderCnt - reference0.PicOrderCnt, -128, 127);
    if (td == 0)
      return 256;
    var tb = Math.Clamp(currentPoc - reference0.PicOrderCnt, -128, 127);
    var tx = (16384 + Math.Abs(td / 2)) / td;
    return Math.Clamp((tb * tx + 32) >> 6, -1024, 1023);
  }

  private void _PredictBStored(int mbAddr, int x, int y, int width, int height, bool addResidual) {
    if (width > 4 || height > 4) {
      for (var dy = 0; dy < height; dy += 4)
        for (var dx = 0; dx < width; dx += 4)
          this._PredictBStored(mbAddr, x + dx, y + dy, 4, 4, addResidual);
      return;
    }

    var at = (y >> 2) * this._blockWidth + (x >> 2);
    var ref0 = this._refIdx[at];
    var ref1 = this._refIdx1![at];
    this._PredictB(
      mbAddr, x, y, width, height,
      ref0, this._mvX[at], this._mvY[at],
      ref1, this._mvX1![at], this._mvY1![at],
      addResidual);
  }

  private void _PredictB(
    int mbAddr,
    int x,
    int y,
    int width,
    int height,
    int ref0,
    int mv0X,
    int mv0Y,
    int ref1,
    int mv1X,
    int mv1Y,
    bool addResidual) {
    if (ref0 < 0 && ref1 < 0)
      throw new InvalidDataException("An H.264 B partition has neither list-0 nor list-1 prediction.");

    Span<byte> p0 = stackalloc byte[256];
    Span<byte> p1 = stackalloc byte[256];
    Span<byte> prediction = stackalloc byte[256];
    var count = width * height;
    if (ref0 >= 0) {
      var reference = this._referenceList[ref0];
      H264MotionCompensation.PredictLuma(
        reference.Luma, reference.LumaWidth, reference.LumaHeight,
        x, y, mv0X, mv0Y, width, height, p0);
    }
    if (ref1 >= 0) {
      var reference = this._referenceList1[ref1];
      H264MotionCompensation.PredictLuma(
        reference.Luma, reference.LumaWidth, reference.LumaHeight,
        x, y, mv1X, mv1Y, width, height, p1);
    }

    if (ref0 >= 0 && ref1 >= 0)
      this._CombineBPredictions(ref0, ref1, p0[..count], p1[..count], prediction[..count]);
    else if (ref0 >= 0) {
      p0[..count].CopyTo(prediction);
      if (this._header.Pps.WeightedBipredIdc == 1)
        this._header.PredictionWeights?.ApplyLuma(0, ref0, prediction[..count]);
    } else {
      p1[..count].CopyTo(prediction);
      if (this._header.Pps.WeightedBipredIdc == 1)
        this._header.PredictionWeights?.ApplyLuma(1, ref1, prediction[..count]);
    }

    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var qp = this._qpY[mbAddr];
    var scaling = this._scalingLists.FourByFour(3);
    Span<int> residual = stackalloc int[16];
    Span<byte> blockPrediction = stackalloc byte[16];
    for (var blockRow = 0; blockRow < height >> 2; ++blockRow)
      for (var blockColumn = 0; blockColumn < width >> 2; ++blockColumn) {
        for (var row = 0; row < 4; ++row)
          for (var column = 0; column < 4; ++column)
            blockPrediction[(row << 2) + column] =
              prediction[(blockRow * 4 + row) * width + blockColumn * 4 + column];
        var blockX = (x >> 2) + blockColumn;
        var blockY = (y >> 2) + blockRow;
        var blkIdx = _BlockIndex(blockX - mbX * 4, blockY - mbY * 4);
        if (addResidual && this._lumaCoeffCount[blockY * this._blockWidth + blockX] > 0) {
          H264Transform.DecodeBlock(
            this._lumaLevels.AsSpan(blkIdx * 16, 16), qp, hasSeparateDc: false, 0, scaling, residual);
          _AddResidual(this.Picture.Luma, this.Picture.LumaWidth, blockX * 4, blockY * 4, 4, blockPrediction, residual);
        } else {
          _CopyPrediction(this.Picture.Luma, this.Picture.LumaWidth, blockX * 4, blockY * 4, 4, blockPrediction);
        }
      }
  }

  private void _CombineBPredictions(
    int ref0, int ref1, ReadOnlySpan<byte> p0, ReadOnlySpan<byte> p1, Span<byte> output) {
    switch (this._header.Pps.WeightedBipredIdc) {
      case 1:
        if (this._header.PredictionWeights == null)
          throw new InvalidDataException("An explicitly weighted H.264 B slice has no pred_weight_table.");
        this._header.PredictionWeights.CombineLuma(ref0, p0, ref1, p1, output);
        return;
      case 2:
        var (w0, w1) = this._ImplicitWeights(ref0, ref1);
        _WeightedAverage(p0[..], p1[..], output[..], w0, w1);
        return;
      default:
        for (var i = 0; i < p0.Length; ++i)
          output[i] = (byte)((p0[i] + p1[i] + 1) >> 1);
        return;
    }
  }

  private (int W0, int W1) _ImplicitWeights(int ref0, int ref1) {
    var first = this._referenceList[ref0];
    var second = this._referenceList1[ref1];
    if (first.IsLongTerm || second.IsLongTerm)
      return (32, 32);
    var td = Math.Clamp(second.PicOrderCnt - first.PicOrderCnt, -128, 127);
    if (td == 0)
      return (32, 32);
    var tb = Math.Clamp(this.Picture.PicOrderCnt - first.PicOrderCnt, -128, 127);
    var tx = (16384 + Math.Abs(td / 2)) / td;
    var scale = Math.Clamp((tb * tx + 32) >> 6, -1024, 1023);
    var w1 = scale >> 2;
    if (w1 is < -64 or > 128)
      return (32, 32);
    return (64 - w1, w1);
  }

  private static void _WeightedAverage(
    ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> output, int weight0, int weight1) {
    for (var i = 0; i < first.Length; ++i)
      output[i] = (byte)Math.Clamp((weight0 * first[i] + weight1 * second[i] + 32) >> 6, 0, 255);
  }

  private void _ReconstructChromaB(int mbAddr) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    var picture = this.Picture;
    var qpY = this._qpY[mbAddr];
    Span<byte> pred = stackalloc byte[64];
    Span<int> dc = stackalloc int[4];
    Span<int> residual = stackalloc int[16];
    Span<byte> blockPrediction = stackalloc byte[16];

    for (var component = 0; component < 2; ++component) {
      this._PredictChromaB(mbAddr, component, pred);
      var plane = picture.Chroma(component);
      var qp = H264Transform.ChromaQp(Math.Clamp(qpY + this.ChromaQpOffsetOf(mbAddr, component), 0, 51));
      var scaling = this._scalingLists.FourByFour(component == 0 ? 5 : 4);
      H264Transform.DecodeChromaDc(this._chromaDcLevels.AsSpan(component * 4, 4), qp, scaling, dc);
      for (var blkIdx = 0; blkIdx < 4; ++blkIdx) {
        var bx = (blkIdx & 1) * 4;
        var by = (blkIdx >> 1) * 4;
        for (var row = 0; row < 4; ++row)
          for (var column = 0; column < 4; ++column)
            blockPrediction[(row << 2) + column] = pred[((by + row) << 3) + bx + column];
        H264Transform.DecodeBlock(
          this._chromaLevels.AsSpan((component * 4 + blkIdx) * 16, 16),
          qp, hasSeparateDc: true, dc[blkIdx], scaling, residual);
        _AddResidual(plane, picture.ChromaWidth, mbX * 8 + bx, mbY * 8 + by, 4, blockPrediction, residual);
      }
    }
  }

  private void _PredictChromaB(int mbAddr, int component, Span<byte> output) {
    var mbX = mbAddr % this._mbWidth;
    var mbY = mbAddr / this._mbWidth;
    Span<byte> p0 = stackalloc byte[4];
    Span<byte> p1 = stackalloc byte[4];
    Span<byte> combined = stackalloc byte[4];
    for (var by = 0; by < 4; ++by)
      for (var bx = 0; bx < 4; ++bx) {
        var at = (mbY * 4 + by) * this._blockWidth + mbX * 4 + bx;
        var ref0 = this._refIdx[at];
        var ref1 = this._refIdx1![at];
        if (ref0 < 0 && ref1 < 0)
          throw new InvalidDataException("An H.264 B block has no chroma reference in either list.");

        if (ref0 >= 0) {
          var reference = this._referenceList[ref0];
          H264MotionCompensation.PredictChroma(
            reference.Chroma(component), reference.ChromaWidth, reference.ChromaHeight,
            mbX * 8 + bx * 2, mbY * 8 + by * 2,
            this._mvX[at], this._mvY[at], 2, 2, p0);
        }
        if (ref1 >= 0) {
          var reference = this._referenceList1[ref1];
          H264MotionCompensation.PredictChroma(
            reference.Chroma(component), reference.ChromaWidth, reference.ChromaHeight,
            mbX * 8 + bx * 2, mbY * 8 + by * 2,
            this._mvX1![at], this._mvY1![at], 2, 2, p1);
        }

        if (ref0 >= 0 && ref1 >= 0) {
          if (this._header.Pps.WeightedBipredIdc == 1) {
            if (this._header.PredictionWeights == null)
              throw new InvalidDataException("An explicitly weighted H.264 B slice has no chroma pred_weight_table.");
            this._header.PredictionWeights.CombineChroma(ref0, ref1, component, p0, p1, combined);
          } else if (this._header.Pps.WeightedBipredIdc == 2) {
            var (w0, w1) = this._ImplicitWeights(ref0, ref1);
            _WeightedAverage(p0, p1, combined, w0, w1);
          } else {
            for (var i = 0; i < 4; ++i)
              combined[i] = (byte)((p0[i] + p1[i] + 1) >> 1);
          }
        } else if (ref0 >= 0) {
          p0.CopyTo(combined);
          if (this._header.Pps.WeightedBipredIdc == 1)
            this._header.PredictionWeights?.ApplyChroma(0, ref0, component, combined);
        } else {
          p1.CopyTo(combined);
          if (this._header.Pps.WeightedBipredIdc == 1)
            this._header.PredictionWeights?.ApplyChroma(1, ref1, component, combined);
        }

        for (var row = 0; row < 2; ++row)
          for (var column = 0; column < 2; ++column)
            output[((by * 2 + row) << 3) + bx * 2 + column] = combined[(row << 1) + column];
      }
  }
}
