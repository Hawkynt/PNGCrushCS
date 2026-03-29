using System;
using System.Linq;
using FileFormat.NokiaPictureMessage;
using FileFormat.Core;

namespace FileFormat.NokiaPictureMessage.Tests;

[TestFixture]
public sealed class NokiaPictureMessageHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new NokiaPictureMessageHeader(0x00, 72, 28, 0x01);
    Span<byte> buffer = stackalloc byte[NokiaPictureMessageHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = NokiaPictureMessageHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = NokiaPictureMessageHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(NokiaPictureMessageHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is4() {
    Assert.That(NokiaPictureMessageHeader.StructSize, Is.EqualTo(4));
  }
}
