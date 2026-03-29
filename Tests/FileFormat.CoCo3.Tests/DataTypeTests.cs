using System;
using FileFormat.Core;
using FileFormat.CoCo3;

namespace FileFormat.CoCo3.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CoCo3File_DefaultWidth_Is320() {
    var file = new CoCo3File();

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_DefaultHeight_Is200() {
    var file = new CoCo3File();

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_DefaultRawData_IsEmpty() {
    var file = new CoCo3File();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0x3F, 0x2A };
    var file = new CoCo3File { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_ExpectedFileSize_Is32000() {
    Assert.That(CoCo3File.ExpectedFileSize, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_PixelWidth_Is320() {
    Assert.That(CoCo3File.PixelWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_PixelHeight_Is200() {
    Assert.That(CoCo3File.PixelHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_BytesPerRow_Is160() {
    Assert.That(CoCo3File.BytesPerRow, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCo3File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCo3File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<ArgumentException>(() => CoCo3File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 256,
      Height = 192,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[256 * 192],
    };

    Assert.Throws<ArgumentException>(() => CoCo3File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_ToRawImage_ReturnsIndexed8Format() {
    var file = new CoCo3File { RawData = new byte[32000] };

    var raw = CoCo3File.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_ToRawImage_HasCorrectDimensions() {
    var file = new CoCo3File { RawData = new byte[32000] };

    var raw = CoCo3File.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_ToRawImage_HasCorrectPalette() {
    var file = new CoCo3File { RawData = new byte[32000] };

    var raw = CoCo3File.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(16));
    Assert.That(raw.Palette!.Length, Is.EqualTo(48));
    // Palette entry 0: Black (0,0,0)
    Assert.That(raw.Palette[0], Is.EqualTo(0x00));
    Assert.That(raw.Palette[1], Is.EqualTo(0x00));
    Assert.That(raw.Palette[2], Is.EqualTo(0x00));
    // Palette entry 15: White (255,255,255)
    Assert.That(raw.Palette[45], Is.EqualTo(0xFF));
    Assert.That(raw.Palette[46], Is.EqualTo(0xFF));
    Assert.That(raw.Palette[47], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_ToRawImage_PixelDataSize() {
    var file = new CoCo3File { RawData = new byte[32000] };

    var raw = CoCo3File.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200));
  }

  [Test]
  [Category("Unit")]
  public void CoCo3File_ToRawImage_ClonesPixelData() {
    var rawData = new byte[32000];
    rawData[0] = 0x3F;
    var file = new CoCo3File { RawData = rawData };

    var raw1 = CoCo3File.ToRawImage(file);
    var raw2 = CoCo3File.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
