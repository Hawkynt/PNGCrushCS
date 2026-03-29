using System;
using FileFormat.Core;
using FileFormat.MobyDick;

namespace FileFormat.MobyDick.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MobyDickFile_DefaultWidth_Is320() {
    var file = new MobyDickFile();

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_DefaultHeight_Is200() {
    var file = new MobyDickFile();

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_DefaultPalette_IsEmpty() {
    var file = new MobyDickFile();

    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_DefaultPixelData_IsEmpty() {
    var file = new MobyDickFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_InitPalette_StoresCorrectly() {
    var data = new byte[] { 0x12, 0x34, 0x56 };
    var file = new MobyDickFile { Palette = data };

    Assert.That(file.Palette, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_InitPixelData_StoresCorrectly() {
    var data = new byte[] { 0xAB, 0xCD };
    var file = new MobyDickFile { PixelData = data };

    Assert.That(file.PixelData, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_FixedWidth_Is320() {
    Assert.That(MobyDickFile.FixedWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_FixedHeight_Is200() {
    Assert.That(MobyDickFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_ExpectedFileSize_Is64768() {
    Assert.That(MobyDickFile.ExpectedFileSize, Is.EqualTo(64768));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_PaletteDataSize_Is768() {
    Assert.That(MobyDickFile.PaletteDataSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_PixelDataSize_Is64000() {
    Assert.That(MobyDickFile.PixelDataSize, Is.EqualTo(64000));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_SectionSizes_SumToExpectedFileSize() {
    var sum = MobyDickFile.PaletteDataSize + MobyDickFile.PixelDataSize;
    Assert.That(sum, Is.EqualTo(MobyDickFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_PaletteDataSize_Is256RGBTriples() {
    Assert.That(MobyDickFile.PaletteDataSize, Is.EqualTo(256 * 3));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_PixelDataSize_IsWidthTimesHeight() {
    Assert.That(MobyDickFile.PixelDataSize, Is.EqualTo(MobyDickFile.FixedWidth * MobyDickFile.FixedHeight));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MobyDickFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MobyDickFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<NotSupportedException>(() => MobyDickFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_ToRawImage_ReturnsRgb24Format() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    var raw = MobyDickFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_ToRawImage_HasCorrectDimensions() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    var raw = MobyDickFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_ToRawImage_PixelDataSize() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    var raw = MobyDickFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_ToRawImage_PaletteColorApplied() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    // Set color 0 to red (255, 0, 0)
    file.Palette[0] = 255;
    file.Palette[1] = 0;
    file.Palette[2] = 0;

    // First pixel uses index 0
    file.PixelData[0] = 0;

    var raw = MobyDickFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(255)); // R
    Assert.That(raw.PixelData[1], Is.EqualTo(0));   // G
    Assert.That(raw.PixelData[2], Is.EqualTo(0));   // B
  }

  [Test]
  [Category("Unit")]
  public void MobyDickFile_ToRawImage_NonZeroPaletteIndex() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    // Set color 5 to (10, 20, 30)
    file.Palette[15] = 10;
    file.Palette[16] = 20;
    file.Palette[17] = 30;

    // First pixel uses index 5
    file.PixelData[0] = 5;

    var raw = MobyDickFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(10));
    Assert.That(raw.PixelData[1], Is.EqualTo(20));
    Assert.That(raw.PixelData[2], Is.EqualTo(30));
  }
}
