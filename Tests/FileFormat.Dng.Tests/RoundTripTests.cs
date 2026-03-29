using System;
using System.IO;
using FileFormat.Dng;

namespace FileFormat.Dng.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 21 % 256);

    var original = new DngFile {
      Width = 4,
      Height = 3,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      Photometric = DngPhotometric.BlackIsZero,
      PixelData = pixelData
    };

    var bytes = DngWriter.ToBytes(original);
    var restored = DngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(original.SamplesPerPixel));
    Assert.That(restored.BitsPerSample, Is.EqualTo(original.BitsPerSample));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new DngFile {
      Width = 3,
      Height = 2,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      Photometric = DngPhotometric.Rgb,
      PixelData = pixelData
    };

    var bytes = DngWriter.ToBytes(original);
    var restored = DngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(original.SamplesPerPixel));
    Assert.That(restored.BitsPerSample, Is.EqualTo(original.BitsPerSample));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[8 * 6 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new DngFile {
      Width = 8,
      Height = 6,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      Photometric = DngPhotometric.Rgb,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dng");
    try {
      var bytes = DngWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = DngReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.SamplesPerPixel, Is.EqualTo(original.SamplesPerPixel));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var pixelData = new byte[4 * 4 * 3];

    var original = new DngFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      Photometric = DngPhotometric.Rgb,
      PixelData = pixelData
    };

    var bytes = DngWriter.ToBytes(original);
    var restored = DngReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var original = new DngFile {
      Width = width,
      Height = height,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      Photometric = DngPhotometric.Rgb,
      PixelData = pixelData
    };

    var bytes = DngWriter.ToBytes(original);
    var restored = DngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
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

    var dng = DngFile.FromRawImage(rawImage);
    var raw = DngFile.ToRawImage(dng);

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

    var dng = DngFile.FromRawImage(rawImage);
    var raw = DngFile.ToRawImage(dng);

    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CameraModel() {
    var original = new DngFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      Photometric = DngPhotometric.BlackIsZero,
      CameraModel = "TestCamera",
      PixelData = new byte[4]
    };

    var bytes = DngWriter.ToBytes(original);
    var restored = DngReader.FromBytes(bytes);

    Assert.That(restored.CameraModel, Is.EqualTo(original.CameraModel));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DngVersion() {
    var original = new DngFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      Photometric = DngPhotometric.BlackIsZero,
      DngVersion = [1, 6, 0, 0],
      PixelData = new byte[4]
    };

    var bytes = DngWriter.ToBytes(original);
    var restored = DngReader.FromBytes(bytes);

    Assert.That(restored.DngVersion, Is.EqualTo(new byte[] { 1, 6, 0, 0 }));
  }
}
