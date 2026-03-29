using System;
using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DdsFormat_HasExpectedValues() {
    Assert.That((int)DdsFormat.Unknown, Is.EqualTo(0));
    Assert.That((int)DdsFormat.Dxt1, Is.EqualTo(1));
    Assert.That((int)DdsFormat.Dxt3, Is.EqualTo(2));
    Assert.That((int)DdsFormat.Dxt5, Is.EqualTo(3));
    Assert.That((int)DdsFormat.Dx10, Is.EqualTo(4));
    Assert.That((int)DdsFormat.Rgb, Is.EqualTo(5));
    Assert.That((int)DdsFormat.Rgba, Is.EqualTo(6));
    Assert.That((int)DdsFormat.Bc4, Is.EqualTo(7));
    Assert.That((int)DdsFormat.Bc5, Is.EqualTo(8));
    Assert.That((int)DdsFormat.Bc6HUnsigned, Is.EqualTo(9));
    Assert.That((int)DdsFormat.Bc6HSigned, Is.EqualTo(10));
    Assert.That((int)DdsFormat.Bc7, Is.EqualTo(11));

    var values = Enum.GetValues<DdsFormat>();
    Assert.That(values, Has.Length.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void DdsResourceType_HasExpectedValues() {
    Assert.That((int)DdsResourceType.Unknown, Is.EqualTo(0));
    Assert.That((int)DdsResourceType.Texture1D, Is.EqualTo(2));
    Assert.That((int)DdsResourceType.Texture2D, Is.EqualTo(3));
    Assert.That((int)DdsResourceType.Texture3D, Is.EqualTo(4));

    var values = Enum.GetValues<DdsResourceType>();
    Assert.That(values, Has.Length.EqualTo(4));
  }
}
