using System.IO;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264CabacContextInitializationTests {
  [TestCase(20, -15, 0, 62, 0)]
  [TestCase(20, -15, 51, 15, 0)]
  [TestCase(-28, 127, 51, 26, 0)]
  [TestCase(0, 97, 0, 33, 1)]
  [TestCase(0, 63, 26, 0, 0)]
  [TestCase(0, 64, 26, 0, 1)]
  public void ContextInitializationMatchesClause9311(
    int m,
    int n,
    int qp,
    int expectedStateIndex,
    int expectedMps) {
    var context = H264CabacContext.Initialize(m, n, qp);
    Assert.That(context.StateIndex, Is.EqualTo(expectedStateIndex));
    Assert.That(context.MostProbableSymbol, Is.EqualTo(expectedMps));
  }

  [TestCase(-37)]
  [TestCase(52)]
  public void ContextInitializationRejectsSliceQpOutsideEightBitAvcRange(int qp) {
    Assert.Throws<InvalidDataException>(() => H264CabacContext.Initialize(0, 0, qp));
  }
}
