using System;
using System.IO;
using FileFormat.Uhdr;

namespace FileFormat.Uhdr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1() {
    var original = new UhdrFile {
      Width = 1,
      Height = 1,
      PixelData = [42, 84, 126]
    };

    var bytes = UhdrWriter.ToBytes(original);
    var restored = UhdrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 64;
    var height = 32;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new UhdrFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = UhdrWriter.ToBytes(original);
    var restored = UhdrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".uhdr");
    try {
      var original = new UhdrFile {
        Width = 3,
        Height = 2,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ]
      };

      var bytes = UhdrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = UhdrReader.FromFile(new FileInfo(tempPath));

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
    var original = new UhdrFile {
      Width = 2,
      Height = 2,
      PixelData = [
        255, 0, 0, 0, 255, 0,
        0, 0, 255, 128, 128, 128
      ]
    };

    var raw = UhdrFile.ToRawImage(original);
    var restored = UhdrFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new UhdrFile {
      Width = 1,
      Height = 1,
      PixelData = [0, 0, 0]
    };

    var bytes = UhdrWriter.ToBytes(original);
    var restored = UhdrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
