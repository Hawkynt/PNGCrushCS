using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.GemImg;
using FileFormat.Core;

namespace FileFormat.GemImg.Tests;

[TestFixture]
public sealed class GemImgHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new GemImgHeader(1, 8, 4, 2, 85, 170, 320, 200);
    Span<byte> buffer = stackalloc byte[GemImgHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = GemImgHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[16];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 1);     // Version
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 8);     // HeaderLength
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 4);     // NumPlanes
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(6), 2);     // PatternLength
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(8), 85);    // PixelWidth
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(10), 170);  // PixelHeight
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(12), 640);  // ScanWidth
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(14), 400);  // ScanLines

    var header = GemImgHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Version, Is.EqualTo(1));
      Assert.That(header.HeaderLength, Is.EqualTo(8));
      Assert.That(header.NumPlanes, Is.EqualTo(4));
      Assert.That(header.PatternLength, Is.EqualTo(2));
      Assert.That(header.PixelWidth, Is.EqualTo(85));
      Assert.That(header.PixelHeight, Is.EqualTo(170));
      Assert.That(header.ScanWidth, Is.EqualTo(640));
      Assert.That(header.ScanLines, Is.EqualTo(400));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = GemImgHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(GemImgHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = GemImgHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is16() {
    Assert.That(GemImgHeader.StructSize, Is.EqualTo(16));
  }
}
