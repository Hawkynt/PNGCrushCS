using System;
using FileFormat.Ics;

namespace FileFormat.Ics.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IcsCompression_HasExpectedValues() {
    Assert.That((int)IcsCompression.Uncompressed, Is.EqualTo(0));
    Assert.That((int)IcsCompression.Gzip, Is.EqualTo(1));

    var values = Enum.GetValues<IcsCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_DefaultValues() {
    var file = new IcsFile {
      Width = 10,
      Height = 20
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.Channels, Is.EqualTo(1));
    Assert.That(file.BitsPerSample, Is.EqualTo(8));
    Assert.That(file.Version, Is.EqualTo("2.0"));
    Assert.That(file.Compression, Is.EqualTo(IcsCompression.Uncompressed));
    Assert.That(file.IsCompressed, Is.False);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_IsCompressed_TrueForGzip() {
    var file = new IcsFile {
      Width = 1,
      Height = 1,
      Compression = IcsCompression.Gzip,
    };

    Assert.That(file.IsCompressed, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_IsCompressed_FalseForUncompressed() {
    var file = new IcsFile {
      Width = 1,
      Height = 1,
      Compression = IcsCompression.Uncompressed,
    };

    Assert.That(file.IsCompressed, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_ToRawImage_Grayscale() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var file = new IcsFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      BitsPerSample = 8,
      PixelData = pixels
    };

    var raw = IcsFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_ToRawImage_Rgb() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)i;

    var file = new IcsFile {
      Width = 2,
      Height = 2,
      Channels = 3,
      BitsPerSample = 8,
      PixelData = pixels
    };

    var raw = IcsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => IcsFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => IcsFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_FromRawImage_UnsupportedFormat_Throws() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgba32,
      PixelData = new byte[16]
    };

    Assert.Throws<ArgumentException>(() => IcsFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_FromRawImage_Gray8_RoundTrip() {
    var raw = new FileFormat.Core.RawImage {
      Width = 3,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40, 50, 60]
    };

    var ics = IcsFile.FromRawImage(raw);
    var restored = IcsFile.ToRawImage(ics);

    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(restored.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void IcsFile_FromRawImage_Rgb24_RoundTrip() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 5);

    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = pixels
    };

    var ics = IcsFile.FromRawImage(raw);
    var restored = IcsFile.ToRawImage(ics);

    Assert.That(restored.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(restored.PixelData, Is.EqualTo(raw.PixelData));
  }
}
