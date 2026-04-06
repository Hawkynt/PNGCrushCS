using FileFormat.SunIcon;

namespace FileFormat.SunIcon.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SunIconFile_DefaultPixelData_IsEmptyArray() {
    var file = new SunIconFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SunIconFile_DefaultWidth_IsZero() {
    var file = new SunIconFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SunIconFile_DefaultHeight_IsZero() {
    var file = new SunIconFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SunIconFile_InitProperties_RoundTrip() {
    var pixelData = new byte[] { 0xFF, 0x00 };
    var file = new SunIconFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData
    };

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void SunIconFile_ToRawImage_ReturnsIndexed1() {
    var file = new SunIconFile {
      Width = 8,
      Height = 1,
      PixelData = [0xFF]
    };

    var raw = SunIconFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void SunIconFile_ToRawImage_HasBlackWhitePalette() {
    var file = new SunIconFile {
      Width = 8,
      Height = 1,
      PixelData = [0xFF]
    };

    var raw = SunIconFile.ToRawImage(file);

    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Palette, Is.Not.Null);
    // Index 0 = black (0,0,0)
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    // Index 1 = white (255,255,255)
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void SunIconFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<System.ArgumentNullException>(() => SunIconFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void SunIconFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new FileFormat.Core.RawImage {
      Width = 8,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[24]
    };

    Assert.Throws<System.ArgumentException>(() => SunIconFile.FromRawImage(raw));
  }
}
