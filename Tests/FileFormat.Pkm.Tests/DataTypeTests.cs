using System;
using FileFormat.Pkm;

namespace FileFormat.Pkm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PkmFormat_HasExpectedValues() {
    Assert.That((int)PkmFormat.Etc1Rgb, Is.EqualTo(0));
    Assert.That((int)PkmFormat.Etc2Rgb, Is.EqualTo(1));
    Assert.That((int)PkmFormat.Etc2RgbA1, Is.EqualTo(2));
    Assert.That((int)PkmFormat.Etc2Rgba8, Is.EqualTo(3));
    Assert.That((int)PkmFormat.Etc2R, Is.EqualTo(4));
    Assert.That((int)PkmFormat.Etc2Rg, Is.EqualTo(5));
    Assert.That(Enum.GetValues<PkmFormat>(), Has.Length.EqualTo(6));
  }
}
