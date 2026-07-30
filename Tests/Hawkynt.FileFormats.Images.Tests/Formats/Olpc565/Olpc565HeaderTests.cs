using System;
using System.Linq;
using FileFormat.Olpc565;
using FileFormat.Core;

namespace FileFormat.Olpc565.Tests;

[TestFixture]
public sealed class Olpc565HeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new Olpc565Header(320, 240);
    Span<byte> buffer = stackalloc byte[Olpc565Header.StructSize];
    original.WriteTo(buffer);
    var parsed = Olpc565Header.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = Olpc565Header.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(Olpc565Header.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is4() {
    Assert.That(Olpc565Header.StructSize, Is.EqualTo(4));
  }
}
