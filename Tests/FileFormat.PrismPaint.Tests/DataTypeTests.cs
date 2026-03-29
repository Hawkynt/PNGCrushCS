using System;
using FileFormat.Core;
using FileFormat.PrismPaint;

namespace FileFormat.PrismPaint.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_DefaultWidth_IsZero() {
    var file = new PrismPaintFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_DefaultHeight_IsZero() {
    var file = new PrismPaintFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_DefaultPixelData_IsEmptyArray() {
    var file = new PrismPaintFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_DefaultPalette_IsEmptyArray() {
    var file = new PrismPaintFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_HeaderSize_Is4() {
    Assert.That(PrismPaintFile.HeaderSize, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_PaletteDataSize_Is1024() {
    Assert.That(PrismPaintFile.PaletteDataSize, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_MinFileSize_Is1029() {
    Assert.That(PrismPaintFile.MinFileSize, Is.EqualTo(4 + 1024 + 1));
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_InitProperties_RoundTrip() {
    var palette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    palette[0] = 0xAB;
    var pixels = new byte[100];
    pixels[0] = 0xCD;

    var file = new PrismPaintFile {
      Width = 10,
      Height = 10,
      Palette = palette,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(10));
    Assert.That(file.Palette, Is.SameAs(palette));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette[0], Is.EqualTo(0xAB));
    Assert.That(file.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_PrimaryExtension_IsPnt() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".pnt"));
  }

  [Test]
  [Category("Unit")]
  public void PrismPaintFile_FileExtensions_ContainsBoth() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(2));
    Assert.That(exts, Does.Contain(".pnt"));
    Assert.That(exts, Does.Contain(".tpi"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => PrismPaintFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new PrismPaintFile {
      Width = 100,
      Height = 50,
      Palette = new byte[PrismPaintFile.PaletteEntryCount * 3],
      PixelData = new byte[100 * 50],
    };

    var raw = PrismPaintFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(100));
    Assert.That(raw.Height, Is.EqualTo(50));
    Assert.That(raw.PixelData.Length, Is.EqualTo(100 * 50));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => PrismPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };
    Assert.Throws<ArgumentException>(() => PrismPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NullPalette_Throws() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[320 * 200],
    };
    Assert.Throws<ArgumentException>(() => PrismPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ZeroDimensions_Throws() {
    var raw = new RawImage {
      Width = 0,
      Height = 100,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[0],
      Palette = new byte[768],
      PaletteCount = 256,
    };
    Assert.Throws<ArgumentException>(() => PrismPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TooLargeDimensions_Throws() {
    var raw = new RawImage {
      Width = 70000,
      Height = 100,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[100],
      Palette = new byte[768],
      PaletteCount = 256,
    };
    Assert.Throws<ArgumentException>(() => PrismPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[100];
    pixelData[0] = 0x42;
    var file = new PrismPaintFile {
      Width = 10,
      Height = 10,
      Palette = new byte[PrismPaintFile.PaletteEntryCount * 3],
      PixelData = pixelData,
    };

    var raw = PrismPaintFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixelData = new byte[100];
    pixelData[0] = 0x42;
    var raw = new RawImage {
      Width = 10,
      Height = 10,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = new byte[768],
      PaletteCount = 256,
    };

    var file = PrismPaintFile.FromRawImage(raw);
    pixelData[0] = 0x00;

    Assert.That(file.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void Capabilities_IsIndexedOnly() {
    var caps = _GetStaticProperty<FormatCapability>("Capabilities");
    Assert.That(caps, Is.EqualTo(FormatCapability.IndexedOnly));
  }

  private static T _GetStaticProperty<T>(string name) {
    var prop = typeof(PrismPaintFile).GetInterfaceMap(typeof(IImageFileFormat<PrismPaintFile>));
    foreach (var method in prop.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
