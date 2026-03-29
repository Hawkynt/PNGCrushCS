using System;
using System.Buffers.Binary;
using FileFormat.Aai;

namespace FileFormat.Aai.Tests;

[TestFixture]
public sealed class AaiWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsWidthHeight() {
    var file = new AaiFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 4]
    };

    var bytes = AaiWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));

    Assert.That(width, Is.EqualTo(320u));
    Assert.That(height, Is.EqualTo(240u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalLength() {
    var w = 4;
    var h = 3;
    var file = new AaiFile {
      Width = w,
      Height = h,
      PixelData = new byte[w * h * 4]
    };

    var bytes = AaiWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8 + w * h * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xFF };
    var file = new AaiFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var bytes = AaiWriter.ToBytes(file);

    Assert.That(bytes[8], Is.EqualTo(0xAA));
    Assert.That(bytes[9], Is.EqualTo(0xBB));
    Assert.That(bytes[10], Is.EqualTo(0xCC));
    Assert.That(bytes[11], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => AaiWriter.ToBytes(null!));
  }
}
