using System;
using FileFormat.Psd;

namespace FileFormat.Psd.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PsdColorMode_HasExpectedValues() {
    Assert.That((int)PsdColorMode.Bitmap, Is.EqualTo(0));
    Assert.That((int)PsdColorMode.Grayscale, Is.EqualTo(1));
    Assert.That((int)PsdColorMode.Indexed, Is.EqualTo(2));
    Assert.That((int)PsdColorMode.RGB, Is.EqualTo(3));
    Assert.That((int)PsdColorMode.CMYK, Is.EqualTo(4));
    Assert.That((int)PsdColorMode.Multichannel, Is.EqualTo(7));
    Assert.That((int)PsdColorMode.Duotone, Is.EqualTo(8));
    Assert.That((int)PsdColorMode.Lab, Is.EqualTo(9));

    var values = Enum.GetValues<PsdColorMode>();
    Assert.That(values, Has.Length.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void PsdCompression_HasExpectedValues() {
    Assert.That((int)PsdCompression.Raw, Is.EqualTo(0));
    Assert.That((int)PsdCompression.Rle, Is.EqualTo(1));

    var values = Enum.GetValues<PsdCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }
}
