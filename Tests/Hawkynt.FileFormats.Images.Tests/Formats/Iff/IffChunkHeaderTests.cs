using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;

namespace FileFormat.Iff.Tests;

[TestFixture]
public sealed class IffChunkHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new IffChunkHeader("BMHD", 54321);
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = IffChunkHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[8];
    Encoding.ASCII.GetBytes("BODY").CopyTo(data, 0);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 1024);

    var header = IffChunkHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.ChunkId.ToString(), Is.EqualTo("BODY"));
      Assert.That(header.Size, Is.EqualTo(1024));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new IffChunkHeader("CMAP", 768);
    var buffer = new byte[IffChunkHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(buffer, 0, 4), Is.EqualTo("CMAP"));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4)), Is.EqualTo(768));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = IffChunkHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(IffChunkHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = IffChunkHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is8() {
    Assert.That(IffChunkHeader.StructSize, Is.EqualTo(8));
  }
}
