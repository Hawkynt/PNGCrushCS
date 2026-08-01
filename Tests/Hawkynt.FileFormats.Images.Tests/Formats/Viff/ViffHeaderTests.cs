using System;
using System.Buffers.Binary;
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
    var original = _Sample();

    var buffer = new byte[ViffHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = ViffHeader.ReadFrom(buffer.AsSpan());

    Assert.That(parsed.Identifier, Is.EqualTo(original.Identifier));
    Assert.That(parsed.RowSize, Is.EqualTo(original.RowSize));
    Assert.That(parsed.ColSize, Is.EqualTo(original.ColSize));
    Assert.That(parsed.SubRowSize, Is.EqualTo(original.SubRowSize));
    Assert.That(parsed.NumberDataBands, Is.EqualTo(original.NumberDataBands));
    Assert.That(parsed.DataStorageType, Is.EqualTo(original.DataStorageType));
    Assert.That(parsed.ColorSpaceModel, Is.EqualTo(original.ColorSpaceModel));
    Assert.That(parsed.Comment, Is.EqualTo(original.Comment));
    Assert.That(parsed.StartX, Is.EqualTo(original.StartX));
    Assert.That(parsed.StartY, Is.EqualTo(original.StartY));
    Assert.That(parsed.PixelSizeX, Is.EqualTo(original.PixelSizeX));
    Assert.That(parsed.PixelSizeY, Is.EqualTo(original.PixelSizeY));
  }

  /// <summary>
  /// Pins each field to the byte offset Khoros' <c>xvimage</c> struct puts it at.
  /// A round-trip through this library cannot see a layout error — writer and reader move together —
  /// so this reads the bytes directly. It is the test that was missing when the band count and the
  /// storage type sat four fields away from where every other VIFF tool looks for them.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void WriteTo_PlacesFieldsAtTheKhorosOffsets() {
    var buffer = new byte[ViffHeader.StructSize];
    _Sample().WriteTo(buffer.AsSpan());

    uint At(int offset) => BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset)); // MachineDep 0x02 is big-endian

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo(ViffHeader.Magic), "identifier");
      Assert.That(buffer[4], Is.EqualTo(0x02), "machine_dep");
      Assert.That(At(520), Is.EqualTo(320u), "row_size");
      Assert.That(At(524), Is.EqualTo(240u), "col_size");
      Assert.That(At(528), Is.EqualTo(0u), "subrow_size");
      Assert.That(At(548), Is.EqualTo(1u), "location_type");
      Assert.That(At(556), Is.EqualTo(1u), "num_of_images");
      Assert.That(At(560), Is.EqualTo(3u), "num_data_bands");
      Assert.That(At(564), Is.EqualTo((uint)ViffStorageType.Byte), "data_storage_type");
      Assert.That(At(572), Is.EqualTo((uint)ViffMapScheme.None), "map_scheme");
      Assert.That(At(600), Is.EqualTo((uint)ViffColorSpaceModel.GenericRgb), "color_space_model");
    });
  }

  private static ViffHeader _Sample() => new(
    Identifier: ViffHeader.Magic,
    FileType: 1,
    Release: 1,
    Version: 3,
    MachineDep: 0x02,
    Comment: "Round-trip test",
    RowSize: 320,
    ColSize: 240,
    SubRowSize: 0,
    StartX: -1,
    StartY: -1,
    PixelSizeX: 1f,
    PixelSizeY: 1f,
    LocationType: 1,
    LocationDim: 0,
    NumberOfImages: 1,
    NumberDataBands: 3,
    DataStorageType: (uint)ViffStorageType.Byte,
    DataEncodeScheme: 0,
    MapScheme: (uint)ViffMapScheme.None,
    MapStorageType: (uint)ViffMapType.None,
    MapRowSize: 0,
    MapColSize: 0,
    MapSubRowSize: 0,
    MapEnable: 1,
    MapsPerCycle: 0,
    ColorSpaceModel: (uint)ViffColorSpaceModel.GenericRgb,
    ISpare1: 0,
    ISpare2: 0,
    FSpare1: 0f,
    FSpare2: 0f
  );

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
