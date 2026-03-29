using System;
using FileFormat.C64Multi;

namespace FileFormat.C64Multi.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void C64MultiFormat_HasExpectedValues() {
    Assert.That((int)C64MultiFormat.ArtStudioHires, Is.EqualTo(0));
    Assert.That((int)C64MultiFormat.ArtStudioMulti, Is.EqualTo(1));
    Assert.That((int)C64MultiFormat.AmicaPaint, Is.EqualTo(2));

    var values = Enum.GetValues<C64MultiFormat>();
    Assert.That(values, Has.Length.EqualTo(3));
  }
}
