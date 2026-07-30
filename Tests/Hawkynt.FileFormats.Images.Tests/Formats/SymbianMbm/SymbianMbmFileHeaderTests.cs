using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.SymbianMbm;

namespace FileFormat.SymbianMbm.Tests;

[TestFixture]
public sealed class SymbianMbmFileHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is20() => Assert.That(SymbianMbmFileHeader.StructSize, Is.EqualTo(20));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new SymbianMbmFileHeader(0x10000037, 0x10000000, 0, 0x12345678, 100);
    Span<byte> buffer = stackalloc byte[SymbianMbmFileHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = SymbianMbmFileHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[SymbianMbmFileHeader.StructSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0x10000037);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x10000000);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 0xDEADBEEF);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 256);
    var h = SymbianMbmFileHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.Uid1, Is.EqualTo(0x10000037u));
      Assert.That(h.Uid2, Is.EqualTo(0x10000000u));
      Assert.That(h.Uid3, Is.EqualTo(0u));
      Assert.That(h.Checksum, Is.EqualTo(0xDEADBEEFu));
      Assert.That(h.TrailerOffset, Is.EqualTo(256));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = SymbianMbmFileHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(SymbianMbmFileHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = SymbianMbmFileHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
