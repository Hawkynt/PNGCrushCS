using System;
using FileFormat.Tim;

namespace FileFormat.Tim.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void TimBpp_HasExpectedValues() {
    Assert.That((int)TimBpp.Bpp4, Is.EqualTo(0));
    Assert.That((int)TimBpp.Bpp8, Is.EqualTo(1));
    Assert.That((int)TimBpp.Bpp16, Is.EqualTo(2));
    Assert.That((int)TimBpp.Bpp24, Is.EqualTo(3));

    var values = Enum.GetValues<TimBpp>();
    Assert.That(values, Has.Length.EqualTo(4));
  }
}
