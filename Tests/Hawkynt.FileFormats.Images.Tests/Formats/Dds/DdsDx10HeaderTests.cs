using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

[TestFixture]
public sealed class DdsDx10HeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new DdsDx10Header(71, 3, 0, 1, 0);
    var buffer = new byte[DdsDx10Header.StructSize];
    original.WriteTo(buffer);
    var parsed = DdsDx10Header.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = DdsDx10Header.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(DdsDx10Header.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = DdsDx10Header.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is20() {
    Assert.That(DdsDx10Header.StructSize, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[20];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 87);  // DxgiFormat (B8G8R8A8_UNORM)
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 3);   // Texture2D
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 0);   // MiscFlag
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 6);  // ArraySize
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 0);  // MiscFlags2

    var header = DdsDx10Header.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.DxgiFormat, Is.EqualTo(87));
      Assert.That(header.ResourceDimension, Is.EqualTo(3));
      Assert.That(header.MiscFlag, Is.EqualTo(0));
      Assert.That(header.ArraySize, Is.EqualTo(6));
      Assert.That(header.MiscFlags2, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new DdsDx10Header(28, 3, 4, 1, 0);
    var buffer = new byte[DdsDx10Header.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(28));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(16)), Is.EqualTo(0));
    });
  }
}
