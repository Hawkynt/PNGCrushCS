using System;
using FileFormat.Core;
using FileFormat.CoCo;

namespace FileFormat.CoCo.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CoCoFile_DefaultWidth_Is256() {
    var file = new CoCoFile();

    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_DefaultHeight_Is192() {
    var file = new CoCoFile();

    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_DefaultRawData_IsEmpty() {
    var file = new CoCoFile();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0x3F, 0x2A };
    var file = new CoCoFile { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_ExpectedFileSize_Is6144() {
    Assert.That(CoCoFile.ExpectedFileSize, Is.EqualTo(6144));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_PixelWidth_Is256() {
    Assert.That(CoCoFile.PixelWidth, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_PixelHeight_Is192() {
    Assert.That(CoCoFile.PixelHeight, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_BytesPerRow_Is32() {
    Assert.That(CoCoFile.BytesPerRow, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCoFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCoFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 256,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 192 * 3],
    };

    Assert.Throws<ArgumentException>(() => CoCoFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[320 / 8 * 200],
    };

    Assert.Throws<ArgumentException>(() => CoCoFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new CoCoFile { RawData = new byte[6144] };

    var raw = CoCoFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_ToRawImage_HasCorrectDimensions() {
    var file = new CoCoFile { RawData = new byte[6144] };

    var raw = CoCoFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_ToRawImage_HasCorrectPalette() {
    var file = new CoCoFile { RawData = new byte[6144] };

    var raw = CoCoFile.ToRawImage(file);

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
  public void CoCoFile_ToRawImage_PixelDataSize() {
    var file = new CoCoFile { RawData = new byte[6144] };

    var raw = CoCoFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(32 * 192));
  }

  [Test]
  [Category("Unit")]
  public void CoCoFile_ToRawImage_ClonesPixelData() {
    var rawData = new byte[6144];
    rawData[0] = 0x3F;
    var file = new CoCoFile { RawData = rawData };

    var raw1 = CoCoFile.ToRawImage(file);
    var raw2 = CoCoFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
