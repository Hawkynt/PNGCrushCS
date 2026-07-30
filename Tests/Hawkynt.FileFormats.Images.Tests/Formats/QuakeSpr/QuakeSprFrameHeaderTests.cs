using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.QuakeSpr;

namespace FileFormat.QuakeSpr.Tests;

[TestFixture]
public sealed class QuakeSprFrameHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is20() => Assert.That(QuakeSprFrameHeader.StructSize, Is.EqualTo(20));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new QuakeSprFrameHeader(0, -16, -16, 32, 32);
    Span<byte> buffer = stackalloc byte[QuakeSprFrameHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = QuakeSprFrameHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[QuakeSprFrameHeader.StructSize];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), -10);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), -20);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 64);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 128);
    var h = QuakeSprFrameHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.FrameType, Is.EqualTo(0));
      Assert.That(h.OriginX, Is.EqualTo(-10));
      Assert.That(h.OriginY, Is.EqualTo(-20));
      Assert.That(h.Width, Is.EqualTo(64));
      Assert.That(h.Height, Is.EqualTo(128));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = QuakeSprFrameHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(QuakeSprFrameHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = QuakeSprFrameHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
