using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// A picture parameter set: the coding decisions a picture's slices share (ITU-T H.264, clause
/// 7.3.2.2).
/// </summary>
/// <remarks>
/// Two of these fields decide whether a stream can be decoded here at all.
/// <c>entropy_coding_mode_flag</c> chooses between the variable-length codes of clause 9.2 and the
/// arithmetic coding of clause 9.3, and they are not two spellings of one bitstream but two
/// bitstreams; this decoder reads the first. <c>num_slice_groups_minus1</c> above zero is flexible
/// macroblock ordering, which scatters a picture's macroblocks between slice groups by a map — a
/// Baseline profile feature that no ordinary encoder emits.
/// </remarks>
internal sealed class H264PictureParameterSet {

  internal int Id { get; private set; }

  internal int SeqParameterSetId { get; private set; }

  /// <summary>Whether the slice data is arithmetic coded (CABAC) rather than variable-length coded (CAVLC).</summary>
  internal bool EntropyCodingModeFlag { get; private set; }

  internal bool BottomFieldPicOrderInFramePresentFlag { get; private set; }

  internal int NumSliceGroups { get; private set; } = 1;

  internal int SliceGroupMapType { get; private set; }

  /// <summary>The default size of reference picture list 0, when a slice does not override it.</summary>
  internal int NumRefIdxL0DefaultActive { get; private set; } = 1;

  internal bool WeightedPredFlag { get; private set; }

  internal int WeightedBipredIdc { get; private set; }

  /// <summary>The quantisation parameter a slice's <c>slice_qp_delta</c> is relative to.</summary>
  internal int PicInitQp { get; private set; } = 26;

  internal int ChromaQpIndexOffset { get; private set; }

  /// <summary>The offset for Cr, which before the High profile was always the same as Cb's.</summary>
  internal int SecondChromaQpIndexOffset { get; private set; }

  internal bool DeblockingFilterControlPresentFlag { get; private set; }

  /// <summary>
  /// Whether intra prediction may read samples of inter-coded neighbours (clause 8.3.1.2).
  /// </summary>
  internal bool ConstrainedIntraPredFlag { get; private set; }

  internal bool RedundantPicCntPresentFlag { get; private set; }

  internal bool Transform8x8ModeFlag { get; private set; }

  internal bool PicScalingMatrixPresentFlag { get; private set; }

  internal static H264PictureParameterSet Parse(ReadOnlySpan<byte> rbsp) {
    var reader = new H264BitReader(rbsp);
    var pps = new H264PictureParameterSet {
      Id = reader.ReadUnsignedExpGolomb(),
      SeqParameterSetId = reader.ReadUnsignedExpGolomb(),
      EntropyCodingModeFlag = reader.ReadBit() != 0,
      BottomFieldPicOrderInFramePresentFlag = reader.ReadBit() != 0,
      NumSliceGroups = reader.ReadUnsignedExpGolomb() + 1,
    };

    if (pps.NumSliceGroups > 1)
      _SkipSliceGroupMap(ref reader, pps);

    pps.NumRefIdxL0DefaultActive = reader.ReadUnsignedExpGolomb() + 1;
    reader.ReadUnsignedExpGolomb(); // num_ref_idx_l1_default_active_minus1, the second list a B slice has
    pps.WeightedPredFlag = reader.ReadBit() != 0;
    pps.WeightedBipredIdc = reader.ReadBits(2);
    pps.PicInitQp = reader.ReadSignedExpGolomb() + 26;
    reader.ReadSignedExpGolomb(); // pic_init_qs_minus26, which only an SP or SI slice quantises with
    pps.ChromaQpIndexOffset = reader.ReadSignedExpGolomb();
    pps.SecondChromaQpIndexOffset = pps.ChromaQpIndexOffset;
    pps.DeblockingFilterControlPresentFlag = reader.ReadBit() != 0;
    pps.ConstrainedIntraPredFlag = reader.ReadBit() != 0;
    pps.RedundantPicCntPresentFlag = reader.ReadBit() != 0;

    // The three High profile fields are present only when there is payload left for them — the one
    // place in the H.264 syntax where a field's presence is decided by where the stop bit is rather
    // than by a flag (clause 7.3.2.2). A Baseline parameter set simply ends here.
    if (reader.MoreRbspData) {
      pps.Transform8x8ModeFlag = reader.ReadBit() != 0;
      pps.PicScalingMatrixPresentFlag = reader.ReadBit() != 0;

      // Skipped rather than kept: a stream carrying them is refused, and what is wanted from the walk
      // is the position of second_chroma_qp_index_offset after it.
      if (pps.PicScalingMatrixPresentFlag)
        _SkipPicScalingMatrix(ref reader, pps);

      pps.SecondChromaQpIndexOffset = reader.ReadSignedExpGolomb();
    }

    return pps;
  }

  /// <summary>
  /// Refuses, by name, a picture parameter set this decoder cannot act on.
  /// </summary>
  /// <remarks>
  /// In order of how fundamental the obstacle is, because a stream may well be outside the boundary
  /// for more than one reason and the first message is the one a reader acts on. A High profile
  /// stream encoded with weighted prediction on is refused for being High profile rather than for the
  /// weighting, which is the more useful thing to be told.
  /// </remarks>
  internal void RefuseUnsupported() {
    if (this.EntropyCodingModeFlag)
      throw new NotSupportedException(
        "This H.264 stream is arithmetic coded: its picture parameter set sets entropy_coding_mode_flag, which "
        + "selects CABAC (H.264, clause 9.3) instead of the variable-length coding of clause 9.2. CABAC is not "
        + "implemented. Re-encoding with CAVLC — x264's cabac=0, or -profile:v baseline — produces a stream this "
        + "decoder reads.");

    if (this.NumSliceGroups > 1)
      throw new NotSupportedException(
        $"This H.264 stream divides its pictures into {this.NumSliceGroups} slice groups "
        + $"(num_slice_groups_minus1 {this.NumSliceGroups - 1}, slice_group_map_type {this.SliceGroupMapType}, H.264 "
        + "clause 8.2.2). Flexible macroblock ordering is not implemented; a decoder that ignored the map would "
        + "reconstruct every macroblock at the wrong address.");

    if (this.Transform8x8ModeFlag)
      throw new NotSupportedException(
        "This H.264 picture parameter set sets transform_8x8_mode_flag, so its macroblocks may use the 8x8 "
        + "transform (H.264, clause 8.5.13). That is a High profile feature and is not implemented.");

    if (this.PicScalingMatrixPresentFlag)
      throw new NotSupportedException(
        "This H.264 picture parameter set carries scaling matrices (pic_scaling_matrix_present_flag, H.264 clause "
        + "7.3.2.2). Non-flat quantiser weighting is a High profile feature and is not implemented; decoding it "
        + "with the flat matrices would dequantise every coefficient by the wrong factor.");

    if (this.WeightedPredFlag)
      throw new NotSupportedException(
        "This H.264 picture parameter set sets weighted_pred_flag, so its P slices carry explicit prediction "
        + "weights (H.264, clause 8.4.2.3). Weighted prediction is not implemented; predicting without the weights "
        + "would be wrong by the whole of the weighting. Re-encoding with x264's weightp=0 produces a stream this "
        + "decoder reads.");

    if (this.WeightedBipredIdc != 0)
      throw new NotSupportedException(
        $"This H.264 picture parameter set states weighted_bipred_idc {this.WeightedBipredIdc}, which weights "
        + "bidirectional prediction (H.264, clause 8.4.2.3). Weighted prediction is not implemented.");
  }

  /// <summary>
  /// Walks the slice group map so that the fields after it are read at the right offset
  /// (clause 7.3.2.2).
  /// </summary>
  private static void _SkipSliceGroupMap(ref H264BitReader reader, H264PictureParameterSet pps) {
    pps.SliceGroupMapType = reader.ReadUnsignedExpGolomb();

    switch (pps.SliceGroupMapType) {
      case 0:
        for (var group = 0; group < pps.NumSliceGroups; ++group)
          reader.ReadUnsignedExpGolomb(); // run_length_minus1
        break;

      case 2:
        for (var group = 0; group < pps.NumSliceGroups - 1; ++group) {
          reader.ReadUnsignedExpGolomb(); // top_left
          reader.ReadUnsignedExpGolomb(); // bottom_right
        }

        break;

      case 3:
      case 4:
      case 5:
        reader.ReadBit(); // slice_group_change_direction_flag
        reader.ReadUnsignedExpGolomb(); // slice_group_change_rate_minus1
        break;

      case 6:
        var mapUnits = reader.ReadUnsignedExpGolomb() + 1;
        var bits = _CeilLog2(pps.NumSliceGroups);
        for (var unit = 0; unit < mapUnits; ++unit)
          reader.ReadBits(bits); // slice_group_id
        break;

      case 1:
        // Dispersed: the map is computed rather than transmitted, so nothing follows.
        break;

      default:
        throw new InvalidDataException(
          $"This H.264 picture parameter set states slice_group_map_type {pps.SliceGroupMapType}. H.264, clause "
          + "7.4.2.2 defines 0 to 6 only.");
    }
  }

  /// <summary>Walks the picture scaling lists (clauses 7.3.2.2 and 7.3.2.1.1.1).</summary>
  private static void _SkipPicScalingMatrix(ref H264BitReader reader, H264PictureParameterSet pps) {
    // Six 4x4 lists always, and then the 8x8 lists only where the 8x8 transform is in use: two of
    // them for a 4:2:0 or 4:2:2 picture and six for 4:4:4 (clause 7.3.2.2). The chroma format lives
    // in the sequence parameter set, which this one names but does not hold — so the count is taken
    // from the two-list case and a 4:4:4 stream is caught by the sequence parameter set's own
    // refusal before it can matter.
    var listCount = 6 + (pps.Transform8x8ModeFlag ? 2 : 0);
    for (var list = 0; list < listCount; ++list) {
      if (reader.ReadBit() == 0)
        continue;

      var size = list < 6 ? 16 : 64;
      var lastScale = 8;
      var nextScale = 8;
      for (var j = 0; j < size; ++j) {
        if (nextScale != 0)
          nextScale = (lastScale + reader.ReadSignedExpGolomb() + 256) % 256;

        lastScale = nextScale == 0 ? lastScale : nextScale;
      }
    }
  }

  private static int _CeilLog2(int value) {
    var bits = 0;
    while ((1 << bits) < value)
      ++bits;

    return bits;
  }
}
