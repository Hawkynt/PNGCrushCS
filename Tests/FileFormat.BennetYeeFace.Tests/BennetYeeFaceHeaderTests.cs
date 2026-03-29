using System;
using System.Linq;
using FileFormat.BennetYeeFace;
using FileFormat.Core;

namespace FileFormat.BennetYeeFace.Tests;

[TestFixture]
public sealed class BennetYeeFaceHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new BennetYeeFaceHeader(320, 200);
    Span<byte> buffer = stackalloc byte[BennetYeeFaceHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = BennetYeeFaceHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = BennetYeeFaceHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(BennetYeeFaceHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is4() {
    Assert.That(BennetYeeFaceHeader.StructSize, Is.EqualTo(4));
  }
}
