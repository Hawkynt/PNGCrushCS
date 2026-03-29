using System;
using FileFormat.Core;
using FileFormat.AppleIIDhr;

namespace FileFormat.AppleIIDhr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_DefaultWidth_Is560() {
    var file = new AppleIIDhrFile();

    Assert.That(file.Width, Is.EqualTo(560));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_DefaultHeight_Is192() {
    var file = new AppleIIDhrFile();

    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_DefaultRawData_IsEmpty() {
    var file = new AppleIIDhrFile();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0x7F, 0x2A };
    var file = new AppleIIDhrFile { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_FileSize_Is16384() {
    Assert.That(AppleIIDhrFile.FileSize, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_BankSize_Is8192() {
    Assert.That(AppleIIDhrFile.BankSize, Is.EqualTo(8192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_PixelWidth_Is560() {
    Assert.That(AppleIIDhrFile.PixelWidth, Is.EqualTo(560));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_PixelHeight_Is192() {
    Assert.That(AppleIIDhrFile.PixelHeight, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_BytesPerRowPerBank_Is40() {
    Assert.That(AppleIIDhrFile.BytesPerRowPerBank, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIDhrFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIDhrFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 560,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[560 * 192 * 3],
    };

    Assert.Throws<ArgumentException>(() => AppleIIDhrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[320 / 8 * 200],
    };

    Assert.Throws<ArgumentException>(() => AppleIIDhrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new AppleIIDhrFile { RawData = new byte[16384] };

    var raw = AppleIIDhrFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_ToRawImage_HasCorrectDimensions() {
    var file = new AppleIIDhrFile { RawData = new byte[16384] };

    var raw = AppleIIDhrFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(560));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_ToRawImage_HasCorrectPalette() {
    var file = new AppleIIDhrFile { RawData = new byte[16384] };

    var raw = AppleIIDhrFile.ToRawImage(file);

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
  public void AppleIIDhrFile_ToRawImage_PixelDataSize() {
    var file = new AppleIIDhrFile { RawData = new byte[16384] };

    var raw = AppleIIDhrFile.ToRawImage(file);

    // 560 pixels / 8 = 70 bytes per row, 192 rows
    Assert.That(raw.PixelData.Length, Is.EqualTo(70 * 192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIDhrFile_ToRawImage_ClonesPixelData() {
    var rawData = new byte[16384];
    rawData[0] = 0x7F;
    var file = new AppleIIDhrFile { RawData = rawData };

    var raw1 = AppleIIDhrFile.ToRawImage(file);
    var raw2 = AppleIIDhrFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
