using System;
using System.Linq;
using FileFormat.Fbm;
using FileFormat.Core;

namespace FileFormat.Fbm.Tests;

[TestFixture]
public sealed class FbmHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is256() {
    Assert.That(FbmHeader.StructSize, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new FbmHeader(
      Magic: FbmHeader.MagicBytes,
      Cols: 640,
      Rows: 480,
      Bands: 3,
      Bits: 8,
      PhysBits: 8,
      RowLen: 1936,
      PlnLen: 929280,
      ClrLen: 0,
      Aspect: 1.0,
      Title: "Test"
    );

    var buffer = new byte[FbmHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = FbmHeader.ReadFrom(buffer.AsSpan());

    Assert.That(parsed.Cols, Is.EqualTo(original.Cols));
    Assert.That(parsed.Rows, Is.EqualTo(original.Rows));
    Assert.That(parsed.Bands, Is.EqualTo(original.Bands));
    Assert.That(parsed.Bits, Is.EqualTo(original.Bits));
    Assert.That(parsed.PhysBits, Is.EqualTo(original.PhysBits));
    Assert.That(parsed.RowLen, Is.EqualTo(original.RowLen));
    Assert.That(parsed.PlnLen, Is.EqualTo(original.PlnLen));
    Assert.That(parsed.ClrLen, Is.EqualTo(original.ClrLen));
    Assert.That(parsed.Aspect, Is.EqualTo(original.Aspect));
    Assert.That(parsed.Title, Is.EqualTo(original.Title));
    Assert.That(parsed.Magic, Is.EqualTo(original.Magic));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasExpectedEntries() {
    var map = FbmHeader.GetFieldMap();

    // 11 fields: magic, cols, rows, bands, bits, physbits, rowlen, plnlen, clrlen, aspect, title
    // plus 1 filler for reserved area = potentially more, but at least the 11 field entries
    Assert.That(map.Length, Is.GreaterThanOrEqualTo(11));
  }

  [Test]
  [Category("Unit")]
  public void MagicBytes_IsCorrect() {
    Assert.That(FbmHeader.MagicBytes, Is.EqualTo(new byte[] { (byte)'%', (byte)'b', (byte)'i', (byte)'t', (byte)'m', (byte)'a', (byte)'p', 0 }));
  }
}
