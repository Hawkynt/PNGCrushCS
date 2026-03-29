using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.IffRgb8;

namespace FileFormat.IffRgb8.Tests;

[TestFixture]
public sealed class IffRgb8WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgb8Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFormMagic() {
    var file = _CreateMinimalFile();
    var bytes = IffRgb8Writer.ToBytes(file);

    var magic = Encoding.ASCII.GetString(bytes, 0, 4);
    Assert.That(magic, Is.EqualTo("FORM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasRgb8FormType() {
    var file = _CreateMinimalFile();
    var bytes = IffRgb8Writer.ToBytes(file);

    var formType = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(formType, Is.EqualTo("RGB8"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBmhdChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffRgb8Writer.ToBytes(file);

    Assert.That(_FindChunk(bytes, "BMHD"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBodyChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffRgb8Writer.ToBytes(file);

    Assert.That(_FindChunk(bytes, "BODY"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BmhdNumPlanesIs25() {
    var file = _CreateMinimalFile();
    var bytes = IffRgb8Writer.ToBytes(file);

    var bmhdDataOffset = _FindChunkDataOffset(bytes, "BMHD");
    Assert.That(bmhdDataOffset, Is.GreaterThan(0));

    // NumPlanes is at offset 8 within BMHD data
    var numPlanes = bytes[bmhdDataOffset + 8];
    Assert.That(numPlanes, Is.EqualTo(25));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsPreserved() {
    var file = new IffRgb8File {
      Width = 320,
      Height = 200,
      Compression = IffRgb8Compression.None,
      PixelData = new byte[320 * 200 * 3],
    };

    var bytes = IffRgb8Writer.ToBytes(file);
    var bmhdDataOffset = _FindChunkDataOffset(bytes, "BMHD");
    Assert.That(bmhdDataOffset, Is.GreaterThan(0));

    var w = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(bmhdDataOffset));
    var h = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(bmhdDataOffset + 2));

    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(320));
      Assert.That(h, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CompressionFieldPreserved() {
    var file = new IffRgb8File {
      Width = 2,
      Height = 1,
      Compression = IffRgb8Compression.ByteRun1,
      PixelData = new byte[2 * 1 * 3],
    };

    var bytes = IffRgb8Writer.ToBytes(file);
    var bmhdDataOffset = _FindChunkDataOffset(bytes, "BMHD");
    Assert.That(bmhdDataOffset, Is.GreaterThan(0));

    // Compression is at offset 10 within BMHD data
    var compression = bytes[bmhdDataOffset + 10];
    Assert.That(compression, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FormSizeIsCorrect() {
    var file = _CreateMinimalFile();
    var bytes = IffRgb8Writer.ToBytes(file);

    var formSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(formSize, Is.EqualTo(bytes.Length - 8));
  }

  private static IffRgb8File _CreateMinimalFile() => new() {
    Width = 2,
    Height = 2,
    Compression = IffRgb8Compression.None,
    PixelData = new byte[2 * 2 * 3],
  };

  private static bool _FindChunk(byte[] data, string chunkId) {
    var id = Encoding.ASCII.GetBytes(chunkId);
    for (var i = 12; i + 8 <= data.Length; ++i)
      if (data[i] == id[0] && data[i + 1] == id[1] && data[i + 2] == id[2] && data[i + 3] == id[3])
        return true;
    return false;
  }

  private static int _FindChunkDataOffset(byte[] data, string chunkId) {
    var id = Encoding.ASCII.GetBytes(chunkId);
    for (var i = 12; i + 8 <= data.Length; ++i)
      if (data[i] == id[0] && data[i + 1] == id[1] && data[i + 2] == id[2] && data[i + 3] == id[3])
        return i + 8;
    return -1;
  }
}
