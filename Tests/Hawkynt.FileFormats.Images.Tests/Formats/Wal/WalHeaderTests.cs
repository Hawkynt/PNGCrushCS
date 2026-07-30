using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Wal;

namespace FileFormat.Wal.Tests;

[TestFixture]
public sealed class WalHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new WalHeader(
      "e1u1/floor01",
      64,
      64,
      100,
      4196,
      5220,
      5476,
      "e1u1/floor02",
      0x10,
      0x20,
      0x30
    );

    var buffer = new byte[WalHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = WalHeader.ReadFrom(buffer);

    Assert.That(parsed.Name, Is.EqualTo(original.Name));
    Assert.That(parsed.Width, Is.EqualTo(original.Width));
    Assert.That(parsed.Height, Is.EqualTo(original.Height));
    Assert.That(parsed.MipOffset0, Is.EqualTo(original.MipOffset0));
    Assert.That(parsed.MipOffset1, Is.EqualTo(original.MipOffset1));
    Assert.That(parsed.MipOffset2, Is.EqualTo(original.MipOffset2));
    Assert.That(parsed.MipOffset3, Is.EqualTo(original.MipOffset3));
    Assert.That(parsed.NextFrameName, Is.EqualTo(original.NextFrameName));
    Assert.That(parsed.Flags, Is.EqualTo(original.Flags));
    Assert.That(parsed.Contents, Is.EqualTo(original.Contents));
    Assert.That(parsed.Value, Is.EqualTo(original.Value));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = WalHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(WalHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = WalHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is100() {
    Assert.That(WalHeader.StructSize, Is.EqualTo(100));
  }
}
