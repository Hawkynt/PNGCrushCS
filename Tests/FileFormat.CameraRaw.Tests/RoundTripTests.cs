using System;
using System.IO;
using FileFormat.CameraRaw;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var pixelData = new byte[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new CameraRawFile {
      Width = 3,
      Height = 2,
      PixelData = pixelData,
      Manufacturer = CameraRawManufacturer.Generic,
    };

    var bytes = CameraRawWriter.ToBytes(original);
    var restored = CameraRawReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[8 * 6 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new CameraRawFile {
      Width = 8,
      Height = 6,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cr2");
    try {
      var bytes = CameraRawWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = CameraRawReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var rawImage = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = pixelData,
    };

    var cameraRaw = CameraRawFile.FromRawImage(rawImage);
    var raw = CameraRawFile.ToRawImage(cameraRaw);

    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var pixelData = new byte[4 * 4 * 3];

    var original = new CameraRawFile {
      Width = 4,
      Height = 4,
      PixelData = pixelData,
    };

    var bytes = CameraRawWriter.ToBytes(original);
    var restored = CameraRawReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient() {
    var width = 16;
    var height = 8;
    var pixelData = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = (y * width + x) * 3;
        pixelData[idx] = (byte)(x * 255 / (width - 1));
        pixelData[idx + 1] = (byte)(y * 255 / (height - 1));
        pixelData[idx + 2] = (byte)((x + y) * 127 / (width + height - 2));
      }

    var original = new CameraRawFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = CameraRawWriter.ToBytes(original);
    var restored = CameraRawReader.FromBytes(bytes);

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

    var original = new CameraRawFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = CameraRawWriter.ToBytes(original);
    var restored = CameraRawReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var pixelData = new byte[3 * 3 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 19 % 256);

    var original = new CameraRawFile {
      Width = 3,
      Height = 3,
      PixelData = pixelData,
    };

    var bytes = CameraRawWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var restored = CameraRawReader.FromStream(ms);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new CameraRawFile {
      Width = 1,
      Height = 1,
      PixelData = [0xAA, 0xBB, 0xCC],
    };

    var bytes = CameraRawWriter.ToBytes(original);
    var restored = CameraRawReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
