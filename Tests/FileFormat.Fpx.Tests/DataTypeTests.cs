using System;
using System.Linq;
using FileFormat.Fpx;
using FileFormat.Core;

namespace FileFormat.Fpx.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FpxFile_DefaultPixelData_IsEmptyArray() {
    var file = new FpxFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FpxFile_DefaultWidth_IsZero() {
    var file = new FpxFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FpxFile_DefaultHeight_IsZero() {
    var file = new FpxFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FpxFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new FpxFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FpxFile_PrimaryExtension_IsFpx() {
    var ext = _GetPrimaryExtension<FpxFile>();
    Assert.That(ext, Is.EqualTo(".fpx"));
  }

  [Test]
  [Category("Unit")]
  public void FpxFile_FileExtensions_ContainsFpx() {
    var exts = _GetFileExtensions<FpxFile>();
    Assert.That(exts, Does.Contain(".fpx"));
  }

  [Test]
  [Category("Unit")]
  public void FpxFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FpxFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FpxFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4]
    };
    Assert.Throws<ArgumentException>(() => FpxFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FpxHeader_StructSize_Is16() {
    Assert.That(FpxHeader.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FpxHeader_GetFieldMap_CoversStructSize() {
    var map = FpxHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(FpxHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void FpxHeader_RoundTrip_PreservesAllFields() {
    var original = new FpxHeader(1, 640, 480);
    Span<byte> buffer = stackalloc byte[FpxHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = FpxHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void FpxHeader_Magic_IsFpxNull() {
    Assert.That(FpxHeader.Magic[0], Is.EqualTo((byte)'F'));
    Assert.That(FpxHeader.Magic[1], Is.EqualTo((byte)'P'));
    Assert.That(FpxHeader.Magic[2], Is.EqualTo((byte)'X'));
    Assert.That(FpxHeader.Magic[3], Is.EqualTo(0x00));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
