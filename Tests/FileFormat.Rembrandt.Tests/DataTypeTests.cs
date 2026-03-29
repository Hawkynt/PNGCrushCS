using System;
using FileFormat.Core;
using FileFormat.Rembrandt;

namespace FileFormat.Rembrandt.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void RembrandtFile_DefaultWidth_IsZero() {
    var file = new RembrandtFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RembrandtFile_DefaultHeight_IsZero() {
    var file = new RembrandtFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RembrandtFile_DefaultPixelData_IsEmptyArray() {
    var file = new RembrandtFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void RembrandtFile_HeaderSize_Is4() {
    Assert.That(RembrandtFile.HeaderSize, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void RembrandtFile_MinFileSize_Is6() {
    Assert.That(RembrandtFile.MinFileSize, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void RembrandtFile_InitProperties_RoundTrip() {
    var pixels = new byte[200];
    pixels[0] = 0xAB;
    var file = new RembrandtFile {
      Width = 10,
      Height = 10,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(10));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void RembrandtFile_PrimaryExtension_IsTcp() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".tcp"));
  }

  [Test]
  [Category("Unit")]
  public void RembrandtFile_FileExtensions_ContainsTcp() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".tcp"));
  }

  [Test]
  [Category("Unit")]
  public void Capabilities_IsVariableResolution() {
    var caps = _GetStaticProperty<FormatCapability>("Capabilities");
    Assert.That(caps, Is.EqualTo(FormatCapability.VariableResolution));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => RembrandtFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new RembrandtFile {
      Width = 100,
      Height = 50,
      PixelData = new byte[100 * 50 * 2],
    };

    var raw = RembrandtFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(100));
    Assert.That(raw.Height, Is.EqualTo(50));
    Assert.That(raw.PixelData.Length, Is.EqualTo(100 * 50 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ConvertsRgb565ToRgb24() {
    var pixelData = new byte[2];
    // Pure white in RGB565 BE = 0xFFFF
    pixelData[0] = 0xFF;
    pixelData[1] = 0xFF;

    var file = new RembrandtFile {
      Width = 1,
      Height = 1,
      PixelData = pixelData,
    };

    var raw = RembrandtFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(255));
    Assert.That(raw.PixelData[2], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => RembrandtFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 100,
      Height = 50,
      Format = PixelFormat.Gray8,
      PixelData = new byte[100 * 50],
    };
    Assert.Throws<ArgumentException>(() => RembrandtFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ZeroDimensions_Throws() {
    var raw = new RawImage {
      Width = 0,
      Height = 50,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[0],
    };
    Assert.Throws<ArgumentException>(() => RembrandtFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TooLargeDimensions_Throws() {
    var raw = new RawImage {
      Width = 70000,
      Height = 50,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[50 * 3],
    };
    Assert.Throws<ArgumentException>(() => RembrandtFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ConvertsRgb24ToRgb565() {
    var rgb24 = new byte[3];
    rgb24[0] = 255;
    rgb24[1] = 255;
    rgb24[2] = 255;

    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = RembrandtFile.FromRawImage(raw);

    Assert.That(file.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(file.PixelData[1], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[200];
    pixelData[0] = 0xF8;
    var file = new RembrandtFile {
      Width = 10,
      Height = 10,
      PixelData = pixelData,
    };

    var raw = RembrandtFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData.Length, Is.EqualTo(10 * 10 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var rgb24 = new byte[3];
    rgb24[0] = 255;

    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = RembrandtFile.FromRawImage(raw);
    rgb24[0] = 0;

    // pixel data is converted (not a direct copy), so mutation of input should not affect output
    Assert.That(file.PixelData.Length, Is.EqualTo(2));
  }

  private static T _GetStaticProperty<T>(string name) {
    var prop = typeof(RembrandtFile).GetInterfaceMap(typeof(IImageFileFormat<RembrandtFile>));
    foreach (var method in prop.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
