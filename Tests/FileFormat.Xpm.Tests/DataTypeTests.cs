using System;
using FileFormat.Xpm;

namespace FileFormat.Xpm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void XpmVersion_HasExpectedValues() {
    Assert.That((int)XpmVersion.Xpm3, Is.EqualTo(3));
    Assert.That(Enum.GetValues<XpmVersion>(), Has.Length.EqualTo(1));
  }
}
