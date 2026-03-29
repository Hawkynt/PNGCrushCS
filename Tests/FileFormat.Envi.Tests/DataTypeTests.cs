using System;
using FileFormat.Envi;

namespace FileFormat.Envi.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void EnviInterleave_HasExpectedValues() {
    Assert.That((int)EnviInterleave.Bsq, Is.EqualTo(0));
    Assert.That((int)EnviInterleave.Bip, Is.EqualTo(1));
    Assert.That((int)EnviInterleave.Bil, Is.EqualTo(2));

    var values = Enum.GetValues<EnviInterleave>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_DefaultWidth_IsZero() {
    var file = new EnviFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_DefaultHeight_IsZero() {
    var file = new EnviFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_DefaultBands_Is1() {
    var file = new EnviFile();

    Assert.That(file.Bands, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_DefaultDataType_Is1() {
    var file = new EnviFile();

    Assert.That(file.DataType, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_DefaultInterleave_IsBsq() {
    var file = new EnviFile();

    Assert.That(file.Interleave, Is.EqualTo(EnviInterleave.Bsq));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_DefaultByteOrder_IsZero() {
    var file = new EnviFile();

    Assert.That(file.ByteOrder, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_DefaultPixelData_IsEmpty() {
    var file = new EnviFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_PrimaryExtension_IsHdr() {
    var ext = GetPrimaryExtension();

    Assert.That(ext, Is.EqualTo(".hdr"));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_FileExtensions_ContainsHdr() {
    var exts = GetFileExtensions();

    Assert.That(exts, Does.Contain(".hdr"));
    Assert.That(exts, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void EnviFile_InitProperties_RoundTrip() {
    var file = new EnviFile {
      Width = 10,
      Height = 20,
      Bands = 3,
      DataType = 12,
      Interleave = EnviInterleave.Bil,
      ByteOrder = 1,
      PixelData = [0xAA, 0xBB]
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.Bands, Is.EqualTo(3));
    Assert.That(file.DataType, Is.EqualTo(12));
    Assert.That(file.Interleave, Is.EqualTo(EnviInterleave.Bil));
    Assert.That(file.ByteOrder, Is.EqualTo(1));
    Assert.That(file.PixelData, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EnviFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EnviFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = new byte[8],
    };

    Assert.Throws<ArgumentException>(() => EnviFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_UnsupportedConfig_ThrowsArgumentException() {
    var file = new EnviFile {
      Width = 2,
      Height = 1,
      Bands = 1,
      DataType = 99, // unknown data type, not supported for ToRawImage
      PixelData = new byte[8]
    };

    Assert.Throws<ArgumentException>(() => EnviFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray8_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var file = new EnviFile {
      Width = 2,
      Height = 2,
      Bands = 1,
      DataType = 1,
      PixelData = pixels
    };

    var raw = EnviFile.ToRawImage(file);
    raw.PixelData[0] = 0xFF;

    Assert.That(file.PixelData[0], Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var rawPixels = new byte[] { 10, 20, 30, 40 };
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = rawPixels,
    };

    var envi = EnviFile.FromRawImage(raw);
    rawPixels[0] = 0xFF;

    Assert.That(envi.PixelData[0], Is.EqualTo(10));
  }

  private static string GetPrimaryExtension() => GetPrimary<EnviFile>();

  private static string GetPrimary<T>() where T : FileFormat.Core.IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] GetFileExtensions() => GetExts<EnviFile>();

  private static string[] GetExts<T>() where T : FileFormat.Core.IImageFileFormat<T>
    => T.FileExtensions;
}
