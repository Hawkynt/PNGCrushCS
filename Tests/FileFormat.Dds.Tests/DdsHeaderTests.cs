using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

[TestFixture]
public sealed class DdsHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var pf = new DdsPixelFormat(32, 0x40, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0);
    var original = new DdsHeader(124, 0x1007, 256, 512, 0, 1, 3, pf, 0x1000, 0, 0, 0, 0);
    var buffer = new byte[DdsHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = DdsHeader.ReadFrom(buffer);

    Assert.That(parsed.Size, Is.EqualTo(original.Size));
    Assert.That(parsed.Flags, Is.EqualTo(original.Flags));
    Assert.That(parsed.Height, Is.EqualTo(original.Height));
    Assert.That(parsed.Width, Is.EqualTo(original.Width));
    Assert.That(parsed.PitchOrLinearSize, Is.EqualTo(original.PitchOrLinearSize));
    Assert.That(parsed.Depth, Is.EqualTo(original.Depth));
    Assert.That(parsed.MipMapCount, Is.EqualTo(original.MipMapCount));
    Assert.That(parsed.PixelFormat, Is.EqualTo(original.PixelFormat));
    Assert.That(parsed.Caps, Is.EqualTo(original.Caps));
    Assert.That(parsed.Caps2, Is.EqualTo(original.Caps2));
    Assert.That(parsed.Caps3, Is.EqualTo(original.Caps3));
    Assert.That(parsed.Caps4, Is.EqualTo(original.Caps4));
    Assert.That(parsed.Reserved2, Is.EqualTo(original.Reserved2));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = DdsHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(DdsHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = DdsHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is124() {
    Assert.That(DdsHeader.StructSize, Is.EqualTo(124));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[124];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 124);   // Size
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x1007); // Flags
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 128);   // Height
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 256);  // Width
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 5);    // MipMapCount
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(72), 32);   // PixelFormat.Size

    var header = DdsHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Size, Is.EqualTo(124));
      Assert.That(header.Flags, Is.EqualTo(0x1007));
      Assert.That(header.Height, Is.EqualTo(128));
      Assert.That(header.Width, Is.EqualTo(256));
      Assert.That(header.MipMapCount, Is.EqualTo(5));
      Assert.That(header.PixelFormat.Size, Is.EqualTo(32));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var pf = new DdsPixelFormat(32, 0x4, 0x31545844, 0, 0, 0, 0, 0);
    var header = new DdsHeader(124, 0x1007, 64, 128, 4096, 0, 1, pf, 0x1000, 0, 0, 0, 0);
    var buffer = new byte[DdsHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(124));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8)), Is.EqualTo(64));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12)), Is.EqualTo(128));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(16)), Is.EqualTo(4096));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(104)), Is.EqualTo(0x1000));
    });
  }
}
