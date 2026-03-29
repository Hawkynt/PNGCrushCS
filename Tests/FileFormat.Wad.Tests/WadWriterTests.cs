using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Wad;

namespace FileFormat.Wad.Tests;

[TestFixture]
public sealed class WadWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WadWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Iwad_StartsWithIwadMagic() {
    var bytes = WadWriter.ToBytes(new WadFile { Type = WadType.Iwad, Lumps = [] });

    Assert.That(bytes[0], Is.EqualTo((byte)'I'));
    Assert.That(bytes[1], Is.EqualTo((byte)'W'));
    Assert.That(bytes[2], Is.EqualTo((byte)'A'));
    Assert.That(bytes[3], Is.EqualTo((byte)'D'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Pwad_StartsWithPwadMagic() {
    var bytes = WadWriter.ToBytes(new WadFile { Type = WadType.Pwad, Lumps = [] });

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'W'));
    Assert.That(bytes[2], Is.EqualTo((byte)'A'));
    Assert.That(bytes[3], Is.EqualTo((byte)'D'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DirectoryOffsetCorrect() {
    var data1 = new byte[] { 1, 2, 3 };
    var data2 = new byte[] { 4, 5 };
    var bytes = WadWriter.ToBytes(new WadFile {
      Type = WadType.Iwad,
      Lumps = [
        new WadLump { Name = "A", Data = data1 },
        new WadLump { Name = "B", Data = data2 }
      ]
    });

    var directoryOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
    Assert.That(directoryOffset, Is.EqualTo(WadHeader.StructSize + data1.Length + data2.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LumpDataPreserved() {
    var lumpData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var bytes = WadWriter.ToBytes(new WadFile {
      Type = WadType.Iwad,
      Lumps = [new WadLump { Name = "DEAD", Data = lumpData }]
    });

    var dataStart = WadHeader.StructSize;
    Assert.That(bytes[dataStart], Is.EqualTo(0xDE));
    Assert.That(bytes[dataStart + 1], Is.EqualTo(0xAD));
    Assert.That(bytes[dataStart + 2], Is.EqualTo(0xBE));
    Assert.That(bytes[dataStart + 3], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LumpNamesPreserved() {
    var bytes = WadWriter.ToBytes(new WadFile {
      Type = WadType.Iwad,
      Lumps = [new WadLump { Name = "MYNAME", Data = [1] }]
    });

    var directoryOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
    var nameBytes = bytes.AsSpan(directoryOffset + 8, 8);
    var nameEnd = nameBytes.IndexOf((byte)0);
    var name = nameEnd < 0
      ? Encoding.ASCII.GetString(nameBytes)
      : Encoding.ASCII.GetString(nameBytes[..nameEnd]);

    Assert.That(name, Is.EqualTo("MYNAME"));
  }
}
