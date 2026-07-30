using System;
using FileFormat.GemImg;

namespace FileFormat.GemImg.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void GemImgCompression_HasExpectedValues() {
    Assert.That((int)GemImgCompression.None, Is.EqualTo(0));
    Assert.That((int)GemImgCompression.Standard, Is.EqualTo(1));

    var values = Enum.GetValues<GemImgCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }
}
