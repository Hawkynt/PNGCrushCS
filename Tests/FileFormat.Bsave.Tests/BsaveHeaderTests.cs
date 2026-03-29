using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Bsave;
using FileFormat.Core;

namespace FileFormat.Bsave.Tests;

[TestFixture]
public sealed class BsaveHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new BsaveHeader(
      Magic: BsaveHeader.MagicValue,
      Segment: 0xA000,
      Offset: 0x0000,
      Length: 64000
    );

    var buffer = new byte[BsaveHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = BsaveHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = BsaveHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(BsaveHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is7() {
    Assert.That(BsaveHeader.StructSize, Is.EqualTo(7));
  }
}
