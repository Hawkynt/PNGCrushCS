using System;
using FileFormat.Nifti;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void NiftiDataType_HasExpectedValues() {
    Assert.That((short)NiftiDataType.UInt8, Is.EqualTo(2));
    Assert.That((short)NiftiDataType.Int16, Is.EqualTo(4));
    Assert.That((short)NiftiDataType.Int32, Is.EqualTo(8));
    Assert.That((short)NiftiDataType.Float32, Is.EqualTo(16));
    Assert.That((short)NiftiDataType.Float64, Is.EqualTo(64));
    Assert.That((short)NiftiDataType.Rgb24, Is.EqualTo(128));
    Assert.That((short)NiftiDataType.Int8, Is.EqualTo(256));
    Assert.That((short)NiftiDataType.UInt16, Is.EqualTo(512));
    Assert.That((short)NiftiDataType.UInt32, Is.EqualTo(768));

    var values = Enum.GetValues<NiftiDataType>();
    Assert.That(values, Has.Length.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void NiftiFile_DefaultValues() {
    var file = new NiftiFile();

    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Depth, Is.EqualTo(0));
    Assert.That(file.Bitpix, Is.EqualTo(0));
    Assert.That(file.SclSlope, Is.EqualTo(0f));
    Assert.That(file.SclInter, Is.EqualTo(0f));
    Assert.That(file.VoxOffset, Is.EqualTo(0f));
    Assert.That(file.Description, Is.EqualTo(""));
    Assert.That(file.PixelData, Is.Empty);
    Assert.That(file.Pixdim, Is.Empty);
  }
}
