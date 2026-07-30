using System;
using System.IO;
using FileFormat.JpegXr;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 21 % 256);

    var original = new JpegXrFile {
      Width = 4,
      Height = 3,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegXrWriter.ToBytes(original);
    var restored = JpegXrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ComponentCount, Is.EqualTo(original.ComponentCount));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new JpegXrFile {
      Width = 3,
      Height = 2,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegXrWriter.ToBytes(original);
    var restored = JpegXrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ComponentCount, Is.EqualTo(original.ComponentCount));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new JpegXrFile {
      Width = 4,
      Height = 4,
      ComponentCount = 3,
      PixelData = new byte[4 * 4 * 3]
    };

    var bytes = JpegXrWriter.ToBytes(original);
    var restored = JpegXrReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient() {
    var w = 8;
    var h = 4;
    var pixelData = new byte[w * h * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new JpegXrFile {
      Width = w,
      Height = h,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegXrWriter.ToBytes(original);
    var restored = JpegXrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(w));
    Assert.That(restored.Height, Is.EqualTo(h));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[8 * 6 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new JpegXrFile {
      Width = 8,
      Height = 6,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jxr");
    try {
      var bytes = JpegXrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = JpegXrReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.ComponentCount, Is.EqualTo(original.ComponentCount));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 19 % 256);

    var original = new JpegXrFile {
      Width = 4,
      Height = 4,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegXrWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var restored = JpegXrReader.FromStream(ms);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ComponentCount, Is.EqualTo(original.ComponentCount));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new JpegXrFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var bytes = JpegXrWriter.ToBytes(original);
    var restored = JpegXrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var w = 64;
    var h = 48;
    var pixelData = new byte[w * h * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var original = new JpegXrFile {
      Width = w,
      Height = h,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegXrWriter.ToBytes(original);
    var restored = JpegXrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(w));
    Assert.That(restored.Height, Is.EqualTo(h));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RawImage_Grayscale() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 23 % 256);

    var rawImage = new FileFormat.Core.RawImage {
      Width = 4,
      Height = 3,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = pixelData
    };

    var jxr = JpegXrFile.FromRawImage(rawImage);
    var raw = JpegXrFile.ToRawImage(jxr);

    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(3));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RawImage_Rgb() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var rawImage = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = pixelData
    };

    var jxr = JpegXrFile.FromRawImage(rawImage);
    var raw = JpegXrFile.ToRawImage(jxr);

    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }
}
