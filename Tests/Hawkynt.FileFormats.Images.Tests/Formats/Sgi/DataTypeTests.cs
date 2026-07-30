using System;
using FileFormat.Sgi;

namespace FileFormat.Sgi.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SgiCompression_HasExpectedValues() {
    Assert.That((int)SgiCompression.None, Is.EqualTo(0));
    Assert.That((int)SgiCompression.Rle, Is.EqualTo(1));

    var values = Enum.GetValues<SgiCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void SgiColorMode_HasExpectedValues() {
    Assert.That((int)SgiColorMode.Normal, Is.EqualTo(0));
    Assert.That((int)SgiColorMode.Dither, Is.EqualTo(1));
    Assert.That((int)SgiColorMode.Screen, Is.EqualTo(2));
    Assert.That((int)SgiColorMode.Colormap, Is.EqualTo(3));

    var values = Enum.GetValues<SgiColorMode>();
    Assert.That(values, Has.Length.EqualTo(4));
  }
}
