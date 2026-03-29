using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Ilbm;
using FileFormat.Core;

namespace FileFormat.Ilbm.Tests;

[TestFixture]
public sealed class BmhdChunkTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new BmhdChunk(320, 200, -10, -20, 4, 1, 1, 0, 5, 10, 11, 320, 200);
    Span<byte> buffer = stackalloc byte[BmhdChunk.StructSize];
    original.WriteTo(buffer);
    var parsed = BmhdChunk.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 640);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 480);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 0);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(6), 0);
    data[8] = 8;  // numPlanes
    data[9] = 2;  // masking
    data[10] = 1; // compression
    data[11] = 0; // padding
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 7);
    data[14] = 10; // xAspect
    data[15] = 11; // yAspect
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(16), 640);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(18), 480);

    var header = BmhdChunk.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Width, Is.EqualTo(640));
      Assert.That(header.Height, Is.EqualTo(480));
      Assert.That(header.XOrigin, Is.EqualTo(0));
      Assert.That(header.YOrigin, Is.EqualTo(0));
      Assert.That(header.NumPlanes, Is.EqualTo(8));
      Assert.That(header.Masking, Is.EqualTo(2));
      Assert.That(header.Compression, Is.EqualTo(1));
      Assert.That(header.TransparentColor, Is.EqualTo(7));
      Assert.That(header.XAspect, Is.EqualTo(10));
      Assert.That(header.YAspect, Is.EqualTo(11));
      Assert.That(header.PageWidth, Is.EqualTo(640));
      Assert.That(header.PageHeight, Is.EqualTo(480));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = BmhdChunk.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(BmhdChunk.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = BmhdChunk.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is20() {
    Assert.That(BmhdChunk.StructSize, Is.EqualTo(20));
  }
}
