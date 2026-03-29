using System;
using FileFormat.Wbmp;

namespace FileFormat.Wbmp.Tests;

[TestFixture]
public sealed class WbmpWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithTypeByte0() {
    var file = new WbmpFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = WbmpWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SecondByteIsFixedHeader0() {
    var file = new WbmpFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = WbmpWriter.ToBytes(file);

    Assert.That(bytes[1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SmallDimensions_SingleByteEncoding() {
    // Width=8, Height=2 => both fit in a single byte (< 128)
    var file = new WbmpFile {
      Width = 8,
      Height = 2,
      PixelData = new byte[2]
    };

    var bytes = WbmpWriter.ToBytes(file);

    // type(1) + fixedHeader(1) + width(1) + height(1) + pixelData(2) = 6
    Assert.That(bytes.Length, Is.EqualTo(6));
    Assert.That(bytes[2], Is.EqualTo(8));
    Assert.That(bytes[3], Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LargeDimensions_MultiByteEncoding() {
    // Width=200 => multi-byte: 0x81 0x48
    var bytesPerRow = (200 + 7) / 8; // 25
    var file = new WbmpFile {
      Width = 200,
      Height = 1,
      PixelData = new byte[bytesPerRow]
    };

    var bytes = WbmpWriter.ToBytes(file);

    // type(1) + fixedHeader(1) + width(2) + height(1) + pixelData(25) = 30
    Assert.That(bytes.Length, Is.EqualTo(30));
    Assert.That(bytes[2], Is.EqualTo(0x81));
    Assert.That(bytes[3], Is.EqualTo(0x48));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPresent() {
    var pixelData = new byte[] { 0b10110100 };
    var file = new WbmpFile {
      Width = 8,
      Height = 1,
      PixelData = pixelData
    };

    var bytes = WbmpWriter.ToBytes(file);

    // Last byte should be the pixel data
    Assert.That(bytes[^1], Is.EqualTo(0b10110100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectTotalLength() {
    // 16x3: bytesPerRow=2, pixelData=6 bytes
    var file = new WbmpFile {
      Width = 16,
      Height = 3,
      PixelData = new byte[6]
    };

    var bytes = WbmpWriter.ToBytes(file);

    // type(1) + fixedHeader(1) + width(1) + height(1) + pixelData(6) = 10
    Assert.That(bytes.Length, Is.EqualTo(10));
  }
}
