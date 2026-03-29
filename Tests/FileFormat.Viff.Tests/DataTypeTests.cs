using System;
using FileFormat.Viff;

namespace FileFormat.Viff.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ViffStorageType_HasExpectedValues() {
    Assert.That((uint)ViffStorageType.Bit, Is.EqualTo(0u));
    Assert.That((uint)ViffStorageType.Byte, Is.EqualTo(1u));
    Assert.That((uint)ViffStorageType.Short, Is.EqualTo(2u));
    Assert.That((uint)ViffStorageType.Int, Is.EqualTo(3u));
    Assert.That((uint)ViffStorageType.Float, Is.EqualTo(4u));
    Assert.That((uint)ViffStorageType.Double, Is.EqualTo(5u));
    Assert.That((uint)ViffStorageType.Complex, Is.EqualTo(6u));
  }

  [Test]
  [Category("Unit")]
  public void ViffStorageType_HasExpectedCount() {
    var values = Enum.GetValues<ViffStorageType>();
    Assert.That(values.Length, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void ViffMapType_HasExpectedValues() {
    Assert.That((uint)ViffMapType.None, Is.EqualTo(0u));
    Assert.That((uint)ViffMapType.Byte, Is.EqualTo(1u));
    Assert.That((uint)ViffMapType.Short, Is.EqualTo(2u));
    Assert.That((uint)ViffMapType.Int, Is.EqualTo(3u));
    Assert.That((uint)ViffMapType.Float, Is.EqualTo(4u));
    Assert.That((uint)ViffMapType.Double, Is.EqualTo(5u));
  }

  [Test]
  [Category("Unit")]
  public void ViffMapType_HasExpectedCount() {
    var values = Enum.GetValues<ViffMapType>();
    Assert.That(values.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void ViffColorSpaceModel_HasExpectedValues() {
    Assert.That((uint)ViffColorSpaceModel.None, Is.EqualTo(0u));
    Assert.That((uint)ViffColorSpaceModel.Ntsc, Is.EqualTo(1u));
    Assert.That((uint)ViffColorSpaceModel.Rgb, Is.EqualTo(2u));
    Assert.That((uint)ViffColorSpaceModel.Cmy, Is.EqualTo(3u));
    Assert.That((uint)ViffColorSpaceModel.Spectral, Is.EqualTo(4u));
    Assert.That((uint)ViffColorSpaceModel.Generic, Is.EqualTo(5u));
  }

  [Test]
  [Category("Unit")]
  public void ViffColorSpaceModel_HasExpectedCount() {
    var values = Enum.GetValues<ViffColorSpaceModel>();
    Assert.That(values.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void ViffFile_DefaultValues() {
    var file = new ViffFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Bands, Is.EqualTo(0));
    Assert.That(file.StorageType, Is.EqualTo(ViffStorageType.Bit));
    Assert.That(file.Comment, Is.EqualTo(""));
    Assert.That(file.PixelData, Is.Empty);
    Assert.That(file.MapData, Is.Null);
  }
}
