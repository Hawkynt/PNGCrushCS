using System;
using System.Linq;
using FileFormat.Apng;

namespace FileFormat.Apng.Tests;

[TestFixture]
public sealed class ApngActlTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesFields() {
    var original = new ApngActl(42, 7);
    var buffer = new byte[ApngActl.StructSize];
    original.WriteTo(buffer);
    var restored = ApngActl.ReadFrom(buffer);

    Assert.That(restored.NumFrames, Is.EqualTo(42));
    Assert.That(restored.NumPlays, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_Covers8Bytes() {
    var map = ApngActl.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);

    Assert.That(totalSize, Is.EqualTo(ApngActl.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is8() {
    Assert.That(ApngActl.StructSize, Is.EqualTo(8));
  }
}
