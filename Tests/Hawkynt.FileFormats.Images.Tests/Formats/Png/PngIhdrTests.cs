using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class PngIhdrTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new PngIhdr(Width: 1920, Height: 1080, BitDepth: 8, ColorType: 2, CompressionMethod: 0, FilterMethod: 0, InterlaceMethod: 0);
    Span<byte> buffer = stackalloc byte[PngIhdr.StructSize];
    original.WriteTo(buffer);
    var parsed = PngIhdr.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[PngIhdr.StructSize];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 640);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 480);
    data[8] = 8;
    data[9] = 6;
    data[10] = 0;
    data[11] = 0;
    data[12] = 1;

    var ihdr = PngIhdr.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(ihdr.Width, Is.EqualTo(640));
      Assert.That(ihdr.Height, Is.EqualTo(480));
      Assert.That(ihdr.BitDepth, Is.EqualTo(8));
      Assert.That(ihdr.ColorType, Is.EqualTo(6));
      Assert.That(ihdr.CompressionMethod, Is.EqualTo(0));
      Assert.That(ihdr.FilterMethod, Is.EqualTo(0));
      Assert.That(ihdr.InterlaceMethod, Is.EqualTo(1));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBigEndianBytes() {
    var ihdr = new PngIhdr(Width: 256, Height: 128, BitDepth: 4, ColorType: 3, CompressionMethod: 0, FilterMethod: 0, InterlaceMethod: 0);
    var buffer = new byte[PngIhdr.StructSize];
    ihdr.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0)), Is.EqualTo(256));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4)), Is.EqualTo(128));
      Assert.That(buffer[8], Is.EqualTo(4));
      Assert.That(buffer[9], Is.EqualTo(3));
      Assert.That(buffer[10], Is.EqualTo(0));
      Assert.That(buffer[11], Is.EqualTo(0));
      Assert.That(buffer[12], Is.EqualTo(0));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = PngIhdr.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(PngIhdr.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = PngIhdr.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is13() {
    Assert.That(PngIhdr.StructSize, Is.EqualTo(13));
  }
}
