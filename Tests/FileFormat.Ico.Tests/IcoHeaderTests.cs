using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Ico.Tests;

[TestFixture]
public sealed class IcoHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new IcoHeader(0, 1, 5);
    Span<byte> buffer = stackalloc byte[IcoHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = IcoHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[6];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 3);

    var header = IcoHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Reserved, Is.EqualTo(0));
      Assert.That(header.Type, Is.EqualTo(1));
      Assert.That(header.Count, Is.EqualTo(3));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new IcoHeader(0, 2, 7);
    var buffer = new byte[IcoHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2)), Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4)), Is.EqualTo(7));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = IcoHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(IcoHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = IcoHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is6() {
    Assert.That(IcoHeader.StructSize, Is.EqualTo(6));
  }
}
