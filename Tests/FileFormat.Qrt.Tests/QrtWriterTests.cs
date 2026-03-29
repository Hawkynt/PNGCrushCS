using System;
using System.Buffers.Binary;
using FileFormat.Qrt;

namespace FileFormat.Qrt.Tests;

[TestFixture]
public sealed class QrtWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesCorrectDimensions() {
    var file = new QrtFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = QrtWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    var file = new QrtFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[4 * 2 * 3]
    };

    var bytes = QrtWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(QrtHeader.StructSize + 4 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new QrtFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var bytes = QrtWriter.ToBytes(file);

    Assert.That(bytes[QrtHeader.StructSize], Is.EqualTo(0xAA));
    Assert.That(bytes[QrtHeader.StructSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[QrtHeader.StructSize + 2], Is.EqualTo(0xCC));
  }
}
