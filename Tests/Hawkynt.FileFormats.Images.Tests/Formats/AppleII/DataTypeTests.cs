using System;
using FileFormat.AppleII;

namespace FileFormat.AppleII.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AppleIIMode_HasExpectedValues() {
    Assert.That((int)AppleIIMode.Hgr, Is.EqualTo(0));
    Assert.That((int)AppleIIMode.Dhgr, Is.EqualTo(1));

    var values = Enum.GetValues<AppleIIMode>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIFile_DefaultValues() {
    var file = new AppleIIFile();

    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Mode, Is.EqualTo(AppleIIMode.Hgr));
    Assert.That(file.PixelData, Is.Empty);
  }
}
