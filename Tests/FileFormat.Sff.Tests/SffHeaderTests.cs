using System;
using System.Linq;
using FileFormat.Sff;
using FileFormat.Core;

namespace FileFormat.Sff.Tests;

[TestFixture]
public sealed class SffHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is12() {
    Assert.That(SffHeader.StructSize, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new SffHeader(0x53, 0x66, 0x66, 0x66, 1, 0, 100, 3, 12);
    Span<byte> buffer = stackalloc byte[SffHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = SffHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = SffHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(SffHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasEntries() {
    var map = SffHeader.GetFieldMap();
    Assert.That(map.Length, Is.GreaterThan(0));
  }
}
