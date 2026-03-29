using System;
using System.IO;
using FileFormat.Wad;

namespace FileFormat.Wad.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyIwad() {
    var original = new WadFile { Type = WadType.Iwad, Lumps = [] };

    var bytes = WadWriter.ToBytes(original);
    var restored = WadReader.FromBytes(bytes);

    Assert.That(restored.Type, Is.EqualTo(WadType.Iwad));
    Assert.That(restored.Lumps, Has.Count.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleLump() {
    var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
    var original = new WadFile {
      Type = WadType.Pwad,
      Lumps = [new WadLump { Name = "TESTLUMP", Data = data }]
    };

    var bytes = WadWriter.ToBytes(original);
    var restored = WadReader.FromBytes(bytes);

    Assert.That(restored.Type, Is.EqualTo(WadType.Pwad));
    Assert.That(restored.Lumps, Has.Count.EqualTo(1));
    Assert.That(restored.Lumps[0].Name, Is.EqualTo("TESTLUMP"));
    Assert.That(restored.Lumps[0].Data, Is.EqualTo(data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleLumps() {
    var original = new WadFile {
      Type = WadType.Iwad,
      Lumps = [
        new WadLump { Name = "PLAYPAL", Data = new byte[768] },
        new WadLump { Name = "COLORMAP", Data = new byte[8192] },
        new WadLump { Name = "FLAT1", Data = new byte[4096] }
      ]
    };

    for (var i = 0; i < original.Lumps[0].Data.Length; ++i)
      original.Lumps[0].Data[i] = (byte)(i % 256);
    for (var i = 0; i < original.Lumps[1].Data.Length; ++i)
      original.Lumps[1].Data[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < original.Lumps[2].Data.Length; ++i)
      original.Lumps[2].Data[i] = (byte)(i * 7 % 256);

    var bytes = WadWriter.ToBytes(original);
    var restored = WadReader.FromBytes(bytes);

    Assert.That(restored.Lumps, Has.Count.EqualTo(3));
    Assert.That(restored.Lumps[0].Name, Is.EqualTo("PLAYPAL"));
    Assert.That(restored.Lumps[0].Data, Is.EqualTo(original.Lumps[0].Data));
    Assert.That(restored.Lumps[1].Name, Is.EqualTo("COLORMAP"));
    Assert.That(restored.Lumps[1].Data, Is.EqualTo(original.Lumps[1].Data));
    Assert.That(restored.Lumps[2].Name, Is.EqualTo("FLAT1"));
    Assert.That(restored.Lumps[2].Data, Is.EqualTo(original.Lumps[2].Data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wad");
    try {
      var original = new WadFile {
        Type = WadType.Iwad,
        Lumps = [new WadLump { Name = "FILELMP", Data = [0xAA, 0xBB, 0xCC] }]
      };

      var bytes = WadWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = WadReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Type, Is.EqualTo(WadType.Iwad));
      Assert.That(restored.Lumps, Has.Count.EqualTo(1));
      Assert.That(restored.Lumps[0].Name, Is.EqualTo("FILELMP"));
      Assert.That(restored.Lumps[0].Data, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC }));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
