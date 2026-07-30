using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;

namespace FileFormat.Riff.Tests;

[TestFixture]
public sealed class RiffHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new RiffHeader("RIFF", 12345, "WAVE");
    Span<byte> buffer = stackalloc byte[RiffHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = RiffHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[12];
    Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x00010000);
    Encoding.ASCII.GetBytes("ACON").CopyTo(data, 8);

    var header = RiffHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.ChunkId.ToString(), Is.EqualTo("RIFF"));
      Assert.That(header.Size, Is.EqualTo(0x00010000));
      Assert.That(header.FormType.ToString(), Is.EqualTo("ACON"));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new RiffHeader("RIFF", 256, "WEBP");
    var buffer = new byte[RiffHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(buffer, 0, 4), Is.EqualTo("RIFF"));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(256));
      Assert.That(Encoding.ASCII.GetString(buffer, 8, 4), Is.EqualTo("WEBP"));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = RiffHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(RiffHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = RiffHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is12() {
    Assert.That(RiffHeader.StructSize, Is.EqualTo(12));
  }
}
