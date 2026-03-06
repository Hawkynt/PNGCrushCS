using System;
using FileFormat.Pcx;

namespace FileFormat.Pcx.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PcxColorMode_HasExpectedValues() {
    Assert.That((int)PcxColorMode.Original, Is.EqualTo(0));
    Assert.That((int)PcxColorMode.Rgb24, Is.EqualTo(1));
    Assert.That((int)PcxColorMode.Indexed8, Is.EqualTo(2));
    Assert.That((int)PcxColorMode.Indexed4, Is.EqualTo(3));
    Assert.That((int)PcxColorMode.Monochrome, Is.EqualTo(4));
    Assert.That(Enum.GetValues<PcxColorMode>(), Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void PcxPlaneConfig_HasExpectedValues() {
    Assert.That((int)PcxPlaneConfig.SinglePlane, Is.EqualTo(0));
    Assert.That((int)PcxPlaneConfig.SeparatePlanes, Is.EqualTo(1));
    Assert.That(Enum.GetValues<PcxPlaneConfig>(), Has.Length.EqualTo(2));
  }
}
