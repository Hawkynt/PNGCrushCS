using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Uhdr;

namespace FileFormat.Uhdr.Tests;

[TestFixture]
public sealed class UhdrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithUhdrMagic() {
    var file = new UhdrFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = UhdrWriter.ToBytes(file);
    var magic = Encoding.ASCII.GetString(bytes, 0, 4);

    Assert.That(magic, Is.EqualTo("UHDR"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesVersion1() {
    var file = new UhdrFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = UhdrWriter.ToBytes(file);
    var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

    Assert.That(version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesCorrectDimensions() {
    var file = new UhdrFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = UhdrWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
    var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    var file = new UhdrFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[4 * 2 * 3]
    };

    var bytes = UhdrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(UhdrHeader.StructSize + 4 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new UhdrFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var bytes = UhdrWriter.ToBytes(file);

    Assert.That(bytes[UhdrHeader.StructSize], Is.EqualTo(0xAA));
    Assert.That(bytes[UhdrHeader.StructSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[UhdrHeader.StructSize + 2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ReservedFieldIsZero() {
    var file = new UhdrFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = UhdrWriter.ToBytes(file);
    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));

    Assert.That(reserved, Is.EqualTo(0));
  }
}
