namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264ScalingTransformTests {

  [Test]
  public void FourByFourScalingListChangesInverseQuantizationCoefficientByCoefficient() {
    var levels = new int[16];
    levels[0] = 64;

    Span<int> flat = stackalloc int[16];
    H264Transform.DecodeBlock(levels, qp: 0, hasSeparateDc: false, dc: 0, flat);

    byte[] doubled = [
      32, 16, 16, 16,
      16, 16, 16, 16,
      16, 16, 16, 16,
      16, 16, 16, 16,
    ];
    Span<int> scaled = stackalloc int[16];
    H264Transform.DecodeBlock(levels, qp: 0, hasSeparateDc: false, dc: 0, doubled, scaled);

    Assert.That(flat.ToArray(), Is.All.EqualTo(10));
    Assert.That(scaled.ToArray(), Is.All.EqualTo(20));
  }

  [Test]
  public void LevelScaleUsesTheResolvedWeightRatherThanAssumingSixteen() {
    Assert.That(H264Transform.LevelScale(0, 0, 0), Is.EqualTo(160));
    Assert.That(H264Transform.LevelScale(0, 0, 0, 6), Is.EqualTo(60));
    Assert.That(H264Transform.LevelScale(5, 1, 1, 42), Is.EqualTo(42 * 29));
  }

  [Test]
  public void SeparateDcTransformsUseTheFirstScalingListEntry() {
    var levels = new int[16];
    levels[0] = 8;
    byte[] flat = Enumerable.Repeat((byte)16, 16).ToArray();
    byte[] doubled = Enumerable.Repeat((byte)16, 16).ToArray();
    doubled[0] = 32;

    Span<int> flatDc = stackalloc int[16];
    Span<int> scaledDc = stackalloc int[16];
    H264Transform.DecodeLumaDc(levels, qp: 24, flat, flatDc);
    H264Transform.DecodeLumaDc(levels, qp: 24, doubled, scaledDc);

    for (var i = 0; i < flatDc.Length; ++i)
      Assert.That(scaledDc[i], Is.EqualTo(flatDc[i] * 2), $"DC {i}");
  }
}
