using System;
using FileFormat.SunRaster;

namespace FileFormat.SunRaster.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SunRasterCompression_HasExpectedValues() {
    Assert.That((int)SunRasterCompression.None, Is.EqualTo(0));
    Assert.That((int)SunRasterCompression.Rle, Is.EqualTo(1));
    Assert.That((int)SunRasterCompression.Experimental, Is.EqualTo(2));

    var values = Enum.GetValues<SunRasterCompression>();
    Assert.That(values, Has.Length.EqualTo(3));
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
