using System;
using FileFormat.Core;
using FileFormat.Pagefox;

namespace FileFormat.Pagefox.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PagefoxFile_DefaultWidth_Is640() {
    var file = new PagefoxFile();
    Assert.That(file.Width, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_DefaultHeight_Is200() {
    var file = new PagefoxFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_DefaultRawData_IsEmpty() {
    var file = new PagefoxFile();
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_ExpectedFileSize_Is16384() {
    Assert.That(PagefoxFile.ExpectedFileSize, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0x3F, 0x2A };
    var file = new PagefoxFile { RawData = rawData };
    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PagefoxFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PagefoxFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[640 * 200 * 3],
    };
    Assert.Throws<ArgumentException>(() => PagefoxFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[320 / 8 * 200],
    };
    Assert.Throws<ArgumentException>(() => PagefoxFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new PagefoxFile { RawData = new byte[16384] };
    var raw = PagefoxFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_ToRawImage_HasCorrectDimensions() {
    var file = new PagefoxFile { RawData = new byte[16384] };
    var raw = PagefoxFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(640));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_ToRawImage_HasCorrectPalette() {
    var file = new PagefoxFile { RawData = new byte[16384] };
    var raw = PagefoxFile.ToRawImage(file);
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void PagefoxFile_ToRawImage_PixelDataSize() {
    var file = new PagefoxFile { RawData = new byte[16384] };
    var raw = PagefoxFile.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(80 * 200));
  }
}
