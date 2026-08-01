using System;
using FileFormat.Viff;

namespace FileFormat.Viff.Tests;

[TestFixture]
public sealed class DataTypeTests {

  /// <remarks>
  /// Khoros' VFF_TYP_* numbering is sparse — for the integer types the constant is the width in
  /// bytes, so 3, 7 and 8 are skipped. These had been renumbered 0..6 as if they ran consecutively.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ViffStorageType_HasExpectedValues() {
    Assert.That((uint)ViffStorageType.Bit, Is.EqualTo(0u));
    Assert.That((uint)ViffStorageType.Byte, Is.EqualTo(1u));
    Assert.That((uint)ViffStorageType.Short, Is.EqualTo(2u));
    Assert.That((uint)ViffStorageType.Int, Is.EqualTo(4u));
    Assert.That((uint)ViffStorageType.Float, Is.EqualTo(5u));
    Assert.That((uint)ViffStorageType.Complex, Is.EqualTo(6u));
    Assert.That((uint)ViffStorageType.Double, Is.EqualTo(9u));
    Assert.That((uint)ViffStorageType.DoubleComplex, Is.EqualTo(10u));
  }

  [Test]
  [Category("Unit")]
  public void ViffStorageType_HasExpectedCount() {
    var values = Enum.GetValues<ViffStorageType>();
    Assert.That(values.Length, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ViffMapType_HasExpectedValues() {
    Assert.That((uint)ViffMapType.None, Is.EqualTo(0u));
    Assert.That((uint)ViffMapType.Byte, Is.EqualTo(1u));
    Assert.That((uint)ViffMapType.Short, Is.EqualTo(2u));
    Assert.That((uint)ViffMapType.Int, Is.EqualTo(4u));
    Assert.That((uint)ViffMapType.Float, Is.EqualTo(5u));
    Assert.That((uint)ViffMapType.Complex, Is.EqualTo(6u));
    Assert.That((uint)ViffMapType.Double, Is.EqualTo(7u));
  }

  [Test]
  [Category("Unit")]
  public void ViffMapType_HasExpectedCount() {
    var values = Enum.GetValues<ViffMapType>();
    Assert.That(values.Length, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void ViffMapScheme_HasExpectedValues() {
    Assert.That((uint)ViffMapScheme.None, Is.EqualTo(0u));
    Assert.That((uint)ViffMapScheme.OnePerBand, Is.EqualTo(1u));
    Assert.That((uint)ViffMapScheme.Cycle, Is.EqualTo(2u));
    Assert.That((uint)ViffMapScheme.Shared, Is.EqualTo(3u));
    Assert.That((uint)ViffMapScheme.Group, Is.EqualTo(4u));
  }

  /// <remarks>Plain RGB is the last of the sixteen, not the third — see <see cref="ViffColorSpaceModel"/>.</remarks>
  [Test]
  [Category("Unit")]
  public void ViffColorSpaceModel_HasExpectedValues() {
    Assert.That((uint)ViffColorSpaceModel.None, Is.EqualTo(0u));
    Assert.That((uint)ViffColorSpaceModel.NtscRgb, Is.EqualTo(1u));
    Assert.That((uint)ViffColorSpaceModel.NtscCmy, Is.EqualTo(2u));
    Assert.That((uint)ViffColorSpaceModel.Hsv, Is.EqualTo(4u));
    Assert.That((uint)ViffColorSpaceModel.Generic, Is.EqualTo(14u));
    Assert.That((uint)ViffColorSpaceModel.GenericRgb, Is.EqualTo(15u));
  }

  [Test]
  [Category("Unit")]
  public void ViffColorSpaceModel_HasExpectedCount() {
    var values = Enum.GetValues<ViffColorSpaceModel>();
    Assert.That(values.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ViffFile_DefaultValues() {
    var file = new ViffFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Bands, Is.EqualTo(0));
    Assert.That(file.StorageType, Is.EqualTo(ViffStorageType.Bit));
    Assert.That(file.Comment, Is.Null);
    Assert.That(file.PixelData, Is.Null);
    Assert.That(file.MapData, Is.Null);
  }
}
