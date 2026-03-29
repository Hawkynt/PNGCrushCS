using System;
using FileFormat.Wbmp;

namespace FileFormat.Wbmp.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_8x1_AllWhite() {
    var original = new WbmpFile {
      Width = 8,
      Height = 1,
      PixelData = [0xFF]
    };

    var bytes = WbmpWriter.ToBytes(original);
    var restored = WbmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_8x1_AllBlack() {
    var original = new WbmpFile {
      Width = 8,
      Height = 1,
      PixelData = [0x00]
    };

    var bytes = WbmpWriter.ToBytes(original);
    var restored = WbmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_16x2_Checkerboard() {
    // 16 pixels wide = 2 bytes per row, 2 rows
    var original = new WbmpFile {
      Width = 16,
      Height = 2,
      PixelData = [0xAA, 0x55, 0x55, 0xAA]
    };

    var bytes = WbmpWriter.ToBytes(original);
    var restored = WbmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonByteBoundaryWidth() {
    // 5 pixels wide = ceil(5/8) = 1 byte per row, 3 rows
    var original = new WbmpFile {
      Width = 5,
      Height = 3,
      PixelData = [0b11010000, 0b10100000, 0b01110000]
    };

    var bytes = WbmpWriter.ToBytes(original);
    var restored = WbmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(5));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeWidth_MultiByteEncoding() {
    // Width=200 requires multi-byte encoding (>127)
    var bytesPerRow = (200 + 7) / 8; // 25
    var pixelData = new byte[bytesPerRow * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new WbmpFile {
      Width = 200,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = WbmpWriter.ToBytes(original);
    var restored = WbmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(200));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeHeight_MultiByteEncoding() {
    // Height=300 requires multi-byte encoding
    var bytesPerRow = 1; // 8 pixels wide
    var pixelData = new byte[bytesPerRow * 300];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 2 == 0 ? 0xFF : 0x00);

    var original = new WbmpFile {
      Width = 8,
      Height = 300,
      PixelData = pixelData
    };

    var bytes = WbmpWriter.ToBytes(original);
    var restored = WbmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(300));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BothDimensionsMultiByte() {
    // Both width=200 and height=150 require multi-byte encoding
    var bytesPerRow = (200 + 7) / 8; // 25
    var pixelData = new byte[bytesPerRow * 150];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new WbmpFile {
      Width = 200,
      Height = 150,
      PixelData = pixelData
    };

    var bytes = WbmpWriter.ToBytes(original);
    var restored = WbmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(200));
    Assert.That(restored.Height, Is.EqualTo(150));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1_SinglePixel() {
    var original = new WbmpFile {
      Width = 1,
      Height = 1,
      PixelData = [0x80] // MSB set = white pixel
    };

    var bytes = WbmpWriter.ToBytes(original);
    var restored = WbmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
