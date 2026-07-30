using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

[TestFixture]
public sealed class DdsPixelFormatTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new DdsPixelFormat(32, 0x41, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, unchecked((int)0xFF000000));
    var buffer = new byte[DdsPixelFormat.StructSize];
    original.WriteTo(buffer);
    var parsed = DdsPixelFormat.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = DdsPixelFormat.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(DdsPixelFormat.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = DdsPixelFormat.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is32() {
    Assert.That(DdsPixelFormat.StructSize, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[32];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 32);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x4);          // DDPF_FOURCC
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 0x31545844);   // "DXT1"
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 0);

    var pf = DdsPixelFormat.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(pf.Size, Is.EqualTo(32));
      Assert.That(pf.Flags, Is.EqualTo(0x4));
      Assert.That(pf.FourCC, Is.EqualTo(0x31545844));
      Assert.That(pf.RGBBitCount, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var pf = new DdsPixelFormat(32, 0x40, 0, 24, 0xFF0000, 0x00FF00, 0x0000FF, 0);
    var buffer = new byte[DdsPixelFormat.StructSize];
    pf.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(32));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(0x40));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12)), Is.EqualTo(24));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(16)), Is.EqualTo(0xFF0000));
    });
  }
}
