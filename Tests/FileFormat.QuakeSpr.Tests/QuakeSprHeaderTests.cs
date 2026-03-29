using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.QuakeSpr;

namespace FileFormat.QuakeSpr.Tests;

[TestFixture]
public sealed class QuakeSprHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is36() => Assert.That(QuakeSprHeader.StructSize, Is.EqualTo(36));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new QuakeSprHeader(0x50534449, 1, 2, 3.14f, 64, 64, 5, 1.5f, 0);
    Span<byte> buffer = stackalloc byte[QuakeSprHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = QuakeSprHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[QuakeSprHeader.StructSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0x50534449);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 3);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(12), 10.0f);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 128);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 256);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 8);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(28), 2.0f);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), 1);
    var h = QuakeSprHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.Magic, Is.EqualTo(0x50534449u));
      Assert.That(h.Version, Is.EqualTo(1));
      Assert.That(h.SpriteType, Is.EqualTo(3));
      Assert.That(h.BoundingRadius, Is.EqualTo(10.0f));
      Assert.That(h.MaxWidth, Is.EqualTo(128));
      Assert.That(h.MaxHeight, Is.EqualTo(256));
      Assert.That(h.NumFrames, Is.EqualTo(8));
      Assert.That(h.BeamLength, Is.EqualTo(2.0f));
      Assert.That(h.SyncType, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var h = new QuakeSprHeader(0x50534449, 1, 0, 5.0f, 32, 32, 1, 0.0f, 0);
    var buf = new byte[QuakeSprHeader.StructSize];
    h.WriteTo(buf);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(0)), Is.EqualTo(0x50534449u));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(4)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(12)), Is.EqualTo(5.0f));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(16)), Is.EqualTo(32));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = QuakeSprHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(QuakeSprHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = QuakeSprHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
