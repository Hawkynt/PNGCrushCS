using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264PictureOrderCountTests {

  [Test]
  public void Type0Mmco5StateSurvivesAnInterveningNonReferencePicture() {
    var stream = _Type0SequenceParameterSet(new H264TestStream())
      .PictureParameterSet();
    _IdrSliceHeader(stream, pocLsb: 0).EndNal(5, 3);
    _IntraSliceHeader(stream, frameNum: 1, pocLsb: 6, reference: true, mmco5: true).EndNal(1, 3);
    _IntraSliceHeader(stream, frameNum: 0, pocLsb: 2, reference: false).EndNal(1, 0);
    _IntraSliceHeader(stream, frameNum: 1, pocLsb: 12, reference: true).EndNal(1, 3);

    var (poc, headers) = _Parse(stream.ToArray());
    Assert.That(_Finish(poc, headers[0]).PicOrderCnt, Is.EqualTo(0));

    var mmco5 = poc.Derive(headers[1]);
    Assert.That(mmco5.PicOrderCnt, Is.EqualTo(6));
    Assert.That(poc.FinishPicture(headers[1], mmco5).PicOrderCnt, Is.EqualTo(0));

    Assert.That(_Finish(poc, headers[2]).PicOrderCnt, Is.EqualTo(2));
    Assert.That(_Finish(poc, headers[3]).PicOrderCnt, Is.EqualTo(-4));
  }

  [Test]
  public void Type0UsesTheNormalizedTopFieldOrderCountWithoutReducingItModuloMaxLsb() {
    var stream = _Type0SequenceParameterSet(new H264TestStream());
    _PictureParameterSetWithBottomFieldOrderCount(stream);
    _IdrSliceHeader(stream, pocLsb: 0, deltaPicOrderCntBottom: 0).EndNal(5, 3);
    _IntraSliceHeader(
      stream,
      frameNum: 1,
      pocLsb: 6,
      deltaPicOrderCntBottom: -20,
      reference: true,
      mmco5: true).EndNal(1, 3);
    _IntraSliceHeader(
      stream,
      frameNum: 0,
      pocLsb: 12,
      deltaPicOrderCntBottom: 0,
      reference: true).EndNal(1, 3);

    var (poc, headers) = _Parse(stream.ToArray());
    _Finish(poc, headers[0]);

    var mmco5 = poc.Derive(headers[1]);
    Assert.That(mmco5.TopFieldOrderCnt, Is.EqualTo(6));
    Assert.That(mmco5.BottomFieldOrderCnt, Is.EqualTo(-14));
    var normalized = poc.FinishPicture(headers[1], mmco5);
    Assert.That(normalized.TopFieldOrderCnt, Is.EqualTo(20));
    Assert.That(normalized.BottomFieldOrderCnt, Is.EqualTo(0));

    Assert.That(_Finish(poc, headers[2]).PicOrderCnt, Is.EqualTo(28));
  }

  [Test]
  public void Type2Mmco5StateBelongsToTheImmediatelyPreviousPicture() {
    var stream = new H264TestStream()
      .SequenceParameterSet(maxRefFrames: 2, profileIdc: 77)
      .PictureParameterSet();
    _IdrSliceHeader(stream).EndNal(5, 3);
    _IntraSliceHeader(stream, frameNum: 15, reference: true, mmco5: true).EndNal(1, 3);
    _IntraSliceHeader(stream, frameNum: 15, reference: false).EndNal(1, 0);
    _IntraSliceHeader(stream, frameNum: 0, reference: true).EndNal(1, 3);

    var (poc, headers) = _Parse(stream.ToArray());
    Assert.That(_Finish(poc, headers[0]).PicOrderCnt, Is.EqualTo(0));

    var mmco5 = poc.Derive(headers[1]);
    Assert.That(mmco5.PicOrderCnt, Is.EqualTo(30));
    Assert.That(poc.FinishPicture(headers[1], mmco5).PicOrderCnt, Is.EqualTo(0));

    Assert.That(_Finish(poc, headers[2]).PicOrderCnt, Is.EqualTo(29));
    Assert.That(_Finish(poc, headers[3]).PicOrderCnt, Is.EqualTo(32));
  }

  private static H264PictureOrderCount.Result _Finish(H264PictureOrderCount poc, H264SliceHeader header) {
    var result = poc.Derive(header);
    return poc.FinishPicture(header, result);
  }

  private static (H264PictureOrderCount Poc, H264SliceHeader[] Headers) _Parse(byte[] stream) {
    var nals = H264NalReader.SplitAnnexB(stream).ToArray();
    var sps = H264SequenceParameterSet.Parse(nals[0].Payload);
    var pps = H264PictureParameterSet.Parse(nals[1].Payload);
    var sequenceSets = new Dictionary<int, H264SequenceParameterSet> { [sps.Id] = sps };
    var pictureSets = new Dictionary<int, H264PictureParameterSet> { [pps.Id] = pps };
    var headers = new H264SliceHeader[nals.Length - 2];
    for (var i = 2; i < nals.Length; ++i) {
      var reader = new H264BitReader(nals[i].Payload);
      headers[i - 2] = H264SliceHeader.Parse(ref reader, nals[i], sequenceSets, pictureSets);
    }

    return (new(), headers);
  }

  private static H264TestStream _Type0SequenceParameterSet(H264TestStream stream) {
    stream.Bits(77, 8); // Main profile
    stream.Bits(0, 8); // constraint flags and reserved bits
    stream.Bits(10, 8); // level_idc
    stream.Unsigned(0); // seq_parameter_set_id
    stream.Unsigned(0); // log2_max_frame_num_minus4: four frame_num bits
    stream.Unsigned(0); // pic_order_cnt_type
    stream.Unsigned(0); // log2_max_pic_order_cnt_lsb_minus4: four POC-LSB bits
    stream.Unsigned(2); // max_num_ref_frames
    stream.Bits(0, 1); // gaps_in_frame_num_value_allowed_flag
    stream.Unsigned(0); // pic_width_in_mbs_minus1
    stream.Unsigned(0); // pic_height_in_map_units_minus1
    stream.Bits(1, 1); // frame_mbs_only_flag
    stream.Bits(1, 1); // direct_8x8_inference_flag
    stream.Bits(0, 1); // frame_cropping_flag
    stream.Bits(0, 1); // vui_parameters_present_flag
    return stream.EndNal(7, 3);
  }

  private static H264TestStream _PictureParameterSetWithBottomFieldOrderCount(H264TestStream stream) {
    stream.Unsigned(0); // pic_parameter_set_id
    stream.Unsigned(0); // seq_parameter_set_id
    stream.Bits(0, 1); // entropy_coding_mode_flag
    stream.Bits(1, 1); // bottom_field_pic_order_in_frame_present_flag
    stream.Unsigned(0); // num_slice_groups_minus1
    stream.Unsigned(0); // num_ref_idx_l0_default_active_minus1
    stream.Unsigned(0); // num_ref_idx_l1_default_active_minus1
    stream.Bits(0, 1); // weighted_pred_flag
    stream.Bits(0, 2); // weighted_bipred_idc
    stream.Signed(0); // pic_init_qp_minus26
    stream.Signed(0); // pic_init_qs_minus26
    stream.Signed(0); // chroma_qp_index_offset
    stream.Bits(0, 1); // deblocking_filter_control_present_flag
    stream.Bits(0, 1); // constrained_intra_pred_flag
    stream.Bits(0, 1); // redundant_pic_cnt_present_flag
    return stream.EndNal(8, 3);
  }

  private static H264TestStream _IdrSliceHeader(
    H264TestStream stream,
    int? pocLsb = null,
    int? deltaPicOrderCntBottom = null) {
    stream.Unsigned(0); // first_mb_in_slice
    stream.Unsigned(7); // I slice; all slices in this picture are I
    stream.Unsigned(0); // pic_parameter_set_id
    stream.Bits(0, 4); // frame_num
    stream.Unsigned(0); // idr_pic_id
    if (pocLsb.HasValue)
      stream.Bits(pocLsb.Value, 4);
    if (deltaPicOrderCntBottom.HasValue)
      stream.Signed(deltaPicOrderCntBottom.Value);
    stream.Bits(0, 1); // no_output_of_prior_pics_flag
    stream.Bits(0, 1); // long_term_reference_flag
    stream.Signed(0); // slice_qp_delta
    return stream;
  }

  private static H264TestStream _IntraSliceHeader(
    H264TestStream stream,
    int frameNum,
    int? pocLsb = null,
    int? deltaPicOrderCntBottom = null,
    bool reference = true,
    bool mmco5 = false) {
    stream.Unsigned(0); // first_mb_in_slice
    stream.Unsigned(7); // I slice; all slices in this picture are I
    stream.Unsigned(0); // pic_parameter_set_id
    stream.Bits(frameNum, 4);
    if (pocLsb.HasValue)
      stream.Bits(pocLsb.Value, 4);
    if (deltaPicOrderCntBottom.HasValue)
      stream.Signed(deltaPicOrderCntBottom.Value);

    if (reference) {
      stream.Bits(mmco5 ? 1 : 0, 1); // adaptive_ref_pic_marking_mode_flag
      if (mmco5) {
        stream.Unsigned(5); // memory_management_control_operation: reset
        stream.Unsigned(0); // terminate marking operations
      }
    }

    stream.Signed(0); // slice_qp_delta
    return stream;
  }
}
