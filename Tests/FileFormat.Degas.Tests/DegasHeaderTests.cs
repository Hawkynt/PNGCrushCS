using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Degas;

namespace FileFormat.Degas.Tests;

[TestFixture]
public sealed class DegasHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var original = DegasHeader.FromPalette(2, palette);
    Span<byte> buffer = stackalloc byte[DegasHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = DegasHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[DegasHeader.StructSize];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 1);     // Resolution = Medium
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 0x777); // Palette[0] = white
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 0x700); // Palette[1] = red
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(6), 0x070); // Palette[2] = green
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(8), 0x007); // Palette[3] = blue

    var header = DegasHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Resolution, Is.EqualTo(1));
      Assert.That(header.Palette0, Is.EqualTo(0x777));
      Assert.That(header.Palette1, Is.EqualTo(0x700));
      Assert.That(header.Palette2, Is.EqualTo(0x070));
      Assert.That(header.Palette3, Is.EqualTo(0x007));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = DegasHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(DegasHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = DegasHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is34() {
    Assert.That(DegasHeader.StructSize, Is.EqualTo(34));
  }
}
