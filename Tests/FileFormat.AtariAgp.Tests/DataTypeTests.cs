using System;
using FileFormat.Core;
using FileFormat.AtariAgp;

namespace FileFormat.AtariAgp.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AtariAgpMode_HasExpectedValues() {
    Assert.That((int)AtariAgpMode.Graphics8, Is.EqualTo(0));
    Assert.That((int)AtariAgpMode.Graphics7, Is.EqualTo(1));
    Assert.That((int)AtariAgpMode.Graphics8WithColors, Is.EqualTo(2));

    var values = Enum.GetValues<AtariAgpMode>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_DefaultPixelData_IsEmpty() {
    var file = new AtariAgpFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_DefaultPalette_IsEmpty() {
    var file = new AtariAgpFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_DefaultWidth_IsZero() {
    var file = new AtariAgpFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_DefaultHeight_IsZero() {
    var file = new AtariAgpFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_DefaultMode_IsGraphics8() {
    var file = new AtariAgpFile();
    Assert.That(file.Mode, Is.EqualTo(AtariAgpMode.Graphics8));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_DefaultForegroundColor_IsZero() {
    var file = new AtariAgpFile();
    Assert.That(file.ForegroundColor, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_DefaultBackgroundColor_IsZero() {
    var file = new AtariAgpFile();
    Assert.That(file.BackgroundColor, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_InitProperties_StoreCorrectly() {
    var pixels = new byte[] { 0, 1, 0, 1 };
    var palette = new byte[] { 0, 0, 0, 255, 255, 255 };
    var file = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8WithColors,
      PixelData = pixels,
      Palette = palette,
      ForegroundColor = 0xAB,
      BackgroundColor = 0xCD,
    };

    Assert.That(file.Width, Is.EqualTo(320));
    Assert.That(file.Height, Is.EqualTo(192));
    Assert.That(file.Mode, Is.EqualTo(AtariAgpMode.Graphics8WithColors));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette, Is.SameAs(palette));
    Assert.That(file.ForegroundColor, Is.EqualTo(0xAB));
    Assert.That(file.BackgroundColor, Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_FileSizeConstants() {
    Assert.That(AtariAgpFile.FileSizeGr8, Is.EqualTo(7680));
    Assert.That(AtariAgpFile.FileSizeGr7, Is.EqualTo(3840));
    Assert.That(AtariAgpFile.FileSizeGr8WithColors, Is.EqualTo(7682));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_GetWidth_Graphics8() {
    Assert.That(AtariAgpFile.GetWidth(AtariAgpMode.Graphics8), Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_GetWidth_Graphics7() {
    Assert.That(AtariAgpFile.GetWidth(AtariAgpMode.Graphics7), Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_GetHeight_Graphics8() {
    Assert.That(AtariAgpFile.GetHeight(AtariAgpMode.Graphics8), Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_GetHeight_Graphics7() {
    Assert.That(AtariAgpFile.GetHeight(AtariAgpMode.Graphics7), Is.EqualTo(96));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariAgpFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariAgpFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 192 * 3],
    };
    Assert.Throws<ArgumentException>(() => AtariAgpFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_FromRawImage_UnsupportedDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 480,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[640 * 480],
      PaletteCount = 2,
    };
    Assert.Throws<ArgumentException>(() => AtariAgpFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_ToRawImage_ReturnsIndexed8() {
    var file = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8,
      PixelData = new byte[320 * 192],
    };

    var raw = AtariAgpFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_ToRawImage_ClonesPixelData() {
    var pixels = new byte[320 * 192];
    pixels[0] = 1;
    var file = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8,
      PixelData = pixels,
    };

    var raw1 = AtariAgpFile.ToRawImage(file);
    var raw2 = AtariAgpFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_PrimaryExtension() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".agp"));
  }

  [Test]
  [Category("Unit")]
  public void AtariAgpFile_FileExtensions() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".agp"));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(AtariAgpFile).GetInterfaceMap(typeof(IImageFileFormat<AtariAgpFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
