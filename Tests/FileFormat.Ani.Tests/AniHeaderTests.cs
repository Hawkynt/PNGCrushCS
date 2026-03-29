using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Ani;

namespace FileFormat.Ani.Tests;

[TestFixture]
public sealed class AniHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new AniHeader(36, 4, 6, 32, 32, 24, 1, 10, 3);
    Span<byte> buffer = stackalloc byte[AniHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = AniHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[AniHeader.StructSize];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 36);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 5);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 7);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 64);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 64);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 32);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(28), 15);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), 3);

    var header = AniHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.CbSize, Is.EqualTo(36));
      Assert.That(header.NumFrames, Is.EqualTo(5));
      Assert.That(header.NumSteps, Is.EqualTo(7));
      Assert.That(header.Width, Is.EqualTo(64));
      Assert.That(header.Height, Is.EqualTo(64));
      Assert.That(header.BitCount, Is.EqualTo(32));
      Assert.That(header.NumPlanes, Is.EqualTo(1));
      Assert.That(header.DisplayRate, Is.EqualTo(15));
      Assert.That(header.Flags, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new AniHeader(36, 2, 4, 16, 16, 8, 1, 20, 2);
    var buffer = new byte[AniHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(36));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12)), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(16)), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(20)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(24)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(28)), Is.EqualTo(20));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(32)), Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = AniHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(AniHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = AniHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is36() {
    Assert.That(AniHeader.StructSize, Is.EqualTo(36));
  }

  [Test]
  [Category("Unit")]
  public void HasSequence_ReturnsTrue_WhenFlagBit0Set() {
    var header = new AniHeader(36, 1, 1, 0, 0, 0, 1, 10, 1);
    Assert.That(header.HasSequence, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void HasSequence_ReturnsFalse_WhenFlagBit0NotSet() {
    var header = new AniHeader(36, 1, 1, 0, 0, 0, 1, 10, 2);
    Assert.That(header.HasSequence, Is.False);
  }
}
