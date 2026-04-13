using System;
using FileFormat.Xyz;

namespace FileFormat.Xyz.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void XyzFile_DefaultPalette_IsNull() {
    var file = new XyzFile { Width = 1, Height = 1 };
    Assert.That(file.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void _DefaultPixelData_IsNull() {
    var file = new XyzFile { Width = 1, Height = 1 };
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void XyzFile_InitProperties_RoundTrip() {
    var palette = new byte[768];
    palette[0] = 42;
    var pixels = new byte[] { 1, 2, 3 };
    var file = new XyzFile {
      Width = 10,
      Height = 20,
      Palette = palette,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.Palette, Is.SameAs(palette));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void XyzFile_DefaultWidth_IsZero() {
    var file = new XyzFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XyzFile_DefaultHeight_IsZero() {
    var file = new XyzFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }
}
