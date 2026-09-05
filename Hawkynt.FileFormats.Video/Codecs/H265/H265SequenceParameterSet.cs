using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// A sequence parameter set — ITU-T H.265, clauses 7.3.2.2 and 7.4.3.2.
/// </summary>
/// <remarks>
/// Everything that holds for a whole coded video sequence: the picture size, the sample depth, the
/// chroma format, the three nested block-size hierarchies HEVC decodes through, which loop filters
/// exist, and the reference picture sets a slice may name by index.
/// <para/>
/// The block sizes are the part with no counterpart in H.264, which had one block size for
/// everything. HEVC has three hierarchies and they are independent: coding blocks run from
/// <see cref="MinCbLog2SizeY"/> up to the coding tree block size and carry the prediction mode;
/// transform blocks run from <see cref="MinTbLog2SizeY"/> up to <see cref="MaxTbLog2SizeY"/> and
/// carry the residual; prediction blocks are cut out of a coding block by its partition mode and are
/// neither. A decoder that conflated any two of them would decode the first picture of most streams
/// and then walk off the quadtree of one that chose different limits.
/// <para/>
/// The video usability information is stepped over exactly rather than skipped to the end. It is the
/// last variable-length structure before the extension flags, and those flags say whether the stream
/// uses a residual coding this decoder does not have — so getting the step wrong would mean reading
/// the extension flags out of the middle of the timing information and believing whatever was there.
/// </remarks>
internal sealed class H265SequenceParameterSet {

  private H265SequenceParameterSet() { }

  internal int Id { get; private init; }

  internal int VideoParameterSetId { get; private init; }

  internal H265ProfileTierLevel ProfileTierLevel { get; private init; } = null!;

  /// <summary>0 monochrome, 1 4:2:0, 2 4:2:2, 3 4:4:4.</summary>
  internal int ChromaFormatIdc { get; private init; }

  /// <summary>Whether the three colour planes are coded as three separate monochrome pictures.</summary>
  internal bool SeparateColourPlaneFlag { get; private init; }

  /// <summary>
  /// <c>ChromaArrayType</c>: the chroma format as the decoding process sees it, which is zero for a
  /// stream coding its planes separately because each coded plane is then monochrome.
  /// </summary>
  internal int ChromaArrayType => this.SeparateColourPlaneFlag ? 0 : this.ChromaFormatIdc;

  internal int SubWidthC => this.ChromaArrayType is 1 or 2 ? 2 : 1;

  internal int SubHeightC => this.ChromaArrayType == 1 ? 2 : 1;

  /// <summary>The coded picture width in luma samples, which is a whole number of minimum coding blocks.</summary>
  internal int Width { get; private init; }

  internal int Height { get; private init; }

  internal int ConformanceWindowLeft { get; private init; }

  internal int ConformanceWindowRight { get; private init; }

  internal int ConformanceWindowTop { get; private init; }

  internal int ConformanceWindowBottom { get; private init; }

  /// <summary>The first displayed column, in luma samples.</summary>
  internal int CropOffsetX => this.ConformanceWindowLeft * this.SubWidthC;

  internal int CropOffsetY => this.ConformanceWindowTop * this.SubHeightC;

  internal int DisplayWidth => this.Width - (this.ConformanceWindowLeft + this.ConformanceWindowRight) * this.SubWidthC;

  internal int DisplayHeight
    => this.Height - (this.ConformanceWindowTop + this.ConformanceWindowBottom) * this.SubHeightC;

  internal int BitDepthLuma { get; private init; }

  internal int BitDepthChroma { get; private init; }

  /// <summary><c>QpBdOffsetY</c>: how far below zero the luma quantiser may go at this sample depth.</summary>
  internal int QpBdOffsetLuma => 6 * (this.BitDepthLuma - 8);

  internal int QpBdOffsetChroma => 6 * (this.BitDepthChroma - 8);

  internal int Log2MaxPicOrderCntLsb { get; private init; }

  internal int MaxPicOrderCntLsb => 1 << this.Log2MaxPicOrderCntLsb;

  /// <summary>How many pictures the decoded picture buffer must hold, at the highest sub-layer.</summary>
  internal int MaxDecodedPictureBuffering { get; private init; }

  /// <summary>How many pictures may precede a picture in decoding order and follow it in output order.</summary>
  internal int MaxNumReorderPictures { get; private init; }

  internal int MinCbLog2SizeY { get; private init; }

  internal int CtbLog2SizeY { get; private init; }

  internal int MinCbSizeY => 1 << this.MinCbLog2SizeY;

  internal int CtbSizeY => 1 << this.CtbLog2SizeY;

  internal int MinTbLog2SizeY { get; private init; }

  internal int MaxTbLog2SizeY { get; private init; }

  internal int MaxTransformHierarchyDepthInter { get; private init; }

  internal int MaxTransformHierarchyDepthIntra { get; private init; }

  internal int PicWidthInCtbsY => (this.Width + this.CtbSizeY - 1) >> this.CtbLog2SizeY;

  internal int PicHeightInCtbsY => (this.Height + this.CtbSizeY - 1) >> this.CtbLog2SizeY;

  internal int PicSizeInCtbsY => this.PicWidthInCtbsY * this.PicHeightInCtbsY;

  internal int PicWidthInMinCbsY => this.Width >> this.MinCbLog2SizeY;

  internal int PicHeightInMinCbsY => this.Height >> this.MinCbLog2SizeY;

  internal bool ScalingListEnabled { get; private init; }

  /// <summary>The matrices this sequence uses, or <c>null</c> where the picture parameter set states them.</summary>
  internal H265ScalingList? ScalingList { get; private init; }

  /// <summary>Whether a coding block may be split into unequal halves for prediction.</summary>
  internal bool AmpEnabled { get; private init; }

  internal bool SampleAdaptiveOffsetEnabled { get; private init; }

  internal bool PcmEnabled { get; private init; }

  internal int PcmBitDepthLuma { get; private init; }

  internal int PcmBitDepthChroma { get; private init; }

  internal int Log2MinPcmCbSizeY { get; private init; }

  internal int Log2MaxPcmCbSizeY { get; private init; }

  internal bool PcmLoopFilterDisabled { get; private init; }

  internal H265ShortTermReferencePictureSet[] ShortTermReferencePictureSets { get; private init; } = [];

  internal bool LongTermReferencePicturesPresent { get; private init; }

  internal int[] LongTermReferencePicturePocLsb { get; private init; } = [];

  internal bool[] LongTermReferencePictureUsed { get; private init; } = [];

  /// <summary>Whether a slice may predict its motion vectors from the picture it names as collocated.</summary>
  internal bool TemporalMvpEnabled { get; private init; }

  /// <summary>Whether a flat 32x32 intra block may have its reference samples smoothed the long way.</summary>
  internal bool StrongIntraSmoothingEnabled { get; private init; }

  /// <summary>Whether two sequence parameter sets describe pictures of the same shape.</summary>
  internal bool SameGeometryAs(H265SequenceParameterSet other)
    => this.Width == other.Width
       && this.Height == other.Height
       && this.ChromaFormatIdc == other.ChromaFormatIdc
       && this.BitDepthLuma == other.BitDepthLuma
       && this.BitDepthChroma == other.BitDepthChroma;

  internal static H265SequenceParameterSet Parse(ReadOnlySpan<byte> payload) {
    var reader = new H265BitReader(payload);

    var videoParameterSetId = reader.ReadBits(4);
    var maxSubLayersMinus1 = reader.ReadBits(3);
    reader.Skip(1); // sps_temporal_id_nesting_flag

    var profileTierLevel = H265ProfileTierLevel.Parse(ref reader, true, maxSubLayersMinus1);

    var id = reader.ReadUnsignedExpGolomb();
    var chromaFormatIdc = reader.ReadUnsignedExpGolomb();
    var separateColourPlane = chromaFormatIdc == 3 && reader.ReadFlag();

    var width = reader.ReadUnsignedExpGolomb();
    var height = reader.ReadUnsignedExpGolomb();

    var left = 0;
    var right = 0;
    var top = 0;
    var bottom = 0;
    if (reader.ReadFlag()) {
      left = reader.ReadUnsignedExpGolomb();
      right = reader.ReadUnsignedExpGolomb();
      top = reader.ReadUnsignedExpGolomb();
      bottom = reader.ReadUnsignedExpGolomb();
    }

    var bitDepthLuma = reader.ReadUnsignedExpGolomb() + 8;
    var bitDepthChroma = reader.ReadUnsignedExpGolomb() + 8;
    var log2MaxPocLsb = reader.ReadUnsignedExpGolomb() + 4;

    var subLayerOrderingInfoPresent = reader.ReadFlag();
    var maxDecodedPictureBuffering = 1;
    var maxNumReorder = 0;
    for (var i = subLayerOrderingInfoPresent ? 0 : maxSubLayersMinus1; i <= maxSubLayersMinus1; ++i) {
      maxDecodedPictureBuffering = reader.ReadUnsignedExpGolomb() + 1;
      maxNumReorder = reader.ReadUnsignedExpGolomb();
      reader.ReadUnsignedExpGolomb(); // sps_max_latency_increase_plus1
    }

    var minCbLog2SizeY = reader.ReadUnsignedExpGolomb() + 3;
    var ctbLog2SizeY = minCbLog2SizeY + reader.ReadUnsignedExpGolomb();
    var minTbLog2SizeY = reader.ReadUnsignedExpGolomb() + 2;
    var maxTbLog2SizeY = minTbLog2SizeY + reader.ReadUnsignedExpGolomb();
    var maxTransformDepthInter = reader.ReadUnsignedExpGolomb();
    var maxTransformDepthIntra = reader.ReadUnsignedExpGolomb();

    var scalingListEnabled = reader.ReadFlag();
    H265ScalingList? scalingList = null;
    if (scalingListEnabled)
      // Enabled but not stated means the defaults of Tables 7-5 and 7-6, which are not the flat
      // matrix a disabled stream uses. A picture parameter set may still replace them.
      scalingList = reader.ReadFlag() ? H265ScalingList.Parse(ref reader) : H265ScalingList.Default();

    var ampEnabled = reader.ReadFlag();
    var saoEnabled = reader.ReadFlag();

    var pcmEnabled = reader.ReadFlag();
    var pcmBitDepthLuma = 0;
    var pcmBitDepthChroma = 0;
    var log2MinPcmCbSizeY = 0;
    var log2MaxPcmCbSizeY = 0;
    var pcmLoopFilterDisabled = false;
    if (pcmEnabled) {
      pcmBitDepthLuma = reader.ReadBits(4) + 1;
      pcmBitDepthChroma = reader.ReadBits(4) + 1;
      log2MinPcmCbSizeY = reader.ReadUnsignedExpGolomb() + 3;
      log2MaxPcmCbSizeY = log2MinPcmCbSizeY + reader.ReadUnsignedExpGolomb();
      pcmLoopFilterDisabled = reader.ReadFlag();
    }

    var referencePictureSetCount = reader.ReadUnsignedExpGolomb();
    if (referencePictureSetCount > 64)
      throw new InvalidDataException(
        $"An H.265 sequence parameter set declares {referencePictureSetCount} short-term reference picture sets. "
        + "Clause 7.4.3.2.1 bounds num_short_term_ref_pic_sets at 64, so these bytes are not a parameter set.");

    // One more slot than the sequence declares, because a slice may code a set of its own at the
    // index just past the last of these and a set is allowed to predict from the one before it.
    var referencePictureSets = new H265ShortTermReferencePictureSet[referencePictureSetCount + 1];
    for (var i = 0; i < referencePictureSetCount; ++i)
      referencePictureSets[i] =
        H265ShortTermReferencePictureSet.Parse(ref reader, i, referencePictureSetCount, referencePictureSets);

    var longTermPresent = reader.ReadFlag();
    var longTermPocLsb = Array.Empty<int>();
    var longTermUsed = Array.Empty<bool>();
    if (longTermPresent) {
      var count = reader.ReadUnsignedExpGolomb();
      if (count > 32)
        throw new InvalidDataException(
          $"An H.265 sequence parameter set declares {count} long-term reference pictures, which clause 7.4.3.2.1 "
          + "bounds at 32.");

      longTermPocLsb = new int[count];
      longTermUsed = new bool[count];
      for (var i = 0; i < count; ++i) {
        longTermPocLsb[i] = reader.ReadBits(log2MaxPocLsb);
        longTermUsed[i] = reader.ReadFlag();
      }
    }

    var temporalMvpEnabled = reader.ReadFlag();
    var strongIntraSmoothing = reader.ReadFlag();

    if (reader.ReadFlag())
      _SkipVideoUsabilityInformation(ref reader, maxSubLayersMinus1);

    if (reader.ReadFlag())
      _RefuseExtensions(ref reader);

    var sps = new H265SequenceParameterSet {
      Id = id,
      VideoParameterSetId = videoParameterSetId,
      ProfileTierLevel = profileTierLevel,
      ChromaFormatIdc = chromaFormatIdc,
      SeparateColourPlaneFlag = separateColourPlane,
      Width = width,
      Height = height,
      ConformanceWindowLeft = left,
      ConformanceWindowRight = right,
      ConformanceWindowTop = top,
      ConformanceWindowBottom = bottom,
      BitDepthLuma = bitDepthLuma,
      BitDepthChroma = bitDepthChroma,
      Log2MaxPicOrderCntLsb = log2MaxPocLsb,
      MaxDecodedPictureBuffering = maxDecodedPictureBuffering,
      MaxNumReorderPictures = maxNumReorder,
      MinCbLog2SizeY = minCbLog2SizeY,
      CtbLog2SizeY = ctbLog2SizeY,
      MinTbLog2SizeY = minTbLog2SizeY,
      MaxTbLog2SizeY = maxTbLog2SizeY,
      MaxTransformHierarchyDepthInter = maxTransformDepthInter,
      MaxTransformHierarchyDepthIntra = maxTransformDepthIntra,
      ScalingListEnabled = scalingListEnabled,
      ScalingList = scalingList,
      AmpEnabled = ampEnabled,
      SampleAdaptiveOffsetEnabled = saoEnabled,
      PcmEnabled = pcmEnabled,
      PcmBitDepthLuma = pcmBitDepthLuma,
      PcmBitDepthChroma = pcmBitDepthChroma,
      Log2MinPcmCbSizeY = log2MinPcmCbSizeY,
      Log2MaxPcmCbSizeY = log2MaxPcmCbSizeY,
      PcmLoopFilterDisabled = pcmLoopFilterDisabled,
      ShortTermReferencePictureSets = referencePictureSets,
      LongTermReferencePicturesPresent = longTermPresent,
      LongTermReferencePicturePocLsb = longTermPocLsb,
      LongTermReferencePictureUsed = longTermUsed,
      TemporalMvpEnabled = temporalMvpEnabled,
      StrongIntraSmoothingEnabled = strongIntraSmoothing,
    };

    sps._RefuseWhatIsNotDecodable();
    return sps;
  }

  /// <summary>
  /// Refuses the coded video formats this decoder does not reconstruct, each by the field that says so.
  /// </summary>
  /// <remarks>
  /// All of these are refused at the parameter set rather than where the samples would be wrong,
  /// because by then a picture exists and something would have to decide whether to hand it back. A
  /// stream this decoder cannot read produces no picture at all.
  /// </remarks>
  private void _RefuseWhatIsNotDecodable() {
    if (this.SeparateColourPlaneFlag)
      throw new NotSupportedException(
        "This H.265 stream sets separate_colour_plane_flag (clause 7.4.3.2.1): its three colour planes are coded as "
        + "three monochrome pictures with a colour_plane_id each, rather than as one picture. Reading that form is "
        + "not implemented.");

    if (this.ChromaFormatIdc != 1)
      throw new NotSupportedException(
        $"This H.265 stream is {this.ChromaFormatIdc switch {
          0 => "monochrome",
          2 => "4:2:2",
          3 => "4:4:4",
          _ => $"chroma_format_idc {this.ChromaFormatIdc}",
        }} (clause 7.4.3.2.1). Only 4:2:0, which is what the Main and Main 10 profiles permit, is implemented.");

    if (this.BitDepthLuma is < 8 or > 12 || this.BitDepthChroma is < 8 or > 12)
      throw new NotSupportedException(
        $"This H.265 stream codes {this.BitDepthLuma}-bit luma and {this.BitDepthChroma}-bit chroma samples "
        + "(clause 7.4.3.2.1). Eight to twelve bits are implemented, which covers Main, Main 10 and the twelve-bit "
        + "still profile; deeper samples need the extended precision the range extensions add, and the parameter "
        + "sets refuse every tool of theirs that changes a sample.");
  }

  /// <summary>Reads the extension flags and refuses the ones that change the decoding process.</summary>
  private static void _RefuseExtensions(ref H265BitReader reader) {
    var rangeExtension = reader.ReadFlag();
    var multilayerExtension = reader.ReadFlag();
    var threeDimensionalExtension = reader.ReadFlag();
    var screenContentExtension = reader.ReadFlag();
    reader.Skip(4); // sps_extension_4bits

    if (multilayerExtension || threeDimensionalExtension)
      throw new NotSupportedException(
        "This H.265 sequence parameter set carries a multilayer or 3D extension (clause 7.3.2.2.1, Annexes F, G and "
        + "I). Only the base layer syntax is implemented, and a stream whose extension layers were dropped is not "
        + "the stream that was encoded.");

    if (screenContentExtension)
      throw new NotSupportedException(
        "This H.265 sequence parameter set carries a screen content coding extension (clause 7.3.2.2.3). Palette "
        + "mode, adaptive colour transform and intra block copy change how a coding unit is reconstructed and are "
        + "not implemented.");

    if (!rangeExtension)
      return;

    // Every one of these nine flags changes the decoding process rather than describing it, so the
    // one that is set is named: a decoder that read them and pressed on would return a picture.
    string[] names = [
      "transform_skip_rotation_enabled_flag", "transform_skip_context_enabled_flag",
      "implicit_rdpcm_enabled_flag", "explicit_rdpcm_enabled_flag", "extended_precision_processing_flag",
      "intra_smoothing_disabled_flag", "high_precision_offsets_enabled_flag",
      "persistent_rice_adaptation_enabled_flag", "cabac_bypass_alignment_enabled_flag",
    ];

    for (var i = 0; i < names.Length; ++i)
      if (reader.ReadFlag())
        throw new NotSupportedException(
          $"This H.265 sequence parameter set sets {names[i]} (clause 7.3.2.2.2, the format range extensions). It "
          + "changes how residuals are coded or reconstructed and is not implemented.");
  }

  /// <summary>
  /// Steps over <c>vui_parameters()</c> — Annex E.2.1 — without keeping any of it.
  /// </summary>
  /// <remarks>
  /// None of it reaches a sample. The colour description says which primaries and transfer the
  /// samples were meant for, which is the display's business; the timing says how fast to show them,
  /// which is the container's; the hypothetical reference decoder parameters describe a buffer model
  /// a file being decoded from disc does not have. What the structure has to be read for is its
  /// length, because the flags that follow it say whether the residual coding is one this decoder
  /// implements.
  /// </remarks>
  private static void _SkipVideoUsabilityInformation(ref H265BitReader reader, int maxSubLayersMinus1) {
    if (reader.ReadFlag() && reader.ReadBits(8) == 255)
      reader.Skip(32); // sar_width, sar_height

    if (reader.ReadFlag())
      reader.Skip(1); // overscan_appropriate_flag

    if (reader.ReadFlag()) {
      reader.Skip(4); // video_format, video_full_range_flag
      if (reader.ReadFlag())
        reader.Skip(24); // colour_primaries, transfer_characteristics, matrix_coeffs
    }

    if (reader.ReadFlag()) {
      reader.ReadUnsignedExpGolomb();
      reader.ReadUnsignedExpGolomb();
    }

    reader.Skip(3); // neutral_chroma_indication_flag, field_seq_flag, frame_field_info_present_flag

    if (reader.ReadFlag())
      for (var i = 0; i < 4; ++i)
        reader.ReadUnsignedExpGolomb(); // def_disp_win offsets

    if (reader.ReadFlag()) {
      reader.Skip(64); // vui_num_units_in_tick, vui_time_scale

      if (reader.ReadFlag())
        reader.ReadUnsignedExpGolomb(); // vui_num_ticks_poc_diff_one_minus1

      if (reader.ReadFlag())
        _SkipHypotheticalReferenceDecoderParameters(ref reader, true, maxSubLayersMinus1);
    }

    if (!reader.ReadFlag())
      return;

    reader.Skip(3); // tiles_fixed_structure, motion_vectors_over_pic_boundaries, restricted_ref_pic_lists
    for (var i = 0; i < 5; ++i)
      reader.ReadUnsignedExpGolomb();
  }

  /// <summary>Steps over <c>hrd_parameters()</c> — Annex E.2.2.</summary>
  private static void _SkipHypotheticalReferenceDecoderParameters(
    ref H265BitReader reader, bool commonInfoPresent, int maxSubLayersMinus1) {
    var nalPresent = false;
    var vclPresent = false;
    var subPicturePresent = false;

    if (commonInfoPresent) {
      nalPresent = reader.ReadFlag();
      vclPresent = reader.ReadFlag();

      if (nalPresent || vclPresent) {
        subPicturePresent = reader.ReadFlag();
        if (subPicturePresent)
          reader.Skip(19); // tick_divisor_minus2 and the three sub-picture delay lengths

        reader.Skip(8); // bit_rate_scale, cpb_size_scale
        if (subPicturePresent)
          reader.Skip(4); // cpb_size_du_scale

        reader.Skip(15); // the three delay length fields
      }
    }

    for (var i = 0; i <= maxSubLayersMinus1; ++i) {
      var fixedRateGeneral = reader.ReadFlag();
      var fixedRateWithinSequence = fixedRateGeneral || reader.ReadFlag();

      var lowDelay = false;
      if (fixedRateWithinSequence)
        reader.ReadUnsignedExpGolomb(); // elemental_duration_in_tc_minus1
      else
        lowDelay = reader.ReadFlag();

      var buffers = lowDelay ? 0 : reader.ReadUnsignedExpGolomb();

      for (var pass = 0; pass < 2; ++pass) {
        if (pass == 0 ? !nalPresent : !vclPresent)
          continue;

        for (var j = 0; j <= buffers; ++j) {
          reader.ReadUnsignedExpGolomb();
          reader.ReadUnsignedExpGolomb();
          if (subPicturePresent) {
            reader.ReadUnsignedExpGolomb();
            reader.ReadUnsignedExpGolomb();
          }

          reader.Skip(1); // cbr_flag
        }
      }
    }
  }
}
