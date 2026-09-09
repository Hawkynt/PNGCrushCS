using System;
using System.IO;
using FileFormat.Bpg;

namespace FileFormat.Bpg.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_8Bit() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 20);

    var original = new BpgFile {
      Width = 4,
      Height = 3,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = pixelData,
    };

    var bytes = BpgWriter.ToBytes(original);
    var restored = BpgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelFormat, Is.EqualTo(BpgPixelFormat.Grayscale));
    Assert.That(restored.BitDepth, Is.EqualTo(8));
    Assert.That(restored.ColorSpace, Is.EqualTo(BpgColorSpace.Rgb));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_8Bit() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17);

    var original = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.YCbCr444,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = pixelData,
    };

    var bytes = BpgWriter.ToBytes(original);
    var restored = BpgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelFormat, Is.EqualTo(BpgPixelFormat.YCbCr444));
    Assert.That(restored.BitDepth, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[10 * 10];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new BpgFile {
      Width = 10,
      Height = 10,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bpg");
    try {
      var bytes = BpgWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = BpgReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelFormat, Is.EqualTo(original.PixelFormat));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  public void ToRawImage_WithoutHevcPictureData_Throws() {
    var bpg = new BpgFile {
      Width = 3,
      Height = 3,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = [],
    };

    Assert.Throws<InvalidOperationException>(() => BpgFile.ToRawImage(bpg));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = new byte[4],
    };

    var bytes = BpgWriter.ToBytes(original);
    var restored = BpgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GradientData() {
    var pixelData = new byte[256];
    for (var i = 0; i < 256; ++i)
      pixelData[i] = (byte)i;

    var original = new BpgFile {
      Width = 16,
      Height = 16,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = pixelData,
    };

    var bytes = BpgWriter.ToBytes(original);
    var restored = BpgReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithAlpha() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

    var original = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.YCbCr444,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      HasAlpha = true,
      PixelData = pixelData,
    };

    var bytes = BpgWriter.ToBytes(original);
    var restored = BpgReader.FromBytes(bytes);

    Assert.That(restored.HasAlpha, Is.True);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFlags() {
    var original = new BpgFile {
      Width = 8,
      Height = 8,
      PixelFormat = BpgPixelFormat.YCbCr420,
      BitDepth = 10,
      ColorSpace = BpgColorSpace.YCbCrBT2020,
      HasAlpha = true,
      HasAlpha2 = true,
      LimitedRange = true,
      IsAnimation = true,
      PixelData = [0xFF],
    };

    var bytes = BpgWriter.ToBytes(original);
    var restored = BpgReader.FromBytes(bytes);

    Assert.That(restored.PixelFormat, Is.EqualTo(BpgPixelFormat.YCbCr420));
    Assert.That(restored.BitDepth, Is.EqualTo(10));
    Assert.That(restored.ColorSpace, Is.EqualTo(BpgColorSpace.YCbCrBT2020));
    Assert.That(restored.HasAlpha, Is.True);
    Assert.That(restored.HasAlpha2, Is.True);
    Assert.That(restored.LimitedRange, Is.True);
    Assert.That(restored.IsAnimation, Is.True);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeDimensions() {
    var original = new BpgFile {
      Width = 3840,
      Height = 2160,
      PixelFormat = BpgPixelFormat.YCbCr420,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.YCbCrBT709,
      PixelData = [0x00, 0x01],
    };

    var bytes = BpgWriter.ToBytes(original);
    var restored = BpgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(3840));
    Assert.That(restored.Height, Is.EqualTo(2160));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ExtensionData() {
    var extensionData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
    var original = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      ExtensionPresent = true,
      ExtensionData = extensionData,
      PixelData = [0xAA],
    };

    var bytes = BpgWriter.ToBytes(original);
    var restored = BpgReader.FromBytes(bytes);

    Assert.That(restored.ExtensionPresent, Is.True);
    Assert.That(restored.ExtensionData, Is.EqualTo(extensionData));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
