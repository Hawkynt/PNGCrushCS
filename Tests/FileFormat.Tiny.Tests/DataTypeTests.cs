using System;
using FileFormat.Tiny;

namespace FileFormat.Tiny.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void TinyResolution_HasExpectedValues() {
    Assert.That((int)TinyResolution.Low, Is.EqualTo(0));
    Assert.That((int)TinyResolution.Medium, Is.EqualTo(1));
    Assert.That((int)TinyResolution.High, Is.EqualTo(2));

    var values = Enum.GetValues<TinyResolution>();
    Assert.That(values, Has.Length.EqualTo(3));
  }
}
