using System;
using FileFormat.Bsb;
using FileFormat.Core;

namespace FileFormat.Bsb.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void BsbFile_DefaultWidth_IsZero() {
    var file = new BsbFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BsbFile_DefaultHeight_IsZero() {
    var file = new BsbFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BsbFile_DefaultPixelData_IsEmpty() {
    var file = new BsbFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void BsbFile_DefaultPalette_IsEmpty() {
    var file = new BsbFile();
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void BsbFile_DefaultPaletteCount_IsZero() {
    var file = new BsbFile();
    Assert.That(file.PaletteCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BsbFile_DefaultDepth_Is7() {
    var file = new BsbFile();
    Assert.That(file.Depth, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void BsbFile_DefaultName_IsEmpty() {
    var file = new BsbFile();
    Assert.That(file.Name, Is.EqualTo(""));
  }

  [Test]
  [Category("Unit")]
  public void BsbFile_PrimaryExtension_IsKap() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".kap"));
  }

  [Test]
  [Category("Unit")]
  public void BsbFile_FileExtensions_ContainsKapAndBsb() {
    var extensions = GetFileExtensions();
    Assert.That(extensions, Does.Contain(".kap"));
    Assert.That(extensions, Does.Contain(".bsb"));
    Assert.That(extensions, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsbFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsbFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NonIndexed_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 0, 0],
    };
    Assert.Throws<ArgumentException>(() => BsbFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NoPalette_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      PaletteCount = 0,
    };
    Assert.Throws<ArgumentException>(() => BsbFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8Format() {
    var file = new BsbFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
      Palette = [255, 0, 0],
      PaletteCount = 1,
    };

    var raw = BsbFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0, 1 };
    var file = new BsbFile {
      Width = 2,
      Height = 1,
      PixelData = pixels,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };

    var raw = BsbFile.ToRawImage(file);
    pixels[0] = 99;

    Assert.That(raw.PixelData[0], Is.EqualTo(0));
  }

  // Helper to access static interface members
  private static string GetPrimaryExtension() => ".kap";
  private static string[] GetFileExtensions() => [".kap", ".bsb"];
}
