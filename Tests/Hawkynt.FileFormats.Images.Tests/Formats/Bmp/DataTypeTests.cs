using System;
using FileFormat.Bmp;

namespace FileFormat.Bmp.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void BmpColorMode_HasExpectedValues() {
    Assert.That((int)BmpColorMode.Original, Is.EqualTo(0));
    Assert.That((int)BmpColorMode.Rgb24, Is.EqualTo(1));
    Assert.That((int)BmpColorMode.Rgb16_565, Is.EqualTo(2));
    Assert.That((int)BmpColorMode.Palette8, Is.EqualTo(3));
    Assert.That((int)BmpColorMode.Palette4, Is.EqualTo(4));
    Assert.That((int)BmpColorMode.Palette1, Is.EqualTo(5));
    Assert.That((int)BmpColorMode.Grayscale8, Is.EqualTo(6));

    var values = Enum.GetValues<BmpColorMode>();
    Assert.That(values, Has.Length.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void BmpCompression_HasExpectedValues() {
    Assert.That((int)BmpCompression.None, Is.EqualTo(0));
    Assert.That((int)BmpCompression.Rle8, Is.EqualTo(1));
    Assert.That((int)BmpCompression.Rle4, Is.EqualTo(2));

    var values = Enum.GetValues<BmpCompression>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void BmpRowOrder_HasExpectedValues() {
    Assert.That((int)BmpRowOrder.TopDown, Is.EqualTo(0));
    Assert.That((int)BmpRowOrder.BottomUp, Is.EqualTo(1));

    var values = Enum.GetValues<BmpRowOrder>();
    Assert.That(values, Has.Length.EqualTo(2));
  }
}
