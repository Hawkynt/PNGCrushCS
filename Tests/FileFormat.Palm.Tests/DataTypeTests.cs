using System;
using FileFormat.Palm;

namespace FileFormat.Palm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PalmCompression_HasExpectedValues() {
    Assert.That((int)PalmCompression.None, Is.EqualTo(0));
    Assert.That((int)PalmCompression.Scanline, Is.EqualTo(1));
    Assert.That((int)PalmCompression.Rle, Is.EqualTo(2));
    Assert.That((int)PalmCompression.PackBits, Is.EqualTo(255));

    var values = Enum.GetValues<PalmCompression>();
    Assert.That(values, Has.Length.EqualTo(4));
  }
}
