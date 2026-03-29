using System;
using FileFormat.Core;
using FileFormat.AtariGr7;

namespace FileFormat.AtariGr7.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AtariGr7File_DefaultWidth_Is160() {
    var file = new AtariGr7File();
    Assert.That(file.Width, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_DefaultHeight_Is96() {
    var file = new AtariGr7File();
    Assert.That(file.Height, Is.EqualTo(96));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_DefaultPixelData_IsEmpty() {
    var file = new AtariGr7File();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_DefaultPalette_IsEmpty() {
    var file = new AtariGr7File();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_InitProperties_StoreCorrectly() {
    var pixels = new byte[] { 0, 1, 2, 3 };
    var palette = new byte[] { 0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255 };
    var file = new AtariGr7File {
      PixelData = pixels,
      Palette = palette,
    };

    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_FileSize_Is3840() {
    Assert.That(AtariGr7File.FileSize, Is.EqualTo(3840));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_PixelWidth_Is160() {
    Assert.That(AtariGr7File.PixelWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_PixelHeight_Is96() {
    Assert.That(AtariGr7File.PixelHeight, Is.EqualTo(96));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_BytesPerRow_Is40() {
    Assert.That(AtariGr7File.BytesPerRow, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_BitsPerPixel_Is2() {
    Assert.That(AtariGr7File.BitsPerPixel, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_ColorCount_Is4() {
    Assert.That(AtariGr7File.ColorCount, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr7File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr7File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 160,
      Height = 96,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[160 * 96 * 3],
    };
    Assert.Throws<ArgumentException>(() => AtariGr7File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[320 * 200],
      PaletteCount = 4,
    };
    Assert.Throws<ArgumentException>(() => AtariGr7File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_FromRawImage_TooManyColors_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 160,
      Height = 96,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[160 * 96],
      PaletteCount = 256,
    };
    Assert.Throws<ArgumentException>(() => AtariGr7File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_ToRawImage_ReturnsIndexed8Format() {
    var file = new AtariGr7File { PixelData = new byte[160 * 96] };
    var raw = AtariGr7File.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_ToRawImage_HasCorrectDimensions() {
    var file = new AtariGr7File { PixelData = new byte[160 * 96] };
    var raw = AtariGr7File.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(96));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_ToRawImage_HasGrayscalePalette() {
    var file = new AtariGr7File { PixelData = new byte[160 * 96] };
    var raw = AtariGr7File.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(4));
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[3], Is.EqualTo(85));
    Assert.That(raw.Palette[6], Is.EqualTo(170));
    Assert.That(raw.Palette[9], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_ToRawImage_ClonesPixelData() {
    var pixels = new byte[160 * 96];
    pixels[0] = 2;
    var file = new AtariGr7File { PixelData = pixels };

    var raw1 = AtariGr7File.ToRawImage(file);
    var raw2 = AtariGr7File.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_PrimaryExtension() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".gr7"));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr7File_FileExtensions() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".gr7"));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(AtariGr7File).GetInterfaceMap(typeof(IImageFileFormat<AtariGr7File>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
