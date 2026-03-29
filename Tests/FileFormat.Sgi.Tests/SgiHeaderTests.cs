using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Sgi;
using FileFormat.Core;

namespace FileFormat.Sgi.Tests;

[TestFixture]
public sealed class SgiHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new SgiHeader(0x01DA, 1, 1, 3, 640, 480, 3, 0, 255, 0, "test image", 0);
    var buffer = new byte[SgiHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = SgiHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Magic, Is.EqualTo(original.Magic));
      Assert.That(parsed.Compression, Is.EqualTo(original.Compression));
      Assert.That(parsed.BytesPerChannel, Is.EqualTo(original.BytesPerChannel));
      Assert.That(parsed.Dimension, Is.EqualTo(original.Dimension));
      Assert.That(parsed.XSize, Is.EqualTo(original.XSize));
      Assert.That(parsed.YSize, Is.EqualTo(original.YSize));
      Assert.That(parsed.ZSize, Is.EqualTo(original.ZSize));
      Assert.That(parsed.PixMin, Is.EqualTo(original.PixMin));
      Assert.That(parsed.PixMax, Is.EqualTo(original.PixMax));
      Assert.That(parsed.Dummy, Is.EqualTo(original.Dummy));
      Assert.That(parsed.ImageName, Is.EqualTo(original.ImageName));
      Assert.That(parsed.Colormap, Is.EqualTo(original.Colormap));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[512];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 0x01DA);
    data[2] = 1; // compression
    data[3] = 2; // bytesPerChannel
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 3);   // dimension
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 320); // XSize
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), 240); // YSize
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10), 4);  // ZSize
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), 0);   // PixMin
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), 65535); // PixMax
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(104), 0);  // Colormap

    // Write "SGI" as image name
    data[24] = (byte)'S';
    data[25] = (byte)'G';
    data[26] = (byte)'I';

    var header = SgiHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Magic, Is.EqualTo(0x01DA));
      Assert.That(header.Compression, Is.EqualTo(1));
      Assert.That(header.BytesPerChannel, Is.EqualTo(2));
      Assert.That(header.Dimension, Is.EqualTo(3));
      Assert.That(header.XSize, Is.EqualTo(320));
      Assert.That(header.YSize, Is.EqualTo(240));
      Assert.That(header.ZSize, Is.EqualTo(4));
      Assert.That(header.PixMin, Is.EqualTo(0));
      Assert.That(header.PixMax, Is.EqualTo(65535));
      Assert.That(header.ImageName, Is.EqualTo("SGI"));
      Assert.That(header.Colormap, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = SgiHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(SgiHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = SgiHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is512() {
    Assert.That(SgiHeader.StructSize, Is.EqualTo(512));
  }
}
