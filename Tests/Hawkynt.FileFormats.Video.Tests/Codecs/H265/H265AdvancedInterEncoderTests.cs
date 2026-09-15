using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H265.Tests;

[TestFixture]
public sealed class H265AdvancedInterEncoderTests {

  private const int _WIDTH = 32;
  private const int _HEIGHT = 32;
  private const int _QP = 26;

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void PredictedPictureSelectsSecondList0Reference() {
    var older = _Texture(poc: 0);
    var newest = _Flat(17, poc: 2);
    var source = _Clone(older, poc: 4);
    var sets = _ParameterSets();
    var headerBits = _SliceHeader(
      H265SliceType.P, poc: 4, pastPocs: [2, 0], futurePoc: null,
      activeL0: 2, activeL1: 0, listsModificationPresent: false, list0Entry: null);

    var encoder = _EncodeAndAssertExact(
      sets, headerBits, source, [newest, older], [],
      adaptiveCodingUnits: false, minimumCuLog2: 4);

    var motion = encoder.MotionAt(encoder.BlockIndexAt(8, 8));
    Assert.Multiple(() => {
      Assert.That(motion.PredictL0, Is.True);
      Assert.That(motion.RefIdxL0, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void BidirectionalPictureWritesSecondList1ReferenceIndex() {
    var past = _Texture(poc: 0);
    var future = _Flat(9, poc: 4);
    var source = _Clone(past, poc: 2);
    var sets = _ParameterSets(listsModificationPresent: true);

    // NumPicTotalCurr is two. List 0 is modified to select the future picture only; list 1 keeps
    // the natural [future, past] order, forcing the exact prediction to ref_idx_l1 == 1.
    var headerBits = _SliceHeader(
      H265SliceType.B, poc: 2, pastPocs: [0], futurePoc: 4,
      activeL0: 1, activeL1: 2, listsModificationPresent: true, list0Entry: 1);

    var encoder = _EncodeAndAssertExact(
      sets, headerBits, source, [future], [future, past],
      adaptiveCodingUnits: false, minimumCuLog2: 4);

    var motion = encoder.MotionAt(encoder.BlockIndexAt(8, 8));
    Assert.Multiple(() => {
      Assert.That(motion.PredictL0, Is.False);
      Assert.That(motion.PredictL1, Is.True);
      Assert.That(motion.RefIdxL1, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void CodingUnitChoosesVerticalPredictionHalves() {
    var reference = _Texture(poc: 0);
    var source = _Shifted(reference, poc: 2, static (x, _) => x < 16 ? 0 : 6);
    var sets = _ParameterSets();
    var headerBits = _SliceHeader(
      H265SliceType.P, poc: 2, pastPocs: [0], futurePoc: null,
      activeL0: 1, activeL1: 0, listsModificationPresent: false, list0Entry: null);

    var encoder = _EncodeAndAssertExact(
      sets, headerBits, source, [reference], [],
      adaptiveCodingUnits: false, minimumCuLog2: 5);

    var left = encoder.MotionAt(encoder.BlockIndexAt(8, 8));
    var right = encoder.MotionAt(encoder.BlockIndexAt(24, 8));
    Assert.Multiple(() => {
      Assert.That(encoder.CodingBlockPartitionMode, Is.EqualTo(H265PartitionMode.VerticalHalves));
      Assert.That(left.MvL0X, Is.EqualTo(0));
      Assert.That(right.MvL0X, Is.EqualTo(24));
    });
  }

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void RateDistortionSearchSplitsCodingUnitWhenFourMotionsWin() {
    var reference = _Texture(poc: 0);
    var source = _Shifted(reference, poc: 2, static (x, y) => {
      var quadrant = (x >= 16 ? 1 : 0) + (y >= 16 ? 2 : 0);
      return quadrant * 3;
    });
    var sets = _ParameterSets();
    var headerBits = _SliceHeader(
      H265SliceType.P, poc: 2, pastPocs: [0], futurePoc: null,
      activeL0: 1, activeL1: 0, listsModificationPresent: false, list0Entry: null);

    var encoder = _EncodeAndAssertExact(
      sets, headerBits, source, [reference], [],
      adaptiveCodingUnits: true, minimumCuLog2: 4);

    var vectors = new HashSet<short> {
      encoder.MotionAt(encoder.BlockIndexAt(8, 8)).MvL0X,
      encoder.MotionAt(encoder.BlockIndexAt(24, 8)).MvL0X,
      encoder.MotionAt(encoder.BlockIndexAt(8, 24)).MvL0X,
      encoder.MotionAt(encoder.BlockIndexAt(24, 24)).MvL0X,
    };

    Assert.That(vectors, Is.EquivalentTo(new short[] { 0, 12, 24, 36 }));
  }

  private static H265InterPictureEncoder _EncodeAndAssertExact(
    (H265SequenceParameterSet Sps, H265PictureParameterSet Pps) sets,
    byte[] headerBits,
    H265Picture source,
    IReadOnlyList<H265Picture> list0,
    IReadOnlyList<H265Picture> list1,
    bool adaptiveCodingUnits,
    int minimumCuLog2) {
    var parsed = _ParseHeader(sets, headerBits);
    var encoder = new H265InterPictureEncoder(
      sets.Sps, sets.Pps, parsed, source, list0, list1, parsed.SliceQpY,
      adaptiveCodingUnits, minimumCuLog2);

    var data = encoder.Encode();
    var rbsp = new byte[headerBits.Length + data.Length];
    headerBits.CopyTo(rbsp, 0);
    data.CopyTo(rbsp, headerBits.Length);

    var header = H265SliceHeader.Parse(
      new H265NalUnit(H265NalUnitType.TrailingReference, 0, 0, rbsp, []),
      new Dictionary<int, H265SequenceParameterSet> { [0] = sets.Sps },
      new Dictionary<int, H265PictureParameterSet> { [0] = sets.Pps });

    var decoder = new H265FrameDecoder(sets.Sps, sets.Pps);
    IReadOnlyList<H265Picture>[] lists = [list0, list1];
    decoder.DecodeSliceSegment(header, lists);
    decoder.RefuseIfIncomplete();

    Assert.Multiple(() => {
      Assert.That(decoder.Picture.Luma, Is.EqualTo(encoder.Reconstruction.Luma), "luminance reconstruction");
      Assert.That(decoder.Picture.Cb, Is.EqualTo(encoder.Reconstruction.Cb), "blue chrominance reconstruction");
      Assert.That(decoder.Picture.Cr, Is.EqualTo(encoder.Reconstruction.Cr), "red chrominance reconstruction");
    });

    return encoder;
  }

  private static (H265SequenceParameterSet Sps, H265PictureParameterSet Pps) _ParameterSets(
    bool listsModificationPresent = false) {
    var level = H265PcmStillCodec._SmallestLevelFor(_WIDTH, _HEIGHT);
    var sps = H265PcmStillCodec._BuildSps(
      _WIDTH, _HEIGHT, _WIDTH, _HEIGHT, level,
      maxDecPicBufferingMinus1: 3,
      log2MaxPocLsbMinus4: 4,
      maxNumReorderPics: 2,
      maxTransformHierarchyDepthInter: 1);
    var pps = H265PcmStillCodec._BuildPps(listsModificationPresent: listsModificationPresent);
    return (H265SequenceParameterSet.Parse(sps), H265PictureParameterSet.Parse(pps));
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

  private static byte[] _SliceHeader(
    H265SliceType type,
    int poc,
    IReadOnlyList<int> pastPocs,
    int? futurePoc,
    int activeL0,
    int activeL1,
    bool listsModificationPresent,
    int? list0Entry) {
    var w = new H265PcmStillCodec.Bits();
    w.WriteBit(1);
    w.WriteUe(0);
    w.WriteUe(type == H265SliceType.B ? 0u : 1u);
    w.WriteBits((uint)poc, 8);
    w.WriteBit(0);
    w.WriteUe((uint)pastPocs.Count);
    w.WriteUe(futurePoc.HasValue ? 1u : 0u);

    var previous = poc;
    foreach (var pastPoc in pastPocs) {
      w.WriteUe((uint)(previous - pastPoc - 1));
      w.WriteBit(1);
      previous = pastPoc;
    }

    if (futurePoc.HasValue) {
      w.WriteUe((uint)(futurePoc.Value - poc - 1));
      w.WriteBit(1);
    }

    var overrideActive = activeL0 != 1 || (type == H265SliceType.B && activeL1 != 1);
    w.WriteBit(overrideActive ? 1 : 0);
    if (overrideActive) {
      w.WriteUe((uint)(activeL0 - 1));
      if (type == H265SliceType.B)
        w.WriteUe((uint)(activeL1 - 1));
    }

    var totalReferences = pastPocs.Count + (futurePoc.HasValue ? 1 : 0);
    if (listsModificationPresent && totalReferences > 1) {
      var bits = _CeilLog2(totalReferences);
      w.WriteBit(list0Entry.HasValue ? 1 : 0);
      if (list0Entry.HasValue)
        w.WriteBits((uint)list0Entry.Value, bits);
      if (type == H265SliceType.B)
        w.WriteBit(0);
    }

    if (type == H265SliceType.B)
      w.WriteBit(0);
    w.WriteUe(4);
    w.WriteSe(_QP - 26);
    w.WriteByteAlignment();
    return w.ToArray();
  }

  private static int _CeilLog2(int value) {
    var bits = 0;
    for (var v = value - 1; v > 0; v >>= 1)
      ++bits;
    return bits;
  }

  private static H265Picture _Texture(int poc) {
    var picture = new H265Picture(_WIDTH, _HEIGHT, 2) { PictureOrderCount = poc };
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x)
        picture.Luma[y * _WIDTH + x] =
          (ushort)((x * 37 + y * 53 + x * y * 11 + ((x * 7) ^ (y * 13)) * 17) & 0xFF);
    Array.Fill(picture.Cb, (ushort)128);
    Array.Fill(picture.Cr, (ushort)128);
    return picture;
  }

  private static H265Picture _Flat(ushort value, int poc) {
    var picture = new H265Picture(_WIDTH, _HEIGHT, 2) { PictureOrderCount = poc };
    Array.Fill(picture.Luma, value);
    Array.Fill(picture.Cb, (ushort)128);
    Array.Fill(picture.Cr, (ushort)128);
    return picture;
  }

  private static H265Picture _Clone(H265Picture source, int poc) {
    var picture = new H265Picture(_WIDTH, _HEIGHT, 2) { PictureOrderCount = poc };
    source.Luma.CopyTo(picture.Luma, 0);
    source.Cb.CopyTo(picture.Cb, 0);
    source.Cr.CopyTo(picture.Cr, 0);
    return picture;
  }

  private static H265Picture _Shifted(H265Picture reference, int poc, Func<int, int, int> displacement) {
    var picture = new H265Picture(_WIDTH, _HEIGHT, 2) { PictureOrderCount = poc };
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var sourceX = Math.Clamp(x + displacement(x, y), 0, _WIDTH - 1);
        picture.Luma[y * _WIDTH + x] = reference.Luma[y * _WIDTH + sourceX];
      }
    Array.Fill(picture.Cb, (ushort)128);
    Array.Fill(picture.Cr, (ushort)128);
    return picture;
  }
}
