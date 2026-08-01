using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Wpg;
using FileFormat.Core;

namespace FileFormat.Wpg.Tests;

[TestFixture]
public sealed class WpgHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new WpgHeader(
      Magic1: WpgHeader.MagicByte1,
      Magic2: WpgHeader.MagicByte2,
      Magic3: WpgHeader.MagicByte3,
      Magic4: WpgHeader.MagicByte4,
      DataOffset: WpgHeader.StructSize,
      ProductType: 1,
      FileType: WpgHeader.GraphicFileType,
      MajorVersion: 1,
      MinorVersion: 0,
      EncryptionKey: 0,
      Reserved: 0
    );

    var buffer = new byte[WpgHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = WpgHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  /// <summary>
  /// The four bytes after the magic say where the records start, and they are what a reader seeks to.
  /// They were not modelled at all — a four-byte product type stood in their place — so every file
  /// this wrote claimed its records began at byte 1 and named itself file type 0.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void WriteTo_PutsTheRecordOffsetAndFileTypeWhereAReaderLooks() {
    var buffer = new byte[WpgHeader.StructSize];
    new WpgHeader(
      Magic1: WpgHeader.MagicByte1,
      Magic2: WpgHeader.MagicByte2,
      Magic3: WpgHeader.MagicByte3,
      Magic4: WpgHeader.MagicByte4,
      DataOffset: WpgHeader.StructSize,
      ProductType: 1,
      FileType: WpgHeader.GraphicFileType,
      MajorVersion: 1,
      MinorVersion: 0,
      EncryptionKey: 0,
      Reserved: 0
    ).WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)),
        Is.EqualTo(16u), "the records start after the header");
      Assert.That(buffer[8], Is.EqualTo(1), "product type");
      Assert.That(buffer[9], Is.EqualTo(0x16), "file type: a graphic");
      Assert.That(buffer[10], Is.EqualTo(1), "major version");
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = WpgHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(WpgHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() {
    Assert.That(WpgHeader.StructSize, Is.EqualTo(16));
  }
}
