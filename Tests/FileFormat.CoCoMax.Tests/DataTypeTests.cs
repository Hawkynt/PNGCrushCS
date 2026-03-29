using System;
using FileFormat.Core;
using FileFormat.CoCoMax;

namespace FileFormat.CoCoMax.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_DefaultWidth_Is256() {
    var file = new CoCoMaxFile();

    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_DefaultHeight_Is192() {
    var file = new CoCoMaxFile();

    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_DefaultRawData_IsEmpty() {
    var file = new CoCoMaxFile();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0x3F, 0x2A };
    var file = new CoCoMaxFile { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_ExpectedFileSize_Is6144() {
    Assert.That(CoCoMaxFile.ExpectedFileSize, Is.EqualTo(6144));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_PixelWidth_Is256() {
    Assert.That(CoCoMaxFile.PixelWidth, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_PixelHeight_Is192() {
    Assert.That(CoCoMaxFile.PixelHeight, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_BytesPerRow_Is32() {
    Assert.That(CoCoMaxFile.BytesPerRow, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCoMaxFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCoMaxFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 256,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 192 * 3],
    };

    Assert.Throws<ArgumentException>(() => CoCoMaxFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[320 / 8 * 200],
    };

    Assert.Throws<ArgumentException>(() => CoCoMaxFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new CoCoMaxFile { RawData = new byte[6144] };

    var raw = CoCoMaxFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_ToRawImage_HasCorrectDimensions() {
    var file = new CoCoMaxFile { RawData = new byte[6144] };

    var raw = CoCoMaxFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_ToRawImage_HasCorrectPalette() {
    var file = new CoCoMaxFile { RawData = new byte[6144] };

    var raw = CoCoMaxFile.ToRawImage(file);

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
  public void CoCoMaxFile_ToRawImage_PixelDataSize() {
    var file = new CoCoMaxFile { RawData = new byte[6144] };

    var raw = CoCoMaxFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(32 * 192));
  }

  [Test]
  [Category("Unit")]
  public void CoCoMaxFile_ToRawImage_ClonesPixelData() {
    var rawData = new byte[6144];
    rawData[0] = 0x3F;
    var file = new CoCoMaxFile { RawData = rawData };

    var raw1 = CoCoMaxFile.ToRawImage(file);
    var raw2 = CoCoMaxFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
