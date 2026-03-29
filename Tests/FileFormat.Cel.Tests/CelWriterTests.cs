using System;
using FileFormat.Cel;

namespace FileFormat.Cel.Tests;

[TestFixture]
public sealed class CelWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => CelWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes() {
    var file = new CelFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 32,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = CelWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'K'));
    Assert.That(bytes[1], Is.EqualTo((byte)'i'));
    Assert.That(bytes[2], Is.EqualTo((byte)'S'));
    Assert.That(bytes[3], Is.EqualTo((byte)'S'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MarkByte_Rgba32() {
    var file = new CelFile {
      Width = 1,
      Height = 1,
      BitsPerPixel = 32,
      PixelData = new byte[4]
    };

    var bytes = CelWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0x20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MarkByte_Indexed8() {
    var file = new CelFile {
      Width = 1,
      Height = 1,
      BitsPerPixel = 8,
      PixelData = new byte[1]
    };

    var bytes = CelWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0x04));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MarkByte_Indexed4() {
    var file = new CelFile {
      Width = 2,
      Height = 1,
      BitsPerPixel = 4,
      PixelData = new byte[1]
    };

    var bytes = CelWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0x04));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BppField() {
    var file = new CelFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = new byte[4]
    };

    var bytes = CelWriter.ToBytes(file);

    Assert.That(bytes[5], Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsInHeader() {
    var file = new CelFile {
      Width = 100,
      Height = 200,
      BitsPerPixel = 8,
      PixelData = new byte[100 * 200]
    };

    var bytes = CelWriter.ToBytes(file);

    var width = BitConverter.ToUInt32(bytes, 8);
    var height = BitConverter.ToUInt32(bytes, 12);

    Assert.That(width, Is.EqualTo(100));
    Assert.That(height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OffsetsInHeader() {
    var file = new CelFile {
      Width = 1,
      Height = 1,
      BitsPerPixel = 32,
      XOffset = 42,
      YOffset = 99,
      PixelData = new byte[4]
    };

    var bytes = CelWriter.ToBytes(file);

    var xOffset = BitConverter.ToUInt32(bytes, 16);
    var yOffset = BitConverter.ToUInt32(bytes, 20);

    Assert.That(xOffset, Is.EqualTo(42));
    Assert.That(yOffset, Is.EqualTo(99));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_Rgba32() {
    var file = new CelFile {
      Width = 3,
      Height = 2,
      BitsPerPixel = 32,
      PixelData = new byte[3 * 2 * 4]
    };

    var bytes = CelWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(32 + 3 * 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_Indexed8() {
    var file = new CelFile {
      Width = 5,
      Height = 3,
      BitsPerPixel = 8,
      PixelData = new byte[5 * 3]
    };

    var bytes = CelWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(32 + 5 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var file = new CelFile {
      Width = 4,
      Height = 1,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var bytes = CelWriter.ToBytes(file);

    Assert.That(bytes[32], Is.EqualTo(0xDE));
    Assert.That(bytes[33], Is.EqualTo(0xAD));
    Assert.That(bytes[34], Is.EqualTo(0xBE));
    Assert.That(bytes[35], Is.EqualTo(0xEF));
  }
}
