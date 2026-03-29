using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;

namespace FileFormat.Riff.Tests;

[TestFixture]
public sealed class RiffChunkHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new RiffChunkHeader("data", 54321);
    Span<byte> buffer = stackalloc byte[RiffChunkHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = RiffChunkHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[8];
    Encoding.ASCII.GetBytes("fmt ").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 16);

    var header = RiffChunkHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.ChunkId.ToString(), Is.EqualTo("fmt "));
      Assert.That(header.Size, Is.EqualTo(16));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new RiffChunkHeader("anih", 36);
    var buffer = new byte[RiffChunkHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(buffer, 0, 4), Is.EqualTo("anih"));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(36));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = RiffChunkHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(RiffChunkHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = RiffChunkHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is8() {
    Assert.That(RiffChunkHeader.StructSize, Is.EqualTo(8));
  }
}
