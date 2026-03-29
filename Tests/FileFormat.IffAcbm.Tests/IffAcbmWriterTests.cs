using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.IffAcbm;

namespace FileFormat.IffAcbm.Tests;

[TestFixture]
public sealed class IffAcbmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAcbmWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFormMagic() {
    var file = _CreateMinimalFile();
    var bytes = IffAcbmWriter.ToBytes(file);

    var magic = Encoding.ASCII.GetString(bytes, 0, 4);
    Assert.That(magic, Is.EqualTo("FORM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasAcbmFormType() {
    var file = _CreateMinimalFile();
    var bytes = IffAcbmWriter.ToBytes(file);

    var formType = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(formType, Is.EqualTo("ACBM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBmhdChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffAcbmWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "BMHD"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsCmapChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffAcbmWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "CMAP"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsAbitChunk() {
    var file = _CreateMinimalFile();
    var bytes = IffAcbmWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "ABIT"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NoCmapWhenEmptyPalette() {
    var file = new IffAcbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 1,
      PixelData = new byte[8 * 2],
      Palette = [],
    };
    var bytes = IffAcbmWriter.ToBytes(file);

    Assert.That(_FindChunk(bytes, "CMAP"), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BmhdDimensionsPreserved() {
    var file = new IffAcbmFile {
      Width = 320,
      Height = 200,
      NumPlanes = 4,
      PixelData = new byte[320 * 200],
      Palette = new byte[16 * 3],
    };

    var bytes = IffAcbmWriter.ToBytes(file);
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
  public void ToBytes_BmhdNumPlanesPreserved() {
    var file = new IffAcbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 5,
      PixelData = new byte[8 * 2],
      Palette = new byte[32 * 3],
    };

    var bytes = IffAcbmWriter.ToBytes(file);
    var bmhdDataOffset = _FindChunkDataOffset(bytes, "BMHD");
    Assert.That(bmhdDataOffset, Is.GreaterThan(0));

    Assert.That(bytes[bmhdDataOffset + 8], Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FormSizeFieldCorrect() {
    var file = _CreateMinimalFile();
    var bytes = IffAcbmWriter.ToBytes(file);

    var formSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(formSize, Is.EqualTo(bytes.Length - 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AbitChunkSizeMatchesExpected() {
    var file = new IffAcbmFile {
      Width = 16,
      Height = 4,
      NumPlanes = 2,
      PixelData = new byte[16 * 4],
      Palette = new byte[4 * 3],
    };

    var bytes = IffAcbmWriter.ToBytes(file);
    var abitOffset = _FindChunkDataOffset(bytes, "ABIT");
    Assert.That(abitOffset, Is.GreaterThan(0));

    var abitSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(abitOffset - 4));
    var bytesPerPlaneRow = ((16 + 15) / 16) * 2; // = 2
    var expectedAbitSize = bytesPerPlaneRow * 4 * 2; // 2 planes
    Assert.That(abitSize, Is.EqualTo(expectedAbitSize));
  }

  private static IffAcbmFile _CreateMinimalFile() => new() {
    Width = 8,
    Height = 2,
    NumPlanes = 1,
    PixelData = new byte[8 * 2],
    Palette = new byte[2 * 3],
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
