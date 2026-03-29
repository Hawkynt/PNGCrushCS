using System;
using FileFormat.Sixel;

namespace FileFormat.Sixel.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SixelColorMode_HasExpectedValues() {
    Assert.That((int)SixelColorMode.Hls, Is.EqualTo(1));
    Assert.That((int)SixelColorMode.Rgb, Is.EqualTo(2));

    var values = Enum.GetValues<SixelColorMode>();
    Assert.That(values, Has.Length.EqualTo(2));
  }
}
