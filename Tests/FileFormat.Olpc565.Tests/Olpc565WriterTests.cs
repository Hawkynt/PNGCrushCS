using System;
using System.Buffers.Binary;
using FileFormat.Olpc565;

namespace FileFormat.Olpc565.Tests;

[TestFixture]
public sealed class Olpc565WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => Olpc565Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderDimensions() {
    var file = new Olpc565File {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 2]
    };

    var bytes = Olpc565Writer.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    // 4x2 = 8 pixels = 16 bytes pixel data, total = 4 + 16 = 20
    var file = new Olpc565File {
      Width = 4,
      Height = 2,
      PixelData = new byte[16]
    };

    var bytes = Olpc565Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0x00, 0xF8, 0x1F, 0x00 }; // red, blue
    var file = new Olpc565File {
      Width = 2,
      Height = 1,
      PixelData = pixelData
    };

    var bytes = Olpc565Writer.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0x00));
    Assert.That(bytes[5], Is.EqualTo(0xF8));
    Assert.That(bytes[6], Is.EqualTo(0x1F));
    Assert.That(bytes[7], Is.EqualTo(0x00));
  }
}
