using System;
using FileFormat.Core;
using FileFormat.JpegLs;

namespace FileFormat.JpegLs.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void JpegLsInterleaveMode_HasExpectedValues() {
    Assert.That((byte)JpegLsInterleaveMode.None, Is.EqualTo(0));
    Assert.That((byte)JpegLsInterleaveMode.Line, Is.EqualTo(1));
    Assert.That((byte)JpegLsInterleaveMode.Sample, Is.EqualTo(2));

    var values = Enum.GetValues<JpegLsInterleaveMode>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void JpegLsFile_DefaultPixelData_IsEmptyArray() {
    var file = new JpegLsFile { Width = 1, Height = 1 };
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Has.Length.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JpegLsFile_DefaultBitsPerSample_Is8() {
    var file = new JpegLsFile { Width = 1, Height = 1 };
    Assert.That(file.BitsPerSample, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void JpegLsFile_DefaultComponentCount_Is1() {
    var file = new JpegLsFile { Width = 1, Height = 1 };
    Assert.That(file.ComponentCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void JpegLsFile_DefaultNearLossless_IsZero() {
    var file = new JpegLsFile { Width = 1, Height = 1 };
    Assert.That(file.NearLossless, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JpegLsFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3 };
    var file = new JpegLsFile {
      Width = 10,
      Height = 20,
      BitsPerSample = 8,
      ComponentCount = 3,
      NearLossless = 1,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.BitsPerSample, Is.EqualTo(8));
    Assert.That(file.ComponentCount, Is.EqualTo(3));
    Assert.That(file.NearLossless, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void JpegLsFile_PrimaryExtension_IsJls() {
    var ext = _GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".jls"));
  }

  [Test]
  [Category("Unit")]
  public void JpegLsFile_FileExtensions_ContainsJls() {
    var exts = _GetFileExtensions();
    Assert.That(exts, Contains.Item(".jls"));
    Assert.That(exts, Has.Length.EqualTo(1));
  }

  private static string _GetPrimaryExtension() => _GetPrimary<JpegLsFile>();
  private static string _GetPrimary<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions() => _GetExts<JpegLsFile>();
  private static string[] _GetExts<T>() where T : IImageFileFormat<T> => T.FileExtensions;

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegLsFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Grayscale_ReturnsGray8() {
    var file = new JpegLsFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = [10, 20, 30, 40]
    };

    var raw = JpegLsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24() {
    var file = new JpegLsFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = [255, 128, 64]
    };

    var raw = JpegLsFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 1, 2, 3, 4 };
    var file = new JpegLsFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var raw = JpegLsFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegLsFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_Returns1Component() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40]
    };

    var file = JpegLsFile.FromRawImage(raw);

    Assert.That(file.ComponentCount, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_Returns3Components() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 128, 64]
    };

    var file = JpegLsFile.FromRawImage(raw);
    Assert.That(file.ComponentCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [255, 128, 64, 255]
    };

    Assert.Throws<ArgumentException>(() => JpegLsFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 1, 2, 3 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData
    };

    var file = JpegLsFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(file.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void JpegLsCodec_ConstantValues() {
    Assert.That(JpegLsCodec.RegularContextCount, Is.EqualTo(365));
    Assert.That(JpegLsCodec.TotalContextCount, Is.EqualTo(367));
    Assert.That(JpegLsCodec.RunContextIndex, Is.EqualTo(365));
    Assert.That(JpegLsCodec.RunInterruptContextIndex, Is.EqualTo(366));
    Assert.That(JpegLsCodec.DefaultReset, Is.EqualTo(64));
  }
}
