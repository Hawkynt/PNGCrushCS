using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Psp;

namespace FileFormat.Psp.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1_Rgb24() {
    var original = new PspFile {
      Width = 1,
      Height = 1,
      PixelData = [42, 84, 126]
    };

    var bytes = PspWriter.ToBytes(original);
    var restored = PspReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 16;
    var height = 8;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PspFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = PspWriter.ToBytes(original);
    var restored = PspReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".psp");
    try {
      var original = new PspFile {
        Width = 3,
        Height = 2,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ]
      };

      var bytes = PspWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PspReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_VersionPreserved() {
    var original = new PspFile {
      Width = 2,
      Height = 2,
      MajorVersion = 7,
      MinorVersion = 3,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PspWriter.ToBytes(original);
    var restored = PspReader.FromBytes(bytes);

    Assert.That(restored.MajorVersion, Is.EqualTo(7));
    Assert.That(restored.MinorVersion, Is.EqualTo(3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_Rgb24() {
    var pixels = new byte[] { 255, 0, 0, 0, 255, 0 };
    var file = new PspFile {
      Width = 2,
      Height = 1,
      PixelData = pixels
    };

    var raw = PspFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_Rgb24() {
    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 128, 64, 32, 16, 8]
    };

    var file = PspFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.BitDepth, Is.EqualTo(24));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PspFile {
      Width = 4,
      Height = 4,
      PixelData = new byte[4 * 4 * 3]
    };

    var bytes = PspWriter.ToBytes(original);
    var restored = PspReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
