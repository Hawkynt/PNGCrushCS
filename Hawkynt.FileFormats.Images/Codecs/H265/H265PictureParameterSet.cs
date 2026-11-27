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

  /// <summary>
  /// <c>cross_component_prediction_enabled_flag</c>: a 4:4:4 transform unit may predict each
  /// chrominance residual from the luminance one, scaled by a power of two it states.
  /// </summary>
  internal bool CrossComponentPredictionEnabled { get; private init; }

  /// <summary>
  /// <c>chroma_qp_offset_list_enabled_flag</c>: a coding unit may pick its chrominance quantiser
  /// offsets from <see cref="CbQpOffsetList"/> and <see cref="CrQpOffsetList"/>.
  /// </summary>
  internal bool ChromaQpOffsetListEnabled { get; private init; }

  /// <summary><c>diff_cu_chroma_qp_offset_depth</c>: how far below the coding tree block one choice holds.</summary>
  internal int DiffCuChromaQpOffsetDepth { get; private init; }

  /// <summary>The Cb offsets a coding unit may choose from, <c>cb_qp_offset_list</c>.</summary>
  internal int[] CbQpOffsetList { get; private init; } = [];

  /// <summary>The Cr offsets a coding unit may choose from, <c>cr_qp_offset_list</c>.</summary>
  internal int[] CrQpOffsetList { get; private init; } = [];

  /// <summary><c>log2_sao_offset_scale_luma</c>: how far left every luma sample adaptive offset is shifted.</summary>
  internal int Log2SaoOffsetScaleLuma { get; private init; }

  /// <summary><c>log2_sao_offset_scale_chroma</c>.</summary>
  internal int Log2SaoOffsetScaleChroma { get; private init; }

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

    var range = RangeExtensionValues.None;
    if (reader.ReadFlag())
      range = _ReadExtensions(ref reader, transformSkip);

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
      Log2MaxTransformSkipBlockSize = range.Log2MaxTransformSkipBlockSize,
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
      CrossComponentPredictionEnabled = range.CrossComponentPrediction,
      ChromaQpOffsetListEnabled = range.ChromaQpOffsetList,
      DiffCuChromaQpOffsetDepth = range.DiffCuChromaQpOffsetDepth,
      CbQpOffsetList = range.CbQpOffsets,
      CrQpOffsetList = range.CrQpOffsets,
      Log2SaoOffsetScaleLuma = range.Log2SaoOffsetScaleLuma,
      Log2SaoOffsetScaleChroma = range.Log2SaoOffsetScaleChroma,
    };
  }

  /// <summary>What <c>pps_range_extension()</c> states, with the defaults of a set that sends none.</summary>
  private readonly record struct RangeExtensionValues(
    int Log2MaxTransformSkipBlockSize,
    bool CrossComponentPrediction,
    bool ChromaQpOffsetList,
    int DiffCuChromaQpOffsetDepth,
    int[] CbQpOffsets,
    int[] CrQpOffsets,
    int Log2SaoOffsetScaleLuma,
    int Log2SaoOffsetScaleChroma) {

    internal static RangeExtensionValues None => new(2, false, false, 0, [], [], 0, 0);
  }

  /// <summary>
  /// Reads the extension flags, keeping the format range extension and refusing the extensions
  /// whose syntax this decoder does not have.
  /// </summary>
  private static RangeExtensionValues _ReadExtensions(ref H265BitReader reader, bool transformSkipEnabled) {
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
      return RangeExtensionValues.None;

    // pps_range_extension() — clause 7.3.2.3.2.
    var log2MaxTransformSkipBlockSize = transformSkipEnabled ? reader.ReadUnsignedExpGolomb() + 2 : 2;
    if (log2MaxTransformSkipBlockSize > 5)
      throw new InvalidDataException(
        $"An H.265 picture parameter set allows transform skipping up to blocks of 2^{log2MaxTransformSkipBlockSize}. "
        + "No transform block is larger than 32x32, and clause 7.4.3.3.2 bounds the size there.");

    var crossComponentPrediction = reader.ReadFlag();
    var chromaQpOffsetList = reader.ReadFlag();

    var depth = 0;
    int[] cbOffsets = [];
    int[] crOffsets = [];
    if (chromaQpOffsetList) {
      depth = reader.ReadUnsignedExpGolomb();
      var length = reader.ReadUnsignedExpGolomb() + 1;
      if (length > 6)
        throw new InvalidDataException(
          $"An H.265 picture parameter set lists {length} chroma quantiser offsets; clause 7.4.3.3.2 allows six.");

      cbOffsets = new int[length];
      crOffsets = new int[length];
      for (var i = 0; i < length; ++i) {
        cbOffsets[i] = reader.ReadSignedExpGolomb();
        crOffsets[i] = reader.ReadSignedExpGolomb();
        if (cbOffsets[i] is < -12 or > 12 || crOffsets[i] is < -12 or > 12)
          throw new InvalidDataException(
            $"An H.265 picture parameter set lists the chroma quantiser offsets {cbOffsets[i]} and {crOffsets[i]}; "
            + "clause 7.4.3.3.2 bounds both at twelve either way.");
      }
    }

    var lumaSaoScale = reader.ReadUnsignedExpGolomb();
    var chromaSaoScale = reader.ReadUnsignedExpGolomb();
    if (lumaSaoScale > 6 || chromaSaoScale > 6)
      throw new InvalidDataException(
        $"An H.265 picture parameter set scales its sample adaptive offsets by 2^{lumaSaoScale} and "
        + $"2^{chromaSaoScale}. Clause 7.4.3.3.2 bounds the scale by the sample depth above ten bits, which is at "
        + "most six.");

    return new(log2MaxTransformSkipBlockSize, crossComponentPrediction, chromaQpOffsetList, depth,
      cbOffsets, crOffsets, lumaSaoScale, chromaSaoScale);
  }
}
