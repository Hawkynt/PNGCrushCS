using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Neochrome;
using FileFormat.Core;

namespace FileFormat.Neochrome.Tests;

[TestFixture]
public sealed class NeochromeHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new NeochromeHeader(
      42,    // Flag
      0,     // Resolution
      0x0777, 0x0700, 0x0070, 0x0007,
      0x0770, 0x0707, 0x0077, 0x0000,
      0x0111, 0x0222, 0x0333, 0x0444,
      0x0555, 0x0666, 0x0123, 0x0456,
      5,     // AnimSpeed
      1,     // AnimDirection
      10,    // AnimSteps
      16,    // AnimXOffset
      32,    // AnimYOffset
      64,    // AnimWidth
      48     // AnimHeight
    );
    var buffer = new byte[NeochromeHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = NeochromeHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[128];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 99);    // Flag
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 0);     // Resolution
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 0x0777); // Pal0
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(34), 0x0007); // Pal15
    data[36] = 3;                                                  // AnimSpeed
    data[37] = 2;                                                  // AnimDirection
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(38), 20);    // AnimSteps
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(40), 8);     // AnimXOffset
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(42), 16);    // AnimYOffset
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(44), 128);   // AnimWidth
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(46), 96);    // AnimHeight

    var header = NeochromeHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Flag, Is.EqualTo(99));
      Assert.That(header.Resolution, Is.EqualTo(0));
      Assert.That(header.Pal0, Is.EqualTo((short)0x0777));
      Assert.That(header.Pal15, Is.EqualTo((short)0x0007));
      Assert.That(header.AnimSpeed, Is.EqualTo(3));
      Assert.That(header.AnimDirection, Is.EqualTo(2));
      Assert.That(header.AnimSteps, Is.EqualTo(20));
      Assert.That(header.AnimXOffset, Is.EqualTo(8));
      Assert.That(header.AnimYOffset, Is.EqualTo(16));
      Assert.That(header.AnimWidth, Is.EqualTo(128));
      Assert.That(header.AnimHeight, Is.EqualTo(96));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new NeochromeHeader(
      0,     // Flag
      0,     // Resolution
      0x0777, 0, 0, 0,
      0, 0, 0, 0,
      0, 0, 0, 0,
      0, 0, 0, 0x0007,
      7,     // AnimSpeed
      0,     // AnimDirection
      5,     // AnimSteps
      10,    // AnimXOffset
      20,    // AnimYOffset
      40,    // AnimWidth
      30     // AnimHeight
    );
    var buffer = new byte[NeochromeHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(0)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(2)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(4)), Is.EqualTo((short)0x0777));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(34)), Is.EqualTo((short)0x0007));
      Assert.That(buffer[36], Is.EqualTo(7));
      Assert.That(buffer[37], Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(38)), Is.EqualTo(5));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(40)), Is.EqualTo(10));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(42)), Is.EqualTo(20));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(44)), Is.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(46)), Is.EqualTo(30));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = NeochromeHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(NeochromeHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = NeochromeHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is128() {
    Assert.That(NeochromeHeader.StructSize, Is.EqualTo(128));
  }
}
