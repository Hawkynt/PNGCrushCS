using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class Adam7Tests {

  [Test]
  [Category("Unit")]
  public void PassCount_IsSeven() {
    Assert.That(Adam7.PassCount, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void GetPassDimensions_8x8_Pass0() {
    var (w, h) = Adam7.GetPassDimensions(0, 8, 8);
    Assert.That(w, Is.EqualTo(1));
    Assert.That(h, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void GetPassDimensions_1x1_AllPasses() {
    var (w0, h0) = Adam7.GetPassDimensions(0, 1, 1);
    Assert.That(w0, Is.EqualTo(1));
    Assert.That(h0, Is.EqualTo(1));

    for (var pass = 1; pass < 7; ++pass) {
      var (w, h) = Adam7.GetPassDimensions(pass, 1, 1);
      Assert.That(w * h, Is.EqualTo(0), $"Pass {pass} should be empty (w={w}, h={h})");
    }
  }

  [Test]
  [Category("Unit")]
  public void XStart_AllPasses() {
    var expected = new[] { 0, 4, 0, 2, 0, 1, 0 };
    for (var pass = 0; pass < 7; ++pass)
      Assert.That(Adam7.XStart(pass), Is.EqualTo(expected[pass]), $"Pass {pass}");
  }

  [Test]
  [Category("Unit")]
  public void YStart_AllPasses() {
    var expected = new[] { 0, 0, 4, 0, 2, 0, 1 };
    for (var pass = 0; pass < 7; ++pass)
      Assert.That(Adam7.YStart(pass), Is.EqualTo(expected[pass]), $"Pass {pass}");
  }
}
