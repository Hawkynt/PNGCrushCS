using System;
using FileFormat.Ico;

namespace FileFormat.Ico.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IcoImageFormat_HasExpectedValues() {
    Assert.That((int)IcoImageFormat.Bmp, Is.EqualTo(0));
    Assert.That((int)IcoImageFormat.Png, Is.EqualTo(1));
    Assert.That(Enum.GetValues<IcoImageFormat>(), Has.Length.EqualTo(2));
  }
}
