using System;
using FileFormat.Jng;

namespace FileFormat.Jng.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Color_PreservesFields() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0x00, 0x42, 0xFF, 0xD9 };
    var original = new JngFile {
      Width = 320,
      Height = 240,
      ColorType = 10,
      ImageSampleDepth = 8,
      AlphaSampleDepth = 0,
      AlphaCompression = JngAlphaCompression.PngDeflate,
      JpegData = jpegData,
      AlphaData = null
    };

    var bytes = JngWriter.ToBytes(original);
    var restored = JngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorType, Is.EqualTo(original.ColorType));
    Assert.That(restored.ImageSampleDepth, Is.EqualTo(original.ImageSampleDepth));
    Assert.That(restored.JpegData, Is.EqualTo(original.JpegData));
    Assert.That(restored.AlphaData, Is.Null);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gray_PreservesFields() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0x01, 0x02, 0x03, 0xFF, 0xD9 };
    var original = new JngFile {
      Width = 128,
      Height = 64,
      ColorType = 8,
      ImageSampleDepth = 8,
      AlphaSampleDepth = 0,
      AlphaCompression = JngAlphaCompression.PngDeflate,
      JpegData = jpegData,
      AlphaData = null
    };

    var bytes = JngWriter.ToBytes(original);
    var restored = JngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(128));
    Assert.That(restored.Height, Is.EqualTo(64));
    Assert.That(restored.ColorType, Is.EqualTo(8));
    Assert.That(restored.JpegData, Is.EqualTo(jpegData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPngAlpha_PreservesAlphaData() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
    var alphaData = new byte[] { 0x78, 0x9C, 0x01, 0x02, 0x03, 0x04 };
    var original = new JngFile {
      Width = 64,
      Height = 64,
      ColorType = 14,
      ImageSampleDepth = 8,
      AlphaSampleDepth = 8,
      AlphaCompression = JngAlphaCompression.PngDeflate,
      JpegData = jpegData,
      AlphaData = alphaData
    };

    var bytes = JngWriter.ToBytes(original);
    var restored = JngReader.FromBytes(bytes);

    Assert.That(restored.AlphaSampleDepth, Is.EqualTo(8));
    Assert.That(restored.AlphaCompression, Is.EqualTo(JngAlphaCompression.PngDeflate));
    Assert.That(restored.AlphaData, Is.EqualTo(alphaData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithoutAlpha_AlphaDataIsNull() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
    var original = new JngFile {
      Width = 16,
      Height = 16,
      ColorType = 10,
      ImageSampleDepth = 8,
      AlphaSampleDepth = 0,
      AlphaCompression = JngAlphaCompression.PngDeflate,
      JpegData = jpegData,
      AlphaData = null
    };

    var bytes = JngWriter.ToBytes(original);
    var restored = JngReader.FromBytes(bytes);

    Assert.That(restored.AlphaData, Is.Null);
    Assert.That(restored.AlphaSampleDepth, Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_JpegAlpha_PreservesAlphaData() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xAA, 0xBB, 0xFF, 0xD9 };
    var alphaData = new byte[] { 0xFF, 0xD8, 0xCC, 0xDD, 0xFF, 0xD9 };
    var original = new JngFile {
      Width = 32,
      Height = 32,
      ColorType = 14,
      ImageSampleDepth = 8,
      AlphaSampleDepth = 8,
      AlphaCompression = JngAlphaCompression.Jpeg,
      JpegData = jpegData,
      AlphaData = alphaData
    };

    var bytes = JngWriter.ToBytes(original);
    var restored = JngReader.FromBytes(bytes);

    Assert.That(restored.AlphaCompression, Is.EqualTo(JngAlphaCompression.Jpeg));
    Assert.That(restored.AlphaData, Is.EqualTo(alphaData));
    Assert.That(restored.JpegData, Is.EqualTo(jpegData));
  }
}
