using System;
using FileFormat.Bpg;
using FileFormat.Core;

namespace FileFormat.Bpg.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void BpgPixelFormat_HasExpectedValues() {
    Assert.That((int)BpgPixelFormat.YCbCr420, Is.EqualTo(0));
    Assert.That((int)BpgPixelFormat.YCbCr422, Is.EqualTo(1));
    Assert.That((int)BpgPixelFormat.YCbCr444, Is.EqualTo(2));
    Assert.That((int)BpgPixelFormat.Grayscale, Is.EqualTo(3));
    Assert.That((int)BpgPixelFormat.Cmyk, Is.EqualTo(4));
    Assert.That((int)BpgPixelFormat.YCbCr420Jpeg, Is.EqualTo(5));

    var values = Enum.GetValues<BpgPixelFormat>();
    Assert.That(values, Has.Length.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void BpgColorSpace_HasExpectedValues() {
    Assert.That((int)BpgColorSpace.YCbCrBT601, Is.EqualTo(0));
    Assert.That((int)BpgColorSpace.Rgb, Is.EqualTo(1));
    Assert.That((int)BpgColorSpace.YCbCrBT709, Is.EqualTo(2));
    Assert.That((int)BpgColorSpace.YCbCrBT2020, Is.EqualTo(3));
    Assert.That((int)BpgColorSpace.YCbCrBT2020NCL, Is.EqualTo(4));

    var values = Enum.GetValues<BpgColorSpace>();
    Assert.That(values, Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultWidth_IsZero() {
    var file = new BpgFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultHeight_IsZero() {
    var file = new BpgFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultBitDepth_Is8() {
    var file = new BpgFile();
    Assert.That(file.BitDepth, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultPixelFormat_IsYCbCr420() {
    var file = new BpgFile();
    Assert.That(file.PixelFormat, Is.EqualTo(BpgPixelFormat.YCbCr420));
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultColorSpace_IsYCbCrBT601() {
    var file = new BpgFile();
    Assert.That(file.ColorSpace, Is.EqualTo(BpgColorSpace.YCbCrBT601));
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultHasAlpha_IsFalse() {
    var file = new BpgFile();
    Assert.That(file.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultPixelData_IsEmpty() {
    var file = new BpgFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultExtensionData_IsEmpty() {
    var file = new BpgFile();
    Assert.That(file.ExtensionData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultExtensionPresent_IsFalse() {
    var file = new BpgFile();
    Assert.That(file.ExtensionPresent, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultIsAnimation_IsFalse() {
    var file = new BpgFile();
    Assert.That(file.IsAnimation, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_DefaultLimitedRange_IsFalse() {
    var file = new BpgFile();
    Assert.That(file.LimitedRange, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_InitProperties_Work() {
    var file = new BpgFile {
      Width = 640,
      Height = 480,
      PixelFormat = BpgPixelFormat.YCbCr422,
      BitDepth = 12,
      ColorSpace = BpgColorSpace.YCbCrBT2020NCL,
      HasAlpha = true,
      HasAlpha2 = true,
      LimitedRange = true,
      IsAnimation = true,
    };

    Assert.That(file.Width, Is.EqualTo(640));
    Assert.That(file.Height, Is.EqualTo(480));
    Assert.That(file.PixelFormat, Is.EqualTo(BpgPixelFormat.YCbCr422));
    Assert.That(file.BitDepth, Is.EqualTo(12));
    Assert.That(file.ColorSpace, Is.EqualTo(BpgColorSpace.YCbCrBT2020NCL));
    Assert.That(file.HasAlpha, Is.True);
    Assert.That(file.HasAlpha2, Is.True);
    Assert.That(file.LimitedRange, Is.True);
    Assert.That(file.IsAnimation, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_PrimaryExtension_IsBpg() {
    var ext = _GetPrimaryExtension<BpgFile>();
    Assert.That(ext, Is.EqualTo(".bpg"));
  }

  [Test]
  [Category("Unit")]
  public void BpgFile_FileExtensions_ContainsBpg() {
    var exts = _GetFileExtensions<BpgFile>();
    Assert.That(exts, Contains.Item(".bpg"));
    Assert.That(exts, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BpgFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Grayscale_ReturnsGray8() {
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = [10, 20, 30, 40],
    };

    var raw = BpgFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NonGrayscale_ReturnsRgb24() {
    var file = new BpgFile {
      Width = 1,
      Height = 1,
      PixelFormat = BpgPixelFormat.YCbCr444,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = [255, 128, 64],
    };

    var raw = BpgFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 10, 20, 30 };
    var file = new BpgFile {
      Width = 1,
      Height = 1,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = pixelData,
    };

    var raw = BpgFile.ToRawImage(file);
    raw.PixelData[0] = 0xFF;

    Assert.That(file.PixelData[0], Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BpgFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_ProducesGrayscale() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40],
    };

    var bpg = BpgFile.FromRawImage(raw);

    Assert.That(bpg.PixelFormat, Is.EqualTo(BpgPixelFormat.Grayscale));
    Assert.That(bpg.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_ProducesYCbCr444() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = [255, 128, 64],
    };

    var bpg = BpgFile.FromRawImage(raw);

    Assert.That(bpg.PixelFormat, Is.EqualTo(BpgPixelFormat.YCbCr444));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_Throws() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = [255, 128, 64, 255],
    };

    Assert.Throws<ArgumentException>(() => BpgFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 10, 20, 30 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = pixelData,
    };

    var bpg = BpgFile.FromRawImage(raw);
    bpg.PixelData[0] = 0xFF;

    Assert.That(pixelData[0], Is.EqualTo(10));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
