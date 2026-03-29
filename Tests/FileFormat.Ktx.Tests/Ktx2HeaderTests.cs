using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Ktx;
using FileFormat.Core;

namespace FileFormat.Ktx.Tests;

[TestFixture]
public sealed class Ktx2HeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new Ktx2Header(
      37,
      1,
      512,
      256,
      0,
      0,
      1,
      3,
      0,
      80,
      44,
      124,
      32,
      0L,
      0L
    );
    var buffer = new byte[Ktx2Header.StructSize];
    original.WriteTo(buffer);
    var parsed = Ktx2Header.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[Ktx2Header.StructSize];
    Ktx2Header.Identifier.CopyTo(data, 0);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 37);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 1024);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 768);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(36), 6);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(40), 10);

    var header = Ktx2Header.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.VkFormat, Is.EqualTo(37));
      Assert.That(header.TypeSize, Is.EqualTo(1));
      Assert.That(header.PixelWidth, Is.EqualTo(1024));
      Assert.That(header.PixelHeight, Is.EqualTo(768));
      Assert.That(header.FaceCount, Is.EqualTo(6));
      Assert.That(header.LevelCount, Is.EqualTo(10));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new Ktx2Header(
      37,
      1,
      640,
      480,
      0,
      0,
      1,
      1,
      0,
      80,
      44,
      124,
      0,
      0L,
      0L
    );
    var buffer = new byte[Ktx2Header.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12)), Is.EqualTo(37));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(20)), Is.EqualTo(640));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(24)), Is.EqualTo(480));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = Ktx2Header.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(Ktx2Header.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = Ktx2Header.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is80() {
    Assert.That(Ktx2Header.StructSize, Is.EqualTo(80));
  }
}
