using System;
using FileFormat.Core;
using FileFormat.AtariGr8;

namespace FileFormat.AtariGr8.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AtariGr8File_DefaultWidth_Is320() {
    var file = new AtariGr8File();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_DefaultHeight_Is192() {
    var file = new AtariGr8File();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_DefaultRawData_IsEmpty() {
    var file = new AtariGr8File();
    Assert.That(file.RawData, Is.Not.Null);
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0xAB, 0xCD };
    var file = new AtariGr8File { RawData = rawData };
    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_FileSize_Is7680() {
    Assert.That(AtariGr8File.FileSize, Is.EqualTo(7680));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_PixelWidth_Is320() {
    Assert.That(AtariGr8File.PixelWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_PixelHeight_Is192() {
    Assert.That(AtariGr8File.PixelHeight, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_BytesPerRow_Is40() {
    Assert.That(AtariGr8File.BytesPerRow, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr8File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr8File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 192 * 3],
    };
    Assert.Throws<ArgumentException>(() => AtariGr8File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 480,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[640 / 8 * 480],
    };
    Assert.Throws<ArgumentException>(() => AtariGr8File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_ToRawImage_ReturnsIndexed1Format() {
    var file = new AtariGr8File { RawData = new byte[7680] };
    var raw = AtariGr8File.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_ToRawImage_HasCorrectDimensions() {
    var file = new AtariGr8File { RawData = new byte[7680] };
    var raw = AtariGr8File.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_ToRawImage_HasBlackWhitePalette() {
    var file = new AtariGr8File { RawData = new byte[7680] };
    var raw = AtariGr8File.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_ToRawImage_PixelDataSize() {
    var file = new AtariGr8File { RawData = new byte[7680] };
    var raw = AtariGr8File.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(40 * 192));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_ToRawImage_ClonesPixelData() {
    var rawData = new byte[7680];
    rawData[0] = 0xFF;
    var file = new AtariGr8File { RawData = rawData };

    var raw1 = AtariGr8File.ToRawImage(file);
    var raw2 = AtariGr8File.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_PrimaryExtension() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".gr8"));
  }

  [Test]
  [Category("Unit")]
  public void AtariGr8File_FileExtensions() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".gr8"));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(AtariGr8File).GetInterfaceMap(typeof(IImageFileFormat<AtariGr8File>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
