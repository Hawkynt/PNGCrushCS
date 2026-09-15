using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// Decodes the predicted pictures this package writes with the decoder in this package.
/// </summary>
/// <remarks>
/// The strongest check there is for an encoder, and the one an external oracle cannot give: the
/// decoder reconstructs the picture sample for sample, so the encoder's own reconstruction can be
/// held against it exactly rather than within a tolerance. They have to be identical — not close —
/// because the encoder predicts the next picture from its reconstruction and the decoder predicts it
/// from theirs. A difference of one sample is drift, and drift over a group of pictures is what a
/// tolerance-based comparison reports as "slightly worse than expected" until it is far too late.
/// </remarks>
[TestFixture]
public sealed class H265InterPictureEncoderTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 64;
  private const int _QUANTISER = 26;

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void AStillPictureDecodesToWhatTheEncoderReconstructed() {
    var reference = _Checkerboard(phase: 0, poc: 0);
    var source = _Checkerboard(phase: 0, poc: 2);

    _AssertReconstructionsMatch(source, reference, future: null);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void AMovedPictureDecodesToWhatTheEncoderReconstructed() {
    var reference = _Checkerboard(phase: 0, poc: 0);
    var source = _Checkerboard(phase: 5, poc: 2);

    _AssertReconstructionsMatch(source, reference, future: null);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void APictureOfNoiseDecodesToWhatTheEncoderReconstructed() {
    // Noise is what makes the residual matter: nothing predicts it, so every coding unit codes
    // coefficients rather than taking the skip that a flat or merely shifted picture takes.
    var random = new Random(4711);
    var reference = _Checkerboard(phase: 0, poc: 0);
    var source = _Picture(_ => (ushort)random.Next(0, 256), poc: 2);

    _AssertReconstructionsMatch(source, reference, future: null);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void ABidirectionalPictureDecodesToWhatTheEncoderReconstructed() {
    var past = _Checkerboard(phase: 0, poc: 0);
    var future = _Checkerboard(phase: 6, poc: 4);
    var source = _Checkerboard(phase: 3, poc: 2);

    _AssertReconstructionsMatch(source, past, future);
  }

  private static void _AssertReconstructionsMatch(H265Picture source, H265Picture reference, H265Picture? future) {
    var sets = _ParameterSets();
    var type = future == null ? H265SliceType.P : H265SliceType.B;
    var headerBits = _SliceHeader(type, poc: 2, pastPoc: 0, futurePoc: future == null ? null : 4);

    var parsed = _ParseHeader(sets, headerBits);
    var encoder = new H265InterPictureEncoder(
      sets.Sps, sets.Pps, parsed, source, reference, future, parsed.SliceQpY);

    var data = encoder.Encode();

    var rbsp = new byte[headerBits.Length + data.Length];
    headerBits.CopyTo(rbsp, 0);
    data.CopyTo(rbsp, headerBits.Length);

    var header = H265SliceHeader.Parse(
      new H265NalUnit(H265NalUnitType.TrailingReference, 0, 0, rbsp, []),
      new Dictionary<int, H265SequenceParameterSet> { [0] = sets.Sps },
      new Dictionary<int, H265PictureParameterSet> { [0] = sets.Pps });

    var decoder = new H265FrameDecoder(sets.Sps, sets.Pps);
    IReadOnlyList<H265Picture>[] lists = future == null ? [[reference], []] : [[reference], [future]];
    decoder.DecodeSliceSegment(header, lists);

    var mine = encoder.Reconstruction;
    var theirs = decoder.Picture;

    var differing = 0;
    var worst = 0;
    var worstAt = -1;
    for (var i = 0; i < mine.Luma.Length; ++i) {
      var difference = Math.Abs(mine.Luma[i] - theirs.Luma[i]);
      if (difference == 0)
        continue;

      ++differing;
      if (difference <= worst)
        continue;

      worst = difference;
      worstAt = i;
    }

    Assert.Multiple(() => {
      Assert.That(differing, Is.Zero,
        $"luminance differs at {differing} of {mine.Luma.Length} samples, worst {worst} at "
        + $"({(worstAt < 0 ? -1 : worstAt % _WIDTH)}, {(worstAt < 0 ? -1 : worstAt / _WIDTH)})");
      Assert.That(theirs.Cb, Is.EqualTo(mine.Cb), "the blue chrominance plane");
      Assert.That(theirs.Cr, Is.EqualTo(mine.Cr), "the red chrominance plane");
    });
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
    w.WriteBit(1);
    w.WriteUe(0);
    w.WriteUe(type == H265SliceType.B ? 0u : 1u);
    w.WriteBits((uint)poc, 8);
    w.WriteBit(0);
    w.WriteUe(1);
    w.WriteUe(futurePoc.HasValue ? 1u : 0u);
    w.WriteUe((uint)(poc - pastPoc - 1));
    w.WriteBit(1);
    if (futurePoc.HasValue) {
      w.WriteUe((uint)(futurePoc.Value - poc - 1));
      w.WriteBit(1);
    }

    w.WriteBit(0);
    if (type == H265SliceType.B)
      w.WriteBit(0);

    w.WriteUe(4);
    w.WriteSe(_QUANTISER - 26);
    w.WriteByteAlignment();
    return w.ToArray();
  }

  private static H265Picture _Checkerboard(int phase, int poc = 0) => _Picture(index => {
    var x = index % _WIDTH;
    var y = index / _WIDTH;
    var moved = x - phase;
    return (ushort)(moved is >= 8 and < 40 && y is >= 8 and < 40 ? 210 : 40 + (((x >> 3) + (y >> 3)) & 1) * 30);
  }, poc);

  private static H265Picture _Picture(Func<int, ushort> luma, int poc = 0) {
    var picture = new H265Picture(_WIDTH, _HEIGHT, 2) { PictureOrderCount = poc };
    for (var i = 0; i < picture.Luma.Length; ++i)
      picture.Luma[i] = luma(i);

    for (var i = 0; i < picture.Cb.Length; ++i) {
      picture.Cb[i] = (ushort)(128 + (i % 17) - 8);
      picture.Cr[i] = (ushort)(128 - (i % 13) + 6);
    }

    return picture;
  }
}
