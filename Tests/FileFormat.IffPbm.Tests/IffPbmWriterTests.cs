using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.IffPbm;

namespace FileFormat.IffPbm.Tests;

[TestFixture]
public sealed class IffPbmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffPbmWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFormMagic() {
    var file = _CreateMinimalFile();
    var bytes = IffPbmWriter.ToBytes(file);

    var magic = Encoding.ASCII.GetString(bytes, 0, 4);
    Assert.That(magic, Is.EqualTo("FORM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasPbmFormType() {
    var file = _CreateMinimalFile();
    var bytes = IffPbmWriter.ToBytes(file);

    var formType = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(formType, Is.EqualTo("PBM "));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBmhdChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffPbmWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "BMHD"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsCmapChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffPbmWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "CMAP"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBodyChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffPbmWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "BODY"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsPreserved() {
    var file = new IffPbmFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200],
      Palette = new byte[256 * 3],
      Compression = IffPbmCompression.None,
      XAspect = 1,
      YAspect = 1,
      PageWidth = 320,
      PageHeight = 200,
    };

    var bytes = IffPbmWriter.ToBytes(file);

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
  public void ToBytes_BmhdNumPlanesIs8() {
    var file = _CreateMinimalFile();
    var bytes = IffPbmWriter.ToBytes(file);

    var bmhdDataOffset = _FindChunkDataOffset(bytes, "BMHD");
    Assert.That(bmhdDataOffset, Is.GreaterThan(0));

    // numPlanes is at offset 8 within BMHD
    Assert.That(bytes[bmhdDataOffset + 8], Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FormSizeFieldCorrect() {
    var file = _CreateMinimalFile();
    var bytes = IffPbmWriter.ToBytes(file);

    var formSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(formSize, Is.EqualTo(bytes.Length - 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NoPalette_OmitsCmapChunk() {
    var file = new IffPbmFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[4 * 2],
      Palette = null,
      Compression = IffPbmCompression.None,
      XAspect = 1,
      YAspect = 1,
      PageWidth = 4,
      PageHeight = 2,
    };

    var bytes = IffPbmWriter.ToBytes(file);
    Assert.That(_FindChunk(bytes, "CMAP"), Is.False);
  }

  private static IffPbmFile _CreateMinimalFile() => new() {
    Width = 8,
    Height = 2,
    PixelData = new byte[8 * 2],
    Palette = new byte[256 * 3],
    Compression = IffPbmCompression.None,
    XAspect = 1,
    YAspect = 1,
    PageWidth = 8,
    PageHeight = 2,
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
