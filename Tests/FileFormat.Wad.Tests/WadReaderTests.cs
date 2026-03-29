using System;
using System.IO;
using FileFormat.Wad;

namespace FileFormat.Wad.Tests;

[TestFixture]
public sealed class WadReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WadReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WadReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wad"));
    Assert.Throws<FileNotFoundException>(() => WadReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WadReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => WadReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[WadHeader.StructSize];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => WadReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidIwad_ParsesCorrectly() {
    var wad = WadWriter.ToBytes(new WadFile {
      Type = WadType.Iwad,
      Lumps = [new WadLump { Name = "TEST", Data = [1, 2, 3] }]
    });

    var result = WadReader.FromBytes(wad);

    Assert.That(result.Type, Is.EqualTo(WadType.Iwad));
    Assert.That(result.Lumps, Has.Count.EqualTo(1));
    Assert.That(result.Lumps[0].Name, Is.EqualTo("TEST"));
    Assert.That(result.Lumps[0].Data, Is.EqualTo(new byte[] { 1, 2, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidPwad_ParsesCorrectly() {
    var wad = WadWriter.ToBytes(new WadFile {
      Type = WadType.Pwad,
      Lumps = [new WadLump { Name = "PATCH", Data = [10, 20] }]
    });

    var result = WadReader.FromBytes(wad);

    Assert.That(result.Type, Is.EqualTo(WadType.Pwad));
    Assert.That(result.Lumps, Has.Count.EqualTo(1));
    Assert.That(result.Lumps[0].Name, Is.EqualTo("PATCH"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_EmptyWad_ParsesCorrectly() {
    var wad = WadWriter.ToBytes(new WadFile { Type = WadType.Iwad, Lumps = [] });

    var result = WadReader.FromBytes(wad);

    Assert.That(result.Type, Is.EqualTo(WadType.Iwad));
    Assert.That(result.Lumps, Has.Count.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MultipleLumps_ParsesAll() {
    var wad = WadWriter.ToBytes(new WadFile {
      Type = WadType.Pwad,
      Lumps = [
        new WadLump { Name = "LUMP1", Data = [1] },
        new WadLump { Name = "LUMP2", Data = [2, 3] },
        new WadLump { Name = "LUMP3", Data = [4, 5, 6] }
      ]
    });

    var result = WadReader.FromBytes(wad);

    Assert.That(result.Lumps, Has.Count.EqualTo(3));
    Assert.That(result.Lumps[0].Name, Is.EqualTo("LUMP1"));
    Assert.That(result.Lumps[1].Name, Is.EqualTo("LUMP2"));
    Assert.That(result.Lumps[2].Name, Is.EqualTo("LUMP3"));
    Assert.That(result.Lumps[2].Data, Is.EqualTo(new byte[] { 4, 5, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidIwad_ParsesCorrectly() {
    var wad = WadWriter.ToBytes(new WadFile {
      Type = WadType.Iwad,
      Lumps = [new WadLump { Name = "STEST", Data = [42] }]
    });

    using var ms = new MemoryStream(wad);
    var result = WadReader.FromStream(ms);

    Assert.That(result.Type, Is.EqualTo(WadType.Iwad));
    Assert.That(result.Lumps, Has.Count.EqualTo(1));
    Assert.That(result.Lumps[0].Name, Is.EqualTo("STEST"));
  }
}
