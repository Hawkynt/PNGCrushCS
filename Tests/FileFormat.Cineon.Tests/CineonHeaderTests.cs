using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Cineon;
using FileFormat.Core;

namespace FileFormat.Cineon.Tests;

[TestFixture]
public sealed class CineonHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new CineonHeader(
      CineonHeader.MagicNumber,
      1024,
      1024,
      0,
      0,
      2048,
      "V4.5",
      "test.cin",
      "2024:01:15",
      "10:30:00",
      0,
      1,
      0,
      0,
      10,
      640,
      480,
      0f,
      0f,
      1023f,
      2.046f
    );

    var buffer = new byte[CineonHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = CineonHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Magic, Is.EqualTo(original.Magic));
      Assert.That(parsed.ImageDataOffset, Is.EqualTo(original.ImageDataOffset));
      Assert.That(parsed.GenericHeaderLength, Is.EqualTo(original.GenericHeaderLength));
      Assert.That(parsed.IndustryHeaderLength, Is.EqualTo(original.IndustryHeaderLength));
      Assert.That(parsed.UserDataLength, Is.EqualTo(original.UserDataLength));
      Assert.That(parsed.FileSize, Is.EqualTo(original.FileSize));
      Assert.That(parsed.Version, Is.EqualTo(original.Version));
      Assert.That(parsed.FileName, Is.EqualTo(original.FileName));
      Assert.That(parsed.CreateDate, Is.EqualTo(original.CreateDate));
      Assert.That(parsed.CreateTime, Is.EqualTo(original.CreateTime));
      Assert.That(parsed.Orientation, Is.EqualTo(original.Orientation));
      Assert.That(parsed.NumElements, Is.EqualTo(original.NumElements));
      Assert.That(parsed.DesignatorCode1, Is.EqualTo(original.DesignatorCode1));
      Assert.That(parsed.DesignatorCode2, Is.EqualTo(original.DesignatorCode2));
      Assert.That(parsed.BitsPerSample, Is.EqualTo(original.BitsPerSample));
      Assert.That(parsed.PixelsPerLine, Is.EqualTo(original.PixelsPerLine));
      Assert.That(parsed.LinesPerElement, Is.EqualTo(original.LinesPerElement));
      Assert.That(parsed.MinData, Is.EqualTo(original.MinData));
      Assert.That(parsed.MinQuantity, Is.EqualTo(original.MinQuantity));
      Assert.That(parsed.MaxData, Is.EqualTo(original.MaxData));
      Assert.That(parsed.MaxQuantity, Is.EqualTo(original.MaxQuantity));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[1024];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), CineonHeader.MagicNumber);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 1024);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 1024);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 5000);
    data[192] = 2; // orientation
    data[193] = 1; // numElements
    data[198] = 10; // bitsPerSample
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(200), 1920);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(204), 1080);

    var header = CineonHeader.ReadFrom(data);

    Assert.Multiple(() => {
      Assert.That(header.Magic, Is.EqualTo(CineonHeader.MagicNumber));
      Assert.That(header.ImageDataOffset, Is.EqualTo(1024));
      Assert.That(header.GenericHeaderLength, Is.EqualTo(1024));
      Assert.That(header.FileSize, Is.EqualTo(5000));
      Assert.That(header.Orientation, Is.EqualTo(2));
      Assert.That(header.NumElements, Is.EqualTo(1));
      Assert.That(header.BitsPerSample, Is.EqualTo(10));
      Assert.That(header.PixelsPerLine, Is.EqualTo(1920));
      Assert.That(header.LinesPerElement, Is.EqualTo(1080));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = CineonHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(CineonHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = CineonHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is1024() {
    Assert.That(CineonHeader.StructSize, Is.EqualTo(1024));
  }
}
