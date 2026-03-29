using System;
using FileFormat.Vicar;

namespace FileFormat.Vicar.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void VicarPixelType_HasExpectedValues() {
    Assert.That((int)VicarPixelType.Byte, Is.EqualTo(0));
    Assert.That((int)VicarPixelType.Half, Is.EqualTo(1));
    Assert.That((int)VicarPixelType.Full, Is.EqualTo(2));
    Assert.That((int)VicarPixelType.Real, Is.EqualTo(3));
    Assert.That((int)VicarPixelType.Doub, Is.EqualTo(4));

    var values = Enum.GetValues<VicarPixelType>();
    Assert.That(values, Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void VicarOrganization_HasExpectedValues() {
    Assert.That((int)VicarOrganization.Bsq, Is.EqualTo(0));
    Assert.That((int)VicarOrganization.Bil, Is.EqualTo(1));
    Assert.That((int)VicarOrganization.Bip, Is.EqualTo(2));

    var values = Enum.GetValues<VicarOrganization>();
    Assert.That(values, Has.Length.EqualTo(3));
  }
}
