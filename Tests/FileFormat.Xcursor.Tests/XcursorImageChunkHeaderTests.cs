using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Xcursor;

namespace FileFormat.Xcursor.Tests;

[TestFixture]
public sealed class XcursorImageChunkHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is36() => Assert.That(XcursorImageChunkHeader.StructSize, Is.EqualTo(36));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new XcursorImageChunkHeader(36, 0xFFFD0002, 32, 1, 64, 48, 10, 15, 200);
    Span<byte> buffer = stackalloc byte[XcursorImageChunkHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = XcursorImageChunkHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[XcursorImageChunkHeader.StructSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 36);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0xFFFD0002);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 24);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 128);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 96);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 5);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 12);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 500);
    var h = XcursorImageChunkHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.ChunkHeaderSize, Is.EqualTo(36u));
      Assert.That(h.ChunkType, Is.EqualTo(0xFFFD0002u));
      Assert.That(h.ChunkSubtype, Is.EqualTo(24u));
      Assert.That(h.Version, Is.EqualTo(1u));
      Assert.That(h.Width, Is.EqualTo(128u));
      Assert.That(h.Height, Is.EqualTo(96u));
      Assert.That(h.XHot, Is.EqualTo(5u));
      Assert.That(h.YHot, Is.EqualTo(12u));
      Assert.That(h.Delay, Is.EqualTo(500u));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var h = new XcursorImageChunkHeader(36, 0xFFFD0002, 32, 1, 64, 48, 10, 15, 200);
    var buf = new byte[XcursorImageChunkHeader.StructSize];
    h.WriteTo(buf);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(0)), Is.EqualTo(36u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4)), Is.EqualTo(0xFFFD0002u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(8)), Is.EqualTo(32u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(12)), Is.EqualTo(1u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(16)), Is.EqualTo(64u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(20)), Is.EqualTo(48u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(24)), Is.EqualTo(10u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(28)), Is.EqualTo(15u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(32)), Is.EqualTo(200u));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = XcursorImageChunkHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(XcursorImageChunkHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = XcursorImageChunkHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
