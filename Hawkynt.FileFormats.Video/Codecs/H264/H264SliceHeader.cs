using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>The slice types of ITU-T H.264, Table 7-6, reduced to the five distinct kinds.</summary>
internal enum H264SliceType {

  /// <summary>Predicted from earlier pictures, one list.</summary>
  P = 0,

  /// <summary>Predicted from pictures either side, two lists.</summary>
  B = 1,

  /// <summary>Coded without reference to any other picture.</summary>
  I = 2,

  /// <summary>Switching predicted, an Extended profile slice for stream switching.</summary>
  SP = 3,

  /// <summary>Switching intra, likewise.</summary>
  SI = 4,
}

/// <summary>One reference picture list modification instruction (H.264, clause 7.3.3.1).</summary>
internal readonly record struct H264ListModification(int Idc, int Value);

/// <summary>One memory management control operation (H.264, clause 7.3.3.3).</summary>
internal readonly record struct H264MarkingOperation(int Operation, int First, int Second);

/// <summary>
/// The header every slice begins with: which picture it belongs to, where in it, and how it is coded
/// (ITU-T H.264, clause 7.3.3).
/// </summary>
internal sealed class H264SliceHeader {

  internal int FirstMbInSlice { get; private set; }

  /// <summary>The slice type proper, with Table 7-6's second set of values folded onto the first.</summary>
  internal H264SliceType SliceType { get; private set; }

  internal H264PictureParameterSet Pps { get; private set; } = null!;

  internal H264SequenceParameterSet Sps { get; private set; } = null!;

  internal int FrameNum { get; private set; }

  internal bool IdrPicFlag { get; private set; }

  internal int IdrPicId { get; private set; }

  internal int RedundantPicCnt { get; private set; }

  /// <summary>How many entries of reference picture list 0 this slice may index.</summary>
  internal int NumRefIdxL0Active { get; private set; }

  internal IReadOnlyList<H264ListModification> ListModificationsL0 { get; private set; } = [];

  internal bool LongTermReferenceFlag { get; private set; }

  internal bool AdaptiveRefPicMarkingModeFlag { get; private set; }

  internal IReadOnlyList<H264MarkingOperation> MarkingOperations { get; private set; } = [];

  /// <summary>The quantisation parameter this slice starts at — clause 7.4.3, SliceQPY.</summary>
  internal int SliceQpY { get; private set; }

  /// <summary>
  /// 0 to filter every edge, 1 to filter none, 2 to filter all but the edges between slices
  /// (clause 7.4.3).
  /// </summary>
  internal int DisableDeblockingFilterIdc { get; private set; }

  /// <summary>FilterOffsetA — twice <c>slice_alpha_c0_offset_div2</c> (clause 7.4.3).</summary>
  internal int FilterOffsetA { get; private set; }

  internal int FilterOffsetB { get; private set; }

  /// <summary>Whether the slice's <c>nal_ref_idc</c> was non-zero, so the picture is a reference.</summary>
  internal bool IsReference { get; private set; }

  internal bool IsIntra => this.SliceType is H264SliceType.I or H264SliceType.SI;

  /// <summary>
  /// Reads a slice header, refusing a stream this decoder cannot follow as soon as the field saying
  /// so has been read.
  /// </summary>
  /// <remarks>
  /// The refusals are interleaved with the parse rather than done after it, because the syntax that
  /// follows an unsupported field cannot be read. A B slice's header has a
  /// <c>direct_spatial_mv_pred_flag</c> in it that a P slice's has not, and reading a B slice's
  /// header as though the flag were absent puts every field after it one bit out — so the refusal has
  /// to come before the parse reaches it, not after the whole header has been misread.
  /// </remarks>
  internal static H264SliceHeader Parse(
    ref H264BitReader reader,
    H264NalUnit nal,
    IReadOnlyDictionary<int, H264SequenceParameterSet> sequenceSets,
    IReadOnlyDictionary<int, H264PictureParameterSet> pictureSets) {
    var header = new H264SliceHeader {
      FirstMbInSlice = reader.ReadUnsignedExpGolomb(),
      IdrPicFlag = nal.IsIdr,
      IsReference = nal.RefIdc != 0,
    };

    var sliceType = reader.ReadUnsignedExpGolomb();
    if (sliceType > 9)
      throw new InvalidDataException(
        $"This H.264 slice states slice_type {sliceType}. H.264, Table 7-6 defines 0 to 9 only.");

    // Table 7-6 gives each type twice: the values 5 to 9 repeat 0 to 4 and additionally promise that
    // every slice of the picture has that type. Nothing here needs the promise, so the two halves are
    // folded together.
    header.SliceType = (H264SliceType)(sliceType % 5);

    // Before the parameter sets, because the slice type is the more fundamental obstacle and it is
    // already known: it is the second field of the header, where the parameter sets are the third.
    _RefuseSliceType(header.SliceType);

    var ppsId = reader.ReadUnsignedExpGolomb();
    if (!pictureSets.TryGetValue(ppsId, out var pps))
      throw new InvalidDataException(
        $"This H.264 slice refers to picture parameter set {ppsId}, which has not been seen in this stream. "
        + "Decoding must begin at a point where the parameter sets a slice names have already been transmitted.");

    if (!sequenceSets.TryGetValue(pps.SeqParameterSetId, out var sps))
      throw new InvalidDataException(
        $"H.264 picture parameter set {ppsId} refers to sequence parameter set {pps.SeqParameterSetId}, which has "
        + "not been seen in this stream.");

    // Before anything whose position depends on them is read.
    sps.RefuseUnsupported();
    pps.RefuseUnsupported();

    header.Pps = pps;
    header.Sps = sps;

    if (sps.SeparateColourPlaneFlag)
      reader.Skip(2); // colour_plane_id, unreachable while separate_colour_plane_flag is refused

    header.FrameNum = reader.ReadBits(sps.Log2MaxFrameNum);

    // field_pic_flag is absent while frame_mbs_only_flag is set, which the sequence parameter set's
    // refusal has already guaranteed. So every picture here is a frame.

    if (header.IdrPicFlag)
      header.IdrPicId = reader.ReadUnsignedExpGolomb();

    // The picture order count, which is stepped over rather than computed. It exists to put pictures
    // back into display order after bidirectional prediction has coded them out of it, and this
    // decoder refuses B slices — so for every stream it accepts, decoding order is display order and
    // the count decides nothing. The bits still have to be consumed exactly.
    switch (sps.PicOrderCntType) {
      case 0:
        reader.Skip(sps.Log2MaxPicOrderCntLsb); // pic_order_cnt_lsb
        if (pps.BottomFieldPicOrderInFramePresentFlag)
          reader.ReadSignedExpGolomb(); // delta_pic_order_cnt_bottom

        break;

      case 1 when !sps.DeltaPicOrderAlwaysZeroFlag:
        reader.ReadSignedExpGolomb(); // delta_pic_order_cnt[0]
        if (pps.BottomFieldPicOrderInFramePresentFlag)
          reader.ReadSignedExpGolomb(); // delta_pic_order_cnt[1]

        break;
    }

    if (pps.RedundantPicCntPresentFlag) {
      header.RedundantPicCnt = reader.ReadUnsignedExpGolomb();
      if (header.RedundantPicCnt > 0)
        throw new NotSupportedException(
          $"This H.264 stream carries a redundant coded picture (redundant_pic_cnt {header.RedundantPicCnt}, H.264 "
          + "clause 7.4.3). Redundant slices are a second copy of a picture for use when the primary one is lost, "
          + "and decoding them as though they were primary would decode the picture twice.");
    }

    header.NumRefIdxL0Active = pps.NumRefIdxL0DefaultActive;

    // Only a P slice reaches this: B, SP and SI were refused above, and an I slice has no reference
    // list to size. So there is no second list to override either.
    if (header.SliceType == H264SliceType.P && reader.ReadBit() != 0)
      header.NumRefIdxL0Active = reader.ReadUnsignedExpGolomb() + 1;

    header.ListModificationsL0 = _ReadListModifications(ref reader, header.SliceType);

    // pred_weight_table() is absent while weighted_pred_flag and weighted_bipred_idc are refused.

    if (header.IsReference)
      _ReadReferenceMarking(ref reader, header);

    // cabac_init_idc is absent while entropy_coding_mode_flag is refused.

    header.SliceQpY = pps.PicInitQp + reader.ReadSignedExpGolomb();
    if (header.SliceQpY is < 0 or > 51)
      throw new InvalidDataException(
        $"This H.264 slice states a quantisation parameter of {header.SliceQpY} (pic_init_qp {pps.PicInitQp} plus "
        + "slice_qp_delta). H.264, clause 7.4.3 confines SliceQPY to 0..51 for 8-bit samples.");

    // slice_qs_delta and sp_for_switch_flag are absent while SP and SI slices are refused.

    if (pps.DeblockingFilterControlPresentFlag) {
      header.DisableDeblockingFilterIdc = reader.ReadUnsignedExpGolomb();
      if (header.DisableDeblockingFilterIdc > 2)
        throw new InvalidDataException(
          $"This H.264 slice states disable_deblocking_filter_idc {header.DisableDeblockingFilterIdc}. H.264, "
          + "clause 7.4.3 defines 0, 1 and 2 only.");

      if (header.DisableDeblockingFilterIdc != 1) {
        header.FilterOffsetA = reader.ReadSignedExpGolomb() << 1;
        header.FilterOffsetB = reader.ReadSignedExpGolomb() << 1;
      }
    }

    // slice_group_change_cycle is absent while flexible macroblock ordering is refused.

    return header;
  }

  private static void _RefuseSliceType(H264SliceType type) {
    switch (type) {
      case H264SliceType.B:
        throw new NotSupportedException(
          "This H.264 stream contains a B slice, which is predicted from pictures both before and after it in "
          + "display order (H.264, clause 8.4.1.2). Bidirectional prediction is not implemented; this decoder reads "
          + "I and P slices. Re-encoding with no B pictures — x264's bframes=0, or -profile:v baseline — produces "
          + "a stream this decoder reads.");

      case H264SliceType.SP:
      case H264SliceType.SI:
        throw new NotSupportedException(
          $"This H.264 stream contains an {type} slice, an Extended profile switching slice (H.264, clause 8.6). "
          + "Switching slices are not implemented.");
    }
  }

  /// <summary>Reads <c>ref_pic_list_modification()</c> — clause 7.3.3.1.</summary>
  private static IReadOnlyList<H264ListModification> _ReadListModifications(ref H264BitReader reader, H264SliceType type) {
    if (type is H264SliceType.I or H264SliceType.SI)
      return [];

    if (reader.ReadBit() == 0)
      return [];

    var modifications = new List<H264ListModification>();
    while (true) {
      var idc = reader.ReadUnsignedExpGolomb();
      if (idc == 3)
        break;

      if (idc > 3)
        throw new InvalidDataException(
          $"This H.264 slice states modification_of_pic_nums_idc {idc}. H.264, Table 7-7 defines 0 to 3 only.");

      modifications.Add(new(idc, reader.ReadUnsignedExpGolomb()));

      // A conforming stream ends the loop with idc 3, and every entry moves one picture to the front
      // of the list. More entries than the list can hold means the terminator was missed and the
      // reader is walking through slice data as though it were a header.
      if (modifications.Count > 64)
        throw new InvalidDataException(
          "An H.264 reference picture list modification ran past 64 entries without its terminating "
          + "modification_of_pic_nums_idc of 3. The slice header is being read at the wrong bit position.");
    }

    return modifications;
  }

  /// <summary>Reads <c>dec_ref_pic_marking()</c> — clause 7.3.3.3.</summary>
  private static void _ReadReferenceMarking(ref H264BitReader reader, H264SliceHeader header) {
    if (header.IdrPicFlag) {
      // no_output_of_prior_pics_flag asks a player to drop what it has not shown yet, which is a
      // decision about a display this decoder does not have.
      reader.ReadBit();
      header.LongTermReferenceFlag = reader.ReadBit() != 0;
      return;
    }

    header.AdaptiveRefPicMarkingModeFlag = reader.ReadBit() != 0;
    if (!header.AdaptiveRefPicMarkingModeFlag)
      return;

    var operations = new List<H264MarkingOperation>();
    while (true) {
      var operation = reader.ReadUnsignedExpGolomb();
      if (operation == 0)
        break;

      if (operation > 6)
        throw new InvalidDataException(
          $"This H.264 slice states memory_management_control_operation {operation}. H.264, Table 7-9 defines 0 to "
          + "6 only.");

      var first = operation is 1 or 2 or 3 or 4 ? reader.ReadUnsignedExpGolomb() : 0;
      var second = operation is 3 or 6 ? reader.ReadUnsignedExpGolomb() : 0;
      operations.Add(new(operation, first, second));

      if (operations.Count > 64)
        throw new InvalidDataException(
          "An H.264 reference picture marking ran past 64 operations without its terminating "
          + "memory_management_control_operation of 0. The slice header is being read at the wrong bit position.");
    }

    header.MarkingOperations = operations;
  }
}
