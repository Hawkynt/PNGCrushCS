using System;
using System.IO;
using FileFormat.GeoPaint;

namespace FileFormat.GeoPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleScanline() {
    var pixelData = new byte[80];
    pixelData[0] = 0xFF;
    pixelData[39] = 0xAA;
    pixelData[79] = 0x55;

    var original = new GeoPaintFile {
      Height = 1,
      PixelData = pixelData
    };

    var bytes = GeoPaintWriter.ToBytes(original);
    var restored = GeoPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleScanlines() {
    var height = 10;
    var pixelData = new byte[80 * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new GeoPaintFile {
      Height = height,
      PixelData = pixelData
    };

    var bytes = GeoPaintWriter.ToBytes(original);
    var restored = GeoPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(640));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var height = 5;
    var original = new GeoPaintFile {
      Height = height,
      PixelData = new byte[80 * height]
    };

    var bytes = GeoPaintWriter.ToBytes(original);
    var restored = GeoPaintReader.FromBytes(bytes);

    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var height = 3;
    var pixelData = new byte[80 * height];
    Array.Fill(pixelData, (byte)0xFF);

    var original = new GeoPaintFile {
      Height = height,
      PixelData = pixelData
    };

    var bytes = GeoPaintWriter.ToBytes(original);
    var restored = GeoPaintReader.FromBytes(bytes);

    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MaxHeight() {
    var height = GeoPaintFile.MaxHeight;
    var pixelData = new byte[80 * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new GeoPaintFile {
      Height = height,
      PixelData = pixelData
    };

    var bytes = GeoPaintWriter.ToBytes(original);
    var restored = GeoPaintReader.FromBytes(bytes);

    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var height = 4;
    var pixelData = new byte[80 * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new GeoPaintFile {
      Height = height,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".geo");
    try {
      var bytes = GeoPaintWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = GeoPaintReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_Checkerboard() {
    var height = 2;
    var pixelData = new byte[80 * height];
    for (var i = 0; i < 80; ++i)
      pixelData[i] = 0xAA; // 10101010 pattern
    for (var i = 80; i < 160; ++i)
      pixelData[i] = 0x55; // 01010101 pattern

    var original = new GeoPaintFile {
      Height = height,
      PixelData = pixelData
    };

    var bytes = GeoPaintWriter.ToBytes(original);
    var restored = GeoPaintReader.FromBytes(bytes);

    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void Compression_ReducesSize_ForAllZeros() {
    var height = 100;
    var pixelData = new byte[80 * height];

    var file = new GeoPaintFile {
      Height = height,
      PixelData = pixelData
    };

    var bytes = GeoPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.LessThan(pixelData.Length));
  }
}
