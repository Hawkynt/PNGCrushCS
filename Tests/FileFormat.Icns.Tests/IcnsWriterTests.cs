using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Icns;

namespace FileFormat.Icns.Tests;

[TestFixture]
public sealed class IcnsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcnsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_StartsWithIcnsMagic() {
    var file = new IcnsFile {
      Entries = [new IcnsEntry("ic07", new byte[10], 128, 128)]
    };

    var bytes = IcnsWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'i'));
    Assert.That(bytes[1], Is.EqualTo((byte)'c'));
    Assert.That(bytes[2], Is.EqualTo((byte)'n'));
    Assert.That(bytes[3], Is.EqualTo((byte)'s'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_FileLengthFieldMatchesActualLength() {
    var entryData = new byte[20];
    var file = new IcnsFile {
      Entries = [new IcnsEntry("ic07", entryData, 128, 128)]
    };

    var bytes = IcnsWriter.ToBytes(file);
    var storedLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));

    Assert.That((int)storedLength, Is.EqualTo(bytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleEntry_EntryOsTypeWrittenCorrectly() {
    var file = new IcnsFile {
      Entries = [new IcnsEntry("ic08", new byte[5], 256, 256)]
    };

    var bytes = IcnsWriter.ToBytes(file);
    var osType = Encoding.ASCII.GetString(bytes, 8, 4);

    Assert.That(osType, Is.EqualTo("ic08"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleEntry_EntryLengthFieldCorrect() {
    var entryData = new byte[15];
    var file = new IcnsFile {
      Entries = [new IcnsEntry("ic07", entryData, 128, 128)]
    };

    var bytes = IcnsWriter.ToBytes(file);
    var entryLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));

    Assert.That((int)entryLength, Is.EqualTo(8 + entryData.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleEntry_EntryDataPreserved() {
    var entryData = new byte[] { 1, 2, 3, 4, 5 };
    var file = new IcnsFile {
      Entries = [new IcnsEntry("ic07", entryData, 128, 128)]
    };

    var bytes = IcnsWriter.ToBytes(file);

    for (var i = 0; i < entryData.Length; ++i)
      Assert.That(bytes[16 + i], Is.EqualTo(entryData[i]));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleEntries_AllPresent() {
    var file = new IcnsFile {
      Entries = [
        new IcnsEntry("ic07", new byte[3], 128, 128),
        new IcnsEntry("ic08", new byte[5], 256, 256),
      ]
    };

    var bytes = IcnsWriter.ToBytes(file);

    // First entry at offset 8
    var osType1 = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(osType1, Is.EqualTo("ic07"));

    // Second entry at offset 8 + 8 + 3 = 19
    var osType2 = Encoding.ASCII.GetString(bytes, 19, 4);
    Assert.That(osType2, Is.EqualTo("ic08"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyEntries_WritesHeaderOnly() {
    var file = new IcnsFile { Entries = [] };

    var bytes = IcnsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8));
    var storedLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
    Assert.That((int)storedLength, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalFileSize_Correct() {
    var data1 = new byte[10];
    var data2 = new byte[20];
    var file = new IcnsFile {
      Entries = [
        new IcnsEntry("ic07", data1, 128, 128),
        new IcnsEntry("ic08", data2, 256, 256),
      ]
    };

    var bytes = IcnsWriter.ToBytes(file);
    var expected = 8 + (8 + 10) + (8 + 20);

    Assert.That(bytes.Length, Is.EqualTo(expected));
  }
}
