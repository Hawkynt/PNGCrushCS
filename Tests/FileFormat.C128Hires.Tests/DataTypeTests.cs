using System;
using FileFormat.Core;
using FileFormat.C128Hires;

namespace FileFormat.C128Hires.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void C128HiresFile_DefaultWidth_Is320() {
    var file = new C128HiresFile();

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_DefaultHeight_Is200() {
    var file = new C128HiresFile();

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_DefaultRawData_IsEmpty() {
    var file = new C128HiresFile();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0xAB, 0xCD };
    var file = new C128HiresFile { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_ExpectedFileSize_Is8000() {
    Assert.That(C128HiresFile.ExpectedFileSize, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_PixelWidth_Is320() {
    Assert.That(C128HiresFile.PixelWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_PixelHeight_Is200() {
    Assert.That(C128HiresFile.PixelHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_CellsX_Is40() {
    Assert.That(C128HiresFile.CellsX, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_CellsY_Is25() {
    Assert.That(C128HiresFile.CellsY, Is.EqualTo(25));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_CellsXTimesCellsYTimes8_EqualsFileSize() {
    Assert.That(C128HiresFile.CellsX * C128HiresFile.CellsY * 8, Is.EqualTo(C128HiresFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128HiresFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128HiresFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<ArgumentException>(() => C128HiresFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 200,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[640 / 8 * 200],
    };

    Assert.Throws<ArgumentException>(() => C128HiresFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new C128HiresFile { RawData = new byte[C128HiresFile.ExpectedFileSize] };

    var raw = C128HiresFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_ToRawImage_HasCorrectDimensions() {
    var file = new C128HiresFile { RawData = new byte[C128HiresFile.ExpectedFileSize] };

    var raw = C128HiresFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_ToRawImage_HasCorrectPalette() {
    var file = new C128HiresFile { RawData = new byte[C128HiresFile.ExpectedFileSize] };

    var raw = C128HiresFile.ToRawImage(file);

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
  public void C128HiresFile_ToRawImage_PixelDataSize() {
    var file = new C128HiresFile { RawData = new byte[C128HiresFile.ExpectedFileSize] };

    var raw = C128HiresFile.ToRawImage(file);

    var expectedSize = (C128HiresFile.PixelWidth / 8) * C128HiresFile.PixelHeight;
    Assert.That(raw.PixelData.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void C128HiresFile_ToRawImage_ClonesPixelData() {
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    rawData[0] = 0xAA;
    var file = new C128HiresFile { RawData = rawData };

    var raw1 = C128HiresFile.ToRawImage(file);
    var raw2 = C128HiresFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
