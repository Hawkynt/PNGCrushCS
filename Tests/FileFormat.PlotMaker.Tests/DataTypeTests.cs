using System;
using FileFormat.Core;
using FileFormat.PlotMaker;

namespace FileFormat.PlotMaker.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_DefaultWidth_IsZero() {
    var file = new PlotMakerFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_DefaultHeight_IsZero() {
    var file = new PlotMakerFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_DefaultPixelData_IsEmpty() {
    var file = new PlotMakerFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_InitWidth_StoresCorrectly() {
    var file = new PlotMakerFile { Width = 320 };

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_InitHeight_StoresCorrectly() {
    var file = new PlotMakerFile { Height = 200 };

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_InitPixelData_StoresReference() {
    var pixelData = new byte[] { 0xFF, 0xAA };
    var file = new PlotMakerFile { PixelData = pixelData };

    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_HeaderSize_Is4() {
    Assert.That(PlotMakerFile.HeaderSize, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PlotMakerFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PlotMakerFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[16 * 16 * 3],
    };

    Assert.Throws<ArgumentException>(() => PlotMakerFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_ToRawImage_ReturnsIndexed1() {
    var file = new PlotMakerFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1],
    };

    var raw = PlotMakerFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_ToRawImage_CorrectDimensions() {
    var file = new PlotMakerFile {
      Width = 16,
      Height = 8,
      PixelData = new byte[16],
    };

    var raw = PlotMakerFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(16));
    Assert.That(raw.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_ToRawImage_HasBlackWhitePalette() {
    var file = new PlotMakerFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1],
    };

    var raw = PlotMakerFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    // Entry 0: black
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    // Entry 1: white
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0xFF, 0xAA };
    var file = new PlotMakerFile {
      Width = 16,
      Height = 1,
      PixelData = pixelData,
    };

    var raw1 = PlotMakerFile.ToRawImage(file);
    var raw2 = PlotMakerFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_FromRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0xFF, 0xAA };
    var raw = new RawImage {
      Width = 16,
      Height = 1,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = new byte[] { 0, 0, 0, 255, 255, 255 },
      PaletteCount = 2,
    };

    var file = PlotMakerFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(file.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void PlotMakerFile_RoundTrip_Functional() {
    var file = new PlotMakerFile {
      Width = 16,
      Height = 16,
      PixelData = new byte[32],
    };

    var bytes = PlotMakerWriter.ToBytes(file);
    var restored = PlotMakerReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
  }
}
