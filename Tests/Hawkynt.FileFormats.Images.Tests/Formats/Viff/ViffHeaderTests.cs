using System;
using System.Linq;
using FileFormat.Viff;
using FileFormat.Core;

namespace FileFormat.Viff.Tests;

[TestFixture]
public sealed class ViffHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is1024() {
    Assert.That(ViffHeader.StructSize, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new ViffHeader(
      Identifier: ViffHeader.Magic,
      FileType: 1,
      Release: 1,
      Version: 3,
      MachineDep: 0x02,
      Comment: "Round-trip test",
      RowSize: 320,
      ColSize: 240,
      SubRowSize: 0,
      StartX: 1.0f,
      StartY: 2.0f,
      PixelSizeX: 0.5f,
      PixelSizeY: 0.25f,
      LocationType: 1,
      LocationDim: 0,
      NumberOfImages: 1,
      NumDataBands: 3,
      DataStorageType: (uint)ViffStorageType.Byte,
      DataEncodeScheme: 0,
      MapScheme: 0,
      MapStorageType: 0,
      MapRowSize: 0,
      MapColSize: 0,
      MapSubRowSize: 0,
      MapEnable: 0,
      MapsPerCycle: 0,
      ColorSpaceModel: (uint)ViffColorSpaceModel.Rgb,
      SpareInt1: 0,
      SpareInt2: 0
    );

    var buffer = new byte[ViffHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = ViffHeader.ReadFrom(buffer.AsSpan());

    Assert.That(parsed.Identifier, Is.EqualTo(original.Identifier));
    Assert.That(parsed.RowSize, Is.EqualTo(original.RowSize));
    Assert.That(parsed.ColSize, Is.EqualTo(original.ColSize));
    Assert.That(parsed.NumDataBands, Is.EqualTo(original.NumDataBands));
    Assert.That(parsed.DataStorageType, Is.EqualTo(original.DataStorageType));
    Assert.That(parsed.ColorSpaceModel, Is.EqualTo(original.ColorSpaceModel));
    Assert.That(parsed.Comment, Is.EqualTo(original.Comment));
    Assert.That(parsed.StartX, Is.EqualTo(original.StartX));
    Assert.That(parsed.StartY, Is.EqualTo(original.StartY));
    Assert.That(parsed.PixelSizeX, Is.EqualTo(original.PixelSizeX));
    Assert.That(parsed.PixelSizeY, Is.EqualTo(original.PixelSizeY));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasEntries() {
    var map = ViffHeader.GetFieldMap();
    Assert.That(map.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = ViffHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(ViffHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void Magic_Is0xAB() {
    Assert.That(ViffHeader.Magic, Is.EqualTo(0xAB));
  }
}
