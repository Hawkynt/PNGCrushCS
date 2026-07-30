using System;
using FileFormat.Msp;

namespace FileFormat.Msp.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MspVersion_HasExpectedValues() {
    Assert.That((int)MspVersion.V1, Is.EqualTo(1));
    Assert.That((int)MspVersion.V2, Is.EqualTo(2));

    var values = Enum.GetValues<MspVersion>();
    Assert.That(values, Has.Length.EqualTo(2));
  }
}
