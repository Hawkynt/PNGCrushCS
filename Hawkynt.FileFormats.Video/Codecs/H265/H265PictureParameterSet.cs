using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// A picture parameter set — ITU-T H.265, clauses 7.3.2.3 and 7.4.3.3.
/// </summary>
/// <remarks>
/// What may change from one picture to the next without a new sequence: the quantiser the slices
/// start from, whether the entropy coder is reinitialised across rows, how a picture is cut into
/// independently decodable pieces, and a dozen switches that turn individual coding tools on.
/// <para/>
/// Three of the switches change the residual coding rather than describing it, and they are the ones
/// worth naming here because a decoder that ignored any of them would decode most of a picture and
/// then be one bin out of step for the rest of the slice. <c>sign_data_hiding_enabled_flag</c> means
/// one coefficient's sign is not transmitted at all but inferred from the parity of the sub-block's
/// levels; <c>cu_qp_delta_enabled_flag</c> means a quantiser change may appear inside a transform
/// unit; <c>transform_skip_enabled_flag</c> means a small block may carry its residual with no
/// transform applied, and the flag saying so is read before the coefficients.
/// </remarks>
internal sealed class H265PictureParameterSet {

  private H265PictureParameterSet() { }

  internal int Id { get; private init; }

  internal int SequenceParameterSetId { get; private init; }

  /// <summary>Whether a slice segment may continue the previous one's header and entropy state.</summary>
  internal bool DependentSliceSegmentsEnabled { get; private init; }

  internal bool OutputFlagPresent { get; private init; }

  /// <summary>How many bits the slice header reserves before <c>slice_type</c> for later extensions.</summary>
  internal int ExtraSliceHeaderBits { get; private init; }

  /// <summary>Whether one coefficient sign per sub-block is carried in the parity of the levels.</summary>
  internal bool SignDataHidingEnabled { get; private init; }

  /// <summary>Whether a slice may choose which of the three context initialisation tables it uses.</summary>
  internal bool CabacInitPresent { get; private init; }

  internal int NumRefIdxL0DefaultActive { get; private init; }

  internal int NumRefIdxL1DefaultActive { get; private init; }

  /// <summary>The quantiser a slice's own delta is measured from.</summary>
  internal int InitQp { get; private init; }

  /// <summary>Whether an intra block may predict from a neighbour that was itself inter coded.</summary>
  internal bool ConstrainedIntraPred { get; private init; }

  internal bool TransformSkipEnabled { get; private init; }

  internal int Log2MaxTransformSkipBlockSize { get; private init; } = 2;

  internal bool CuQpDeltaEnabled { get; private init; }

  /// <summary>How far down the coding quadtree a quantiser group reaches.</summary>
  internal int DiffCuQpDeltaDepth { get; private init; }

  internal int CbQpOffset { get; private init; }

  internal int CrQpOffset { get; private init; }

  internal bool SliceChromaQpOffsetsPresent { get; private init; }

  internal bool WeightedPred { get; private init; }

  internal bool WeightedBipred { get; private init; }

  internal bool TransquantBypassEnabled { get; private init; }

  internal bool TilesEnabled { get; private init; }

  /// <summary>Whether each row of coding tree blocks is its own entropy-coded substream.</summary>
  internal bool EntropyCodingSyncEnabled { get; private init; }

  internal bool LoopFilterAcrossSlicesEnabled { get; private init; }

  internal bool DeblockingFilterOverrideEnabled { get; private init; }

  internal bool DeblockingFilterDisabled { get; private init; }

  internal int BetaOffsetDiv2 { get; private init; }

  internal int TcOffsetDiv2 { get; private init; }

  /// <summary>The matrices this picture uses, or <c>null</c> where the sequence parameter set's stand.</summary>
  internal H265ScalingList? ScalingList { get; private init; }

  internal bool ListsModificationPresent { get; private init; }

  internal int Log2ParallelMergeLevel { get; private init; }

  internal bool SliceSegmentHeaderExtensionPresent { get; private init; }

  internal static H265PictureParameterSet Parse(ReadOnlySpan<byte> payload) {
    var reader = new H265BitReader(payload);

    var id = reader.ReadUnsignedExpGolomb();
    var sequenceId = reader.ReadUnsignedExpGolomb();
    var dependentSliceSegments = reader.ReadFlag();
    var outputFlagPresent = reader.ReadFlag();
    var extraSliceHeaderBits = reader.ReadBits(3);
    var signDataHiding = reader.ReadFlag();
    var cabacInitPresent = reader.ReadFlag();
    var refIdxL0Default = reader.ReadUnsignedExpGolomb() + 1;
    var refIdxL1Default = reader.ReadUnsignedExpGolomb() + 1;
    var initQp = reader.ReadSignedExpGolomb() + 26;
    var constrainedIntraPred = reader.ReadFlag();
    var transformSkip = reader.ReadFlag();

    var cuQpDeltaEnabled = reader.ReadFlag();
    var diffCuQpDeltaDepth = cuQpDeltaEnabled ? reader.ReadUnsignedExpGolomb() : 0;

    var cbQpOffset = reader.ReadSignedExpGolomb();
    var crQpOffset = reader.ReadSignedExpGolomb();
    var sliceChromaQpOffsets = reader.ReadFlag();
    var weightedPred = reader.ReadFlag();
    var weightedBipred = reader.ReadFlag();
    var transquantBypass = reader.ReadFlag();
    var tiles = reader.ReadFlag();
    var entropyCodingSync = reader.ReadFlag();

    if (tiles) {
      var columns = reader.ReadUnsignedExpGolomb() + 1;
      var rows = reader.ReadUnsignedExpGolomb() + 1;
      if (!reader.ReadFlag())
        for (var i = 0; i < columns + rows - 2; ++i)
          reader.ReadUnsignedExpGolomb();

      reader.Skip(1); // loop_filter_across_tiles_enabled_flag

      throw new NotSupportedException(
        $"This H.265 stream divides each picture into {columns} by {rows} tiles (clause 7.3.2.3.1). A tiled picture "
        + "is coded as several independent rectangles with their own entropy coder state and their own prediction "
        + "boundaries; reading them is not implemented.");
    }

    var loopFilterAcrossSlices = reader.ReadFlag();

    var deblockingOverrideEnabled = false;
    var deblockingDisabled = false;
    var betaOffsetDiv2 = 0;
    var tcOffsetDiv2 = 0;
    if (reader.ReadFlag()) {
      deblockingOverrideEnabled = reader.ReadFlag();
      deblockingDisabled = reader.ReadFlag();
      if (!deblockingDisabled) {
        betaOffsetDiv2 = reader.ReadSignedExpGolomb();
        tcOffsetDiv2 = reader.ReadSignedExpGolomb();
      }
    }

    var scalingList = reader.ReadFlag() ? H265ScalingList.Parse(ref reader) : null;
    var listsModification = reader.ReadFlag();
    var log2ParallelMergeLevel = reader.ReadUnsignedExpGolomb() + 2;
    var sliceHeaderExtension = reader.ReadFlag();

    var log2MaxTransformSkipBlockSize = 2;
    if (reader.ReadFlag())
      log2MaxTransformSkipBlockSize = _RefuseExtensions(ref reader, transformSkip);

    return new() {
      Id = id,
      SequenceParameterSetId = sequenceId,
      DependentSliceSegmentsEnabled = dependentSliceSegments,
      OutputFlagPresent = outputFlagPresent,
      ExtraSliceHeaderBits = extraSliceHeaderBits,
      SignDataHidingEnabled = signDataHiding,
      CabacInitPresent = cabacInitPresent,
      NumRefIdxL0DefaultActive = refIdxL0Default,
      NumRefIdxL1DefaultActive = refIdxL1Default,
      InitQp = initQp,
      ConstrainedIntraPred = constrainedIntraPred,
      TransformSkipEnabled = transformSkip,
      Log2MaxTransformSkipBlockSize = log2MaxTransformSkipBlockSize,
      CuQpDeltaEnabled = cuQpDeltaEnabled,
      DiffCuQpDeltaDepth = diffCuQpDeltaDepth,
      CbQpOffset = cbQpOffset,
      CrQpOffset = crQpOffset,
      SliceChromaQpOffsetsPresent = sliceChromaQpOffsets,
      WeightedPred = weightedPred,
      WeightedBipred = weightedBipred,
      TransquantBypassEnabled = transquantBypass,
      TilesEnabled = false,
      EntropyCodingSyncEnabled = entropyCodingSync,
      LoopFilterAcrossSlicesEnabled = loopFilterAcrossSlices,
      DeblockingFilterOverrideEnabled = deblockingOverrideEnabled,
      DeblockingFilterDisabled = deblockingDisabled,
      BetaOffsetDiv2 = betaOffsetDiv2,
      TcOffsetDiv2 = tcOffsetDiv2,
      ScalingList = scalingList,
      ListsModificationPresent = listsModification,
      Log2ParallelMergeLevel = log2ParallelMergeLevel,
      SliceSegmentHeaderExtensionPresent = sliceHeaderExtension,
    };
  }

  /// <summary>Reads the extension flags and refuses the ones that change how a block is reconstructed.</summary>
  private static int _RefuseExtensions(ref H265BitReader reader, bool transformSkipEnabled) {
    var rangeExtension = reader.ReadFlag();
    var multilayerExtension = reader.ReadFlag();
    var threeDimensionalExtension = reader.ReadFlag();
    var screenContentExtension = reader.ReadFlag();
    reader.Skip(4); // pps_extension_4bits

    if (multilayerExtension || threeDimensionalExtension)
      throw new NotSupportedException(
        "This H.265 picture parameter set carries a multilayer or 3D extension (clause 7.3.2.3.1, Annexes F, G and "
        + "I). Only the base layer syntax is implemented.");

    if (screenContentExtension)
      throw new NotSupportedException(
        "This H.265 picture parameter set carries a screen content coding extension (clause 7.3.2.3.3). Palette "
        + "mode and the adaptive colour transform are not implemented.");

    if (!rangeExtension)
      return 2;

    var log2MaxTransformSkipBlockSize = transformSkipEnabled ? reader.ReadUnsignedExpGolomb() + 2 : 2;

    if (reader.ReadFlag())
      throw new NotSupportedException(
        "This H.265 picture parameter set sets cross_component_prediction_enabled_flag (clause 7.3.2.3.2). The "
        + "chroma residual is then predicted from the luma residual, which is a format range extension tool and is "
        + "not implemented.");

    if (reader.ReadFlag())
      throw new NotSupportedException(
        "This H.265 picture parameter set sets chroma_qp_offset_list_enabled_flag (clause 7.3.2.3.2). A coding unit "
        + "may then choose its chroma quantiser offset from a list, which is a format range extension tool and is "
        + "not implemented.");

    var lumaSaoScale = reader.ReadUnsignedExpGolomb();
    var chromaSaoScale = reader.ReadUnsignedExpGolomb();
    if (lumaSaoScale != 0 || chromaSaoScale != 0)
      throw new NotSupportedException(
        $"This H.265 picture parameter set scales the sample adaptive offset by 2^{lumaSaoScale} for luma and "
        + $"2^{chromaSaoScale} for chroma (log2_sao_offset_scale, clause 7.3.2.3.2). That is a format range "
        + "extension tool for sample depths above ten and is not implemented.");

    return log2MaxTransformSkipBlockSize;
  }
}
