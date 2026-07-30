using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.BigTiff;

namespace FileFormat.BigTiff.Tests;

[TestFixture]
public sealed class BigTiffFileHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() => Assert.That(BigTiffFileHeader.StructSize, Is.EqualTo(16));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new BigTiffFileHeader(0x4949, 43, 8, 0, 16L);
    Span<byte> buffer = stackalloc byte[BigTiffFileHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = BigTiffFileHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[BigTiffFileHeader.StructSize];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 0x4949);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 43);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 0);
    BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), 256);
    var h = BigTiffFileHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.ByteOrder, Is.EqualTo((ushort)0x4949));
      Assert.That(h.Version, Is.EqualTo((ushort)43));
      Assert.That(h.OffsetSize, Is.EqualTo((ushort)8));
      Assert.That(h.Reserved, Is.EqualTo((ushort)0));
      Assert.That(h.FirstIfdOffset, Is.EqualTo(256L));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var h = new BigTiffFileHeader(0x4949, 43, 8, 0, 512L);
    var buf = new byte[BigTiffFileHeader.StructSize];
    h.WriteTo(buf);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(0)), Is.EqualTo((ushort)0x4949));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(2)), Is.EqualTo((ushort)43));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(4)), Is.EqualTo((ushort)8));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(6)), Is.EqualTo((ushort)0));
      Assert.That(BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(8)), Is.EqualTo(512L));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = BigTiffFileHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(BigTiffFileHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = BigTiffFileHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
