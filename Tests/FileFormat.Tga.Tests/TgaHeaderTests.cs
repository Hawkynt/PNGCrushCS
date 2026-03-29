using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Tga.Tests;

[TestFixture]
public sealed class TgaHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new TgaHeader(
      IdLength: 5,
      ColorMapType: 1,
      ImageType: 2,
      ColorMapFirstEntry: 10,
      ColorMapLength: 256,
      ColorMapEntrySize: 24,
      XOrigin: 100,
      YOrigin: 200,
      Width: 640,
      Height: 480,
      BitsPerPixel: 24,
      ImageDescriptor: 0x20
    );
    Span<byte> buffer = stackalloc byte[TgaHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = TgaHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[TgaHeader.StructSize];
    data[0] = 0;                                                       // IdLength
    data[1] = 1;                                                       // ColorMapType
    data[2] = 10;                                                      // ImageType (RLE true-color)
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(3), 0);        // ColorMapFirstEntry
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(5), 256);      // ColorMapLength
    data[7] = 24;                                                      // ColorMapEntrySize
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), 50);       // XOrigin
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(10), 75);      // YOrigin
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 320);     // Width
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 240);     // Height
    data[16] = 32;                                                     // BitsPerPixel
    data[17] = 0x28;                                                   // ImageDescriptor (top-left + 8 alpha bits)

    var header = TgaHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.IdLength, Is.EqualTo(0));
      Assert.That(header.ColorMapType, Is.EqualTo(1));
      Assert.That(header.ImageType, Is.EqualTo(10));
      Assert.That(header.ColorMapFirstEntry, Is.EqualTo(0));
      Assert.That(header.ColorMapLength, Is.EqualTo(256));
      Assert.That(header.ColorMapEntrySize, Is.EqualTo(24));
      Assert.That(header.XOrigin, Is.EqualTo(50));
      Assert.That(header.YOrigin, Is.EqualTo(75));
      Assert.That(header.Width, Is.EqualTo(320));
      Assert.That(header.Height, Is.EqualTo(240));
      Assert.That(header.BitsPerPixel, Is.EqualTo(32));
      Assert.That(header.ImageDescriptor, Is.EqualTo(0x28));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new TgaHeader(
      IdLength: 12,
      ColorMapType: 0,
      ImageType: 2,
      ColorMapFirstEntry: 0,
      ColorMapLength: 0,
      ColorMapEntrySize: 0,
      XOrigin: 0,
      YOrigin: 0,
      Width: 800,
      Height: 600,
      BitsPerPixel: 24,
      ImageDescriptor: 0x00
    );
    var buffer = new byte[TgaHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo(12));
      Assert.That(buffer[1], Is.EqualTo(0));
      Assert.That(buffer[2], Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(3)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(5)), Is.EqualTo(0));
      Assert.That(buffer[7], Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(8)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(10)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(12)), Is.EqualTo(800));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(14)), Is.EqualTo(600));
      Assert.That(buffer[16], Is.EqualTo(24));
      Assert.That(buffer[17], Is.EqualTo(0));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = TgaHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(TgaHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = TgaHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is18() {
    Assert.That(TgaHeader.StructSize, Is.EqualTo(18));
  }
}
