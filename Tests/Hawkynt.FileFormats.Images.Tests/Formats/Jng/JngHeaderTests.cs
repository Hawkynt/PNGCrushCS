using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Jng;
using FileFormat.Core;

namespace FileFormat.Jng.Tests;

[TestFixture]
public sealed class JngHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() {
    Assert.That(JngHeader.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new JngHeader(640, 480, 14, 8, 8, 0, 8, 0, 0, 0);
    Span<byte> buffer = stackalloc byte[JngHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = JngHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasEntries() {
    var map = JngHeader.GetFieldMap();
    Assert.That(map, Has.Length.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = JngHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(JngHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = JngHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void ColorType_ValuesAreCorrect() {
    var header8 = new JngHeader(1, 1, 8, 8, 8, 0, 0, 0, 0, 0);
    Assert.That(header8.ColorType, Is.EqualTo(8));

    var header10 = new JngHeader(1, 1, 10, 8, 8, 0, 0, 0, 0, 0);
    Assert.That(header10.ColorType, Is.EqualTo(10));

    var header12 = new JngHeader(1, 1, 12, 8, 8, 0, 8, 0, 0, 0);
    Assert.That(header12.ColorType, Is.EqualTo(12));

    var header14 = new JngHeader(1, 1, 14, 8, 8, 0, 8, 0, 0, 0);
    Assert.That(header14.ColorType, Is.EqualTo(14));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[16];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 1024);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 768);
    data[8] = 14;  // ColorType: color+alpha
    data[9] = 8;   // ImageSampleDepth
    data[10] = 8;  // ImageCompressionMethod: JPEG
    data[11] = 0;  // ImageInterlaceMethod
    data[12] = 8;  // AlphaSampleDepth
    data[13] = 0;  // AlphaCompressionMethod: PNG deflate
    data[14] = 0;  // AlphaFilterMethod
    data[15] = 0;  // AlphaInterlaceMethod

    var header = JngHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Width, Is.EqualTo(1024));
      Assert.That(header.Height, Is.EqualTo(768));
      Assert.That(header.ColorType, Is.EqualTo(14));
      Assert.That(header.ImageSampleDepth, Is.EqualTo(8));
      Assert.That(header.ImageCompressionMethod, Is.EqualTo(8));
      Assert.That(header.ImageInterlaceMethod, Is.EqualTo(0));
      Assert.That(header.AlphaSampleDepth, Is.EqualTo(8));
      Assert.That(header.AlphaCompressionMethod, Is.EqualTo(0));
      Assert.That(header.AlphaFilterMethod, Is.EqualTo(0));
      Assert.That(header.AlphaInterlaceMethod, Is.EqualTo(0));
    });
  }
}
