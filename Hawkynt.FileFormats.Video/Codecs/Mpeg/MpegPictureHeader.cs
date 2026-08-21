using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// The picture header of ISO/IEC 11172-2, 2.4.2.5 and ISO/IEC 13818-2, 6.2.3, together with the
/// picture coding extension that amends it.
/// </summary>
/// <remarks>
/// The two standards print the same header, and in MPEG-2 most of it is a formality: the f_codes
/// there are required to be all ones and the full-pel flags to be zero, because the values that are
/// actually used arrive one start code later in the picture coding extension. So this is parsed once
/// and then amended, the same way the sequence header is — a stream that has the extension replaces
/// what the header said, and a stream that does not keeps it.
/// <para/>
/// Everything here is read before the first slice and none of it changes until the next picture,
/// which is why it is a value handed to the picture decoder rather than a set of fields the picture
/// decoder parses for itself. The decoder that parses its own header cannot be handed a header to
/// test it with.
/// </remarks>
internal sealed class MpegPictureHeader {

  /// <summary>Which of I, P, B, D this picture is.</summary>
  internal int CodingType { get; private set; }

  internal int ForwardHorizontalFCode { get; private set; } = 1;

  internal int ForwardVerticalFCode { get; private set; } = 1;

  internal int BackwardHorizontalFCode { get; private set; } = 1;

  internal int BackwardVerticalFCode { get; private set; } = 1;

  /// <summary>full_pel_forward_vector: the forward vectors count whole pixels (11172-2 only).</summary>
  internal bool ForwardIsFullPel { get; private set; }

  internal bool BackwardIsFullPel { get; private set; }

  /// <summary>intra_dc_precision: 0 for eight bits through 3 for eleven (13818-2, Table 7-2).</summary>
  internal int IntraDcPrecision { get; private set; }

  /// <summary>picture_structure: 1 top field, 2 bottom field, 3 a whole frame (13818-2, Table 6-14).</summary>
  internal int PictureStructure { get; private set; } = 3;

  /// <summary>
  /// frame_pred_frame_dct: every macroblock of this picture is frame predicted and frame
  /// transformed, so neither <c>frame_motion_type</c> nor <c>dct_type</c> is coded.
  /// </summary>
  internal bool FramePredFrameDct { get; private set; } = true;

  /// <summary>concealment_motion_vectors: intra macroblocks carry a vector for error concealment.</summary>
  internal bool ConcealmentMotionVectors { get; private set; }

  /// <summary>q_scale_type: the quantiser scale code maps through the non-linear column of Table 7-6.</summary>
  internal bool NonLinearQuantiser { get; private set; }

  /// <summary>intra_vlc_format: intra blocks read their coefficients from Table B.15.</summary>
  internal bool IntraVlcFormat { get; private set; }

  /// <summary>alternate_scan: coefficients are scanned by Figure 7-3 rather than Figure 7-2.</summary>
  internal bool AlternateScan { get; private set; }

  /// <summary>Reads a picture header, positioned just past its start code.</summary>
  internal static MpegPictureHeader Parse(ref MpegBitReader reader) {
    var header = new MpegPictureHeader();

    reader.ReadBits(10); // temporal_reference — display order within the group, which the reordering
                         // rule this decoder uses does not need: an anchor is shown when the next
                         // one arrives.
    header.CodingType = reader.ReadBits(3);
    reader.ReadBits(16); // vbv_delay

    if (header.CodingType is MpegPictureDecoder.PredictiveCoded or MpegPictureDecoder.BidirectionallyCoded) {
      header.ForwardIsFullPel = reader.ReadBit() == 1;
      var forward = reader.ReadBits(3);
      _RefuseForbiddenFCode(forward, "forward_f_code");
      header.ForwardHorizontalFCode = header.ForwardVerticalFCode = forward;
    }

    if (header.CodingType == MpegPictureDecoder.BidirectionallyCoded) {
      header.BackwardIsFullPel = reader.ReadBit() == 1;
      var backward = reader.ReadBits(3);
      _RefuseForbiddenFCode(backward, "backward_f_code");
      header.BackwardHorizontalFCode = header.BackwardVerticalFCode = backward;
    }

    // extra_information_picture: bytes nothing in either standard defines, each introduced by a set bit.
    while (reader.NextBits(1) == 1) {
      reader.Skip(1);
      reader.ReadBits(8);
    }

    reader.Skip(1);
    return header;
  }

  /// <summary>
  /// Applies the picture coding extension (ISO/IEC 13818-2, 6.2.3.1), positioned just past its
  /// four-bit identifier.
  /// </summary>
  /// <remarks>
  /// This is where an MPEG-2 picture says nearly everything about itself that changes how it is
  /// read: four separate f_codes instead of one per direction, how finely the intra DC is coded,
  /// whether it is a frame or a single field, whether its macroblocks may be field predicted or
  /// field transformed, which of the two quantiser scales and which of the two coefficient tables
  /// and which of the two scans it uses. A decoder that skipped this extension and carried on would
  /// read the very next slice with the wrong f_code and produce nothing but noise, which is at least
  /// an honest failure — the ones that produce a picture are the fields further down.
  /// </remarks>
  internal void ApplyPictureCodingExtension(ref MpegBitReader reader) {
    this.ForwardHorizontalFCode = reader.ReadBits(4);
    this.ForwardVerticalFCode = reader.ReadBits(4);
    this.BackwardHorizontalFCode = reader.ReadBits(4);
    this.BackwardVerticalFCode = reader.ReadBits(4);

    // MPEG-2 has no full-pel vectors at all; the header's flags are required to be zero and are
    // cleared here rather than trusted, because a stream that set one would otherwise double every
    // vector in the picture.
    this.ForwardIsFullPel = false;
    this.BackwardIsFullPel = false;

    this.IntraDcPrecision = reader.ReadBits(2);
    this.PictureStructure = reader.ReadBits(2);

    reader.ReadBit(); // top_field_first — which field is displayed first, and in a frame picture
                      // decoded here both are reconstructed together, so it changes no sample.

    this.FramePredFrameDct = reader.ReadBit() == 1;
    this.ConcealmentMotionVectors = reader.ReadBit() == 1;
    this.NonLinearQuantiser = reader.ReadBit() == 1;
    this.IntraVlcFormat = reader.ReadBit() == 1;
    this.AlternateScan = reader.ReadBit() == 1;

    reader.ReadBit(); // repeat_first_field — display timing.
    reader.ReadBit(); // chroma_420_type.
    reader.ReadBit(); // progressive_frame.

    if (reader.ReadBit() == 1) {
      // composite_display_flag: how the picture was once a composite analogue signal. Nothing here
      // reconstructs one, but the twenty bits have to be stepped over or the extension ends in the
      // wrong place.
      reader.ReadBit();     // v_axis
      reader.ReadBits(3);   // field_sequence
      reader.ReadBit();     // sub_carrier
      reader.ReadBits(7);   // burst_amplitude
      reader.ReadBits(8);   // sub_carrier_phase
    }

    if (this.PictureStructure == 0)
      throw new InvalidDataException(
        "The MPEG-2 picture coding extension states picture_structure 0, which ISO/IEC 13818-2 Table 6-14 leaves "
        + "reserved.");

    if (this.PictureStructure != 3)
      throw new NotSupportedException(
        $"This MPEG-2 picture is a field picture (picture_structure {this.PictureStructure}, "
        + $"{(this.PictureStructure == 1 ? "top" : "bottom")} field), in which the two fields of a frame are coded as "
        + "two separate pictures with their own headers and their own prediction (ISO/IEC 13818-2, 6.3.10 and 7.6.4). "
        + "This decoder reads frame pictures; field pictures are not implemented.");
  }

  /// <summary>
  /// Refuses the one value the picture header's f_codes may not take.
  /// </summary>
  /// <remarks>
  /// Only zero, and in both standards. MPEG-1 defines the range as 1 to 7 and uses these fields;
  /// MPEG-2 requires them to be all ones and states the f_codes it actually uses in the picture
  /// coding extension, so the value here is a formality — but a zero is forbidden either way, and it
  /// is the value that would make a motion vector's residual a negative number of bits.
  /// </remarks>
  private static void _RefuseForbiddenFCode(int fCode, string field) {
    if (fCode != 0)
      return;

    throw new InvalidDataException(
      $"The MPEG picture header states {field} 0, which both ISO/IEC 11172-2 and ISO/IEC 13818-2 forbid; the range "
      + "is 1 to 7.");
  }
}
