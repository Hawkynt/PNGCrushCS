using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.DrHalo;
using FileFormat.Core;

namespace FileFormat.DrHalo.Tests;

[TestFixture]
public sealed class DrHaloHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new DrHaloHeader(320, 200, 0);
    Span<byte> buffer = stackalloc byte[DrHaloHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = DrHaloHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[6];
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0), 640);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 480);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 0);

    var header = DrHaloHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Width, Is.EqualTo(640));
      Assert.That(header.Height, Is.EqualTo(480));
      Assert.That(header.Reserved, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = DrHaloHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(DrHaloHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = DrHaloHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is6() {
    Assert.That(DrHaloHeader.StructSize, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new DrHaloHeader(100, 50, 0);
    var buffer = new byte[DrHaloHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(0)), Is.EqualTo(100));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(2)), Is.EqualTo(50));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(4)), Is.EqualTo(0));
    });
  }
}
