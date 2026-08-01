using System;
using FileFormat.SunRaster;

namespace FileFormat.SunRaster.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SunRasterType_HasExpectedValues() {
    Assert.That((int)SunRasterType.Old, Is.EqualTo(0));
    Assert.That((int)SunRasterType.Standard, Is.EqualTo(1));
    Assert.That((int)SunRasterType.ByteEncoded, Is.EqualTo(2));
    Assert.That((int)SunRasterType.FormatRgb, Is.EqualTo(3));

    Assert.That((int)SunRasterType.FormatTiff, Is.EqualTo(4));
    Assert.That((int)SunRasterType.FormatIff, Is.EqualTo(5));
    Assert.That((int)SunRasterType.Experimental, Is.EqualTo(0xFFFF));

    var values = Enum.GetValues<SunRasterType>();
    Assert.That(values, Has.Length.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void SunRasterColorMode_HasExpectedValues() {
    Assert.That((int)SunRasterColorMode.Original, Is.EqualTo(0));
    Assert.That((int)SunRasterColorMode.Rgb24, Is.EqualTo(1));
    Assert.That((int)SunRasterColorMode.Rgb32, Is.EqualTo(2));
    Assert.That((int)SunRasterColorMode.Palette8, Is.EqualTo(3));
    Assert.That((int)SunRasterColorMode.Monochrome, Is.EqualTo(4));

    var values = Enum.GetValues<SunRasterColorMode>();
    Assert.That(values, Has.Length.EqualTo(5));
  }
}
