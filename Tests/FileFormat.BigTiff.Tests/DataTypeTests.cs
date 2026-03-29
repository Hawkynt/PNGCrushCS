using System;
using FileFormat.BigTiff;
using FileFormat.Core;

namespace FileFormat.BigTiff.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Version_Is43()
    => Assert.That(BigTiffFile.Version, Is.EqualTo(43));

  [Test]
  [Category("Unit")]
  public void OffsetSize_Is8()
    => Assert.That(BigTiffFile.OffsetSize, Is.EqualTo(8));

  [Test]
  [Category("Unit")]
  public void MinimumFileSize_Is16()
    => Assert.That(BigTiffFile.MinimumFileSize, Is.EqualTo(16));

  [Test]
  [Category("Unit")]
  public void PhotometricMinIsBlack_Is1()
    => Assert.That(BigTiffFile.PhotometricMinIsBlack, Is.EqualTo(1));

  [Test]
  [Category("Unit")]
  public void PhotometricRgb_Is2()
    => Assert.That(BigTiffFile.PhotometricRgb, Is.EqualTo(2));

  [Test]
  [Category("Unit")]
  public void Defaults_AreCorrect() {
    var file = new BigTiffFile();
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(0));
      Assert.That(file.Height, Is.EqualTo(0));
      Assert.That(file.SamplesPerPixel, Is.EqualTo(1));
      Assert.That(file.BitsPerSample, Is.EqualTo(8));
      Assert.That(file.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricMinIsBlack));
      Assert.That(file.PixelData, Is.Empty);
      Assert.That(file.IsBigEndian, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3, 4 };
    var file = new BigTiffFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack,
      PixelData = pixels,
      IsBigEndian = true,
    };
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.SamplesPerPixel, Is.EqualTo(1));
      Assert.That(file.BitsPerSample, Is.EqualTo(8));
      Assert.That(file.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricMinIsBlack));
      Assert.That(file.PixelData, Is.SameAs(pixels));
      Assert.That(file.IsBigEndian, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsBtf() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".btf"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsBtfAndTf8() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.That(exts, Has.Length.EqualTo(2));
    Assert.That(exts, Does.Contain(".btf"));
    Assert.That(exts, Does.Contain(".tf8"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffFile.ToRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_NullImage_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_InvalidFormat_Throws()
    => Assert.Throws<ArgumentException>(() => BigTiffFile.FromRawImage(new RawImage {
      Width = 2, Height = 2, Format = PixelFormat.Rgba32, PixelData = new byte[16],
    }));

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray_ReturnsGray8() {
    var file = new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, PixelData = new byte[4],
    };
    var raw = BigTiffFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24() {
    var file = new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 3, PixelData = new byte[12],
    };
    var raw = BigTiffFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var file = new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, PixelData = pixels,
    };
    var raw = BigTiffFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_SetsFields() {
    var raw = new RawImage {
      Width = 4, Height = 3, Format = PixelFormat.Gray8, PixelData = new byte[12],
    };
    var file = BigTiffFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(3));
      Assert.That(file.SamplesPerPixel, Is.EqualTo(1));
      Assert.That(file.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricMinIsBlack));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_SetsFields() {
    var raw = new RawImage {
      Width = 4, Height = 3, Format = PixelFormat.Rgb24, PixelData = new byte[36],
    };
    var file = BigTiffFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(3));
      Assert.That(file.SamplesPerPixel, Is.EqualTo(3));
      Assert.That(file.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricRgb));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[12];
    pixels[0] = 99;
    var raw = new RawImage {
      Width = 4, Height = 3, Format = PixelFormat.Gray8, PixelData = pixels,
    };
    var file = BigTiffFile.FromRawImage(raw);
    pixels[0] = 0;
    Assert.That(file.PixelData[0], Is.EqualTo(99));
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(BigTiffFile).GetInterfaceMap(typeof(IImageFileFormat<BigTiffFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException("Property " + name + " not found.");
  }
}
