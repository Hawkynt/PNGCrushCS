using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Ico.Tests;

[TestFixture]
public sealed class IcoDirectoryEntryTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new IcoDirectoryEntry(32, 32, 0, 0, 1, 32, 2048, 22);
    Span<byte> buffer = stackalloc byte[IcoDirectoryEntry.StructSize];
    original.WriteTo(buffer);
    var parsed = IcoDirectoryEntry.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[16];
    data[0] = 48;  // Width
    data[1] = 48;  // Height
    data[2] = 16;  // ColorCount
    data[3] = 0;   // Reserved
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);     // Field4 (Planes)
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 8);     // Field5 (BitCount)
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 4096);   // DataSize
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 54);    // DataOffset

    var entry = IcoDirectoryEntry.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(entry.Width, Is.EqualTo(48));
      Assert.That(entry.Height, Is.EqualTo(48));
      Assert.That(entry.ColorCount, Is.EqualTo(16));
      Assert.That(entry.Reserved, Is.EqualTo(0));
      Assert.That(entry.Field4, Is.EqualTo(1));
      Assert.That(entry.Field5, Is.EqualTo(8));
      Assert.That(entry.DataSize, Is.EqualTo(4096));
      Assert.That(entry.DataOffset, Is.EqualTo(54));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var entry = new IcoDirectoryEntry(64, 64, 0, 0, 1, 24, 8192, 118);
    var buffer = new byte[IcoDirectoryEntry.StructSize];
    entry.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo(64));
      Assert.That(buffer[1], Is.EqualTo(64));
      Assert.That(buffer[2], Is.EqualTo(0));
      Assert.That(buffer[3], Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6)), Is.EqualTo(24));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8)), Is.EqualTo(8192));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12)), Is.EqualTo(118));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = IcoDirectoryEntry.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(IcoDirectoryEntry.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = IcoDirectoryEntry.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is16() {
    Assert.That(IcoDirectoryEntry.StructSize, Is.EqualTo(16));
  }
}
