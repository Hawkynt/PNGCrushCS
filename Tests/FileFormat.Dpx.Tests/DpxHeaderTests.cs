using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Dpx;
using FileFormat.Core;

namespace FileFormat.Dpx.Tests;

[TestFixture]
public sealed class DpxHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_BigEndian_PreservesAllFields() {
    var original = new DpxHeader(
      DpxHeader.MagicBigEndian,
      2048,
      "V2.0",
      10000,
      1,
      1408,
      0
    );

    var buffer = new byte[DpxHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = DpxHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Magic, Is.EqualTo(original.Magic));
      Assert.That(parsed.ImageDataOffset, Is.EqualTo(original.ImageDataOffset));
      Assert.That(parsed.Version, Is.EqualTo(original.Version));
      Assert.That(parsed.FileSize, Is.EqualTo(original.FileSize));
      Assert.That(parsed.DittoKey, Is.EqualTo(original.DittoKey));
      Assert.That(parsed.GenericHeaderSize, Is.EqualTo(original.GenericHeaderSize));
      Assert.That(parsed.IndustryHeaderSize, Is.EqualTo(original.IndustryHeaderSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LittleEndian_PreservesAllFields() {
    var original = new DpxHeader(
      DpxHeader.MagicLittleEndian,
      2048,
      "V2.0",
      8000,
      0,
      1408,
      640
    );

    var buffer = new byte[DpxHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = DpxHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Magic, Is.EqualTo(original.Magic));
      Assert.That(parsed.ImageDataOffset, Is.EqualTo(original.ImageDataOffset));
      Assert.That(parsed.Version, Is.EqualTo(original.Version));
      Assert.That(parsed.FileSize, Is.EqualTo(original.FileSize));
      Assert.That(parsed.DittoKey, Is.EqualTo(original.DittoKey));
      Assert.That(parsed.GenericHeaderSize, Is.EqualTo(original.GenericHeaderSize));
      Assert.That(parsed.IndustryHeaderSize, Is.EqualTo(original.IndustryHeaderSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_BigEndian_ParsesKnownValues() {
    var data = new byte[32];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), DpxHeader.MagicBigEndian);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 2048);
    data[8] = (byte)'V';
    data[9] = (byte)'2';
    data[10] = (byte)'.';
    data[11] = (byte)'0';
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), 5000);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 1);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(24), 1408);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28), 0);

    var header = DpxHeader.ReadFrom(data);

    Assert.Multiple(() => {
      Assert.That(header.Magic, Is.EqualTo(DpxHeader.MagicBigEndian));
      Assert.That(header.ImageDataOffset, Is.EqualTo(2048));
      Assert.That(header.Version, Is.EqualTo("V2.0"));
      Assert.That(header.FileSize, Is.EqualTo(5000));
      Assert.That(header.DittoKey, Is.EqualTo(1));
      Assert.That(header.GenericHeaderSize, Is.EqualTo(1408));
      Assert.That(header.IndustryHeaderSize, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_BigEndian_ProducesCorrectBytes() {
    var header = new DpxHeader(DpxHeader.MagicBigEndian, 2048, "V2.0", 3000, 1, 1408, 0);
    var buffer = new byte[DpxHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0)), Is.EqualTo(DpxHeader.MagicBigEndian));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4)), Is.EqualTo(2048));
      Assert.That(buffer[8], Is.EqualTo((byte)'V'));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(16)), Is.EqualTo(3000));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = DpxHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(DpxHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = DpxHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is32() {
    Assert.That(DpxHeader.StructSize, Is.EqualTo(32));
  }
}
