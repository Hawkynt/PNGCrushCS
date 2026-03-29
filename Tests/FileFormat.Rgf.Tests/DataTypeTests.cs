using FileFormat.Rgf;

namespace FileFormat.Rgf.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void RgfFile_DefaultPixelData_IsEmptyArray() {
    var file = new RgfFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RgfFile_DefaultWidth_IsZero() {
    var file = new RgfFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RgfFile_DefaultHeight_IsZero() {
    var file = new RgfFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RgfFile_InitProperties_RoundTrip() {
    var pixelData = new byte[] { 0xFF, 0x00 };
    var file = new RgfFile {
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
  public void RgfFile_ToRawImage_ReturnsIndexed1() {
    var file = new RgfFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[] { 0xFF }
    };

    var raw = file.ToRawImage();

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed1));
    Assert.That(raw.Width, Is.EqualTo(8));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.Palette!.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void RgfFile_ToRawImage_PaletteIsBlackAndWhite() {
    var file = new RgfFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[] { 0x00 }
    };

    var raw = file.ToRawImage();

    // Entry 0: black (0, 0, 0)
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    // Entry 1: white (255, 255, 255)
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void RgfFile_ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0xAA };
    var file = new RgfFile {
      Width = 8,
      Height = 1,
      PixelData = pixelData
    };

    var raw = file.ToRawImage();

    Assert.That(raw.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RgfFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<System.ArgumentNullException>(() => RgfFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void RgfFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new FileFormat.Core.RawImage {
      Width = 8,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[24]
    };

    Assert.Throws<System.ArgumentException>(() => RgfFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void RgfFile_FromRawImage_Valid() {
    var raw = new FileFormat.Core.RawImage {
      Width = 8,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Indexed1,
      PixelData = new byte[] { 0xFF, 0x00 },
      Palette = new byte[] { 0, 0, 0, 255, 255, 255 },
      PaletteCount = 2
    };

    var file = RgfFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0xFF, 0x00 }));
    Assert.That(file.PixelData, Is.Not.SameAs(raw.PixelData));
  }
}
