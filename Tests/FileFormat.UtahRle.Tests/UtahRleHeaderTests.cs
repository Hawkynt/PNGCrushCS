using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.UtahRle;
using FileFormat.Core;

namespace FileFormat.UtahRle.Tests;

[TestFixture]
public sealed class UtahRleHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new UtahRleHeader(
      UtahRleHeader.MagicValue,
      10,
      20,
      320,
      240,
      0x04,
      3,
      8,
      0
    );

    Span<byte> buffer = stackalloc byte[UtahRleHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = UtahRleHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[14];
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0), UtahRleHeader.MagicValue);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 5);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 10);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 640);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), 480);
    data[10] = 0x04;
    data[11] = 3;
    data[12] = 8;
    data[13] = 0;

    var header = UtahRleHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Magic, Is.EqualTo(UtahRleHeader.MagicValue));
      Assert.That(header.XPos, Is.EqualTo(5));
      Assert.That(header.YPos, Is.EqualTo(10));
      Assert.That(header.XSize, Is.EqualTo(640));
      Assert.That(header.YSize, Is.EqualTo(480));
      Assert.That(header.Flags, Is.EqualTo(0x04));
      Assert.That(header.NumChannels, Is.EqualTo(3));
      Assert.That(header.NumBitsPerPixel, Is.EqualTo(8));
      Assert.That(header.NumColorMapChannels, Is.EqualTo(0));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new UtahRleHeader(
      UtahRleHeader.MagicValue,
      0,
      0,
      100,
      50,
      0x02,
      1,
      8,
      0
    );

    var buffer = new byte[UtahRleHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(0)), Is.EqualTo(UtahRleHeader.MagicValue));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(6)), Is.EqualTo(100));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(8)), Is.EqualTo(50));
      Assert.That(buffer[11], Is.EqualTo(1));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = UtahRleHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(UtahRleHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = UtahRleHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is14() {
    Assert.That(UtahRleHeader.StructSize, Is.EqualTo(14));
  }
}
