using System;
using System.Linq;
using FileFormat.SpookySpritesFalcon;
using FileFormat.Core;

namespace FileFormat.SpookySpritesFalcon.Tests;

[TestFixture]
public sealed class SpookySpritesFalconHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new SpookySpritesFalconHeader(320, 200);
    Span<byte> buffer = stackalloc byte[SpookySpritesFalconHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = SpookySpritesFalconHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = SpookySpritesFalconHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(SpookySpritesFalconHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is4() {
    Assert.That(SpookySpritesFalconHeader.StructSize, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void BigEndian_ByteOrder() {
    var header = new SpookySpritesFalconHeader(0x0140, 0x00C8);
    var buffer = new byte[SpookySpritesFalconHeader.StructSize];
    header.WriteTo(buffer);

    Assert.That(buffer[0], Is.EqualTo(0x01));
    Assert.That(buffer[1], Is.EqualTo(0x40));
    Assert.That(buffer[2], Is.EqualTo(0x00));
    Assert.That(buffer[3], Is.EqualTo(0xC8));
  }
}
