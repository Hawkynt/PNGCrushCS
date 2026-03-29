using System;
using FileFormat.BbcMicro;

namespace FileFormat.BbcMicro.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void BbcMicroMode_HasExpectedValues() {
    Assert.That((int)BbcMicroMode.Mode0, Is.EqualTo(0));
    Assert.That((int)BbcMicroMode.Mode1, Is.EqualTo(1));
    Assert.That((int)BbcMicroMode.Mode2, Is.EqualTo(2));
    Assert.That((int)BbcMicroMode.Mode4, Is.EqualTo(4));
    Assert.That((int)BbcMicroMode.Mode5, Is.EqualTo(5));

    var values = Enum.GetValues<BbcMicroMode>();
    Assert.That(values, Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void BbcMicroFile_ScreenSizeConstants() {
    Assert.That(BbcMicroFile.ScreenSizeModes012, Is.EqualTo(20480));
    Assert.That(BbcMicroFile.ScreenSizeModes45, Is.EqualTo(10240));
  }

  [Test]
  [Category("Unit")]
  public void BbcMicroFile_FixedHeight_Is256() {
    Assert.That(BbcMicroFile.FixedHeight, Is.EqualTo(256));
  }
}
