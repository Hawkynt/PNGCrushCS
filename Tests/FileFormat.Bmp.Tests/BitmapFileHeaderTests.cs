using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Bmp.Tests;

[TestFixture]
public sealed class BitmapFileHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new BitmapFileHeader((byte)'B', (byte)'M', 12345, 0, 0, 54);
    Span<byte> buffer = stackalloc byte[BitmapFileHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = BitmapFileHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[14];
    data[0] = (byte)'B';
    data[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), 1000);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 0);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), 0);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);

    var header = BitmapFileHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Sig1, Is.EqualTo((byte)'B'));
      Assert.That(header.Sig2, Is.EqualTo((byte)'M'));
      Assert.That(header.FileSize, Is.EqualTo(1000));
      Assert.That(header.Reserved1, Is.EqualTo(0));
      Assert.That(header.Reserved2, Is.EqualTo(0));
      Assert.That(header.PixelDataOffset, Is.EqualTo(54));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new BitmapFileHeader((byte)'B', (byte)'M', 2000, 0, 0, 118);
    var buffer = new byte[BitmapFileHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo((byte)'B'));
      Assert.That(buffer[1], Is.EqualTo((byte)'M'));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(2)), Is.EqualTo(2000));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(10)), Is.EqualTo(118));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = BitmapFileHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(BitmapFileHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = BitmapFileHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is14() {
    Assert.That(BitmapFileHeader.StructSize, Is.EqualTo(14));
  }
}
