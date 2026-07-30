using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Fli;

namespace FileFormat.Fli.Tests;

[TestFixture]
public sealed class FliHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new FliHeader(12345, unchecked((short)0xAF11), 10, 320, 200, 8, 0, 5);
    var buffer = new byte[FliHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = FliHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[FliHeader.StructSize];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 50000);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), unchecked((short)0xAF12));
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 20);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), 640);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(10), 480);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 8);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 3);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 33);

    var header = FliHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Size, Is.EqualTo(50000));
      Assert.That(header.Magic, Is.EqualTo(unchecked((short)0xAF12)));
      Assert.That(header.Frames, Is.EqualTo(20));
      Assert.That(header.Width, Is.EqualTo(640));
      Assert.That(header.Height, Is.EqualTo(480));
      Assert.That(header.Depth, Is.EqualTo(8));
      Assert.That(header.Flags, Is.EqualTo(3));
      Assert.That(header.Speed, Is.EqualTo(33));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new FliHeader(99999, unchecked((short)0xAF11), 5, 160, 100, 8, 0, 70);
    var buffer = new byte[FliHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(99999));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(4)), Is.EqualTo(unchecked((short)0xAF11)));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(6)), Is.EqualTo(5));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(8)), Is.EqualTo(160));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(10)), Is.EqualTo(100));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(12)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(14)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(16)), Is.EqualTo(70));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = FliHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(FliHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = FliHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is128() {
    Assert.That(FliHeader.StructSize, Is.EqualTo(128));
  }
}
