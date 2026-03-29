using System;
using System.Linq;
using FileFormat.Cmu;
using FileFormat.Core;

namespace FileFormat.Cmu.Tests;

[TestFixture]
public sealed class CmuHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new CmuHeader(640, 480);
    Span<byte> buffer = stackalloc byte[CmuHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = CmuHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = CmuHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(CmuHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is8() {
    Assert.That(CmuHeader.StructSize, Is.EqualTo(8));
  }
}
