using System;
using FileFormat.Psb;

namespace FileFormat.Psb.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PsbColorMode_HasExpectedValues() {
    Assert.That((int)PsbColorMode.Bitmap, Is.EqualTo(0));
    Assert.That((int)PsbColorMode.Grayscale, Is.EqualTo(1));
    Assert.That((int)PsbColorMode.Indexed, Is.EqualTo(2));
    Assert.That((int)PsbColorMode.RGB, Is.EqualTo(3));
    Assert.That((int)PsbColorMode.CMYK, Is.EqualTo(4));
    Assert.That((int)PsbColorMode.Multichannel, Is.EqualTo(7));
    Assert.That((int)PsbColorMode.Duotone, Is.EqualTo(8));
    Assert.That((int)PsbColorMode.Lab, Is.EqualTo(9));

    var values = Enum.GetValues<PsbColorMode>();
    Assert.That(values, Has.Length.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void PsbCompression_HasExpectedValues() {
    Assert.That((int)PsbCompression.Raw, Is.EqualTo(0));
    Assert.That((int)PsbCompression.Rle, Is.EqualTo(1));

    var values = Enum.GetValues<PsbCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void PsbFile_DefaultValues() {
    var file = new PsbFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Channels, Is.EqualTo(0));
    Assert.That(file.Depth, Is.EqualTo(0));
    Assert.That(file.ColorMode, Is.EqualTo(PsbColorMode.Bitmap));
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
    Assert.That(file.Palette, Is.Null);
    Assert.That(file.ImageResources, Is.Null);
    Assert.That(file.LayerMaskInfo, Is.Null);
  }
}
