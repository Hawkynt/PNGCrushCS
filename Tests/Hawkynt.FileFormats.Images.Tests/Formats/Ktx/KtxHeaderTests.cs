using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Ktx;
using FileFormat.Core;

namespace FileFormat.Ktx.Tests;

[TestFixture]
public sealed class KtxHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new KtxHeader(
      KtxHeader.EndiannessLE,
      0x1401,
      1,
      0x1908,
      0x8058,
      0x1908,
      256,
      128,
      0,
      0,
      1,
      3,
      0
    );
    var buffer = new byte[KtxHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = KtxHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[KtxHeader.StructSize];
    KtxHeader.Identifier.CopyTo(data, 0);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), KtxHeader.EndiannessLE);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 0x1401);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(36), 512);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(40), 256);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(52), 6);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(56), 5);

    var header = KtxHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Endianness, Is.EqualTo(KtxHeader.EndiannessLE));
      Assert.That(header.GlType, Is.EqualTo(0x1401));
      Assert.That(header.GlTypeSize, Is.EqualTo(1));
      Assert.That(header.PixelWidth, Is.EqualTo(512));
      Assert.That(header.PixelHeight, Is.EqualTo(256));
      Assert.That(header.NumberOfFaces, Is.EqualTo(6));
      Assert.That(header.NumberOfMipmapLevels, Is.EqualTo(5));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new KtxHeader(
      KtxHeader.EndiannessLE,
      0x1401,
      1,
      0x1908,
      0x8058,
      0x1908,
      1024,
      768,
      0,
      0,
      1,
      1,
      0
    );
    var buffer = new byte[KtxHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      // Identifier region is a filler — written by KtxWriter, not the header struct
      for (var i = 0; i < KtxHeader.IdentifierSize; ++i)
        Assert.That(buffer[i], Is.EqualTo(0));

      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12)), Is.EqualTo(KtxHeader.EndiannessLE));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(36)), Is.EqualTo(1024));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(40)), Is.EqualTo(768));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = KtxHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(KtxHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = KtxHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is64() {
    Assert.That(KtxHeader.StructSize, Is.EqualTo(64));
  }
}
