using System;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IlbmCompression_HasExpectedValues() {
    Assert.That((int)IlbmCompression.None, Is.EqualTo(0));
    Assert.That((int)IlbmCompression.ByteRun1, Is.EqualTo(1));

    var values = Enum.GetValues<IlbmCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void IlbmMasking_HasExpectedValues() {
    Assert.That((int)IlbmMasking.None, Is.EqualTo(0));
    Assert.That((int)IlbmMasking.HasMask, Is.EqualTo(1));
    Assert.That((int)IlbmMasking.HasTransparentColor, Is.EqualTo(2));
    Assert.That((int)IlbmMasking.Lasso, Is.EqualTo(3));

    var values = Enum.GetValues<IlbmMasking>();
    Assert.That(values, Has.Length.EqualTo(4));
  }
}
