using System;
using FileFormat.Atari8Bit;
using FileFormat.Core;

namespace FileFormat.Atari8Bit.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Atari8BitMode_HasExpectedValues() {
    Assert.That((int)Atari8BitMode.Gr7, Is.EqualTo(0));
    Assert.That((int)Atari8BitMode.Gr8, Is.EqualTo(1));
    Assert.That((int)Atari8BitMode.Gr9, Is.EqualTo(2));
    Assert.That((int)Atari8BitMode.Gr15, Is.EqualTo(3));

    var values = Enum.GetValues<Atari8BitMode>();
    Assert.That(values, Has.Length.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_DefaultPixelData_IsEmptyArray() {
    var file = new Atari8BitFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_DefaultPalette_IsEmptyArray() {
    var file = new Atari8BitFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_DefaultWidth_IsZero() {
    var file = new Atari8BitFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_DefaultHeight_IsZero() {
    var file = new Atari8BitFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_DefaultMode_IsGr7() {
    var file = new Atari8BitFile();
    Assert.That(file.Mode, Is.EqualTo(Atari8BitMode.Gr7));
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0, 1, 0, 1 };
    var palette = new byte[] { 0, 0, 0, 255, 255, 255 };
    var file = new Atari8BitFile {
      Width = 4,
      Height = 1,
      Mode = Atari8BitMode.Gr8,
      PixelData = pixels,
      Palette = palette,
    };

    Assert.That(file.Width, Is.EqualTo(4));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Mode, Is.EqualTo(Atari8BitMode.Gr8));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_PrimaryExtension_IsGr8() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".gr8"));
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_FileExtensions_Contains7Entries() {
    var exts = GetFileExtensions();
    Assert.That(exts, Has.Length.EqualTo(7));
    Assert.That(exts, Does.Contain(".gr7"));
    Assert.That(exts, Does.Contain(".gr8"));
    Assert.That(exts, Does.Contain(".gr9"));
    Assert.That(exts, Does.Contain(".gr15"));
    Assert.That(exts, Does.Contain(".hip"));
    Assert.That(exts, Does.Contain(".mic"));
    Assert.That(exts, Does.Contain(".int"));
  }

  [Test]
  [Category("Unit")]
  public void Atari8BitFile_FileSizeConstants() {
    Assert.That(Atari8BitFile.FileSize7680, Is.EqualTo(7680));
    Assert.That(Atari8BitFile.FileSize1920, Is.EqualTo(1920));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => Atari8BitFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new Atari8BitFile {
      Width = 320,
      Height = 192,
      Mode = Atari8BitMode.Gr8,
      PixelData = new byte[320 * 192],
    };

    var raw = Atari8BitFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[320 * 192];
    pixels[0] = 1;
    var file = new Atari8BitFile {
      Width = 320,
      Height = 192,
      Mode = Atari8BitMode.Gr8,
      PixelData = pixels,
    };

    var raw = Atari8BitFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => Atari8BitFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 320,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 192 * 3],
    };

    Assert.Throws<ArgumentException>(() => Atari8BitFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gr8_InfersMode() {
    var raw = new RawImage {
      Width = 320,
      Height = 192,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[320 * 192],
      PaletteCount = 2,
    };

    var file = Atari8BitFile.FromRawImage(raw);

    Assert.That(file.Mode, Is.EqualTo(Atari8BitMode.Gr8));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedDimensions_Throws() {
    var raw = new RawImage {
      Width = 640,
      Height = 480,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[640 * 480],
      PaletteCount = 2,
    };

    Assert.Throws<ArgumentException>(() => Atari8BitFile.FromRawImage(raw));
  }

  private static string GetPrimaryExtension() => _GetStaticProperty<string>("PrimaryExtension");
  private static string[] GetFileExtensions() => _GetStaticProperty<string[]>("FileExtensions");

  private static T _GetStaticProperty<T>(string name) {
    var prop = typeof(Atari8BitFile).GetInterfaceMap(typeof(IImageFileFormat<Atari8BitFile>));
    foreach (var method in prop.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
