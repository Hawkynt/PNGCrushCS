using System;
using System.Linq;
using FileFormat.Qrt;
using FileFormat.Core;

namespace FileFormat.Qrt.Tests;

[TestFixture]
public sealed class QrtHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new QrtHeader(640, 480);
    Span<byte> buffer = stackalloc byte[QrtHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = QrtHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = QrtHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(QrtHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is10() {
    Assert.That(QrtHeader.StructSize, Is.EqualTo(10));
  }
}
