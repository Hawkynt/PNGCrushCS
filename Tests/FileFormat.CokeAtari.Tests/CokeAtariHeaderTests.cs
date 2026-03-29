using System;
using System.Linq;
using FileFormat.CokeAtari;
using FileFormat.Core;

namespace FileFormat.CokeAtari.Tests;

[TestFixture]
public sealed class CokeAtariHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new CokeAtariHeader(320, 200);
    Span<byte> buffer = stackalloc byte[CokeAtariHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = CokeAtariHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = CokeAtariHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(CokeAtariHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is4() {
    Assert.That(CokeAtariHeader.StructSize, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void BigEndian_ByteOrder() {
    var header = new CokeAtariHeader(0x0140, 0x00C8);
    var buffer = new byte[CokeAtariHeader.StructSize];
    header.WriteTo(buffer);

    Assert.That(buffer[0], Is.EqualTo(0x01));
    Assert.That(buffer[1], Is.EqualTo(0x40));
    Assert.That(buffer[2], Is.EqualTo(0x00));
    Assert.That(buffer[3], Is.EqualTo(0xC8));
  }
}
