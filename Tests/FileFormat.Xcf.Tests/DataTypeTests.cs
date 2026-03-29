using System;
using FileFormat.Xcf;

namespace FileFormat.Xcf.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void XcfColorMode_HasExpectedValues() {
    Assert.That((int)XcfColorMode.Rgb, Is.EqualTo(0));
    Assert.That((int)XcfColorMode.Grayscale, Is.EqualTo(1));
    Assert.That((int)XcfColorMode.Indexed, Is.EqualTo(2));

    var values = Enum.GetValues<XcfColorMode>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void XcfCompression_HasExpectedValues() {
    Assert.That((int)XcfCompression.None, Is.EqualTo(0));
    Assert.That((int)XcfCompression.Rle, Is.EqualTo(1));
    Assert.That((int)XcfCompression.Zlib, Is.EqualTo(2));

    var values = Enum.GetValues<XcfCompression>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void XcfFile_DefaultPixelData_IsEmpty() {
    var file = new XcfFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Has.Length.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XcfFile_DefaultPalette_IsNull() {
    var file = new XcfFile();
    Assert.That(file.Palette, Is.Null);
  }
}
