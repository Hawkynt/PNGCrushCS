using System;
using FileFormat.Core;
using FileFormat.DuneGraph;

namespace FileFormat.DuneGraph.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_FixedWidth_Is320() {
    var file = new DuneGraphFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_FixedHeight_Is200() {
    var file = new DuneGraphFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_DefaultPixelData_IsEmptyArray() {
    var file = new DuneGraphFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_DefaultPalette_IsEmptyArray() {
    var file = new DuneGraphFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_DefaultIsCompressed_IsFalse() {
    var file = new DuneGraphFile();
    Assert.That(file.IsCompressed, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_UncompressedFileSize_Is65024() {
    Assert.That(DuneGraphFile.UncompressedFileSize, Is.EqualTo(65024));
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_PaletteDataSize_Is1024() {
    Assert.That(DuneGraphFile.PaletteDataSize, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_PixelDataSize_Is64000() {
    Assert.That(DuneGraphFile.PixelDataSize, Is.EqualTo(64000));
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_InitProperties_RoundTrip() {
    var palette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    palette[0] = 0xAB;
    var pixels = new byte[DuneGraphFile.PixelDataSize];
    pixels[0] = 0xCD;

    var file = new DuneGraphFile {
      IsCompressed = true,
      Palette = palette,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(320));
    Assert.That(file.Height, Is.EqualTo(200));
    Assert.That(file.IsCompressed, Is.True);
    Assert.That(file.Palette, Is.SameAs(palette));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette[0], Is.EqualTo(0xAB));
    Assert.That(file.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_PrimaryExtension_IsDg1() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".dg1"));
  }

  [Test]
  [Category("Unit")]
  public void DuneGraphFile_FileExtensions_ContainsBoth() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(2));
    Assert.That(exts, Does.Contain(".dg1"));
    Assert.That(exts, Does.Contain(".dc1"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => DuneGraphFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new DuneGraphFile {
      Palette = new byte[DuneGraphFile.PaletteEntryCount * 3],
      PixelData = new byte[DuneGraphFile.PixelDataSize],
    };

    var raw = DuneGraphFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => DuneGraphFile.FromRawImage(null!));
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
    Assert.Throws<ArgumentException>(() => DuneGraphFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongDimensions_Throws() {
    var raw = new RawImage {
      Width = 640,
      Height = 480,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[640 * 480],
      Palette = new byte[768],
      PaletteCount = 256,
    };
    Assert.Throws<ArgumentException>(() => DuneGraphFile.FromRawImage(raw));
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
    Assert.Throws<ArgumentException>(() => DuneGraphFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    pixelData[0] = 0x42;
    var file = new DuneGraphFile {
      Palette = new byte[DuneGraphFile.PaletteEntryCount * 3],
      PixelData = pixelData,
    };

    var raw = DuneGraphFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixelData = new byte[320 * 200];
    pixelData[0] = 0x42;
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = new byte[768],
      PaletteCount = 256,
    };

    var file = DuneGraphFile.FromRawImage(raw);
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
    var prop = typeof(DuneGraphFile).GetInterfaceMap(typeof(IImageFileFormat<DuneGraphFile>));
    foreach (var method in prop.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
