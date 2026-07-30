using System;
using System.Linq;
using FileFormat.Sff;
using FileFormat.Core;

namespace FileFormat.Sff.Tests;

[TestFixture]
public sealed class SffPageHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is18() {
    Assert.That(SffPageHeader.StructSize, Is.EqualTo(18));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new SffPageHeader(256, 1, 0, 0, 0, 1728, 1145, 100, 500);
    Span<byte> buffer = stackalloc byte[SffPageHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = SffPageHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = SffPageHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(SffPageHeader.StructSize));
  }
}
