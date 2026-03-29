using System;
using FileFormat.Xyz;

namespace FileFormat.Xyz.Tests;

[TestFixture]
public sealed class XyzWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithXyz1Magic() {
    var file = new XyzFile {
      Width = 1,
      Height = 1,
      Palette = new byte[768],
      PixelData = [0],
    };

    var bytes = XyzWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'X'));
    Assert.That(bytes[1], Is.EqualTo((byte)'Y'));
    Assert.That(bytes[2], Is.EqualTo((byte)'Z'));
    Assert.That(bytes[3], Is.EqualTo((byte)'1'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WidthIsLittleEndian() {
    var file = new XyzFile {
      Width = 320,
      Height = 1,
      Palette = new byte[768],
      PixelData = new byte[320],
    };

    var bytes = XyzWriter.ToBytes(file);

    // 320 = 0x0140 => LE: 0x40, 0x01
    Assert.That(bytes[4], Is.EqualTo(0x40));
    Assert.That(bytes[5], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeightIsLittleEndian() {
    var file = new XyzFile {
      Width = 1,
      Height = 240,
      Palette = new byte[768],
      PixelData = new byte[240],
    };

    var bytes = XyzWriter.ToBytes(file);

    // 240 = 0x00F0 => LE: 0xF0, 0x00
    Assert.That(bytes[6], Is.EqualTo(0xF0));
    Assert.That(bytes[7], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CompressedDataPresent() {
    var file = new XyzFile {
      Width = 1,
      Height = 1,
      Palette = new byte[768],
      PixelData = [0],
    };

    var bytes = XyzWriter.ToBytes(file);

    // Must be larger than just the header (8 bytes + zlib header 2 bytes)
    Assert.That(bytes.Length, Is.GreaterThan(10));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ZlibHeaderPresent() {
    var file = new XyzFile {
      Width = 1,
      Height = 1,
      Palette = new byte[768],
      PixelData = [0],
    };

    var bytes = XyzWriter.ToBytes(file);

    // Zlib header: 0x78 0x9C (default compression)
    Assert.That(bytes[8], Is.EqualTo(0x78));
    Assert.That(bytes[9], Is.EqualTo(0x9C));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsSmaller_ThanUncompressed() {
    // 320x240 = 76800 pixels + 768 palette = 77568 uncompressed
    var file = new XyzFile {
      Width = 320,
      Height = 240,
      Palette = new byte[768],
      PixelData = new byte[320 * 240],
    };

    var bytes = XyzWriter.ToBytes(file);

    // 8 header + 2 zlib header + compressed + 4 adler32
    // All zeros should compress very well
    Assert.That(bytes.Length, Is.LessThan(77568 + 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsInHeader_MatchInput() {
    var file = new XyzFile {
      Width = 160,
      Height = 120,
      Palette = new byte[768],
      PixelData = new byte[160 * 120],
    };

    var bytes = XyzWriter.ToBytes(file);

    var width = bytes[4] | (bytes[5] << 8);
    var height = bytes[6] | (bytes[7] << 8);

    Assert.That(width, Is.EqualTo(160));
    Assert.That(height, Is.EqualTo(120));
  }
}
