using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class PngSignatureHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new PngSignatureHeader(137, 80, 78, 71, 13, 10, 26, 10);
    Span<byte> buffer = stackalloc byte[PngSignatureHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = PngSignatureHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void Expected_HasCorrectValues() {
    var expected = PngSignatureHeader.Expected;
    Assert.Multiple(() => {
      Assert.That(expected.Byte0, Is.EqualTo(137));
      Assert.That(expected.Byte1, Is.EqualTo(80));
      Assert.That(expected.Byte2, Is.EqualTo(78));
      Assert.That(expected.Byte3, Is.EqualTo(71));
      Assert.That(expected.Byte4, Is.EqualTo(13));
      Assert.That(expected.Byte5, Is.EqualTo(10));
      Assert.That(expected.Byte6, Is.EqualTo(26));
      Assert.That(expected.Byte7, Is.EqualTo(10));
    });
  }

  [Test]
  public void IsValid_ReturnsTrueForValidSignature() {
    var sig = new PngSignatureHeader(137, 80, 78, 71, 13, 10, 26, 10);
    Assert.That(sig.IsValid, Is.True);
  }

  [Test]
  public void IsValid_ReturnsFalseForInvalidSignature() {
    var sig = new PngSignatureHeader(0, 80, 78, 71, 13, 10, 26, 10);
    Assert.That(sig.IsValid, Is.False);
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = PngSignatureHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(PngSignatureHeader.StructSize));
  }

  [Test]
  public void StructSize_Is8() {
    Assert.That(PngSignatureHeader.StructSize, Is.EqualTo(8));
  }
}
