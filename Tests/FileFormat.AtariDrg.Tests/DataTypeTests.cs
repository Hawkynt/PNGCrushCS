using System;
using FileFormat.Core;
using FileFormat.AtariDrg;

namespace FileFormat.AtariDrg.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_DefaultWidth_Is160() {
    var file = new AtariDrgFile();
    Assert.That(file.Width, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_DefaultHeight_Is192() {
    var file = new AtariDrgFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_DefaultPixelData_IsEmpty() {
    var file = new AtariDrgFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_DefaultPalette_IsEmpty() {
    var file = new AtariDrgFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_InitProperties_StoreCorrectly() {
    var pixels = new byte[] { 0, 1, 2, 3 };
    var palette = new byte[] { 0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255 };
    var file = new AtariDrgFile {
      PixelData = pixels,
      Palette = palette,
    };

    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_FileSize_Is7680() {
    Assert.That(AtariDrgFile.FileSize, Is.EqualTo(7680));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_PixelWidth_Is160() {
    Assert.That(AtariDrgFile.PixelWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_PixelHeight_Is192() {
    Assert.That(AtariDrgFile.PixelHeight, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_BytesPerRow_Is40() {
    Assert.That(AtariDrgFile.BytesPerRow, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_BitsPerPixel_Is2() {
    Assert.That(AtariDrgFile.BitsPerPixel, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_ColorCount_Is4() {
    Assert.That(AtariDrgFile.ColorCount, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariDrgFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariDrgFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 160,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[160 * 192 * 3],
    };
    Assert.Throws<ArgumentException>(() => AtariDrgFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[320 * 200],
      PaletteCount = 4,
    };
    Assert.Throws<ArgumentException>(() => AtariDrgFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_FromRawImage_TooManyColors_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 160,
      Height = 192,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[160 * 192],
      PaletteCount = 256,
    };
    Assert.Throws<ArgumentException>(() => AtariDrgFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_ToRawImage_ReturnsIndexed8Format() {
    var file = new AtariDrgFile { PixelData = new byte[160 * 192] };
    var raw = AtariDrgFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_ToRawImage_HasCorrectDimensions() {
    var file = new AtariDrgFile { PixelData = new byte[160 * 192] };
    var raw = AtariDrgFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_ToRawImage_HasGrayscalePalette() {
    var file = new AtariDrgFile { PixelData = new byte[160 * 192] };
    var raw = AtariDrgFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(4));
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[3], Is.EqualTo(85));
    Assert.That(raw.Palette[6], Is.EqualTo(170));
    Assert.That(raw.Palette[9], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_ToRawImage_ClonesPixelData() {
    var pixels = new byte[160 * 192];
    pixels[0] = 2;
    var file = new AtariDrgFile { PixelData = pixels };

    var raw1 = AtariDrgFile.ToRawImage(file);
    var raw2 = AtariDrgFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_PrimaryExtension() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".drg"));
  }

  [Test]
  [Category("Unit")]
  public void AtariDrgFile_FileExtensions() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".drg"));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(AtariDrgFile).GetInterfaceMap(typeof(IImageFileFormat<AtariDrgFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
