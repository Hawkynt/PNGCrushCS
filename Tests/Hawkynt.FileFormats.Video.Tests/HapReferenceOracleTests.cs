namespace Hawkynt.FileFormats.Video.Tests;

[TestFixture]
[Category("Conformance")]
public sealed class HapReferenceOracleTests {

  [Test]
  public void VidvoxHapDecodeAndBcdec_ReadDeterministicHapRAndHdrFrames() {
    HapReferenceOracle.RequireAvailable();

    var (accepted, detail) = HapReferenceOracle.TryValidate();
    TestContext.Out.WriteLine(detail);
    Assert.That(accepted, Is.True, detail);
  }
}
