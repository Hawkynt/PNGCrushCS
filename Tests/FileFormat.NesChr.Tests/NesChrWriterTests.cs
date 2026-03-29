using System;
using FileFormat.NesChr;

namespace FileFormat.NesChr.Tests;

[TestFixture]
public sealed class NesChrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => NesChrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleTile_Returns16Bytes() {
    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = NesChrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16 * 16)); // 16 tiles across x 16 bytes each
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsMultipleOf16() {
    var file = new NesChrFile {
      Width = 128,
      Height = 16,
      PixelData = new byte[128 * 16]
    };

    var bytes = NesChrWriter.ToBytes(file);

    Assert.That(bytes.Length % 16, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValue3_BothPlanesSet() {
    var pixels = new byte[128 * 8];
    pixels[0] = 3; // pixel (0,0) = 3 => both planes set

    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = NesChrWriter.ToBytes(file);

    // Tile 0: plane0 row0 should have MSB set, plane1 row0 should have MSB set
    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x80));  // plane0 row0 MSB
    Assert.That(bytes[8] & 0x80, Is.EqualTo(0x80));  // plane1 row0 MSB
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValue1_OnlyPlane0Set() {
    var pixels = new byte[128 * 8];
    pixels[0] = 1;

    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = NesChrWriter.ToBytes(file);

    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x80));  // plane0 set
    Assert.That(bytes[8] & 0x80, Is.EqualTo(0x00));  // plane1 clear
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValue2_OnlyPlane1Set() {
    var pixels = new byte[128 * 8];
    pixels[0] = 2;

    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = NesChrWriter.ToBytes(file);

    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x00));  // plane0 clear
    Assert.That(bytes[8] & 0x80, Is.EqualTo(0x80));  // plane1 set
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllZeros_AllBytesZero() {
    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = NesChrWriter.ToBytes(file);

    for (var i = 0; i < bytes.Length; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Byte at index {i} should be 0");
  }
}
