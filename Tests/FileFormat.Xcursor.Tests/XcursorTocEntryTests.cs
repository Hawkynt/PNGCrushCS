using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Xcursor;

namespace FileFormat.Xcursor.Tests;

[TestFixture]
public sealed class XcursorTocEntryTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is12() => Assert.That(XcursorTocEntry.StructSize, Is.EqualTo(12));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new XcursorTocEntry(0xFFFD0002, 32, 128);
    Span<byte> buffer = stackalloc byte[XcursorTocEntry.StructSize];
    original.WriteTo(buffer);
    var parsed = XcursorTocEntry.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[XcursorTocEntry.StructSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0xFFFD0002);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 48);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 256);
    var e = XcursorTocEntry.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(e.Type, Is.EqualTo(0xFFFD0002u));
      Assert.That(e.Subtype, Is.EqualTo(48u));
      Assert.That(e.Position, Is.EqualTo(256u));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = XcursorTocEntry.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(XcursorTocEntry.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = XcursorTocEntry.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
