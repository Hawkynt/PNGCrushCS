using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Wmf;

namespace FileFormat.Wmf.Tests;

[TestFixture]
public sealed class WmfStandardHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is18() => Assert.That(WmfStandardHeader.StructSize, Is.EqualTo(18));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new WmfStandardHeader(1, 9, 0x0300, 1000, 0, 500, 0);
    Span<byte> buffer = stackalloc byte[WmfStandardHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = WmfStandardHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[WmfStandardHeader.StructSize];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 9);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0x0300);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), 1000);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 500);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16), 0);
    var h = WmfStandardHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.Type, Is.EqualTo((ushort)1));
      Assert.That(h.HeaderSize, Is.EqualTo((ushort)9));
      Assert.That(h.Version, Is.EqualTo((ushort)0x0300));
      Assert.That(h.FileSizeInWords, Is.EqualTo(1000u));
      Assert.That(h.NumObjects, Is.EqualTo((ushort)0));
      Assert.That(h.MaxRecordSize, Is.EqualTo(500u));
      Assert.That(h.NumMembers, Is.EqualTo((ushort)0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var h = new WmfStandardHeader(1, 9, 0x0300, 1000, 0, 500, 0);
    var buf = new byte[WmfStandardHeader.StructSize];
    h.WriteTo(buf);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(0)), Is.EqualTo((ushort)1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(2)), Is.EqualTo((ushort)9));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(4)), Is.EqualTo((ushort)0x0300));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(6)), Is.EqualTo(1000u));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(10)), Is.EqualTo((ushort)0));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(12)), Is.EqualTo(500u));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(16)), Is.EqualTo((ushort)0));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = WmfStandardHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(WmfStandardHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = WmfStandardHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
