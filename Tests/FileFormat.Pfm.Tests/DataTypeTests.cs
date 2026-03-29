using System;
using FileFormat.Pfm;

namespace FileFormat.Pfm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PfmColorMode_HasExpectedValues() {
    Assert.That((int)PfmColorMode.Grayscale, Is.EqualTo(0));
    Assert.That((int)PfmColorMode.Rgb, Is.EqualTo(1));

    var values = Enum.GetValues<PfmColorMode>();
    Assert.That(values, Has.Length.EqualTo(2));
  }
}
