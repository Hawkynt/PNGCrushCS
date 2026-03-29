using System;
using System.Linq;
using FileFormat.Uhdr;
using FileFormat.Core;

namespace FileFormat.Uhdr.Tests;

[TestFixture]
public sealed class UhdrHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() {
    Assert.That(UhdrHeader.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new UhdrHeader("UHDR", 1, 0, 640, 480);
    var buffer = new byte[UhdrHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = UhdrHeader.ReadFrom(buffer.AsSpan());
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = UhdrHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(UhdrHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasFiveEntries() {
    var map = UhdrHeader.GetFieldMap();
    Assert.That(map, Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void MagicValue_IsUHDR() {
    Assert.That(UhdrHeader.MagicValue, Is.EqualTo("UHDR"));
  }

  [Test]
  [Category("Unit")]
  public void CurrentVersion_Is1() {
    Assert.That(UhdrHeader.CurrentVersion, Is.EqualTo(1));
  }
}
