using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Pcx.Tests;

[TestFixture]
public sealed class PcxHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var egaPalette = new byte[48];
    for (var i = 0; i < 48; ++i)
      egaPalette[i] = (byte)(i + 10);

    var padding = new byte[54];
    for (var i = 0; i < 54; ++i)
      padding[i] = (byte)(i + 100);

    var original = new PcxHeader(
      Manufacturer: 0x0A,
      Version: 5,
      Encoding: 1,
      BitsPerPixel: 8,
      XMin: 0,
      YMin: 0,
      XMax: 319,
      YMax: 199,
      HDpi: 72,
      VDpi: 72,
      EgaPalette: egaPalette,
      Reserved: 0,
      NumPlanes: 3,
      BytesPerLine: 320,
      PaletteInfo: 1,
      HScreenSize: 320,
      VScreenSize: 200,
      Padding: padding
    );

    var buffer = new byte[PcxHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = PcxHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Manufacturer, Is.EqualTo(original.Manufacturer));
      Assert.That(parsed.Version, Is.EqualTo(original.Version));
      Assert.That(parsed.Encoding, Is.EqualTo(original.Encoding));
      Assert.That(parsed.BitsPerPixel, Is.EqualTo(original.BitsPerPixel));
      Assert.That(parsed.XMin, Is.EqualTo(original.XMin));
      Assert.That(parsed.YMin, Is.EqualTo(original.YMin));
      Assert.That(parsed.XMax, Is.EqualTo(original.XMax));
      Assert.That(parsed.YMax, Is.EqualTo(original.YMax));
      Assert.That(parsed.HDpi, Is.EqualTo(original.HDpi));
      Assert.That(parsed.VDpi, Is.EqualTo(original.VDpi));
      Assert.That(parsed.EgaPalette, Is.EqualTo(original.EgaPalette));
      Assert.That(parsed.Reserved, Is.EqualTo(original.Reserved));
      Assert.That(parsed.NumPlanes, Is.EqualTo(original.NumPlanes));
      Assert.That(parsed.BytesPerLine, Is.EqualTo(original.BytesPerLine));
      Assert.That(parsed.PaletteInfo, Is.EqualTo(original.PaletteInfo));
      Assert.That(parsed.HScreenSize, Is.EqualTo(original.HScreenSize));
      Assert.That(parsed.VScreenSize, Is.EqualTo(original.VScreenSize));
      Assert.That(parsed.Padding, Is.EqualTo(original.Padding));
    });
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[PcxHeader.StructSize];
    data[0] = 0x0A;                                                        // Manufacturer
    data[1] = 5;                                                           // Version
    data[2] = 1;                                                           // Encoding
    data[3] = 4;                                                           // BitsPerPixel
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 10);           // XMin
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 20);           // YMin
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), 649);          // XMax
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(10), 499);         // YMax
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 96);          // HDpi
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 96);          // VDpi
    for (var i = 0; i < 48; ++i)
      data[16 + i] = (byte)(i * 5);                                       // EgaPalette
    data[64] = 0;                                                          // Reserved
    data[65] = 1;                                                          // NumPlanes
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(66), 326);         // BytesPerLine
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(68), 2);           // PaletteInfo
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(70), 1024);        // HScreenSize
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(72), 768);         // VScreenSize
    for (var i = 0; i < 54; ++i)
      data[74 + i] = (byte)i;                                             // Padding

    var header = PcxHeader.ReadFrom(data);
    var expectedEga = new byte[48];
    for (var i = 0; i < 48; ++i)
      expectedEga[i] = (byte)(i * 5);

    var expectedPadding = new byte[54];
    for (var i = 0; i < 54; ++i)
      expectedPadding[i] = (byte)i;

    Assert.Multiple(() => {
      Assert.That(header.Manufacturer, Is.EqualTo(0x0A));
      Assert.That(header.Version, Is.EqualTo(5));
      Assert.That(header.Encoding, Is.EqualTo(1));
      Assert.That(header.BitsPerPixel, Is.EqualTo(4));
      Assert.That(header.XMin, Is.EqualTo(10));
      Assert.That(header.YMin, Is.EqualTo(20));
      Assert.That(header.XMax, Is.EqualTo(649));
      Assert.That(header.YMax, Is.EqualTo(499));
      Assert.That(header.HDpi, Is.EqualTo(96));
      Assert.That(header.VDpi, Is.EqualTo(96));
      Assert.That(header.EgaPalette, Is.EqualTo(expectedEga));
      Assert.That(header.Reserved, Is.EqualTo(0));
      Assert.That(header.NumPlanes, Is.EqualTo(1));
      Assert.That(header.BytesPerLine, Is.EqualTo(326));
      Assert.That(header.PaletteInfo, Is.EqualTo(2));
      Assert.That(header.HScreenSize, Is.EqualTo(1024));
      Assert.That(header.VScreenSize, Is.EqualTo(768));
      Assert.That(header.Padding, Is.EqualTo(expectedPadding));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var egaPalette = new byte[48];
    egaPalette[0] = 0xFF;
    egaPalette[1] = 0x00;
    egaPalette[2] = 0x00;

    var header = new PcxHeader(
      Manufacturer: 0x0A,
      Version: 3,
      Encoding: 1,
      BitsPerPixel: 8,
      XMin: 0,
      YMin: 0,
      XMax: 799,
      YMax: 599,
      HDpi: 150,
      VDpi: 150,
      EgaPalette: egaPalette,
      Reserved: 0,
      NumPlanes: 3,
      BytesPerLine: 800,
      PaletteInfo: 1,
      HScreenSize: 800,
      VScreenSize: 600,
      Padding: new byte[54]
    );

    var buffer = new byte[PcxHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo(0x0A));
      Assert.That(buffer[1], Is.EqualTo(3));
      Assert.That(buffer[2], Is.EqualTo(1));
      Assert.That(buffer[3], Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(4)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(6)), Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(8)), Is.EqualTo(799));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(10)), Is.EqualTo(599));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(12)), Is.EqualTo(150));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(14)), Is.EqualTo(150));
      Assert.That(buffer[16], Is.EqualTo(0xFF));
      Assert.That(buffer[17], Is.EqualTo(0x00));
      Assert.That(buffer[18], Is.EqualTo(0x00));
      Assert.That(buffer[64], Is.EqualTo(0));
      Assert.That(buffer[65], Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(66)), Is.EqualTo(800));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(68)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(70)), Is.EqualTo(800));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(72)), Is.EqualTo(600));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = PcxHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(PcxHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = PcxHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is128() {
    Assert.That(PcxHeader.StructSize, Is.EqualTo(128));
  }
}
