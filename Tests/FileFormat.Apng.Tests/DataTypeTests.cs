using System;
using FileFormat.Apng;

namespace FileFormat.Apng.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ApngDisposeOp_HasExpectedValues() {
    Assert.That((byte)ApngDisposeOp.None, Is.EqualTo(0));
    Assert.That((byte)ApngDisposeOp.Background, Is.EqualTo(1));
    Assert.That((byte)ApngDisposeOp.Previous, Is.EqualTo(2));
    Assert.That(Enum.GetValues<ApngDisposeOp>(), Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ApngBlendOp_HasExpectedValues() {
    Assert.That((byte)ApngBlendOp.Source, Is.EqualTo(0));
    Assert.That((byte)ApngBlendOp.Over, Is.EqualTo(1));
    Assert.That(Enum.GetValues<ApngBlendOp>(), Has.Length.EqualTo(2));
  }
}
