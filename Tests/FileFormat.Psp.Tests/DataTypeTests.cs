using System;
using FileFormat.Core;
using FileFormat.Psp;

namespace FileFormat.Psp.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PspFile_DefaultPixelData_IsEmptyArray() {
    var file = new PspFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PspFile_DefaultBitDepth_Is24() {
    var file = new PspFile();
    Assert.That(file.BitDepth, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void PspFile_DefaultMajorVersion_Is5() {
    var file = new PspFile();
    Assert.That(file.MajorVersion, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void PspFile_DefaultMinorVersion_Is0() {
    var file = new PspFile();
    Assert.That(file.MinorVersion, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PspFile_DefaultWidth_Is0() {
    var file = new PspFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PspFile_DefaultHeight_Is0() {
    var file = new PspFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PspFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new PspFile {
      Width = 1,
      Height = 1,
      BitDepth = 24,
      MajorVersion = 6,
      MinorVersion = 2,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.BitDepth, Is.EqualTo(24));
    Assert.That(file.MajorVersion, Is.EqualTo(6));
    Assert.That(file.MinorVersion, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void PspFile_PrimaryExtension_IsPsp() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".psp"));
  }

  [Test]
  [Category("Unit")]
  public void PspFile_FileExtensions_ContainsBothExtensions() {
    var exts = GetFileExtensions();
    Assert.That(exts, Does.Contain(".psp"));
    Assert.That(exts, Does.Contain(".pspimage"));
    Assert.That(exts, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PspFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PspFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [128]
    };
    Assert.Throws<ArgumentException>(() => PspFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void Magic_Is32Bytes() {
    Assert.That(PspFile.Magic, Has.Length.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void Magic_StartsWithPaintShopPro() {
    var expected = System.Text.Encoding.ASCII.GetBytes("Paint Shop Pro Image File");
    for (var i = 0; i < expected.Length; ++i)
      Assert.That(PspFile.Magic[i], Is.EqualTo(expected[i]));
  }

  private static string GetPrimaryExtension() => GetPrimary<PspFile>();
  private static string GetPrimary<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] GetFileExtensions() => GetExts<PspFile>();
  private static string[] GetExts<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
