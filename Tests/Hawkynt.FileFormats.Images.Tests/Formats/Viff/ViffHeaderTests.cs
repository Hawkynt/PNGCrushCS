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
      SubRowSize: 3,
      StartX: 1.0f,
      StartY: 2.0f,
      PixelSize: 0.5f,
      Location: 0,
      Padding: 0,
      FileSpare: 0,
      MapType: 0,
      MapRowSize: 0,
      MapColSize: 0,
      MapSubRowSize: 0,
      MapStorageType: 0,
      MapRowSizePad: 0,
      MapEnable: 0,
      MapsPerCycle: 0,
      ColorSpaceModel: (uint)ViffColorSpaceModel.Rgb,
      IsBand: 1,
      DataStorageType: (uint)ViffStorageType.Byte,
      DataEncode: 0,
      MapScheme0: 0f,
      MapScheme1: 0f
    );

    var buffer = new byte[ViffHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = ViffHeader.ReadFrom(buffer.AsSpan());

    Assert.That(parsed.Identifier, Is.EqualTo(original.Identifier));
    Assert.That(parsed.RowSize, Is.EqualTo(original.RowSize));
    Assert.That(parsed.ColSize, Is.EqualTo(original.ColSize));
    Assert.That(parsed.SubRowSize, Is.EqualTo(original.SubRowSize));
    Assert.That(parsed.DataStorageType, Is.EqualTo(original.DataStorageType));
    Assert.That(parsed.ColorSpaceModel, Is.EqualTo(original.ColorSpaceModel));
    Assert.That(parsed.Comment, Is.EqualTo(original.Comment));
    Assert.That(parsed.StartX, Is.EqualTo(original.StartX));
    Assert.That(parsed.StartY, Is.EqualTo(original.StartY));
    Assert.That(parsed.PixelSize, Is.EqualTo(original.PixelSize));
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
