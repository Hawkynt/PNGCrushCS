using System;
using FileFormat.SunRaster;

namespace FileFormat.SunRaster.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SunRasterCompression_HasExpectedValues() {
    // These are the values of Sun Raster's type field, not an invented sequence. They used to read
    // None=0, Rle=1, Experimental=2, which meant an uncompressed RT_STANDARD file (type 1) was fed to
    // the RLE decompressor and a genuinely byte-encoded one (type 2) was copied out raw.
    Assert.That((int)SunRasterCompression.Old, Is.EqualTo(0), "RT_OLD");
    Assert.That((int)SunRasterCompression.None, Is.EqualTo(1), "RT_STANDARD");
    Assert.That((int)SunRasterCompression.Rle, Is.EqualTo(2), "RT_BYTE_ENCODED");
    Assert.That((int)SunRasterCompression.Rgb, Is.EqualTo(3), "RT_FORMAT_RGB");
    Assert.That((int)SunRasterCompression.Experimental, Is.EqualTo(0xFFFF), "RT_EXPERIMENTAL");
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
