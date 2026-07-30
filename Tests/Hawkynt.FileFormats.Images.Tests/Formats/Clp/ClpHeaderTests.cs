using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Clp;
using FileFormat.Core;

namespace FileFormat.Clp.Tests;

[TestFixture]
public sealed class ClpHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new ClpHeader(
      FileId: ClpHeader.FileIdValue,
      FormatCount: 3
    );

    var buffer = new byte[ClpHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = ClpHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = ClpHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(ClpHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is4() {
    Assert.That(ClpHeader.StructSize, Is.EqualTo(4));
  }
}
