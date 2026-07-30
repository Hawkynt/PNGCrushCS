using System;
using System.Buffers.Binary;
using FileFormat.Sgi;

namespace FileFormat.Sgi.Tests;

[TestFixture]
public sealed class SgiWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb_StartsWithMagic() {
    var file = new SgiFile {
      Width = 2,
      Height = 2,
      Channels = 3,
      BytesPerChannel = 1,
      PixelData = new byte[2 * 2 * 3],
      Compression = SgiCompression.None,
      ColorMode = SgiColorMode.Normal
    };

    var bytes = SgiWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0));
    Assert.That(magic, Is.EqualTo(0x01DA));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderIs512Bytes() {
    var file = new SgiFile {
      Width = 1,
      Height = 1,
      Channels = 1,
      BytesPerChannel = 1,
      PixelData = new byte[1],
      Compression = SgiCompression.None,
      ColorMode = SgiColorMode.Normal
    };

    var bytes = SgiWriter.ToBytes(file);

    // Uncompressed: header (512) + pixel data (1) = 513
    Assert.That(bytes.Length, Is.EqualTo(513));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectDimensions() {
    var file = new SgiFile {
      Width = 10,
      Height = 20,
      Channels = 4,
      BytesPerChannel = 1,
      PixelData = new byte[10 * 20 * 4],
      Compression = SgiCompression.None,
      ColorMode = SgiColorMode.Normal
    };

    var bytes = SgiWriter.ToBytes(file);

    var xSize = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6));
    var ySize = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8));
    var zSize = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10));
    Assert.That(xSize, Is.EqualTo(10));
    Assert.That(ySize, Is.EqualTo(20));
    Assert.That(zSize, Is.EqualTo(4));
  }
}
