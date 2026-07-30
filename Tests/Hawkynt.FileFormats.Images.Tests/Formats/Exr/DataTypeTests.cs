using System;
using FileFormat.Exr;

namespace FileFormat.Exr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ExrCompression_HasExpectedValues() {
    Assert.That((int)ExrCompression.None, Is.EqualTo(0));
    Assert.That((int)ExrCompression.Rle, Is.EqualTo(1));
    Assert.That((int)ExrCompression.Zip, Is.EqualTo(2));
    Assert.That((int)ExrCompression.ZipScanline, Is.EqualTo(3));
    Assert.That((int)ExrCompression.Piz, Is.EqualTo(4));
    Assert.That((int)ExrCompression.Pxr24, Is.EqualTo(5));
    Assert.That((int)ExrCompression.B44, Is.EqualTo(6));
    Assert.That((int)ExrCompression.B44a, Is.EqualTo(7));
    Assert.That((int)ExrCompression.Dwaa, Is.EqualTo(8));
    Assert.That((int)ExrCompression.Dwab, Is.EqualTo(9));

    var values = Enum.GetValues<ExrCompression>();
    Assert.That(values, Has.Length.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void ExrPixelType_HasExpectedValues() {
    Assert.That((int)ExrPixelType.UInt, Is.EqualTo(0));
    Assert.That((int)ExrPixelType.Half, Is.EqualTo(1));
    Assert.That((int)ExrPixelType.Float, Is.EqualTo(2));

    var values = Enum.GetValues<ExrPixelType>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ExrLineOrder_HasExpectedValues() {
    Assert.That((int)ExrLineOrder.IncreasingY, Is.EqualTo(0));
    Assert.That((int)ExrLineOrder.DecreasingY, Is.EqualTo(1));
    Assert.That((int)ExrLineOrder.RandomY, Is.EqualTo(2));

    var values = Enum.GetValues<ExrLineOrder>();
    Assert.That(values, Has.Length.EqualTo(3));
  }
}
