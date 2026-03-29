using System;
using FileFormat.Tim2;

namespace FileFormat.Tim2.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Tim2Format_HasExpectedValues() {
    Assert.That((int)Tim2Format.Rgb16, Is.EqualTo(1));
    Assert.That((int)Tim2Format.Rgb24, Is.EqualTo(2));
    Assert.That((int)Tim2Format.Rgb32, Is.EqualTo(3));
    Assert.That((int)Tim2Format.Indexed4, Is.EqualTo(4));
    Assert.That((int)Tim2Format.Indexed8, Is.EqualTo(5));

    var values = Enum.GetValues<Tim2Format>();
    Assert.That(values, Has.Length.EqualTo(5));
  }
}
