using System;
using FileFormat.Core;
using FileFormat.C128VDC;

namespace FileFormat.C128VDC.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void C128VDCFile_DefaultWidth_Is640() {
    var file = new C128VDCFile();

    Assert.That(file.Width, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_DefaultHeight_Is200() {
    var file = new C128VDCFile();

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_DefaultRawData_IsEmpty() {
    var file = new C128VDCFile();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0xAB, 0xCD };
    var file = new C128VDCFile { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_ExpectedFileSize_Is16000() {
    Assert.That(C128VDCFile.ExpectedFileSize, Is.EqualTo(16000));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_PixelWidth_Is640() {
    Assert.That(C128VDCFile.PixelWidth, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_PixelHeight_Is200() {
    Assert.That(C128VDCFile.PixelHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_BytesPerRow_Is80() {
    Assert.That(C128VDCFile.BytesPerRow, Is.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_BytesPerRow_TimesHeight_EqualsFileSize() {
    Assert.That(C128VDCFile.BytesPerRow * C128VDCFile.PixelHeight, Is.EqualTo(C128VDCFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128VDCFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128VDCFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[640 * 200 * 3],
    };

    Assert.Throws<ArgumentException>(() => C128VDCFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[320 / 8 * 200],
    };

    Assert.Throws<ArgumentException>(() => C128VDCFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new C128VDCFile { RawData = new byte[C128VDCFile.ExpectedFileSize] };

    var raw = C128VDCFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_ToRawImage_HasCorrectDimensions() {
    var file = new C128VDCFile { RawData = new byte[C128VDCFile.ExpectedFileSize] };

    var raw = C128VDCFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(640));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_ToRawImage_HasCorrectPalette() {
    var file = new C128VDCFile { RawData = new byte[C128VDCFile.ExpectedFileSize] };

    var raw = C128VDCFile.ToRawImage(file);

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
  public void C128VDCFile_ToRawImage_PixelDataSize() {
    var file = new C128VDCFile { RawData = new byte[C128VDCFile.ExpectedFileSize] };

    var raw = C128VDCFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(C128VDCFile.BytesPerRow * C128VDCFile.PixelHeight));
  }

  [Test]
  [Category("Unit")]
  public void C128VDCFile_ToRawImage_ClonesPixelData() {
    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    rawData[0] = 0xAA;
    var file = new C128VDCFile { RawData = rawData };

    var raw1 = C128VDCFile.ToRawImage(file);
    var raw2 = C128VDCFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
