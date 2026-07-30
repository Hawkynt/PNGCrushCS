using System;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void TiffColorMode_HasExpectedValues() {
    Assert.That((int)TiffColorMode.Original, Is.EqualTo(0));
    Assert.That((int)TiffColorMode.Rgb, Is.EqualTo(1));
    Assert.That((int)TiffColorMode.Grayscale, Is.EqualTo(2));
    Assert.That((int)TiffColorMode.Palette, Is.EqualTo(3));
    Assert.That((int)TiffColorMode.BiLevel, Is.EqualTo(4));
    Assert.That(Enum.GetValues<TiffColorMode>(), Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void TiffCompression_HasExpectedValues() {
    Assert.That((int)TiffCompression.None, Is.EqualTo(0));
    Assert.That((int)TiffCompression.PackBits, Is.EqualTo(1));
    Assert.That((int)TiffCompression.Lzw, Is.EqualTo(2));
    Assert.That((int)TiffCompression.Deflate, Is.EqualTo(3));
    Assert.That((int)TiffCompression.DeflateUltra, Is.EqualTo(4));
    Assert.That((int)TiffCompression.DeflateHyper, Is.EqualTo(5));
    Assert.That(Enum.GetValues<TiffCompression>(), Has.Length.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void TiffPredictor_HasExpectedValues() {
    Assert.That((int)TiffPredictor.None, Is.EqualTo(0));
    Assert.That((int)TiffPredictor.HorizontalDifferencing, Is.EqualTo(1));
    Assert.That(Enum.GetValues<TiffPredictor>(), Has.Length.EqualTo(2));
  }
}
