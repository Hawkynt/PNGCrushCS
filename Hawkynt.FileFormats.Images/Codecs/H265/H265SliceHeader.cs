using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>The three kinds of slice — ITU-T H.265, Table 7-7.</summary>
internal enum H265SliceType {

  /// <summary>Bidirectionally predicted: two reference lists, and a block may average across both.</summary>
  B = 0,

  /// <summary>Predicted from one list of pictures already decoded.</summary>
  P = 1,

  /// <summary>Intra: every block predicted from its own picture's neighbours.</summary>
  I = 2,
}

/// <summary>
/// One slice segment header — ITU-T H.265, clauses 7.3.6.1 and 7.4.7.1.
/// </summary>
/// <remarks>
/// The header is parsed all the way to its byte alignment because that boundary is the CABAC entry
/// point. A dependent segment carries only the fields before <c>dependent_slice_segment_flag</c>, its
/// address, entry-point offsets and optional extension; every slice-level value in between is
/// inherited from the nearest preceding independent segment of the same slice.
/// </remarks>
internal sealed class H265SliceHeader {

  private H265SliceHeader() { }

  internal H265SequenceParameterSet Sps { get; private init; } = null!;
  internal H265PictureParameterSet Pps { get; private init; } = null!;
  internal H265NalUnit Nal { get; private init; } = null!;

  /// <summary>Whether this segment opens a picture.</summary>
  internal bool FirstSliceSegmentInPicture { get; private init; }

  /// <summary>Whether this segment inherits its slice-level header from the preceding independent segment.</summary>
  internal bool DependentSliceSegment { get; private init; }

  /// <summary>The coding tree block this segment starts at, in the picture's raster scan.</summary>
  internal int SegmentAddress { get; private init; }

  internal H265SliceType SliceType { get; private init; }
  internal bool PicOutputFlag { get; private init; } = true;
  internal int PicOrderCntLsb { get; private init; }
  internal H265ShortTermReferencePictureSet ShortTermReferencePictureSet { get; private init; } =
    H265ShortTermReferencePictureSet.Empty;
  internal int[] LongTermPocLsb { get; private init; } = [];
  internal bool[] LongTermUsedByCurrentPicture { get; private init; } = [];
  internal bool[] LongTermMsbPresent { get; private init; } = [];
  internal int[] LongTermMsbCycle { get; private init; } = [];
  internal bool TemporalMvpEnabled { get; private init; }
  internal bool SaoLuma { get; private init; }
  internal bool SaoChroma { get; private init; }
  internal int NumRefIdxL0Active { get; private init; }
  internal int NumRefIdxL1Active { get; private init; }
  internal int[]? ListEntryL0 { get; private init; }
  internal int[]? ListEntryL1 { get; private init; }
  internal bool MvdL1Zero { get; private init; }
  internal bool CabacInitFlag { get; private init; }
  internal bool CollocatedFromL0 { get; private init; } = true;
  internal int CollocatedRefIdx { get; private init; }
  internal H265PredictionWeights? PredictionWeights { get; private init; }
  internal int MaxNumMergeCand { get; private init; } = 5;
  internal int SliceQpY { get; private init; }
  internal int SliceCbQpOffset { get; private init; }
  internal int SliceCrQpOffset { get; private init; }
  internal bool DeblockingFilterDisabled { get; private init; }
  internal int BetaOffsetDiv2 { get; private init; }
  internal int TcOffsetDiv2 { get; private init; }
  internal bool LoopFilterAcrossSlicesEnabled { get; private init; }
  internal int[] SubstreamOffsets { get; private init; } = [];
  internal int DataOffset { get; private init; }
  internal int NumPicTotalCurr { get; private init; }

  internal bool IsIntra => this.SliceType == H265SliceType.I;

  /// <summary>Reads one header, leaving the reader at the byte the slice segment data starts on.</summary>
  internal static H265SliceHeader Parse(
    H265NalUnit nal,
    IReadOnlyDictionary<int, H265SequenceParameterSet> sequenceSets,
    IReadOnlyDictionary<int, H265PictureParameterSet> pictureSets,
    H265SliceHeader? precedingIndependent = null) {
    var reader = new H265BitReader(nal.Payload);

    var firstSegment = reader.ReadFlag();
    if (nal.IsRandomAccessPoint)
      reader.Skip(1); // no_output_of_prior_pics_flag

    var ppsId = reader.ReadUnsignedExpGolomb();
    if (!pictureSets.TryGetValue(ppsId, out var pps))
      throw new InvalidDataException(
        $"An H.265 slice segment names picture parameter set {ppsId}, which this stream has not carried. Either the "
        + "stream was entered somewhere other than a random access point, or its parameter sets are out of band and "
        + "were not handed over.");

    if (!sequenceSets.TryGetValue(pps.SequenceParameterSetId, out var sps))
      throw new InvalidDataException(
        $"An H.265 picture parameter set {ppsId} names sequence parameter set {pps.SequenceParameterSetId}, which "
        + "this stream has not carried.");

    var dependent = false;
    var segmentAddress = 0;
    if (!firstSegment) {
      if (pps.DependentSliceSegmentsEnabled)
        dependent = reader.ReadFlag();

      segmentAddress = reader.ReadBits(_CeilLog2(sps.PicSizeInCtbsY));
      if (segmentAddress >= sps.PicSizeInCtbsY)
        throw new InvalidDataException(
          $"An H.265 slice segment states it begins at coding tree block {segmentAddress}, but the picture has only "
          + $"{sps.PicSizeInCtbsY}.");
    }

    if (dependent)
      return _ParseDependent(
        nal, sps, pps, segmentAddress, precedingIndependent, ref reader);

    reader.Skip(pps.ExtraSliceHeaderBits);

    var sliceTypeValue = reader.ReadUnsignedExpGolomb();
    if (sliceTypeValue > 2)
      throw new InvalidDataException(
        $"An H.265 slice states slice_type {sliceTypeValue}. Table 7-7 defines only 0 (B), 1 (P) and 2 (I).");

    var sliceType = (H265SliceType)sliceTypeValue;
    if (nal.IsRandomAccessPoint && sliceType != H265SliceType.I)
      throw new InvalidDataException(
        $"An H.265 intra random access point picture (NAL unit type {(int)nal.Type}) carries a "
        + $"{sliceType} slice. Clause 7.4.7.1 requires every slice of such a picture to be intra.");

    var picOutputFlag = !pps.OutputFlagPresent || reader.ReadFlag();

    var picOrderCntLsb = 0;
    var shortTermSet = H265ShortTermReferencePictureSet.Empty;
    var longTermPocLsb = Array.Empty<int>();
    var longTermUsed = Array.Empty<bool>();
    var longTermMsbPresent = Array.Empty<bool>();
    var longTermMsbCycle = Array.Empty<int>();
    var temporalMvp = false;

    if (!nal.IsInstantaneousRefresh) {
      picOrderCntLsb = reader.ReadBits(sps.Log2MaxPicOrderCntLsb);

      if (reader.ReadFlag()) {
        var count = sps.ShortTermReferencePictureSets.Length - 1;
        var index = count > 1 ? reader.ReadBits(_CeilLog2(count)) : 0;
        if (index >= count)
          throw new InvalidDataException(
            $"An H.265 slice names short-term reference picture set {index}, but the sequence declares only {count}.");
        shortTermSet = sps.ShortTermReferencePictureSets[index];
      } else {
        var count = sps.ShortTermReferencePictureSets.Length - 1;
        shortTermSet = H265ShortTermReferencePictureSet.Parse(
          ref reader, count, count, sps.ShortTermReferencePictureSets);
      }

      if (sps.LongTermReferencePicturesPresent) {
        var fromSequence = sps.LongTermReferencePicturePocLsb.Length > 0 ? reader.ReadUnsignedExpGolomb() : 0;
        var fromSlice = reader.ReadUnsignedExpGolomb();
        var total = fromSequence + fromSlice;
        if (total > 32)
          throw new InvalidDataException(
            $"An H.265 slice names {total} long-term reference pictures, which exceeds every decoded picture buffer "
            + "the standard defines.");

        longTermPocLsb = new int[total];
        longTermUsed = new bool[total];
        longTermMsbPresent = new bool[total];
        longTermMsbCycle = new int[total];

        for (var i = 0; i < total; ++i) {
          if (i < fromSequence) {
            var index = sps.LongTermReferencePicturePocLsb.Length > 1
              ? reader.ReadBits(_CeilLog2(sps.LongTermReferencePicturePocLsb.Length))
              : 0;
            longTermPocLsb[i] = sps.LongTermReferencePicturePocLsb[index];
            longTermUsed[i] = sps.LongTermReferencePictureUsed[index];
          } else {
            longTermPocLsb[i] = reader.ReadBits(sps.Log2MaxPicOrderCntLsb);
            longTermUsed[i] = reader.ReadFlag();
          }

          longTermMsbPresent[i] = reader.ReadFlag();
          if (longTermMsbPresent[i])
            longTermMsbCycle[i] = reader.ReadUnsignedExpGolomb();
        }
      }

      if (sps.TemporalMvpEnabled)
        temporalMvp = reader.ReadFlag();
    }

    var saoLuma = false;
    var saoChroma = false;
    if (sps.SampleAdaptiveOffsetEnabled) {
      saoLuma = reader.ReadFlag();
      if (sps.ChromaArrayType != 0)
        saoChroma = reader.ReadFlag();
    }

    var numRefIdxL0 = 0;
    var numRefIdxL1 = 0;
    int[]? listEntryL0 = null;
    int[]? listEntryL1 = null;
    var mvdL1Zero = false;
    var cabacInit = false;
    var collocatedFromL0 = true;
    var collocatedRefIdx = 0;
    H265PredictionWeights? weights = null;
    var maxNumMergeCand = 5;

    var picTotalCurr = _CountPicturesUsedByCurrentPicture(shortTermSet, longTermUsed);

    if (sliceType != H265SliceType.I) {
      numRefIdxL0 = pps.NumRefIdxL0DefaultActive;
      numRefIdxL1 = sliceType == H265SliceType.B ? pps.NumRefIdxL1DefaultActive : 0;

      if (reader.ReadFlag()) {
        numRefIdxL0 = reader.ReadUnsignedExpGolomb() + 1;
        if (sliceType == H265SliceType.B)
          numRefIdxL1 = reader.ReadUnsignedExpGolomb() + 1;
      }

      if (numRefIdxL0 > 15 || numRefIdxL1 > 15)
        throw new InvalidDataException(
          $"An H.265 slice activates {numRefIdxL0} and {numRefIdxL1} reference indices. Clause 7.4.7.1 bounds each "
          + "at sixteen.");

      if (pps.ListsModificationPresent && picTotalCurr > 1) {
        var bits = _CeilLog2(picTotalCurr);
        if (reader.ReadFlag()) {
          listEntryL0 = new int[numRefIdxL0];
          for (var i = 0; i < numRefIdxL0; ++i)
            listEntryL0[i] = reader.ReadBits(bits);
        }

        if (sliceType == H265SliceType.B && reader.ReadFlag()) {
          listEntryL1 = new int[numRefIdxL1];
          for (var i = 0; i < numRefIdxL1; ++i)
            listEntryL1[i] = reader.ReadBits(bits);
        }
      }

      if (sliceType == H265SliceType.B)
        mvdL1Zero = reader.ReadFlag();
      if (pps.CabacInitPresent)
        cabacInit = reader.ReadFlag();

      if (temporalMvp) {
        if (sliceType == H265SliceType.B)
          collocatedFromL0 = reader.ReadFlag();
        if (collocatedFromL0 ? numRefIdxL0 > 1 : numRefIdxL1 > 1)
          collocatedRefIdx = reader.ReadUnsignedExpGolomb();
      }

      if ((pps.WeightedPred && sliceType == H265SliceType.P)
          || (pps.WeightedBipred && sliceType == H265SliceType.B))
        weights = H265PredictionWeights.Parse(ref reader, sps, sliceType, numRefIdxL0, numRefIdxL1);

      maxNumMergeCand = 5 - reader.ReadUnsignedExpGolomb();
      if (maxNumMergeCand is < 1 or > 5)
        throw new InvalidDataException(
          $"An H.265 slice states MaxNumMergeCand of {maxNumMergeCand}, which clause 7.4.7.1 bounds to 1 through 5.");
    }

    var sliceQpY = pps.InitQp + reader.ReadSignedExpGolomb();
    if (sliceQpY < -sps.QpBdOffsetLuma || sliceQpY > 51)
      throw new InvalidDataException(
        $"An H.265 slice states SliceQpY of {sliceQpY}, outside the range clause 7.4.7.1 permits "
        + $"({-sps.QpBdOffsetLuma} to 51).");

    var sliceCbQpOffset = 0;
    var sliceCrQpOffset = 0;
    if (pps.SliceChromaQpOffsetsPresent) {
      sliceCbQpOffset = reader.ReadSignedExpGolomb();
      sliceCrQpOffset = reader.ReadSignedExpGolomb();
    }

    var deblockingDisabled = pps.DeblockingFilterDisabled;
    var betaOffsetDiv2 = pps.BetaOffsetDiv2;
    var tcOffsetDiv2 = pps.TcOffsetDiv2;
    if (pps.DeblockingFilterOverrideEnabled && reader.ReadFlag()) {
      deblockingDisabled = reader.ReadFlag();
      if (!deblockingDisabled) {
        betaOffsetDiv2 = reader.ReadSignedExpGolomb();
        tcOffsetDiv2 = reader.ReadSignedExpGolomb();
      }
    }

    var loopFilterAcrossSlices = pps.LoopFilterAcrossSlicesEnabled;
    if (pps.LoopFilterAcrossSlicesEnabled && (saoLuma || saoChroma || !deblockingDisabled))
      loopFilterAcrossSlices = reader.ReadFlag();

    _FinishHeader(ref reader, nal, pps, out var substreams, out var dataOffset);

    return new() {
      Sps = sps,
      Pps = pps,
      Nal = nal,
      FirstSliceSegmentInPicture = firstSegment,
      DependentSliceSegment = false,
      SegmentAddress = segmentAddress,
      SliceType = sliceType,
      PicOutputFlag = picOutputFlag,
      PicOrderCntLsb = picOrderCntLsb,
      ShortTermReferencePictureSet = shortTermSet,
      LongTermPocLsb = longTermPocLsb,
      LongTermUsedByCurrentPicture = longTermUsed,
      LongTermMsbPresent = longTermMsbPresent,
      LongTermMsbCycle = longTermMsbCycle,
      TemporalMvpEnabled = temporalMvp,
      SaoLuma = saoLuma,
      SaoChroma = saoChroma,
      NumRefIdxL0Active = numRefIdxL0,
      NumRefIdxL1Active = numRefIdxL1,
      ListEntryL0 = listEntryL0,
      ListEntryL1 = listEntryL1,
      MvdL1Zero = mvdL1Zero,
      CabacInitFlag = cabacInit,
      CollocatedFromL0 = collocatedFromL0,
      CollocatedRefIdx = collocatedRefIdx,
      PredictionWeights = weights,
      MaxNumMergeCand = maxNumMergeCand,
      SliceQpY = sliceQpY,
      SliceCbQpOffset = sliceCbQpOffset,
      SliceCrQpOffset = sliceCrQpOffset,
      DeblockingFilterDisabled = deblockingDisabled,
      BetaOffsetDiv2 = betaOffsetDiv2,
      TcOffsetDiv2 = tcOffsetDiv2,
      LoopFilterAcrossSlicesEnabled = loopFilterAcrossSlices,
      SubstreamOffsets = substreams,
      DataOffset = dataOffset,
      NumPicTotalCurr = picTotalCurr,
    };
  }

  private static H265SliceHeader _ParseDependent(
    H265NalUnit nal,
    H265SequenceParameterSet sps,
    H265PictureParameterSet pps,
    int segmentAddress,
    H265SliceHeader? inherited,
    ref H265BitReader reader) {
    if (inherited == null)
      throw new InvalidDataException(
        "An H.265 dependent slice segment has no preceding independent slice segment to inherit. The stream was "
        + "entered part way through a slice or the preceding segment is missing.");

    if (inherited.Pps.Id != pps.Id || inherited.Sps.Id != sps.Id)
      throw new InvalidDataException(
        $"An H.265 dependent slice segment names PPS {pps.Id}, but the independent segment it follows used PPS "
        + $"{inherited.Pps.Id}. A dependent segment cannot change the syntax it inherits.");

    _FinishHeader(ref reader, nal, pps, out var substreams, out var dataOffset);

    return new() {
      Sps = sps,
      Pps = pps,
      Nal = nal,
      FirstSliceSegmentInPicture = false,
      DependentSliceSegment = true,
      SegmentAddress = segmentAddress,
      SliceType = inherited.SliceType,
      PicOutputFlag = inherited.PicOutputFlag,
      PicOrderCntLsb = inherited.PicOrderCntLsb,
      ShortTermReferencePictureSet = inherited.ShortTermReferencePictureSet,
      LongTermPocLsb = inherited.LongTermPocLsb,
      LongTermUsedByCurrentPicture = inherited.LongTermUsedByCurrentPicture,
      LongTermMsbPresent = inherited.LongTermMsbPresent,
      LongTermMsbCycle = inherited.LongTermMsbCycle,
      TemporalMvpEnabled = inherited.TemporalMvpEnabled,
      SaoLuma = inherited.SaoLuma,
      SaoChroma = inherited.SaoChroma,
      NumRefIdxL0Active = inherited.NumRefIdxL0Active,
      NumRefIdxL1Active = inherited.NumRefIdxL1Active,
      ListEntryL0 = inherited.ListEntryL0,
      ListEntryL1 = inherited.ListEntryL1,
      MvdL1Zero = inherited.MvdL1Zero,
      CabacInitFlag = inherited.CabacInitFlag,
      CollocatedFromL0 = inherited.CollocatedFromL0,
      CollocatedRefIdx = inherited.CollocatedRefIdx,
      PredictionWeights = inherited.PredictionWeights,
      MaxNumMergeCand = inherited.MaxNumMergeCand,
      SliceQpY = inherited.SliceQpY,
      SliceCbQpOffset = inherited.SliceCbQpOffset,
      SliceCrQpOffset = inherited.SliceCrQpOffset,
      DeblockingFilterDisabled = inherited.DeblockingFilterDisabled,
      BetaOffsetDiv2 = inherited.BetaOffsetDiv2,
      TcOffsetDiv2 = inherited.TcOffsetDiv2,
      LoopFilterAcrossSlicesEnabled = inherited.LoopFilterAcrossSlicesEnabled,
      SubstreamOffsets = substreams,
      DataOffset = dataOffset,
      NumPicTotalCurr = inherited.NumPicTotalCurr,
    };
  }

  private static void _FinishHeader(
    ref H265BitReader reader,
    H265NalUnit nal,
    H265PictureParameterSet pps,
    out int[] substreams,
    out int dataOffset) {
    substreams = Array.Empty<int>();
    if (pps.TilesEnabled || pps.EntropyCodingSyncEnabled)
      substreams = _ReadEntryPoints(ref reader, nal);

    if (pps.SliceSegmentHeaderExtensionPresent)
      reader.Skip(reader.ReadUnsignedExpGolomb() << 3);

    // byte_alignment(): alignment_bit_equal_to_one followed by zero bits.
    reader.Skip(1);
    reader.AlignToByte();
    dataOffset = reader.BytePosition;

    // entry_point_offset_minus1 is counted in the escaped byte sequence. The decoder consumes the
    // unescaped RBSP, so translate every cumulative offset once, after the data origin is known.
    for (var i = 0; i < substreams.Length; ++i)
      substreams[i] = nal.UnescapedOffsetOf(nal.EscapedOffsetOf(dataOffset) + substreams[i]);
  }

  private static int[] _ReadEntryPoints(ref H265BitReader reader, H265NalUnit nal) {
    var count = reader.ReadUnsignedExpGolomb();
    if (count == 0)
      return [];

    if (count > nal.Payload.Length)
      throw new InvalidDataException(
        $"An H.265 slice segment header states {count} entry point offsets, more than the {nal.Payload.Length} "
        + "bytes the whole NAL unit holds.");

    var length = reader.ReadUnsignedExpGolomb() + 1;
    if (length > 32)
      throw new InvalidDataException(
        $"An H.265 slice segment header states entry point offsets of {length} bits, which clause 7.4.7.1 bounds "
        + "at 32.");

    var offsets = new int[count];
    var running = 0;
    for (var i = 0; i < count; ++i) {
      running += (int)(uint)reader.ReadBitsLong(length) + 1;
      offsets[i] = running;
    }

    return offsets;
  }

  private static int _CountPicturesUsedByCurrentPicture(
    H265ShortTermReferencePictureSet shortTerm, bool[] longTermUsed) {
    var total = 0;
    foreach (var used in shortTerm.UsedByCurrPicS0)
      if (used)
        ++total;
    foreach (var used in shortTerm.UsedByCurrPicS1)
      if (used)
        ++total;
    foreach (var used in longTermUsed)
      if (used)
        ++total;
    return total;
  }

  private static int _CeilLog2(int count) {
    var bits = 0;
    while (1 << bits < count)
      ++bits;
    return bits;
  }
}

/// <summary>
/// The explicit weights a predicted slice applies to each reference — clause 7.3.6.3.
/// </summary>
internal sealed class H265PredictionWeights {

  private H265PredictionWeights(
    int lumaLog2Denom, int chromaLog2Denom,
    int[] lumaWeightL0, int[] lumaOffsetL0, int[,] chromaWeightL0, int[,] chromaOffsetL0,
    int[] lumaWeightL1, int[] lumaOffsetL1, int[,] chromaWeightL1, int[,] chromaOffsetL1) {
    this.LumaLog2WeightDenom = lumaLog2Denom;
    this.ChromaLog2WeightDenom = chromaLog2Denom;
    this.LumaWeightL0 = lumaWeightL0;
    this.LumaOffsetL0 = lumaOffsetL0;
    this.ChromaWeightL0 = chromaWeightL0;
    this.ChromaOffsetL0 = chromaOffsetL0;
    this.LumaWeightL1 = lumaWeightL1;
    this.LumaOffsetL1 = lumaOffsetL1;
    this.ChromaWeightL1 = chromaWeightL1;
    this.ChromaOffsetL1 = chromaOffsetL1;
  }

  internal int LumaLog2WeightDenom { get; }
  internal int ChromaLog2WeightDenom { get; }
  internal int[] LumaWeightL0 { get; }
  internal int[] LumaOffsetL0 { get; }
  internal int[,] ChromaWeightL0 { get; }
  internal int[,] ChromaOffsetL0 { get; }
  internal int[] LumaWeightL1 { get; }
  internal int[] LumaOffsetL1 { get; }
  internal int[,] ChromaWeightL1 { get; }
  internal int[,] ChromaOffsetL1 { get; }

  internal int LumaWeight(int list, int index) => list == 0 ? this.LumaWeightL0[index] : this.LumaWeightL1[index];
  internal int LumaOffset(int list, int index) => list == 0 ? this.LumaOffsetL0[index] : this.LumaOffsetL1[index];
  internal int ChromaWeight(int list, int index, int component)
    => list == 0 ? this.ChromaWeightL0[index, component] : this.ChromaWeightL1[index, component];
  internal int ChromaOffset(int list, int index, int component)
    => list == 0 ? this.ChromaOffsetL0[index, component] : this.ChromaOffsetL1[index, component];

  internal static H265PredictionWeights Parse(
    ref H265BitReader reader, H265SequenceParameterSet sps, H265SliceType sliceType, int countL0, int countL1) {
    var lumaLog2Denom = reader.ReadUnsignedExpGolomb();
    if (lumaLog2Denom > 7)
      throw new InvalidDataException(
        $"An H.265 slice states luma_log2_weight_denom of {lumaLog2Denom}, which clause 7.4.7.3 bounds at 7.");

    var chromaLog2Denom = lumaLog2Denom;
    if (sps.ChromaArrayType != 0) {
      chromaLog2Denom += reader.ReadSignedExpGolomb();
      if (chromaLog2Denom is < 0 or > 7)
        throw new InvalidDataException(
          $"An H.265 slice states ChromaLog2WeightDenom of {chromaLog2Denom}, outside the range 0 to 7 that clause "
          + "7.4.7.3 permits.");
    }

    _ParseList(ref reader, sps, countL0, lumaLog2Denom, chromaLog2Denom,
      out var lumaWeightL0, out var lumaOffsetL0, out var chromaWeightL0, out var chromaOffsetL0);

    var lumaWeightL1 = new int[Math.Max(countL1, 1)];
    var lumaOffsetL1 = new int[Math.Max(countL1, 1)];
    var chromaWeightL1 = new int[Math.Max(countL1, 1), 2];
    var chromaOffsetL1 = new int[Math.Max(countL1, 1), 2];

    if (sliceType == H265SliceType.B)
      _ParseList(ref reader, sps, countL1, lumaLog2Denom, chromaLog2Denom,
        out lumaWeightL1, out lumaOffsetL1, out chromaWeightL1, out chromaOffsetL1);

    return new(lumaLog2Denom, chromaLog2Denom,
      lumaWeightL0, lumaOffsetL0, chromaWeightL0, chromaOffsetL0,
      lumaWeightL1, lumaOffsetL1, chromaWeightL1, chromaOffsetL1);
  }

  private static void _ParseList(
    ref H265BitReader reader, H265SequenceParameterSet sps, int count, int lumaLog2Denom, int chromaLog2Denom,
    out int[] lumaWeight, out int[] lumaOffset, out int[,] chromaWeight, out int[,] chromaOffset) {
    lumaWeight = new int[Math.Max(count, 1)];
    lumaOffset = new int[Math.Max(count, 1)];
    chromaWeight = new int[Math.Max(count, 1), 2];
    chromaOffset = new int[Math.Max(count, 1), 2];

    for (var i = 0; i < count; ++i) {
      lumaWeight[i] = 1 << lumaLog2Denom;
      chromaWeight[i, 0] = 1 << chromaLog2Denom;
      chromaWeight[i, 1] = 1 << chromaLog2Denom;
    }

    var lumaPresent = new bool[Math.Max(count, 1)];
    for (var i = 0; i < count; ++i)
      lumaPresent[i] = reader.ReadFlag();

    var chromaPresent = new bool[Math.Max(count, 1)];
    if (sps.ChromaArrayType != 0)
      for (var i = 0; i < count; ++i)
        chromaPresent[i] = reader.ReadFlag();

    for (var i = 0; i < count; ++i) {
      if (lumaPresent[i]) {
        lumaWeight[i] = (1 << lumaLog2Denom) + reader.ReadSignedExpGolomb();
        lumaOffset[i] = reader.ReadSignedExpGolomb();
      }

      if (!chromaPresent[i])
        continue;

      for (var component = 0; component < 2; ++component) {
        var weight = (1 << chromaLog2Denom) + reader.ReadSignedExpGolomb();
        var delta = reader.ReadSignedExpGolomb();
        chromaWeight[i, component] = weight;
        chromaOffset[i, component] =
          Math.Clamp(128 + delta - ((128 * weight) >> chromaLog2Denom), -128, 127);
      }
    }
  }
}
