using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.IffPbm;
using FileFormat.Core;

namespace FileFormat.IffPbm.Tests;

[TestFixture]
public sealed class BmhdTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is20() {
    Assert.That(IffPbmBmhd.StructSize, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new IffPbmBmhd(320, 200, -10, -20, 8, 0, 1, 0, 5, 10, 11, 320, 200);
    Span<byte> buffer = stackalloc byte[IffPbmBmhd.StructSize];
    original.WriteTo(buffer);
    var parsed = IffPbmBmhd.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 640);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 480);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 0);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(6), 0);
    data[8] = 8;  // numPlanes
    data[9] = 0;  // masking
    data[10] = 1; // compression
    data[11] = 0; // padding
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 7);
    data[14] = 10; // xAspect
    data[15] = 11; // yAspect
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(16), 640);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(18), 480);

    var header = IffPbmBmhd.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Width, Is.EqualTo(640));
      Assert.That(header.Height, Is.EqualTo(480));
      Assert.That(header.XOrigin, Is.EqualTo(0));
      Assert.That(header.YOrigin, Is.EqualTo(0));
      Assert.That(header.NumPlanes, Is.EqualTo(8));
      Assert.That(header.Masking, Is.EqualTo(0));
      Assert.That(header.Compression, Is.EqualTo(1));
      Assert.That(header.TransparentColor, Is.EqualTo(7));
      Assert.That(header.XAspect, Is.EqualTo(10));
      Assert.That(header.YAspect, Is.EqualTo(11));
      Assert.That(header.PageWidth, Is.EqualTo(640));
      Assert.That(header.PageHeight, Is.EqualTo(480));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = IffPbmBmhd.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(IffPbmBmhd.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = IffPbmBmhd.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
