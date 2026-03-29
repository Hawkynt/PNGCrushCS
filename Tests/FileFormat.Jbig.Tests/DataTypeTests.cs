using System;
using FileFormat.Jbig;
using FileFormat.Core;

namespace FileFormat.Jbig.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void JbigFile_DefaultPixelData_IsEmpty() {
    var file = new JbigFile { Width = 8, Height = 1 };

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void JbigFile_DefaultWidth_IsZero() {
    var file = new JbigFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JbigFile_DefaultHeight_IsZero() {
    var file = new JbigFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JbigFile_InitProperties_StoreCorrectly() {
    var pixelData = new byte[] { 0xFF };
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = pixelData
    };

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void JbigFile_PrimaryExtension_IsJbg() {
    var ext = _GetPrimaryExtension<JbigFile>();
    Assert.That(ext, Is.EqualTo(".jbg"));
  }

  [Test]
  [Category("Unit")]
  public void JbigFile_FileExtensions_ContainsAllExpected() {
    var extensions = _GetFileExtensions<JbigFile>();

    Assert.That(extensions, Contains.Item(".jbg"));
    Assert.That(extensions, Contains.Item(".bie"));
    Assert.That(extensions, Contains.Item(".jbig"));
    Assert.That(extensions, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JbigFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3]
    };

    Assert.Throws<ArgumentException>(() => JbigFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesIndexed1() {
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = [0xFF]
    };

    var raw = file.ToRawImage();

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.Palette!.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteIsBlackAndWhite() {
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = [0x00]
    };

    var raw = file.ToRawImage();

    // Index 0 = black (0,0,0), Index 1 = white (255,255,255)
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette![1], Is.EqualTo(0));
    Assert.That(raw.Palette![2], Is.EqualTo(0));
    Assert.That(raw.Palette![3], Is.EqualTo(255));
    Assert.That(raw.Palette![4], Is.EqualTo(255));
    Assert.That(raw.Palette![5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0xAA };
    var file = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = pixelData
    };

    var raw = file.ToRawImage();

    Assert.That(raw.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
