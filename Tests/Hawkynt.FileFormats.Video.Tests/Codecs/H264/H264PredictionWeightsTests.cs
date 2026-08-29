using System.Linq;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264PredictionWeightsTests {

  [Test]
  public void ExplicitList0WeightsAreAppliedAfterInterpolation() {
    var spsNal = H264NalReader.SplitAnnexB(new H264TestStream().SequenceParameterSet().ToArray()).Single();
    var sps = H264SequenceParameterSet.Parse(spsNal.Payload);

    var payload = new H264TestStream()
      .Unsigned(1) // luma_log2_weight_denom
      .Unsigned(0) // chroma_log2_weight_denom
      .Bits(1, 1).Signed(1).Signed(10) // luma weight/offset
      .Bits(1, 1) // chroma_weight_l0_flag
      .Signed(1).Signed(-5) // Cb weight/offset
      .Signed(1).Signed(7) // Cr weight/offset
      .RawPayload();

    var reader = new H264BitReader(payload);
    var weights = H264PredictionWeights.ParseP(ref reader, sps, 1);

    byte[] luma = [100, 0, 255];
    weights.ApplyLuma(0, luma);
    Assert.That(luma, Is.EqualTo(new byte[] { 60, 10, 138 }));

    byte[] cb = [100, 4];
    weights.ApplyChroma(0, 0, cb);
    Assert.That(cb, Is.EqualTo(new byte[] { 95, 0 }));

    byte[] cr = [100, 250];
    weights.ApplyChroma(0, 1, cr);
    Assert.That(cr, Is.EqualTo(new byte[] { 107, 255 }));
  }

  [Test]
  public void MissingWeightFlagsUseIdentityWeights() {
    var spsNal = H264NalReader.SplitAnnexB(new H264TestStream().SequenceParameterSet().ToArray()).Single();
    var sps = H264SequenceParameterSet.Parse(spsNal.Payload);

    var payload = new H264TestStream()
      .Unsigned(3) // default weight = 8
      .Unsigned(2) // default chroma weight = 4
      .Bits(0, 1) // no explicit luma weight
      .Bits(0, 1) // no explicit chroma weight
      .RawPayload();

    var reader = new H264BitReader(payload);
    var weights = H264PredictionWeights.ParseP(ref reader, sps, 1);

    byte[] luma = [0, 1, 127, 255];
    var expectedLuma = luma.ToArray();
    weights.ApplyLuma(0, luma);
    Assert.That(luma, Is.EqualTo(expectedLuma));

    byte[] chroma = [0, 17, 129, 255];
    var expectedChroma = chroma.ToArray();
    weights.ApplyChroma(0, 0, chroma);
    Assert.That(chroma, Is.EqualTo(expectedChroma));
  }

  [Test]
  public void DenominatorsOutsideTheSyntaxRangeAreRefused() {
    var spsNal = H264NalReader.SplitAnnexB(new H264TestStream().SequenceParameterSet().ToArray()).Single();
    var sps = H264SequenceParameterSet.Parse(spsNal.Payload);
    var payload = new H264TestStream().Unsigned(8).RawPayload();

    Assert.That(
      () => _ParseWeights(payload, sps),
      Throws.TypeOf<System.IO.InvalidDataException>().With.Message.Contains("luma_log2_weight_denom"));
  }

  private static H264PredictionWeights _ParseWeights(byte[] payload, H264SequenceParameterSet sps) {
    var reader = new H264BitReader(payload);
    return H264PredictionWeights.ParseP(ref reader, sps, 1);
  }
}
