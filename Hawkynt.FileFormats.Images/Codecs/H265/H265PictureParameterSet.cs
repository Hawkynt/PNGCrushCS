using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// A picture parameter set — ITU-T H.265, clauses 7.3.2.3 and 7.4.3.3.
/// </summary>
internal sealed class H265PictureParameterSet {

  private H265PictureParameterSet() { }

  internal int Id { get; private init; }
  internal int SequenceParameterSetId { get; private init; }
  internal bool DependentSliceSegmentsEnabled { get; private init; }
  internal bool OutputFlagPresent { get; private init; }
  internal int ExtraSliceHeaderBits { get; private init; }
  internal bool SignDataHidingEnabled { get; private init; }
  internal bool CabacInitPresent { get; private init; }
  internal int NumRefIdxL0DefaultActive { get; private init; }
  internal int NumRefIdxL1DefaultActive { get; private init; }
  internal int InitQp { get; private init; }
  internal bool ConstrainedIntraPred { get; private init; }
  internal bool TransformSkipEnabled { get; private init; }
  internal int Log2MaxTransformSkipBlockSize { get; private init; } = 2;
  internal bool CuQpDeltaEnabled { get; private init; }
  internal int DiffCuQpDeltaDepth { get; private init; }
  internal int CbQpOffset { get; private init; }
  internal int CrQpOffset { get; private init; }
  internal bool SliceChromaQpOffsetsPresent { get; private init; }
  internal bool WeightedPred { get; private init; }
  internal bool WeightedBipred { get; private init; }
  internal bool TransquantBypassEnabled { get; private init; }

  internal bool TilesEnabled { get; private init; }
  internal int NumTileColumns { get; private init; } = 1;
  internal int NumTileRows { get; private init; } = 1;
  internal bool UniformTileSpacing { get; private init; } = true;

  /// <summary>Explicit widths for every tile column except the last, as syntax value plus one CTB.</summary>
  internal int[] TileColumnWidths { get; private init; } = [];

  /// <summary>Explicit heights for every tile row except the last, as syntax value plus one CTB.</summary>
  internal int[] TileRowHeights { get; private init; } = [];

  internal bool LoopFilterAcrossTilesEnabled { get; private init; } = true;

  internal bool EntropyCodingSyncEnabled { get; private init; }
  internal bool LoopFilterAcrossSlicesEnabled { get; private init; }
  internal bool DeblockingFilterOverrideEnabled { get; private init; }
  internal bool DeblockingFilterDisabled { get; private init; }
  internal int BetaOffsetDiv2 { get; private init; }
  internal int TcOffsetDiv2 { get; private init; }
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

    var tileColumns = 1;
    var tileRows = 1;
    var uniformTileSpacing = true;
    var tileColumnWidths = Array.Empty<int>();
    var tileRowHeights = Array.Empty<int>();
    var loopFilterAcrossTiles = true;

    if (tiles) {
      tileColumns = reader.ReadUnsignedExpGolomb() + 1;
      tileRows = reader.ReadUnsignedExpGolomb() + 1;
      uniformTileSpacing = reader.ReadFlag();

      if (!uniformTileSpacing) {
        tileColumnWidths = new int[tileColumns - 1];
        for (var i = 0; i < tileColumnWidths.Length; ++i)
          tileColumnWidths[i] = reader.ReadUnsignedExpGolomb() + 1;

        tileRowHeights = new int[tileRows - 1];
        for (var i = 0; i < tileRowHeights.Length; ++i)
          tileRowHeights[i] = reader.ReadUnsignedExpGolomb() + 1;
      }

      loopFilterAcrossTiles = reader.ReadFlag();
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
      TilesEnabled = tiles,
      NumTileColumns = tileColumns,
      NumTileRows = tileRows,
      UniformTileSpacing = uniformTileSpacing,
      TileColumnWidths = tileColumnWidths,
      TileRowHeights = tileRowHeights,
      LoopFilterAcrossTilesEnabled = loopFilterAcrossTiles,
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
