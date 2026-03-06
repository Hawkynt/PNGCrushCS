using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Bmp.Tests;

[TestFixture]
public sealed class BitmapInfoHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new BitmapInfoHeader(40, 320, 240, 1, 24, 0, 230400, 2835, 2835, 0, 0);
    Span<byte> buffer = stackalloc byte[BitmapInfoHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = BitmapInfoHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[40];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 100);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), -50);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 32);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 0);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), 256);

    var header = BitmapInfoHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.HeaderSize, Is.EqualTo(40));
      Assert.That(header.Width, Is.EqualTo(100));
      Assert.That(header.Height, Is.EqualTo(-50));
      Assert.That(header.Planes, Is.EqualTo(1));
      Assert.That(header.BitsPerPixel, Is.EqualTo(32));
      Assert.That(header.Compression, Is.EqualTo(0));
      Assert.That(header.ColorsUsed, Is.EqualTo(256));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new BitmapInfoHeader(40, 640, 480, 1, 8, 1, 307200, 2835, 2835, 256, 0);
    var buffer = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(640));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8)), Is.EqualTo(480));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(14)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(32)), Is.EqualTo(256));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = BitmapInfoHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(BitmapInfoHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = BitmapInfoHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is40() {
    Assert.That(BitmapInfoHeader.StructSize, Is.EqualTo(40));
  }
}
