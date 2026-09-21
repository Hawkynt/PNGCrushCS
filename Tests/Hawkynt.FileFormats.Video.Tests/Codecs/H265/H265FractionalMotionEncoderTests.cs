using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H265.Tests;

[TestFixture]
public sealed class H265FractionalMotionEncoderTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 64;
  private const int _QUANTISER = 26;

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void PredictedPictureFindsQuarterSampleMotion() {
    var past = _Texture(phase: 0, poc: 0);
    var source = _MotionPredictedPicture(past, future: null, list: 0, mvX: 1, mvY: 2, poc: 2);

    var encoder = _EncodeAndAssertExactDecode(source, past, future: null);
    var motion = encoder.MotionAt(encoder.BlockIndexAt(16, 16));

    Assert.Multiple(() => {
      Assert.That(motion.PredictL0, Is.True);
      Assert.That(motion.PredictL1, Is.False);
      Assert.That(motion.MvL0X, Is.EqualTo(1), "quarter-sample horizontal motion");
      Assert.That(motion.MvL0Y, Is.EqualTo(2), "half-sample vertical motion");
    });
  }

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void BidirectionalPictureFindsFractionalMotionFromFutureReference() {
    var past = _Texture(phase: 91, poc: 0);
    var future = _Texture(phase: 7, poc: 4);
    var source = _MotionPredictedPicture(past, future, list: 1, mvX: -1, mvY: 3, poc: 2);

    var encoder = _EncodeAndAssertExactDecode(source, past, future);
    var motion = encoder.MotionAt(encoder.BlockIndexAt(16, 16));

    Assert.Multiple(() => {
      Assert.That(motion.PredictL0, Is.False);
      Assert.That(motion.PredictL1, Is.True);
      Assert.That(motion.MvL1X, Is.EqualTo(-1), "negative quarter-sample horizontal motion");
      Assert.That(motion.MvL1Y, Is.EqualTo(3), "three-quarter-sample vertical motion");
    });
  }

  private static H265InterPictureEncoder _EncodeAndAssertExactDecode(
    H265Picture source, H265Picture past, H265Picture? future) {
    var sets = _ParameterSets();
    var type = future == null ? H265SliceType.P : H265SliceType.B;
    var headerBits = _SliceHeader(type, source.PictureOrderCount, past.PictureOrderCount, future?.PictureOrderCount);
    var parsed = _ParseHeader(sets, headerBits);
    var encoder = new H265InterPictureEncoder(
      sets.Sps, sets.Pps, parsed, source, past, future, parsed.SliceQpY);

    var data = encoder.Encode();
    var rbsp = new byte[headerBits.Length + data.Length];
    headerBits.CopyTo(rbsp, 0);
    data.CopyTo(rbsp, headerBits.Length);

    var header = H265SliceHeader.Parse(
      new H265NalUnit(H265NalUnitType.TrailingReference, 0, 0, rbsp, []),
      new Dictionary<int, H265SequenceParameterSet> { [0] = sets.Sps },
      new Dictionary<int, H265PictureParameterSet> { [0] = sets.Pps });

    var decoder = new H265FrameDecoder(sets.Sps, sets.Pps);
    IReadOnlyList<H265Picture>[] lists = future == null ? [[past], []] : [[past], [future]];
    decoder.DecodeSliceSegment(header, lists);

    Assert.Multiple(() => {
      Assert.That(decoder.Picture.Luma, Is.EqualTo(encoder.Reconstruction.Luma), "luminance reconstruction");
      Assert.That(decoder.Picture.Cb, Is.EqualTo(encoder.Reconstruction.Cb), "blue chrominance reconstruction");
      Assert.That(decoder.Picture.Cr, Is.EqualTo(encoder.Reconstruction.Cr), "red chrominance reconstruction");
    });

    return encoder;
  }

  private static H265Picture _MotionPredictedPicture(
    H265Picture past, H265Picture? future, int list, int mvX, int mvY, int poc) {
    var sets = _ParameterSets();
    var type = future == null ? H265SliceType.P : H265SliceType.B;
    var headerBits = _SliceHeader(type, poc, past.PictureOrderCount, future?.PictureOrderCount);
    var parsed = _ParseHeader(sets, headerBits);
    var sink = _Texture(phase: 173, poc);
    var context = new H265InterPictureEncoder(
      sets.Sps, sets.Pps, parsed, sink, past, future, parsed.SliceQpY);

    var motion = H265MotionInfo.None;
    motion.Set(list, true, 0, mvX, mvY);
    H265MotionCompensation.Predict(context, 0, 0, _WIDTH, _HEIGHT, motion);
    return context.Reconstruction;
  }

  private static (H265SequenceParameterSet Sps, H265PictureParameterSet Pps) _ParameterSets() {
    var level = H265PcmStillCodec._SmallestLevelFor(_WIDTH, _HEIGHT);
    var sps = H265PcmStillCodec._BuildSps(_WIDTH, _HEIGHT, _WIDTH, _HEIGHT, level, 1, 4);
    return (H265SequenceParameterSet.Parse(sps), H265PictureParameterSet.Parse(H265PcmStillCodec._BuildPps()));
  }

  private static H265SliceHeader _ParseHeader(
    (H265SequenceParameterSet Sps, H265PictureParameterSet Pps) sets, byte[] headerBits) {
    var rbsp = new byte[headerBits.Length + 1];
    headerBits.CopyTo(rbsp, 0);
    rbsp[^1] = 0x80;

    return H265SliceHeader.Parse(
      new H265NalUnit(H265NalUnitType.TrailingReference, 0, 0, rbsp, []),
      new Dictionary<int, H265SequenceParameterSet> { [0] = sets.Sps },
      new Dictionary<int, H265PictureParameterSet> { [0] = sets.Pps });
  }

  private static byte[] _SliceHeader(H265SliceType type, int poc, int pastPoc, int? futurePoc) {
    var w = new H265PcmStillCodec.Bits();
    w.WriteBit(1); // first_slice_segment_in_pic_flag
    w.WriteUe(0);  // slice_pic_parameter_set_id
    w.WriteUe(type == H265SliceType.B ? 0u : 1u); // slice_type
    w.WriteBits((uint)poc, 8); // slice_pic_order_cnt_lsb
    w.WriteBit(0); // short_term_ref_pic_set_sps_flag
    w.WriteUe(1);  // num_negative_pics
    w.WriteUe(futurePoc.HasValue ? 1u : 0u); // num_positive_pics
    w.WriteUe((uint)(poc - pastPoc - 1));
    w.WriteBit(1); // used_by_curr_pic_s0_flag
    if (futurePoc.HasValue) {
      w.WriteUe((uint)(futurePoc.Value - poc - 1));
      w.WriteBit(1); // used_by_curr_pic_s1_flag
    }

    w.WriteBit(0); // num_ref_idx_active_override_flag
    if (type == H265SliceType.B)
      w.WriteBit(0); // mvd_l1_zero_flag

    w.WriteUe(4); // five_minus_max_num_merge_cand
    w.WriteSe(_QUANTISER - 26); // slice_qp_delta
    w.WriteByteAlignment();
    return w.ToArray();
  }

  private static H265Picture _Texture(int phase, int poc) => _Picture(index => {
    var x = index % _WIDTH;
    var y = index / _WIDTH;
    return (ushort)((x * 37 + y * 53 + x * y * 11 + phase * 97 + ((x + phase) ^ (y * 3 + phase)) * 17) & 0xFF);
  }, poc);

  private static H265Picture _Picture(Func<int, ushort> luma, int poc) {
    var picture = new H265Picture(_WIDTH, _HEIGHT, 2) { PictureOrderCount = poc };
    for (var i = 0; i < picture.Luma.Length; ++i)
      picture.Luma[i] = luma(i);

    for (var i = 0; i < picture.Cb.Length; ++i) {
      picture.Cb[i] = (ushort)(96 + ((i * 29 + poc * 7) & 63));
      picture.Cr[i] = (ushort)(96 + ((i * 43 + poc * 11) & 63));
    }

    return picture;
  }
}