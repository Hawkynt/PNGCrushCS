using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>A sequence parameter set — ITU-T H.264 clause 7.3.2.1.1.</summary>
internal sealed class H264SequenceParameterSet {

  internal int Id { get; private set; }
  internal int ProfileIdc { get; private set; }
  internal int ChromaFormatIdc { get; private set; } = 1;
  internal bool SeparateColourPlaneFlag { get; private set; }
  internal int BitDepthLuma { get; private set; } = 8;
  internal int BitDepthChroma { get; private set; } = 8;
  internal bool QpPrimeYZeroTransformBypassFlag { get; private set; }
  internal bool SeqScalingMatrixPresentFlag { get; private set; }
  internal H264ScalingLists ScalingLists { get; private set; } = H264ScalingLists.Flat();
  internal int Log2MaxFrameNum { get; private set; }
  internal int MaxFrameNum => 1 << this.Log2MaxFrameNum;
  internal int PicOrderCntType { get; private set; }
  internal int Log2MaxPicOrderCntLsb { get; private set; }
  internal bool DeltaPicOrderAlwaysZeroFlag { get; private set; }
  internal int OffsetForNonRefPic { get; private set; }
  internal int OffsetForTopToBottomField { get; private set; }
  internal int[] OffsetForRefFrame { get; private set; } = [];
  internal int MaxNumRefFrames { get; private set; }
  internal bool GapsInFrameNumValueAllowedFlag { get; private set; }
  internal int PicWidthInMbs { get; private set; }
  internal int PicHeightInMapUnits { get; private set; }
  internal bool FrameMbsOnlyFlag { get; private set; }
  internal bool MbAdaptiveFrameFieldFlag { get; private set; }
  internal bool Direct8x8InferenceFlag { get; private set; }
  internal int CropLeft { get; private set; }
  internal int CropRight { get; private set; }
  internal int CropTop { get; private set; }
  internal int CropBottom { get; private set; }

  internal int FrameHeightInMbs => (this.FrameMbsOnlyFlag ? 1 : 2) * this.PicHeightInMapUnits;
  internal int CodedWidth => this.PicWidthInMbs * 16;
  internal int CodedHeight => this.FrameHeightInMbs * 16;
  internal int ChromaArrayType => this.SeparateColourPlaneFlag ? 0 : this.ChromaFormatIdc;
  internal int CropOffsetX => _CropUnitX(this) * this.CropLeft;
  internal int CropOffsetY => _CropUnitY(this) * this.CropTop;
  internal int DisplayWidth => this.CodedWidth - _CropUnitX(this) * (this.CropLeft + this.CropRight);
  internal int DisplayHeight => this.CodedHeight - _CropUnitY(this) * (this.CropTop + this.CropBottom);

  internal bool SameGeometryAs(H264SequenceParameterSet other)
    => other != null
       && this.PicWidthInMbs == other.PicWidthInMbs
       && this.FrameHeightInMbs == other.FrameHeightInMbs
       && this.ChromaArrayType == other.ChromaArrayType
       && this.DisplayWidth == other.DisplayWidth
       && this.DisplayHeight == other.DisplayHeight;

  internal static H264SequenceParameterSet Parse(ReadOnlySpan<byte> rbsp) {
    var reader = new H264BitReader(rbsp);
    var profileIdc = reader.ReadBits(8);
    reader.Skip(16); // constraint flags/reserved bits + level_idc

    var sps = new H264SequenceParameterSet {
      ProfileIdc = profileIdc,
      Id = reader.ReadUnsignedExpGolomb(),
    };

    if (_HasChromaFormat(profileIdc)) {
      sps.ChromaFormatIdc = reader.ReadUnsignedExpGolomb();
      if (sps.ChromaFormatIdc is < 0 or > 3)
        throw new InvalidDataException(
          $"This H.264 SPS states chroma_format_idc {sps.ChromaFormatIdc}; Table 6-1 defines 0 through 3.");

      if (sps.ChromaFormatIdc == 3)
        sps.SeparateColourPlaneFlag = reader.ReadBit() != 0;

      sps.BitDepthLuma = reader.ReadUnsignedExpGolomb() + 8;
      sps.BitDepthChroma = reader.ReadUnsignedExpGolomb() + 8;
      sps.QpPrimeYZeroTransformBypassFlag = reader.ReadBit() != 0;
      sps.SeqScalingMatrixPresentFlag = reader.ReadBit() != 0;
      sps.ScalingLists = sps.SeqScalingMatrixPresentFlag
        ? H264ScalingLists.ParseSequence(ref reader, sps.ChromaFormatIdc)
        : H264ScalingLists.Flat();
    }

    sps.Log2MaxFrameNum = reader.ReadUnsignedExpGolomb() + 4;
    if (sps.Log2MaxFrameNum is < 4 or > 16)
      throw new InvalidDataException(
        $"This H.264 SPS gives Log2MaxFrameNum {sps.Log2MaxFrameNum}; clause 7.4.2.1.1 permits 4 through 16.");

    sps.PicOrderCntType = reader.ReadUnsignedExpGolomb();
    switch (sps.PicOrderCntType) {
      case 0:
        sps.Log2MaxPicOrderCntLsb = reader.ReadUnsignedExpGolomb() + 4;
        break;

      case 1: {
        sps.DeltaPicOrderAlwaysZeroFlag = reader.ReadBit() != 0;
        sps.OffsetForNonRefPic = reader.ReadSignedExpGolomb();
        sps.OffsetForTopToBottomField = reader.ReadSignedExpGolomb();
        var count = reader.ReadUnsignedExpGolomb();
        sps.OffsetForRefFrame = new int[count];
        for (var i = 0; i < count; ++i)
          sps.OffsetForRefFrame[i] = reader.ReadSignedExpGolomb();
        break;
      }

      case 2:
        break;

      default:
        throw new InvalidDataException(
          $"This H.264 sequence states pic_order_cnt_type {sps.PicOrderCntType}. Clause 7.4.2.1.1 defines 0, 1 and 2 only.");
    }

    sps.MaxNumRefFrames = reader.ReadUnsignedExpGolomb();
    sps.GapsInFrameNumValueAllowedFlag = reader.ReadBit() != 0;
    sps.PicWidthInMbs = reader.ReadUnsignedExpGolomb() + 1;
    sps.PicHeightInMapUnits = reader.ReadUnsignedExpGolomb() + 1;
    sps.FrameMbsOnlyFlag = reader.ReadBit() != 0;
    if (!sps.FrameMbsOnlyFlag)
      sps.MbAdaptiveFrameFieldFlag = reader.ReadBit() != 0;

    sps.Direct8x8InferenceFlag = reader.ReadBit() != 0;

    if (reader.ReadBit() != 0) {
      sps.CropLeft = reader.ReadUnsignedExpGolomb();
      sps.CropRight = reader.ReadUnsignedExpGolomb();
      sps.CropTop = reader.ReadUnsignedExpGolomb();
      sps.CropBottom = reader.ReadUnsignedExpGolomb();
    }

    // VUI follows; it does not change reconstructed sample values.
    _RefuseImplausibleGeometry(sps);
    return sps;
  }

  /// <summary>Rejects profile tools that still require a different picture representation/process.</summary>
  internal void RefuseUnsupported() {
    if (this.ChromaFormatIdc != 1)
      throw new NotSupportedException(
        $"This H.264 stream is coded at chroma_format_idc {this.ChromaFormatIdc} "
        + $"({_ChromaFormatName(this.ChromaFormatIdc)}). This decoder currently reconstructs 4:2:0 only.");

    if (this.SeparateColourPlaneFlag)
      throw new NotSupportedException(
        "This H.264 stream sets separate_colour_plane_flag. Separate 4:4:4 colour planes are not implemented.");

    if (this.BitDepthLuma != 8 || this.BitDepthChroma != 8)
      throw new NotSupportedException(
        $"This H.264 stream carries {this.BitDepthLuma}-bit luma and {this.BitDepthChroma}-bit chroma samples. "
        + "The current picture buffers are 8-bit; High 10/4:2:2/4:4:4 remains outside this decoder boundary.");

    if (this.QpPrimeYZeroTransformBypassFlag)
      throw new NotSupportedException(
        "This H.264 stream sets qpprime_y_zero_transform_bypass_flag. Lossless transform bypass is not implemented.");

    // Scaling matrices are retained and used by both 4x4 and 8x8 inverse quantisation; they are no
    // longer a reason to reject ordinary 8-bit 4:2:0 High-profile streams.

    if (!this.FrameMbsOnlyFlag)
      throw new NotSupportedException(
        "This H.264 stream has frame_mbs_only_flag equal to 0, so it uses field coding or MBAFF. "
        + "Interlaced reconstruction is not implemented.");
  }

  private static bool _HasChromaFormat(int profileIdc) => profileIdc is 100 or 110 or 122 or 244 or 44
    or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135 or 144;

  private static string _ChromaFormatName(int idc) => idc switch {
    0 => "monochrome",
    1 => "4:2:0",
    2 => "4:2:2",
    3 => "4:4:4",
    _ => "reserved",
  };

  private static int _CropUnitX(H264SequenceParameterSet sps) => sps.ChromaArrayType == 0 ? 1
    : sps.ChromaFormatIdc == 3 ? 1 : 2;

  private static int _CropUnitY(H264SequenceParameterSet sps) {
    var subHeight = sps.ChromaArrayType is 0 or 2 or 3 ? 1 : 2;
    return subHeight * (sps.FrameMbsOnlyFlag ? 1 : 2);
  }

  private static void _RefuseImplausibleGeometry(H264SequenceParameterSet sps) {
    const int MAX_MACROBLOCKS = 139_264;
    var macroblocks = (long)sps.PicWidthInMbs * sps.FrameHeightInMbs;
    if (macroblocks > MAX_MACROBLOCKS)
      throw new InvalidDataException(
        $"This H.264 SPS states {macroblocks} macroblocks, more than the {MAX_MACROBLOCKS} allowed at any level.");

    if (sps.DisplayWidth <= 0 || sps.DisplayHeight <= 0)
      throw new InvalidDataException(
        $"This H.264 SPS crops {sps.CodedWidth}x{sps.CodedHeight} to {sps.DisplayWidth}x{sps.DisplayHeight}, which is not a picture.");
  }
}
