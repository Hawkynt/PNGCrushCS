using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Xcursor;

namespace FileFormat.Xcursor.Tests;

[TestFixture]
public sealed class XcursorFileHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() => Assert.That(XcursorFileHeader.StructSize, Is.EqualTo(16));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new XcursorFileHeader(0x72756358, 16, 0x00010000, 3);
    Span<byte> buffer = stackalloc byte[XcursorFileHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = XcursorFileHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[XcursorFileHeader.StructSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0x72756358);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0x00010000);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 5);
    var h = XcursorFileHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.Magic, Is.EqualTo(0x72756358u));
      Assert.That(h.HeaderSize, Is.EqualTo(16u));
      Assert.That(h.Version, Is.EqualTo(0x00010000u));
      Assert.That(h.TocCount, Is.EqualTo(5u));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = XcursorFileHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(XcursorFileHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = XcursorFileHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
