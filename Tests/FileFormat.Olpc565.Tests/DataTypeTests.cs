using FileFormat.Olpc565;
using FileFormat.Core;

namespace FileFormat.Olpc565.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Olpc565File_DefaultPixelData_IsEmptyArray() {
    var file = new Olpc565File();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Olpc565File_InitProperties_RoundTrip() {
    var pixelData = new byte[] { 0xFF, 0xFF, 0x00, 0x00 };
    var file = new Olpc565File {
      Width = 2,
      Height = 1,
      PixelData = pixelData
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_FormatIsRgb24() {
    var file = new Olpc565File {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x00, 0x00 }
    };

    var raw = Olpc565File.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_BlackPixel() {
    var file = new Olpc565File {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x00, 0x00 }
    };

    var raw = Olpc565File.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));
    Assert.That(raw.PixelData[1], Is.EqualTo(0));
    Assert.That(raw.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_WhitePixel() {
    var file = new Olpc565File {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0xFF, 0xFF } // R=31, G=63, B=31
    };

    var raw = Olpc565File.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(255));
    Assert.That(raw.PixelData[2], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[1],
      Palette = new byte[6],
      PaletteCount = 2,
    };

    Assert.Throws<System.ArgumentException>(() => Olpc565File.FromRawImage(raw));
  }
}
