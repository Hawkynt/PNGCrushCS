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
      ProductType: 1,
      FileType: 1,
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
