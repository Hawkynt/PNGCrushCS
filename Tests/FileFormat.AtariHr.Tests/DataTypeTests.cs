using System;
using FileFormat.Core;
using FileFormat.AtariHr;

namespace FileFormat.AtariHr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Width_AlwaysReturns320() {
    var file = new AtariHrFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void Height_AlwaysReturns192() {
    var file = new AtariHrFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void RawData_DefaultEmpty() {
    var file = new AtariHrFile();
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void RawData_InitProperty() {
    var rawData = new byte[] { 0xAB, 0xCD };
    var file = new AtariHrFile { RawData = rawData };
    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is7680() {
    Assert.That(AtariHrFile.FileSize, Is.EqualTo(7680));
  }

  [Test]
  [Category("Unit")]
  public void BytesPerRow_Is40() {
    Assert.That(AtariHrFile.BytesPerRow, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void PixelWidth_Is320() {
    Assert.That(AtariHrFile.PixelWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void PixelHeight_Is192() {
    Assert.That(AtariHrFile.PixelHeight, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void Capabilities_IsMonochromeOnly() {
    var caps = _GetStaticProperty<FormatCapability>("Capabilities");
    Assert.That(caps, Is.EqualTo(FormatCapability.MonochromeOnly));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsHr() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".hr"));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsHr() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".hr"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => AtariHrFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Format_IsIndexed1() {
    var file = new AtariHrFile { RawData = new byte[7680] };
    var raw = AtariHrFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Dimensions_Match() {
    var file = new AtariHrFile { RawData = new byte[7680] };
    var raw = AtariHrFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Palette_HasTwoEntries() {
    var file = new AtariHrFile { RawData = new byte[7680] };
    var raw = AtariHrFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    // Entry 0: black (0,0,0)
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    // Entry 1: white (255,255,255)
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => AtariHrFile.FromRawImage(null!));
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
    Assert.Throws<ArgumentException>(() => AtariHrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongDimensions_Throws() {
    var raw = new RawImage {
      Width = 256,
      Height = 144,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[256 / 8 * 144],
    };
    Assert.Throws<ArgumentException>(() => AtariHrFile.FromRawImage(raw));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(AtariHrFile).GetInterfaceMap(typeof(IImageFileFormat<AtariHrFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
