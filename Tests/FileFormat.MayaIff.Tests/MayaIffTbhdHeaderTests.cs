using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.MayaIff;

namespace FileFormat.MayaIff.Tests;

[TestFixture]
public sealed class MayaIffTbhdHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is32() => Assert.That(MayaIffTbhdHeader.StructSize, Is.EqualTo(32));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new MayaIffTbhdHeader(640, 480, 1, 1, 3, 1, 1, 0);
    Span<byte> buffer = stackalloc byte[MayaIffTbhdHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = MayaIffTbhdHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesBigEndianValues() {
    var data = new byte[MayaIffTbhdHeader.StructSize];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 320);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 240);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 0);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), 1);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(18), 1);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), 0);
    var h = MayaIffTbhdHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.Width, Is.EqualTo(320u));
      Assert.That(h.Height, Is.EqualTo(240u));
      Assert.That(h.Prnum, Is.EqualTo((ushort)1));
      Assert.That(h.Prden, Is.EqualTo((ushort)1));
      Assert.That(h.Flags, Is.EqualTo(0u));
      Assert.That(h.Bytes, Is.EqualTo((ushort)1));
      Assert.That(h.Tiles, Is.EqualTo((ushort)1));
      Assert.That(h.Compression, Is.EqualTo(0u));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesBigEndianBytes() {
    var h = new MayaIffTbhdHeader(1024, 768, 2, 3, 3, 1, 4, 0);
    var buf = new byte[MayaIffTbhdHeader.StructSize];
    h.WriteTo(buf);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(0)), Is.EqualTo(1024u));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(4)), Is.EqualTo(768u));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(8)), Is.EqualTo((ushort)2));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(10)), Is.EqualTo((ushort)3));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12)), Is.EqualTo(3u));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(16)), Is.EqualTo((ushort)1));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(18)), Is.EqualTo((ushort)4));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ZerosReservedBytes() {
    var h = new MayaIffTbhdHeader(1, 1, 1, 1, 0, 1, 1, 0);
    var buf = new byte[MayaIffTbhdHeader.StructSize];
    for (var i = 0; i < buf.Length; ++i) buf[i] = 0xFF;
    h.WriteTo(buf);
    for (var i = 24; i < 32; ++i)
      Assert.That(buf[i], Is.EqualTo(0), $"Reserved byte at offset {i} should be zero");
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = MayaIffTbhdHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(MayaIffTbhdHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = MayaIffTbhdHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
