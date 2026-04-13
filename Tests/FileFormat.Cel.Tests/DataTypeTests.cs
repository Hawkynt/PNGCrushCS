using FileFormat.Cel;

namespace FileFormat.Cel.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CelFile_DefaultPixelData_IsNull() {
    var file = new CelFile();
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void CelFile_DefaultPalette_IsNull() {
    var file = new CelFile();
    Assert.That(file.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void CelFile_DefaultWidth_IsZero() {
    var file = new CelFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CelFile_DefaultHeight_IsZero() {
    var file = new CelFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CelFile_DefaultBitsPerPixel_IsZero() {
    var file = new CelFile();
    Assert.That(file.BitsPerPixel, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CelFile_DefaultXOffset_IsZero() {
    var file = new CelFile();
    Assert.That(file.XOffset, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CelFile_DefaultYOffset_IsZero() {
    var file = new CelFile();
    Assert.That(file.YOffset, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CelFile_InitProperties_RoundTrip() {
    var pixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55 };
    var palette = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 };
    var file = new CelFile {
      Width = 4,
      Height = 1,
      BitsPerPixel = 8,
      XOffset = 10,
      YOffset = 20,
      PixelData = pixelData,
      Palette = palette
    };

    Assert.That(file.Width, Is.EqualTo(4));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));
    Assert.That(file.XOffset, Is.EqualTo(10));
    Assert.That(file.YOffset, Is.EqualTo(20));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
    Assert.That(file.Palette, Is.SameAs(palette));
  }
}
