using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.SymbianMbm;

namespace FileFormat.SymbianMbm.Tests;

[TestFixture]
public sealed class SymbianMbmBitmapHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is40() => Assert.That(SymbianMbmBitmapHeader.StructSize, Is.EqualTo(40));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new SymbianMbmBitmapHeader(1000, 40, 320, 240, 24, 0, 0, 0, 960, 0);
    Span<byte> buffer = stackalloc byte[SymbianMbmBitmapHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = SymbianMbmBitmapHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[SymbianMbmBitmapHeader.StructSize];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 500);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 64);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 64);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 8);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 256);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 460);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(36), 0);
    var h = SymbianMbmBitmapHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.HeaderSize, Is.EqualTo(500));
      Assert.That(h.HeaderLength, Is.EqualTo(40));
      Assert.That(h.Width, Is.EqualTo(64));
      Assert.That(h.Height, Is.EqualTo(64));
      Assert.That(h.BitsPerPixel, Is.EqualTo(8));
      Assert.That(h.ColorMode, Is.EqualTo(1u));
      Assert.That(h.Compression, Is.EqualTo(0u));
      Assert.That(h.PaletteSize, Is.EqualTo(256u));
      Assert.That(h.DataSize, Is.EqualTo(460u));
      Assert.That(h.Padding, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = SymbianMbmBitmapHeader.GetFieldMap();
    Assert.That(map.Sum(f => f.Size), Is.EqualTo(SymbianMbmBitmapHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = SymbianMbmBitmapHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset),
        $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }
}
