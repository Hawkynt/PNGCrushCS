using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>A picture parameter set — ITU-T H.264 clause 7.3.2.2.</summary>
internal sealed class H264PictureParameterSet {

  internal int Id { get; private set; }
  internal int SeqParameterSetId { get; private set; }
  internal bool EntropyCodingModeFlag { get; private set; }
  internal bool BottomFieldPicOrderInFramePresentFlag { get; private set; }
  internal int NumSliceGroups { get; private set; } = 1;
  internal int SliceGroupMapType { get; private set; }
  internal int NumRefIdxL0DefaultActive { get; private set; } = 1;
  internal int NumRefIdxL1DefaultActive { get; private set; } = 1;
  internal bool WeightedPredFlag { get; private set; }
  internal int WeightedBipredIdc { get; private set; }
  internal int PicInitQp { get; private set; } = 26;
  internal int ChromaQpIndexOffset { get; private set; }
  internal int SecondChromaQpIndexOffset { get; private set; }
  internal bool DeblockingFilterControlPresentFlag { get; private set; }
  internal bool ConstrainedIntraPredFlag { get; private set; }
  internal bool RedundantPicCntPresentFlag { get; private set; }
  internal bool Transform8x8ModeFlag { get; private set; }
  internal bool PicScalingMatrixPresentFlag { get; private set; }
  internal H264ScalingListOverrides? ScalingListOverrides { get; private set; }

  internal H264ScalingLists ResolveScalingLists(H264SequenceParameterSet sps)
    => H264ScalingLists.ResolvePicture(
      this.ScalingListOverrides, sps.ScalingLists, sps.SeqScalingMatrixPresentFlag);

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
    pps.NumRefIdxL1DefaultActive = reader.ReadUnsignedExpGolomb() + 1;
    pps.WeightedPredFlag = reader.ReadBit() != 0;
    pps.WeightedBipredIdc = reader.ReadBits(2);
    if (pps.WeightedBipredIdc > 2)
      throw new InvalidDataException(
        $"This H.264 PPS states weighted_bipred_idc {pps.WeightedBipredIdc}; clause 7.4.2.2 defines 0 through 2.");

    pps.PicInitQp = reader.ReadSignedExpGolomb() + 26;
    reader.ReadSignedExpGolomb(); // pic_init_qs_minus26, SP/SI only
    pps.ChromaQpIndexOffset = reader.ReadSignedExpGolomb();
    pps.SecondChromaQpIndexOffset = pps.ChromaQpIndexOffset;
    pps.DeblockingFilterControlPresentFlag = reader.ReadBit() != 0;
    pps.ConstrainedIntraPredFlag = reader.ReadBit() != 0;
    pps.RedundantPicCntPresentFlag = reader.ReadBit() != 0;

    if (reader.MoreRbspData) {
      pps.Transform8x8ModeFlag = reader.ReadBit() != 0;
      pps.PicScalingMatrixPresentFlag = reader.ReadBit() != 0;
      if (pps.PicScalingMatrixPresentFlag)
        pps.ScalingListOverrides = H264ScalingLists.ParsePictureOverrides(ref reader, pps.Transform8x8ModeFlag);

      pps.SecondChromaQpIndexOffset = reader.ReadSignedExpGolomb();
    }

    return pps;
  }

  /// <summary>Rejects only tools that still lack a reconstruction/syntax path.</summary>
  internal void RefuseUnsupported() {
    if (this.NumSliceGroups > 1)
      throw new NotSupportedException(
        $"This H.264 stream divides its pictures into {this.NumSliceGroups} slice groups "
        + $"(slice_group_map_type {this.SliceGroupMapType}). Flexible macroblock ordering is not implemented.");

    // transform_8x8_mode_flag and both SPS/PPS scaling matrices are now retained and used by the
    // common High-profile 8-bit 4:2:0 reconstruction path.

    if (this.EntropyCodingModeFlag)
      throw new NotSupportedException(
        "This H.264 stream sets entropy_coding_mode_flag and therefore uses CABAC (clause 9.3). "
        + "The CABAC syntax reader is not connected yet.");

    if (this.WeightedPredFlag)
      throw new NotSupportedException(
        "This H.264 PPS sets weighted_pred_flag. Explicit weighted P prediction is not connected yet.");

    if (this.WeightedBipredIdc != 0)
      throw new NotSupportedException(
        $"This H.264 PPS states weighted_bipred_idc {this.WeightedBipredIdc}. Bidirectional weighting is not connected yet.");
  }

  private static void _SkipSliceGroupMap(ref H264BitReader reader, H264PictureParameterSet pps) {
    pps.SliceGroupMapType = reader.ReadUnsignedExpGolomb();

    switch (pps.SliceGroupMapType) {
      case 0:
        for (var group = 0; group < pps.NumSliceGroups; ++group)
          reader.ReadUnsignedExpGolomb();
        break;
      case 1:
        break;
      case 2:
        for (var group = 0; group < pps.NumSliceGroups - 1; ++group) {
          reader.ReadUnsignedExpGolomb();
          reader.ReadUnsignedExpGolomb();
        }
        break;
      case 3:
      case 4:
      case 5:
        reader.ReadBit();
        reader.ReadUnsignedExpGolomb();
        break;
      case 6: {
        var mapUnits = reader.ReadUnsignedExpGolomb() + 1;
        var bits = _CeilLog2(pps.NumSliceGroups);
        for (var unit = 0; unit < mapUnits; ++unit)
          reader.ReadBits(bits);
        break;
      }
      default:
        throw new InvalidDataException(
          $"This H.264 PPS states slice_group_map_type {pps.SliceGroupMapType}; clause 7.4.2.2 defines 0 through 6.");
    }
  }

  private static int _CeilLog2(int value) {
    var bits = 0;
    while ((1 << bits) < value)
      ++bits;
    return bits;
  }
}
