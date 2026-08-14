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
    var original = new SymbianMbmBitmapHeader(1000, 40, 320, 240, 4535, 3401, 24, 1, 0, 0);
    Span<byte> buffer = stackalloc byte[SymbianMbmBitmapHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = SymbianMbmBitmapHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  // The field order is Symbian's SEpocBitmapHeader, and the size in twips between the size in pixels
  // and the depth is the part that is easy to leave out - doing so reads the depth off the width in
  // twips, which is zero on every file the converters write.
  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[SymbianMbmBitmapHeader.StructSize];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 500);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 64);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 48);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 907);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 680);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 8);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 256);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(36), 2);
    var h = SymbianMbmBitmapHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(h.BitmapSize, Is.EqualTo(500));
      Assert.That(h.HeaderLength, Is.EqualTo(40));
      Assert.That(h.Width, Is.EqualTo(64));
      Assert.That(h.Height, Is.EqualTo(48));
      Assert.That(h.WidthInTwips, Is.EqualTo(907));
      Assert.That(h.HeightInTwips, Is.EqualTo(680));
      Assert.That(h.BitsPerPixel, Is.EqualTo(8));
      Assert.That(h.ColorMode, Is.EqualTo(1u));
      Assert.That(h.PaletteSize, Is.EqualTo(256u));
      Assert.That(h.Compression, Is.EqualTo(2u));
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
