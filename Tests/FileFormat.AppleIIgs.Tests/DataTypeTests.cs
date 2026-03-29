using System;
using FileFormat.AppleIIgs;

namespace FileFormat.AppleIIgs.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AppleIIgsMode_HasExpectedValues() {
    Assert.That((int)AppleIIgsMode.Mode320, Is.EqualTo(0));
    Assert.That((int)AppleIIgsMode.Mode640, Is.EqualTo(1));

    var values = Enum.GetValues<AppleIIgsMode>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIgsFile_DefaultsAreEmpty() {
    var file = new AppleIIgsFile();

    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Mode, Is.EqualTo(AppleIIgsMode.Mode320));
    Assert.That(file.PixelData, Is.Empty);
    Assert.That(file.Scbs, Is.Empty);
    Assert.That(file.Palettes, Is.Empty);
  }
}
