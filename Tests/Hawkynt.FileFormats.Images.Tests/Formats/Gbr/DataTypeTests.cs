using FileFormat.Core;
using FileFormat.Gbr;

namespace FileFormat.Gbr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void _DefaultPixelData_IsNull() {
    var file = new GbrFile();
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultName_IsNull() {
    var file = new GbrFile();
    Assert.That(file.Name, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultWidth_IsZero() {
    var file = new GbrFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultHeight_IsZero() {
    var file = new GbrFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultBytesPerPixel_IsZero() {
    var file = new GbrFile();
    Assert.That(file.BytesPerPixel, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_DefaultSpacing_IsZero() {
    var file = new GbrFile();
    Assert.That(file.Spacing, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GbrFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new GbrFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      Spacing = 42,
      Name = "Test Brush",
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.BytesPerPixel, Is.EqualTo(1));
    Assert.That(file.Spacing, Is.EqualTo(42));
    Assert.That(file.Name, Is.EqualTo("Test Brush"));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Grayscale_IsGray8() {
    var file = new GbrFile {
      Width = 2, Height = 2, BytesPerPixel = 1, Spacing = 10, Name = "g",
      PixelData = [0x00, 0x40, 0x80, 0xFF]
    };

    var image = GbrFile.ToRawImage(file);

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(image.PixelData, Is.EqualTo(file.PixelData));
  }

  // An RGB brush has no mask channel, so it decodes opaque - three bytes per pixel, not four.
  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_IsRgb24() {
    var file = new GbrFile {
      Width = 2, Height = 1, BytesPerPixel = 3, Spacing = 10, Name = "rgb",
      PixelData = [0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00]
    };

    var image = GbrFile.ToRawImage(file);

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.Width, Is.EqualTo(2));
    Assert.That(image.Height, Is.EqualTo(1));
    Assert.That(image.PixelData, Is.EqualTo(file.PixelData));
  }

  // A brush is written at depth 1 or 4 only, so a colour source has to become RGBA rather than the
  // grey the mask alone would leave of it.
  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_KeepsItsColour() {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00],
    };

    var file = GbrFile.FromRawImage(image);

    Assert.That(file.BytesPerPixel, Is.EqualTo(4));
    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0xFF }));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_StaysGrey() {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0x20, 0xC0],
    };

    var file = GbrFile.FromRawImage(image);

    Assert.That(file.BytesPerPixel, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x20, 0xC0 }));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgba_IsRgba32() {
    var file = new GbrFile {
      Width = 1, Height = 1, BytesPerPixel = 4, Spacing = 10, Name = "rgba",
      PixelData = [0x11, 0x22, 0x33, 0x44]
    };

    var image = GbrFile.ToRawImage(file);

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(image.PixelData, Is.EqualTo(file.PixelData));
  }
}
