using System;
using System.Buffers.Binary;
using FileFormat.AliasPix;

namespace FileFormat.AliasPix.Tests;

[TestFixture]
public sealed class AliasPixWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize() {
    var file = new AliasPixFile {
      Width = 1,
      Height = 1,
      BitsPerPixel = 24,
      PixelData = new byte[] { 0x10, 0x20, 0x30 }
    };

    var bytes = AliasPixWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(AliasPixHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderValues() {
    var file = new AliasPixFile {
      Width = 320,
      Height = 240,
      XOffset = 10,
      YOffset = 20,
      BitsPerPixel = 24,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = AliasPixWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));
    var xOffset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
    var yOffset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6));
    var bpp = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
    Assert.That(xOffset, Is.EqualTo(10));
    Assert.That(yOffset, Is.EqualTo(20));
    Assert.That(bpp, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => AliasPixWriter.ToBytes(null!));
  }
}
