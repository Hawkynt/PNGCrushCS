using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// A sequence parameter set: the geometry and the coding decisions every picture of a coded video
/// sequence shares (ITU-T H.264, clause 7.3.2.1.1).
/// </summary>
/// <remarks>
/// Parsed in full rather than only as far as this decoder needs, because the fields are written one
/// after another with no lengths and a field skipped is every field after it read at the wrong
/// offset. So <c>pic_order_cnt_type</c>'s three branches and the scaling matrix loop are all walked
/// even where what they hold is then refused: reading them and refusing is a message naming what is
/// unsupported, where stopping early is a picture size read out of the middle of a scaling list.
/// <para/>
/// The refusals live in <see cref="RefuseUnsupported"/> and not in the parse. A stream may well carry
/// a High profile parameter set that no slice ever references — MP4 files written by two-pass
/// encoders do — and refusing at the parse would turn a decodable stream into an error.
/// </remarks>
internal sealed class H264SequenceParameterSet {

  internal int Id { get; private set; }

  /// <summary>
  /// <c>chroma_format_idc</c>: 0 monochrome, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4 (Table 6-1).
  /// </summary>
  /// <remarks>
  /// Inferred as 1 and not read for the profiles whose parameter set has no such field, which is
  /// what clause 7.4.2.1.1 says those profiles mean: Baseline, Main and Extended are 4:2:0 only.
  /// </remarks>
  internal int ChromaFormatIdc { get; private set; } = 1;

  internal bool SeparateColourPlaneFlag { get; private set; }

  internal int BitDepthLuma { get; private set; } = 8;

  internal int BitDepthChroma { get; private set; } = 8;

  internal bool QpPrimeYZeroTransformBypassFlag { get; private set; }

  internal bool SeqScalingMatrixPresentFlag { get; private set; }

  /// <summary>The bits <c>frame_num</c> occupies in a slice header.</summary>
  internal int Log2MaxFrameNum { get; private set; }

  internal int MaxFrameNum => 1 << this.Log2MaxFrameNum;

  internal int PicOrderCntType { get; private set; }

  internal int Log2MaxPicOrderCntLsb { get; private set; }

  internal bool DeltaPicOrderAlwaysZeroFlag { get; private set; }

  /// <summary>How many frames the decoded picture buffer holds as references.</summary>
  internal int MaxNumRefFrames { get; private set; }

  /// <summary>
  /// Whether the encoder was allowed to leave holes in the reference frame numbering
  /// (clause 8.2.5.2), which decides what a jump in <c>frame_num</c> means.
  /// </summary>
  internal bool GapsInFrameNumValueAllowedFlag { get; private set; }

  internal int PicWidthInMbs { get; private set; }

  internal int PicHeightInMapUnits { get; private set; }

  internal bool FrameMbsOnlyFlag { get; private set; }

  /// <summary>
  /// Whether an interlaced sequence codes macroblock pairs that choose frame or field each, rather
  /// than whole field pictures — which is the difference between the two kinds of interlacing.
  /// </summary>
  internal bool MbAdaptiveFrameFieldFlag { get; private set; }

  internal int CropLeft { get; private set; }

  internal int CropRight { get; private set; }

  internal int CropTop { get; private set; }

  internal int CropBottom { get; private set; }

  /// <summary>Macroblock rows in a coded frame — clause 7.4.2.1.1, FrameHeightInMbs.</summary>
  internal int FrameHeightInMbs => (this.FrameMbsOnlyFlag ? 1 : 2) * this.PicHeightInMapUnits;

  /// <summary>The coded luma width, which is a whole number of macroblocks.</summary>
  internal int CodedWidth => this.PicWidthInMbs * 16;

  /// <summary>The coded luma height, likewise.</summary>
  internal int CodedHeight => this.FrameHeightInMbs * 16;

  /// <summary>
  /// <c>ChromaArrayType</c> — clause 7.4.2.1.1: the chroma format as the decoding process sees it,
  /// which is zero when the three colour planes are coded as separate monochrome pictures.
  /// </summary>
  internal int ChromaArrayType => this.SeparateColourPlaneFlag ? 0 : this.ChromaFormatIdc;

  /// <summary>The first displayed column, in luma samples (clause 7.4.2.1.1).</summary>
  internal int CropOffsetX => _CropUnitX(this) * this.CropLeft;

  /// <summary>The first displayed row, in luma samples.</summary>
  internal int CropOffsetY => _CropUnitY(this) * this.CropTop;

  /// <summary>The displayed width after the frame cropping offsets are applied (clause 7.4.2.1.1).</summary>
  internal int DisplayWidth => this.CodedWidth - _CropUnitX(this) * (this.CropLeft + this.CropRight);

  /// <summary>The displayed height after cropping.</summary>
  internal int DisplayHeight => this.CodedHeight - _CropUnitY(this) * (this.CropTop + this.CropBottom);

  /// <summary>Whether two parameter sets describe pictures of the same shape.</summary>
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

    // The six constraint_set flags, reserved_zero_2bits and level_idc. The profile decides which
    // fields the rest of this parameter set has and so is kept; the level bounds the picture size
    // and the bit rate, neither of which this decoder enforces, so it is read and dropped rather
    // than stored where it would look consulted.
    reader.Skip(16);

    var sps = new H264SequenceParameterSet { Id = reader.ReadUnsignedExpGolomb() };

    // Only the profiles listed in clause 7.3.2.1.1 write the chroma format, the sample depths and
    // the scaling matrices. For every other profile they are not absent-and-defaulted so much as
    // fixed by the profile itself, which is why the defaults above are the values rather than zero.
    if (_HasChromaFormat(profileIdc)) {
      sps.ChromaFormatIdc = reader.ReadUnsignedExpGolomb();
      if (sps.ChromaFormatIdc == 3)
        sps.SeparateColourPlaneFlag = reader.ReadBit() != 0;

      sps.BitDepthLuma = reader.ReadUnsignedExpGolomb() + 8;
      sps.BitDepthChroma = reader.ReadUnsignedExpGolomb() + 8;
      sps.QpPrimeYZeroTransformBypassFlag = reader.ReadBit() != 0;
      sps.SeqScalingMatrixPresentFlag = reader.ReadBit() != 0;
      if (sps.SeqScalingMatrixPresentFlag)
        _SkipScalingMatrix(ref reader, sps.ChromaFormatIdc != 3 ? 8 : 12);
    }

    sps.Log2MaxFrameNum = reader.ReadUnsignedExpGolomb() + 4;
    sps.PicOrderCntType = reader.ReadUnsignedExpGolomb();

    switch (sps.PicOrderCntType) {
      case 0:
        sps.Log2MaxPicOrderCntLsb = reader.ReadUnsignedExpGolomb() + 4;
        break;

      case 1:
        // The cycle of offsets a type 1 picture order count is built from. Only its length matters
        // here — the offsets themselves feed the display ordering, which this decoder does not
        // compute because it refuses the slices that reorder anything — but every one of them has to
        // be stepped over or the picture size is read out of the middle of them.
        sps.DeltaPicOrderAlwaysZeroFlag = reader.ReadBit() != 0;
        reader.ReadSignedExpGolomb(); // offset_for_non_ref_pic
        reader.ReadSignedExpGolomb(); // offset_for_top_to_bottom_field
        for (var i = reader.ReadUnsignedExpGolomb(); i > 0; --i)
          reader.ReadSignedExpGolomb(); // offset_for_ref_frame[i]

        break;
    }

    sps.MaxNumRefFrames = reader.ReadUnsignedExpGolomb();
    sps.GapsInFrameNumValueAllowedFlag = reader.ReadBit() != 0;
    sps.PicWidthInMbs = reader.ReadUnsignedExpGolomb() + 1;
    sps.PicHeightInMapUnits = reader.ReadUnsignedExpGolomb() + 1;
    sps.FrameMbsOnlyFlag = reader.ReadBit() != 0;
    if (!sps.FrameMbsOnlyFlag)
      sps.MbAdaptiveFrameFieldFlag = reader.ReadBit() != 0;

    reader.ReadBit(); // direct_8x8_inference_flag, which only a B slice's direct mode consults

    if (reader.ReadBit() != 0) {
      sps.CropLeft = reader.ReadUnsignedExpGolomb();
      sps.CropRight = reader.ReadUnsignedExpGolomb();
      sps.CropTop = reader.ReadUnsignedExpGolomb();
      sps.CropBottom = reader.ReadUnsignedExpGolomb();
    }

    // vui_parameters() follows and holds nothing that changes a sample: aspect ratio, timing,
    // and buffering. The container states the frame rate this library reports, so the VUI is left
    // unread rather than parsed and discarded.

    _RefuseImplausibleGeometry(sps);
    return sps;
  }

  /// <summary>
  /// Refuses, by name, a sequence this decoder cannot decode.
  /// </summary>
  /// <remarks>
  /// Called when a slice first refers to this parameter set and not when it is parsed, so that a
  /// stream carrying a parameter set nothing uses still decodes.
  /// </remarks>
  internal void RefuseUnsupported() {
    if (this.ChromaFormatIdc != 1)
      throw new NotSupportedException(
        $"This H.264 stream is coded at chroma_format_idc {this.ChromaFormatIdc} "
        + $"({_ChromaFormatName(this.ChromaFormatIdc)}), H.264 Table 6-1. This decoder implements 4:2:0 "
        + "(chroma_format_idc 1) only.");

    if (this.SeparateColourPlaneFlag)
      throw new NotSupportedException(
        "This H.264 stream sets separate_colour_plane_flag, which codes the three colour planes as separate "
        + "monochrome pictures (H.264, clause 7.4.2.1.1). That is a 4:4:4 profile feature and is not implemented.");

    if (this.BitDepthLuma != 8 || this.BitDepthChroma != 8)
      throw new NotSupportedException(
        $"This H.264 stream carries {this.BitDepthLuma}-bit luma and {this.BitDepthChroma}-bit chroma samples "
        + "(H.264, bit_depth_luma_minus8 and bit_depth_chroma_minus8). This decoder implements 8-bit samples only.");

    if (this.QpPrimeYZeroTransformBypassFlag)
      throw new NotSupportedException(
        "This H.264 stream sets qpprime_y_zero_transform_bypass_flag, which codes macroblocks at QP 0 losslessly "
        + "without the transform (H.264, clause 8.5). Transform bypass is not implemented.");

    if (this.SeqScalingMatrixPresentFlag)
      throw new NotSupportedException(
        "This H.264 sequence parameter set carries scaling matrices (seq_scaling_matrix_present_flag, H.264 clause "
        + "7.3.2.1.1). Non-flat quantiser weighting is a High profile feature and is not implemented; decoding it "
        + "with the flat matrices would dequantise every coefficient by the wrong factor.");

    if (!this.FrameMbsOnlyFlag)
      throw new NotSupportedException(
        "This H.264 stream has frame_mbs_only_flag equal to 0, so it codes "
        + (this.MbAdaptiveFrameFieldFlag
          ? "macroblock pairs that are each either frame or field coded (MBAFF)"
          : "field pictures")
        + " (H.264, clause 7.4.2.1.1). Interlaced coding is not implemented; this decoder reads progressive "
        + "frames only.");

    if (this.PicOrderCntType > 2)
      throw new InvalidDataException(
        $"This H.264 sequence states pic_order_cnt_type {this.PicOrderCntType}. H.264, clause 7.4.2.1.1 defines "
        + "0, 1 and 2 only.");
  }

  private static bool _HasChromaFormat(int profileIdc) => profileIdc is 100 or 110 or 122 or 244 or 44
    or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135;

  private static string _ChromaFormatName(int idc) => idc switch {
    0 => "monochrome",
    2 => "4:2:2",
    3 => "4:4:4",
    _ => "reserved",
  };

  /// <summary>The horizontal unit the cropping offsets are counted in — clause 7.4.2.1.1, Table 6-1.</summary>
  private static int _CropUnitX(H264SequenceParameterSet sps) => sps.ChromaArrayType == 0 ? 1
    : sps.ChromaFormatIdc == 3 ? 1 : 2;

  /// <summary>The vertical unit, which doubles for a sequence that may code fields.</summary>
  private static int _CropUnitY(H264SequenceParameterSet sps) {
    var subHeight = sps.ChromaArrayType is 0 or 2 or 3 ? 1 : 2;
    return subHeight * (sps.FrameMbsOnlyFlag ? 1 : 2);
  }

  /// <summary>
  /// Steps over the scaling lists so that the fields after them are read at the right offset
  /// (clause 7.3.2.1.1.1).
  /// </summary>
  /// <remarks>
  /// Stepped over rather than kept because a stream carrying them is refused: what is needed here is
  /// the bit position after the loop, and the values would only be stored to be thrown away. The walk
  /// still has to be exact — <c>nextScale</c> reaching zero ends a list early, and a walk that read a
  /// fixed count of codes would leave the reader inside the next list.
  /// </remarks>
  private static void _SkipScalingMatrix(ref H264BitReader reader, int listCount) {
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

  private static void _RefuseImplausibleGeometry(H264SequenceParameterSet sps) {
    // A picture larger than any level allows is a parameter set read at the wrong bit position, and
    // allocating for it before finding that out is how a malformed file becomes an out-of-memory.
    // Level 6.2, the largest the standard defines, allows 139 264 macroblocks (H.264, Table A-1).
    const int MAX_MACROBLOCKS = 139_264;
    var macroblocks = (long)sps.PicWidthInMbs * sps.FrameHeightInMbs;
    if (macroblocks > MAX_MACROBLOCKS)
      throw new InvalidDataException(
        $"This H.264 sequence parameter set states a picture of {sps.PicWidthInMbs}x{sps.FrameHeightInMbs} "
        + $"macroblocks ({macroblocks}), more than the {MAX_MACROBLOCKS} that H.264 Table A-1 allows at any level. "
        + "These bytes are not a sequence parameter set.");

    if (sps.DisplayWidth <= 0 || sps.DisplayHeight <= 0)
      throw new InvalidDataException(
        $"This H.264 sequence parameter set crops a {sps.CodedWidth}x{sps.CodedHeight} coded picture down to "
        + $"{sps.DisplayWidth}x{sps.DisplayHeight}, which is not a picture. The frame cropping offsets are wrong.");
  }
}
