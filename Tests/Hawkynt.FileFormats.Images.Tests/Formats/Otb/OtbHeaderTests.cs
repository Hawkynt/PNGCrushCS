using System;
using System.Linq;
using FileFormat.Otb;
using FileFormat.Core;

namespace FileFormat.Otb.Tests;

[TestFixture]
public sealed class OtbHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new OtbHeader(0x00, 128, 64, 0x01);
    Span<byte> buffer = stackalloc byte[OtbHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = OtbHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = OtbHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(OtbHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is4() {
    Assert.That(OtbHeader.StructSize, Is.EqualTo(4));
  }
}
