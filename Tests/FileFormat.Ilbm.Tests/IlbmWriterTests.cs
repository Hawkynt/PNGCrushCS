using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

[TestFixture]
public sealed class IlbmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IlbmWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFormMagic() {
    var file = _CreateMinimalFile();
    var bytes = IlbmWriter.ToBytes(file);

    var magic = Encoding.ASCII.GetString(bytes, 0, 4);
    Assert.That(magic, Is.EqualTo("FORM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasIlbmFormType() {
    var file = _CreateMinimalFile();
    var bytes = IlbmWriter.ToBytes(file);

    var formType = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(formType, Is.EqualTo("ILBM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBmhdChunk() {
    var file = _CreateMinimalFile();
    var bytes = IlbmWriter.ToBytes(file);

    var found = _FindChunk(bytes, "BMHD");
    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBodyChunk() {
    var file = _CreateMinimalFile();
    var bytes = IlbmWriter.ToBytes(file);

    var found = _FindChunk(bytes, "BODY");
    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsPreserved() {
    var file = new IlbmFile {
      Width = 320,
      Height = 200,
      NumPlanes = 4,
      PixelData = new byte[320 * 200],
      Compression = IlbmCompression.None
    };

    var bytes = IlbmWriter.ToBytes(file);

    // BMHD starts at offset 12 (FORM header 8 + "ILBM" 4) + chunk header 8 = 20
    // Width is first 2 bytes of BMHD data (big-endian)
    var bmhdDataOffset = _FindChunkDataOffset(bytes, "BMHD");
    Assert.That(bmhdDataOffset, Is.GreaterThan(0));

    var w = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(bmhdDataOffset));
    var h = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(bmhdDataOffset + 2));

    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(320));
      Assert.That(h, Is.EqualTo(200));
    });
  }

  private static IlbmFile _CreateMinimalFile() => new() {
    Width = 8,
    Height = 2,
    NumPlanes = 1,
    PixelData = new byte[8 * 2],
    Compression = IlbmCompression.None,
    Palette = new byte[2 * 3]
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
