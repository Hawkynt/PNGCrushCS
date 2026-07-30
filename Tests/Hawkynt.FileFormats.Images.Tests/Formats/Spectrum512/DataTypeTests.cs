using System;
using FileFormat.Spectrum512;

namespace FileFormat.Spectrum512.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Spectrum512Variant_HasExpectedValues() {
    Assert.That((int)Spectrum512Variant.Uncompressed, Is.EqualTo(0));
    Assert.That((int)Spectrum512Variant.Compressed, Is.EqualTo(1));

    var values = Enum.GetValues<Spectrum512Variant>();
    Assert.That(values, Has.Length.EqualTo(2));
  }
}
