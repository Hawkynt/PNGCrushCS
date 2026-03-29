using System;
using System.Linq;
using FileFormat.AliasPix;
using FileFormat.Core;

namespace FileFormat.AliasPix.Tests;

[TestFixture]
public sealed class AliasPixHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is10() {
    Assert.That(AliasPixHeader.StructSize, Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new AliasPixHeader(640, 480, 10, 20, 24);
    Span<byte> buffer = stackalloc byte[AliasPixHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = AliasPixHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = AliasPixHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(AliasPixHeader.StructSize));
  }
}
