using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.DrawIt;

namespace FileFormat.DrawIt.Tests;

[TestFixture]
public sealed class DrawItHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new DrawItHeader(320, 200);
    Span<byte> buffer = stackalloc byte[DrawItHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = DrawItHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Width, Is.EqualTo(320));
      Assert.That(parsed.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[DrawItHeader.StructSize];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 640);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 480);

    var header = DrawItHeader.ReadFrom(data);

    Assert.Multiple(() => {
      Assert.That(header.Width, Is.EqualTo(640));
      Assert.That(header.Height, Is.EqualTo(480));
    });
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is4() {
    Assert.That(DrawItHeader.StructSize, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = DrawItHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(DrawItHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = DrawItHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
