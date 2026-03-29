using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

[TestFixture]
public sealed class CamgTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_HamViewportMode_PreservesIsHam() {
    var original = new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 6,
      Compression = IlbmCompression.None,
      PixelData = new byte[8 * 2],
      Palette = new byte[16 * 3],
      ViewportMode = 0x800 // HAM flag
    };

    var bytes = IlbmWriter.ToBytes(original);
    var restored = IlbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.ViewportMode, Is.EqualTo(0x800u));
      Assert.That(restored.IsHam, Is.True);
      Assert.That(restored.IsEhb, Is.False);
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EhbViewportMode_PreservesIsEhb() {
    var original = new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 6,
      Compression = IlbmCompression.None,
      PixelData = new byte[8 * 2],
      Palette = new byte[32 * 3],
      ViewportMode = 0x80 // EHB flag
    };

    var bytes = IlbmWriter.ToBytes(original);
    var restored = IlbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.ViewportMode, Is.EqualTo(0x80u));
      Assert.That(restored.IsHam, Is.False);
      Assert.That(restored.IsEhb, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void ViewportMode_DefaultsToZero() {
    var file = new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 4,
      PixelData = new byte[8 * 2]
    };

    Assert.Multiple(() => {
      Assert.That(file.ViewportMode, Is.EqualTo(0u));
      Assert.That(file.IsHam, Is.False);
      Assert.That(file.IsEhb, Is.False);
    });
  }

  [Test]
  [Category("Integration")]
  public void BackwardCompatibility_FileWithoutCamg_ParsesWithZeroViewportMode() {
    // Build a minimal ILBM without CAMG chunk using the old-style writer path (ViewportMode=0)
    var data = IlbmReaderTests._BuildMinimalIlbm(8, 2, 4, IlbmCompression.None);
    var result = IlbmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.ViewportMode, Is.EqualTo(0u));
      Assert.That(result.IsHam, Is.False);
      Assert.That(result.IsEhb, Is.False);
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_NoCamgChunk_WhenViewportModeIsZero() {
    var file = new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 1,
      PixelData = new byte[8 * 2],
      Compression = IlbmCompression.None,
      Palette = new byte[2 * 3],
      ViewportMode = 0
    };

    var bytes = IlbmWriter.ToBytes(file);
    var found = _FindChunk(bytes, "CAMG");
    Assert.That(found, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void Writer_EmitsCamgChunk_WhenViewportModeIsNonZero() {
    var file = new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 6,
      PixelData = new byte[8 * 2],
      Compression = IlbmCompression.None,
      Palette = new byte[16 * 3],
      ViewportMode = 0x800
    };

    var bytes = IlbmWriter.ToBytes(file);
    var found = _FindChunk(bytes, "CAMG");
    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Writer_CamgChunkContainsCorrectValue() {
    var file = new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 6,
      PixelData = new byte[8 * 2],
      Compression = IlbmCompression.None,
      Palette = new byte[16 * 3],
      ViewportMode = 0x800
    };

    var bytes = IlbmWriter.ToBytes(file);
    var dataOffset = _FindChunkDataOffset(bytes, "CAMG");
    Assert.That(dataOffset, Is.GreaterThan(0));

    var value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(dataOffset));
    Assert.That(value, Is.EqualTo(0x800u));
  }

  [Test]
  [Category("Unit")]
  public void Writer_CamgChunkAppearsBeforeCmap() {
    var file = new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 6,
      PixelData = new byte[8 * 2],
      Compression = IlbmCompression.None,
      Palette = new byte[16 * 3],
      ViewportMode = 0x800
    };

    var bytes = IlbmWriter.ToBytes(file);
    var camgOffset = _FindChunkOffset(bytes, "CAMG");
    var cmapOffset = _FindChunkOffset(bytes, "CMAP");

    Assert.Multiple(() => {
      Assert.That(camgOffset, Is.GreaterThan(0));
      Assert.That(cmapOffset, Is.GreaterThan(0));
      Assert.That(camgOffset, Is.LessThan(cmapOffset));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CombinedHamEhb_PreservesBothFlags() {
    var original = new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 6,
      Compression = IlbmCompression.None,
      PixelData = new byte[8 * 2],
      Palette = new byte[16 * 3],
      ViewportMode = 0x880 // both HAM and EHB bits (unusual but valid for testing)
    };

    var bytes = IlbmWriter.ToBytes(original);
    var restored = IlbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.ViewportMode, Is.EqualTo(0x880u));
      Assert.That(restored.IsHam, Is.True);
      Assert.That(restored.IsEhb, Is.True);
    });
  }

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
