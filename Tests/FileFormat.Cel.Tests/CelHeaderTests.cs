using System;
using System.Linq;
using FileFormat.Cel;
using FileFormat.Core;

namespace FileFormat.Cel.Tests;

[TestFixture]
public sealed class CelHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is32() {
    Assert.That(CelHeader.StructSize, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new CelHeader(CelHeader.ExpectedMagic, 0x04, 8, 100, 200, 10, 20);
    var buffer = new byte[CelHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = CelHeader.ReadFrom(buffer.AsSpan());

    Assert.That(parsed.Magic, Is.EqualTo(CelHeader.ExpectedMagic));
    Assert.That(parsed.Mark, Is.EqualTo(0x04));
    Assert.That(parsed.BitsPerPixel, Is.EqualTo(8));
    Assert.That(parsed.Width, Is.EqualTo(100));
    Assert.That(parsed.Height, Is.EqualTo(200));
    Assert.That(parsed.XOffset, Is.EqualTo(10));
    Assert.That(parsed.YOffset, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = CelHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(CelHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ExpectedMagic_MatchesKiSS() {
    var bytes = BitConverter.GetBytes(CelHeader.ExpectedMagic);
    Assert.That(bytes[0], Is.EqualTo((byte)'K'));
    Assert.That(bytes[1], Is.EqualTo((byte)'i'));
    Assert.That(bytes[2], Is.EqualTo((byte)'S'));
    Assert.That(bytes[3], Is.EqualTo((byte)'S'));
  }
}
