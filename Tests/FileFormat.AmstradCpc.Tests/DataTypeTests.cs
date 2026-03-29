using System;
using FileFormat.AmstradCpc;

namespace FileFormat.AmstradCpc.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AmstradCpcMode_HasExpectedValues() {
    Assert.That((int)AmstradCpcMode.Mode0, Is.EqualTo(0));
    Assert.That((int)AmstradCpcMode.Mode1, Is.EqualTo(1));
    Assert.That((int)AmstradCpcMode.Mode2, Is.EqualTo(2));

    var values = Enum.GetValues<AmstradCpcMode>();
    Assert.That(values, Has.Length.EqualTo(3));
  }
}
