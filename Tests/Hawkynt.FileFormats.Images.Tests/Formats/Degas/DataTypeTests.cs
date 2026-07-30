using System;
using FileFormat.Degas;

namespace FileFormat.Degas.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DegasResolution_HasExpectedValues() {
    Assert.That((int)DegasResolution.Low, Is.EqualTo(0));
    Assert.That((int)DegasResolution.Medium, Is.EqualTo(1));
    Assert.That((int)DegasResolution.High, Is.EqualTo(2));

    var values = Enum.GetValues<DegasResolution>();
    Assert.That(values, Has.Length.EqualTo(3));
  }
}
