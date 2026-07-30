using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.SunRaster;
using FileFormat.Core;

namespace FileFormat.SunRaster.Tests;

[TestFixture]
public sealed class SunRasterHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new SunRasterHeader(
      Magic: SunRasterHeader.MagicValue,
      Width: 640,
      Height: 480,
      Depth: 24,
      Length: 921600,
      Type: 1,
      MapType: 0,
      MapLength: 0
    );

    var buffer = new byte[SunRasterHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = SunRasterHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[32];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), SunRasterHeader.MagicValue);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 320);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 240);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), 8);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), 76800);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 0);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(24), 1);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28), 768);

    var header = SunRasterHeader.ReadFrom(data);

    Assert.Multiple(() => {
      Assert.That(header.Magic, Is.EqualTo(SunRasterHeader.MagicValue));
      Assert.That(header.Width, Is.EqualTo(320));
      Assert.That(header.Height, Is.EqualTo(240));
      Assert.That(header.Depth, Is.EqualTo(8));
      Assert.That(header.Length, Is.EqualTo(76800));
      Assert.That(header.Type, Is.EqualTo(0));
      Assert.That(header.MapType, Is.EqualTo(1));
      Assert.That(header.MapLength, Is.EqualTo(768));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new SunRasterHeader(
      Magic: SunRasterHeader.MagicValue,
      Width: 100,
      Height: 50,
      Depth: 24,
      Length: 15000,
      Type: 1,
      MapType: 0,
      MapLength: 0
    );

    var buffer = new byte[SunRasterHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0)), Is.EqualTo(SunRasterHeader.MagicValue));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4)), Is.EqualTo(100));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(8)), Is.EqualTo(50));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(12)), Is.EqualTo(24));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(16)), Is.EqualTo(15000));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(20)), Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = SunRasterHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(SunRasterHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = SunRasterHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is32() {
    Assert.That(SunRasterHeader.StructSize, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void MagicValue_IsCorrect() {
    Assert.That(SunRasterHeader.MagicValue, Is.EqualTo(unchecked((int)0x59A66A95)));
  }
}
