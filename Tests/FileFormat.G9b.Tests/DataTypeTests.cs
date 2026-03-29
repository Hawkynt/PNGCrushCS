using System;
using FileFormat.G9b;
using FileFormat.Core;

namespace FileFormat.G9b.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void G9bScreenMode_Indexed8_Is3() {
    Assert.That((int)G9bScreenMode.Indexed8, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void G9bScreenMode_Rgb555_Is5() {
    Assert.That((int)G9bScreenMode.Rgb555, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void G9bFile_DefaultPixelData_IsEmpty() {
    var file = new G9bFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void G9bFile_DefaultWidth_IsZero() {
    var file = new G9bFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void G9bFile_DefaultHeight_IsZero() {
    var file = new G9bFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void G9bFile_DefaultHeaderSize_Is11() {
    var file = new G9bFile();
    Assert.That(file.HeaderSize, Is.EqualTo(11));
  }

  [Test]
  [Category("Unit")]
  public void G9bFile_InitProperties() {
    var file = new G9bFile {
      Width = 256,
      Height = 212,
      ScreenMode = G9bScreenMode.Rgb555,
      ColorMode = 1,
      PixelData = new byte[256 * 212 * 2],
    };

    Assert.That(file.Width, Is.EqualTo(256));
    Assert.That(file.Height, Is.EqualTo(212));
    Assert.That(file.ScreenMode, Is.EqualTo(G9bScreenMode.Rgb555));
    Assert.That(file.ColorMode, Is.EqualTo(1));
    Assert.That(file.PixelData.Length, Is.EqualTo(256 * 212 * 2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => G9bFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => G9bFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[64],
    };
    Assert.Throws<ArgumentException>(() => G9bFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Mode3_ProducesGray8() {
    var file = new G9bFile {
      Width = 4,
      Height = 4,
      ScreenMode = G9bScreenMode.Indexed8,
      PixelData = new byte[16],
    };

    var raw = G9bFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Mode5_ProducesRgb24() {
    var file = new G9bFile {
      Width = 4,
      Height = 4,
      ScreenMode = G9bScreenMode.Rgb555,
      PixelData = new byte[32],
    };

    var raw = G9bFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void MinHeaderSize_Is11() {
    Assert.That(G9bReader.MinHeaderSize, Is.EqualTo(11));
  }

  [Test]
  [Category("Unit")]
  public void DefaultHeaderSize_Is11() {
    Assert.That(G9bReader.DefaultHeaderSize, Is.EqualTo(11));
  }

  [Test]
  [Category("Unit")]
  public void Magic_IsG9B() {
    Assert.That(G9bReader.Magic[0], Is.EqualTo(0x47));
    Assert.That(G9bReader.Magic[1], Is.EqualTo(0x39));
    Assert.That(G9bReader.Magic[2], Is.EqualTo(0x42));
  }
}
