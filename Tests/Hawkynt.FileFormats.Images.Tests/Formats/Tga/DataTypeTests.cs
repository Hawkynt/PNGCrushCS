using System;
using FileFormat.Tga;

namespace FileFormat.Tga.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void TgaColorMode_HasExpectedValues() {
    Assert.That((int)TgaColorMode.Original, Is.EqualTo(0));
    Assert.That((int)TgaColorMode.Rgba32, Is.EqualTo(1));
    Assert.That((int)TgaColorMode.Rgb24, Is.EqualTo(2));
    Assert.That((int)TgaColorMode.Grayscale8, Is.EqualTo(3));
    Assert.That((int)TgaColorMode.Indexed8, Is.EqualTo(4));
    Assert.That(Enum.GetValues<TgaColorMode>(), Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void TgaCompression_HasExpectedValues() {
    Assert.That((int)TgaCompression.None, Is.EqualTo(0));
    Assert.That((int)TgaCompression.Rle, Is.EqualTo(1));
    Assert.That(Enum.GetValues<TgaCompression>(), Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void TgaOrigin_HasExpectedValues() {
    Assert.That((int)TgaOrigin.BottomLeft, Is.EqualTo(0));
    Assert.That((int)TgaOrigin.TopLeft, Is.EqualTo(1));
    Assert.That(Enum.GetValues<TgaOrigin>(), Has.Length.EqualTo(2));
  }
}
