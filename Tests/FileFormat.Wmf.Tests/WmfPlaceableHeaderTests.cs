using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Wmf;

namespace FileFormat.Wmf.Tests;

[TestFixture]
public sealed class WmfPlaceableHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is22() => Assert.That(WmfPlaceableHeader.StructSize, Is.EqualTo(22));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new WmfPlaceableHeader(0x9AC6CDD7, 0, 0, 0, 320, 240, 1440, 0, 0);
    Span<byte> buffer = stackalloc byte[WmfPlaceableHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = WmfPlaceableHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[WmfPlaceableHeader.StructSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0x9AC6CDD7);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 10);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), 20);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(10), 330);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 260);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), 1440);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), 0xABCD);
    var h = WmfPlaceableHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.Magic, Is.EqualTo(0x9AC6CDD7u));
      Assert.That(h.Handle, Is.EqualTo((ushort)0));
      Assert.That(h.Left, Is.EqualTo((short)10));
      Assert.That(h.Top, Is.EqualTo((short)20));
      Assert.That(h.Right, Is.EqualTo((short)330));
      Assert.That(h.Bottom, Is.EqualTo((short)260));
      Assert.That(h.Inch, Is.EqualTo((ushort)1440));
      Assert.That(h.Reserved, Is.EqualTo(0u));
      Assert.That(h.Checksum, Is.EqualTo((ushort)0xABCD));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var h = new WmfPlaceableHeader(0x9AC6CDD7, 0, 0, 0, 320, 240, 1440, 0, 0);
    var buf = new byte[WmfPlaceableHeader.StructSize];
    h.WriteTo(buf);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(0)), Is.EqualTo(0x9AC6CDD7u));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(4)), Is.EqualTo((ushort)0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(6)), Is.EqualTo((short)0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(8)), Is.EqualTo((short)0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(10)), Is.EqualTo((short)320));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(12)), Is.EqualTo((short)240));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(14)), Is.EqualTo((ushort)1440));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(16)), Is.EqualTo(0u));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(20)), Is.EqualTo((ushort)0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ComputeChecksum_ReturnsXorOfFirst10Words() {
    var header = new WmfPlaceableHeader(0x9AC6CDD7, 0, 0, 0, 100, 200, 1440, 0, 0);
    var buf = new byte[WmfPlaceableHeader.StructSize];
    header.WriteTo(buf);
    var checksum = WmfPlaceableHeader.ComputeChecksum(buf);
    ushort expected = 0;
    for (var i = 0; i < 10; ++i)
      expected ^= BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(i * 2));
    Assert.That(checksum, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = WmfPlaceableHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(WmfPlaceableHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = WmfPlaceableHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
