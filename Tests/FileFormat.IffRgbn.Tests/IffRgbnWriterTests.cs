using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.IffRgbn;

namespace FileFormat.IffRgbn.Tests;

[TestFixture]
public sealed class IffRgbnWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgbnWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFormMagic() {
    var file = _CreateMinimalFile();
    var bytes = IffRgbnWriter.ToBytes(file);

    var magic = Encoding.ASCII.GetString(bytes, 0, 4);
    Assert.That(magic, Is.EqualTo("FORM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasRgbnFormType() {
    var file = _CreateMinimalFile();
    var bytes = IffRgbnWriter.ToBytes(file);

    var formType = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(formType, Is.EqualTo("RGBN"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBmhdChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffRgbnWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "BMHD"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBodyChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffRgbnWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "BODY"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BmhdNumPlanesIs13() {
    var file = _CreateMinimalFile();
    var bytes = IffRgbnWriter.ToBytes(file);

    var bmhdDataOffset = _FindChunkDataOffset(bytes, "BMHD");
    Assert.That(bmhdDataOffset, Is.GreaterThan(0));

    var numPlanes = bytes[bmhdDataOffset + 8];
    Assert.That(numPlanes, Is.EqualTo(13));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsPreserved() {
    var file = new IffRgbnFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 3],
    };

    var bytes = IffRgbnWriter.ToBytes(file);
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
  public void ToBytes_FormSizeIsCorrect() {
    var file = _CreateMinimalFile();
    var bytes = IffRgbnWriter.ToBytes(file);

    var formSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(formSize, Is.EqualTo(bytes.Length - 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BodySizeIsTwoBytesPerPixel() {
    var file = new IffRgbnFile {
      Width = 3,
      Height = 2,
      PixelData = new byte[3 * 2 * 3],
    };

    var bytes = IffRgbnWriter.ToBytes(file);
    var bodyOffset = _FindChunkOffset(bytes, "BODY");
    Assert.That(bodyOffset, Is.GreaterThan(0));

    var bodySize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(bodyOffset + 4));
    Assert.That(bodySize, Is.EqualTo(3 * 2 * 2)); // 2 bytes per pixel
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_QuantizesChannelsCorrectly() {
    // 0xFF -> 15, 0x80 -> 8, 0x00 -> 0
    var file = new IffRgbnFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x80, 0x00],
    };

    var bytes = IffRgbnWriter.ToBytes(file);
    var bodyDataOffset = _FindChunkDataOffset(bytes, "BODY");
    Assert.That(bodyDataOffset, Is.GreaterThan(0));

    var hi = bytes[bodyDataOffset];
    var lo = bytes[bodyDataOffset + 1];

    Assert.Multiple(() => {
      Assert.That(hi >> 4, Is.EqualTo(15));        // R quantized
      Assert.That(hi & 0x0F, Is.EqualTo(8));       // G quantized: (0x80 + 8) / 17 = 136/17 = 8
      Assert.That(lo >> 4, Is.EqualTo(0));          // B quantized
      Assert.That(lo & 0x07, Is.EqualTo(0));        // repeat = 0
      Assert.That((lo >> 3) & 1, Is.EqualTo(0));    // genlock = 0
    });
  }

  private static IffRgbnFile _CreateMinimalFile() => new() {
    Width = 2,
    Height = 2,
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

  private static int _FindChunkOffset(byte[] data, string chunkId) {
    var id = Encoding.ASCII.GetBytes(chunkId);
    for (var i = 12; i + 8 <= data.Length; ++i)
      if (data[i] == id[0] && data[i + 1] == id[1] && data[i + 2] == id[2] && data[i + 3] == id[3])
        return i;
    return -1;
  }
}
