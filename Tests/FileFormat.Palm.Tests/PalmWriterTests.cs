using System;
using System.Buffers.Binary;
using FileFormat.Palm;

namespace FileFormat.Palm.Tests;

[TestFixture]
public sealed class PalmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize() {
    var file = new PalmFile {
      Width = 8,
      Height = 1,
      BitsPerPixel = 1,
      PixelData = new byte[2] // word-aligned: ceil(8/8)=1, padded to 2
    };

    var bytes = PalmWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(PalmHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderFields() {
    var file = new PalmFile {
      Width = 16,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = new byte[16 * 4]
    };

    var bytes = PalmWriter.ToBytes(file);
    var span = bytes.AsSpan();

    var width = BinaryPrimitives.ReadUInt16BigEndian(span);
    var height = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
    var bpp = span[8];

    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(4));
    Assert.That(bpp, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => PalmWriter.ToBytes(null!));
  }
}
