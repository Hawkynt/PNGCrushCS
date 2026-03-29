using System;
using FileFormat.SpookySpritesFalcon;
using FileFormat.Core;

namespace FileFormat.SpookySpritesFalcon.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SpookySpritesFalconFile_DefaultPixelData_IsEmptyArray() {
    var file = new SpookySpritesFalconFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SpookySpritesFalconFile_DefaultDimensions_AreZero() {
    var file = new SpookySpritesFalconFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SpookySpritesFalconFile_InitProperties_RoundTrip() {
    var pixels = new byte[16];
    pixels[0] = 0xAB;
    var file = new SpookySpritesFalconFile {
      Width = 4,
      Height = 2,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(4));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void SpookySpritesFalconFile_PrimaryExtension_IsTre() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".tre"));
  }

  [Test]
  [Category("Unit")]
  public void SpookySpritesFalconFile_FileExtensions_ContainsTre() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".tre"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => SpookySpritesFalconFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new SpookySpritesFalconFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[4 * 2 * 2],
    };

    var raw = SpookySpritesFalconFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.PixelData.Length, Is.EqualTo(4 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ConvertsWhiteRgb565() {
    var pixelData = new byte[2 * 1 * 2];
    pixelData[0] = 0xFF;
    pixelData[1] = 0xFF;

    var file = new SpookySpritesFalconFile {
      Width = 2,
      Height = 1,
      PixelData = pixelData,
    };

    var raw = SpookySpritesFalconFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(255));
    Assert.That(raw.PixelData[2], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => SpookySpritesFalconFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = new byte[4 * 2],
    };

    Assert.Throws<ArgumentException>(() => SpookySpritesFalconFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ConvertsWhiteRgb24ToRgb565() {
    var rgb24 = new byte[2 * 1 * 3];
    rgb24[0] = 255;
    rgb24[1] = 255;
    rgb24[2] = 255;

    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = SpookySpritesFalconFile.FromRawImage(raw);

    Assert.That(file.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(file.PixelData[1], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_PreservesDimensions() {
    var rgb24 = new byte[10 * 5 * 3];
    var raw = new RawImage {
      Width = 10,
      Height = 5,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = SpookySpritesFalconFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(5));
    Assert.That(file.PixelData.Length, Is.EqualTo(10 * 5 * 2));
  }

  private static T _GetStaticProperty<T>(string name) {
    var prop = typeof(SpookySpritesFalconFile).GetInterfaceMap(typeof(IImageFileFormat<SpookySpritesFalconFile>));
    foreach (var method in prop.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
