using System;
using System.IO;
using FileFormat.Krita;

namespace FileFormat.Krita.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var width = 2;
    var height = 2;
    var rgba = _BuildGradientRgba(width, height);

    var original = new KritaFile {
      Width = width,
      Height = height,
      PixelData = rgba
    };

    var bytes = KritaWriter.ToBytes(original);
    var restored = KritaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 4;
    var height = 3;
    var rgba = _BuildGradientRgba(width, height);

    var original = new KritaFile {
      Width = width,
      Height = height,
      PixelData = rgba
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".kra");
    try {
      var bytes = KritaWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = KritaReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var rgba = _BuildGradientRgba(width, height);

    var original = new KritaFile {
      Width = width,
      Height = height,
      PixelData = rgba
    };

    var bytes = KritaWriter.ToBytes(original);
    var restored = KritaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var width = 3;
    var height = 3;
    var rgba = _BuildGradientRgba(width, height);

    var original = new KritaFile {
      Width = width,
      Height = height,
      PixelData = rgba
    };

    var rawImage = KritaFile.ToRawImage(original);
    var restored = KritaFile.FromRawImage(rawImage);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var width = 4;
    var height = 4;
    var rgba = new byte[width * height * 4];

    var original = new KritaFile {
      Width = width,
      Height = height,
      PixelData = rgba
    };

    var bytes = KritaWriter.ToBytes(original);
    var restored = KritaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  private static byte[] _BuildGradientRgba(int width, int height) {
    var data = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      data[i * 4] = (byte)(i * 7 % 256);
      data[i * 4 + 1] = (byte)(i * 13 % 256);
      data[i * 4 + 2] = (byte)(i * 23 % 256);
      data[i * 4 + 3] = 255;
    }

    return data;
  }
}
