using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Jng;

namespace FileFormat.Jng.Tests;

[TestFixture]
public sealed class JngWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JngWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithJngSignature() {
    var file = _CreateMinimalFile();
    var bytes = JngWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x8B));
    Assert.That(bytes[1], Is.EqualTo(0x4A));
    Assert.That(bytes[2], Is.EqualTo(0x4E));
    Assert.That(bytes[3], Is.EqualTo(0x47));
    Assert.That(bytes[4], Is.EqualTo(0x0D));
    Assert.That(bytes[5], Is.EqualTo(0x0A));
    Assert.That(bytes[6], Is.EqualTo(0x1A));
    Assert.That(bytes[7], Is.EqualTo(0x0A));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasJhdrChunk() {
    var file = _CreateMinimalFile();
    var bytes = JngWriter.ToBytes(file);

    // After 8-byte signature, first chunk should be JHDR
    var chunkType = Encoding.ASCII.GetString(bytes, 12, 4);
    Assert.That(chunkType, Is.EqualTo("JHDR"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasJdatChunk() {
    var file = _CreateMinimalFile();
    var bytes = JngWriter.ToBytes(file);

    var found = _FindChunk(bytes, "JDAT");
    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasIendChunk() {
    var file = _CreateMinimalFile();
    var bytes = JngWriter.ToBytes(file);

    var found = _FindChunk(bytes, "IEND");
    Assert.That(found, Is.True);
  }

  private static JngFile _CreateMinimalFile() => new() {
    Width = 16,
    Height = 16,
    ColorType = 10,
    ImageSampleDepth = 8,
    AlphaSampleDepth = 0,
    AlphaCompression = JngAlphaCompression.PngDeflate,
    JpegData = [0xFF, 0xD8, 0xFF, 0xD9]
  };

  private static bool _FindChunk(byte[] data, string type) {
    var offset = 8; // Skip signature
    while (offset + 8 <= data.Length) {
      var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
      var chunkType = Encoding.ASCII.GetString(data, offset + 4, 4);
      if (chunkType == type)
        return true;

      if (chunkLength < 0)
        break;

      offset += 8 + chunkLength + 4; // header + data + CRC
    }

    return false;
  }
}
