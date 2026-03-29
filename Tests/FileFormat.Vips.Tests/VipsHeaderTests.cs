using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Vips;

namespace FileFormat.Vips.Tests;

[TestFixture]
public sealed class VipsHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is64() {
    Assert.That(VipsHeader.StructSize, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new VipsHeader(
      Magic: unchecked((int)0x08F2A6B6),
      Width: 320,
      Height: 240,
      Bands: 3,
      Unused1: 0,
      BandFormat: 0,
      Coding: 0,
      Type: 22,
      XRes: 1.5f,
      YRes: 2.5f,
      XOffset: 10,
      YOffset: 20,
      Length: 3,
      Compression: 0,
      Level: 0,
      BBits: 8,
      Unused2: 0
    );
    Span<byte> buffer = stackalloc byte[VipsHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = VipsHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[VipsHeader.StructSize];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), unchecked((int)0x08F2A6B6));
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 640);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 480);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 0);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(28), 1);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(32), 3.0f);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(36), 4.0f);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(56), 8);
    var header = VipsHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Magic, Is.EqualTo(unchecked((int)0x08F2A6B6)));
      Assert.That(header.Width, Is.EqualTo(640));
      Assert.That(header.Height, Is.EqualTo(480));
      Assert.That(header.Bands, Is.EqualTo(1));
      Assert.That(header.Type, Is.EqualTo(1));
      Assert.That(header.XRes, Is.EqualTo(3.0f));
      Assert.That(header.YRes, Is.EqualTo(4.0f));
      Assert.That(header.BBits, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new VipsHeader(
      Magic: unchecked((int)0x08F2A6B6),
      Width: 100,
      Height: 200,
      Bands: 3,
      Unused1: 0,
      BandFormat: 0,
      Coding: 0,
      Type: 22,
      XRes: 1.0f,
      YRes: 1.0f,
      XOffset: 0,
      YOffset: 0,
      Length: 3,
      Compression: 0,
      Level: 0,
      BBits: 8,
      Unused2: 0
    );
    var buffer = new byte[VipsHeader.StructSize];
    header.WriteTo(buffer);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(unchecked((int)0x08F2A6B6)));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(100));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8)), Is.EqualTo(200));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12)), Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(28)), Is.EqualTo(22));
      Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(32)), Is.EqualTo(1.0f));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = VipsHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(VipsHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = VipsHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
