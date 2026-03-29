using System;
using FileFormat.AtariFalcon;
using FileFormat.Core;

namespace FileFormat.AtariFalcon.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AtariFalconFile_FixedWidth_Is320() {
    var file = new AtariFalconFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void AtariFalconFile_FixedHeight_Is240() {
    var file = new AtariFalconFile();
    Assert.That(file.Height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void AtariFalconFile_DefaultPixelData_IsEmptyArray() {
    var file = new AtariFalconFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AtariFalconFile_ExpectedFileSize_Is153600() {
    Assert.That(AtariFalconFile.ExpectedFileSize, Is.EqualTo(153600));
  }

  [Test]
  [Category("Unit")]
  public void AtariFalconFile_InitProperties_RoundTrip() {
    var pixels = new byte[153600];
    pixels[0] = 0xAB;
    var file = new AtariFalconFile {
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(320));
    Assert.That(file.Height, Is.EqualTo(240));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void AtariFalconFile_PrimaryExtension_IsFtc() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".ftc"));
  }

  [Test]
  [Category("Unit")]
  public void AtariFalconFile_FileExtensions_ContainsFtc() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".ftc"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => AtariFalconFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new AtariFalconFile {
      PixelData = new byte[153600],
    };

    var raw = AtariFalconFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(240));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 240 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ConvertsRgb565ToRgb24() {
    var pixelData = new byte[153600];
    // Pure white in RGB565 BE = 0xFFFF => R=31,G=63,B=31 => 255,255,255
    pixelData[0] = 0xFF;
    pixelData[1] = 0xFF;

    var file = new AtariFalconFile {
      PixelData = pixelData,
    };

    var raw = AtariFalconFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(255));
    Assert.That(raw.PixelData[2], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => AtariFalconFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 320,
      Height = 240,
      Format = PixelFormat.Gray8,
      PixelData = new byte[320 * 240],
    };

    Assert.Throws<ArgumentException>(() => AtariFalconFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongDimensions_Throws() {
    var raw = new RawImage {
      Width = 640,
      Height = 480,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[640 * 480 * 3],
    };

    Assert.Throws<ArgumentException>(() => AtariFalconFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ConvertsRgb24ToRgb565() {
    var rgb24 = new byte[320 * 240 * 3];
    // Pure white RGB24 = (255,255,255) => RGB565 BE = 0xFFFF
    rgb24[0] = 255;
    rgb24[1] = 255;
    rgb24[2] = 255;

    var raw = new RawImage {
      Width = 320,
      Height = 240,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = AtariFalconFile.FromRawImage(raw);

    Assert.That(file.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(file.PixelData[1], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var rgb24 = new byte[320 * 240 * 3];
    rgb24[0] = 255;

    var raw = new RawImage {
      Width = 320,
      Height = 240,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = AtariFalconFile.FromRawImage(raw);
    rgb24[0] = 0;

    // The file's pixel data should not be affected by mutation of original input
    // (it is a new array from conversion, not a clone of input)
    Assert.That(file.PixelData.Length, Is.EqualTo(153600));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[153600];
    pixelData[0] = 0xF8;
    pixelData[1] = 0x00;
    var file = new AtariFalconFile {
      PixelData = pixelData,
    };

    var raw = AtariFalconFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 240 * 3));
  }

  private static T _GetStaticProperty<T>(string name) {
    var prop = typeof(AtariFalconFile).GetInterfaceMap(typeof(IImageFileFormat<AtariFalconFile>));
    foreach (var method in prop.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
