using System;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for the JPEG XL SizeHeader encoding (ISO/IEC 18181-1 §3.6.2).
/// The existing JpegXlSizeHeaderTests cover encode→decode round-trips (mutually consistent
/// encoder/decoder pair) but those don't prove byte-level conformance to the spec.
/// These tests assert the actual byte output matches what a spec-compliant encoder
/// would produce for known dimensions, so a mismatch with libjxl/j40 would surface here
/// rather than silently in the wild.
/// </summary>
[TestFixture]
public sealed class JpegXlSizeHeaderConformanceTests {

  // ============================================================
  // Small-encoding byte conformance
  // (small flag = 0, 5-bit height_div8, 3-bit ratio; LSB-first)
  // ============================================================

  [Test]
  [Category("Unit")]
  public void Encode_8x8_Square_ProducesSpecBytes() {
    // Per libjxl spec: small=1 (bit value 1, 1 bit), height_div8=0 (5 bits),
    // ratio=1 square (3 bits) → 9 bits, LSB-first.
    // bit 0 = 1 (small)
    // bits 1..5 = 00000 (height_div8=0)
    // bits 6..8 = 001 (ratio=1, LSB-first → bit 6=1)
    // Byte 0 = 0b01000001 = 0x41, byte 1 = 0x00.
    // Verified against djxl on libjxl/testdata/jxl/boxes/square-extended-size-container.jxl
    // whose codestream begins with FF 0A 41 06 ... (the 41 byte = exactly this encoding).
    var bytes = JpegXlSizeHeader.Encode(8, 8);
    Assert.That(bytes, Is.EqualTo(new byte[] { 0x41, 0x00 }));
  }

  [Test]
  [Category("Unit")]
  public void Encode_256x128_DoubleAspect_ProducesSpecBytes() {
    // Per libjxl spec: small=1, height_div8=15, ratio=7 (2:1)
    // bit 0 = 1 (small flag, per spec)
    // bits 1..5 = LSB-first(15) = 1,1,1,1,0 (MSB of 15 in 5-bit field is 0)
    // bits 6..8 = LSB-first(7) = 1,1,1
    // Byte 0 packs bits 7..0 = 1,1,0,1,1,1,1,1 = 0b11011111 = 0xDF
    // Byte 1 = bit 8 = 1 → 0x01
    var bytes = JpegXlSizeHeader.Encode(256, 128);
    Assert.That(bytes, Is.EqualTo(new byte[] { 0xDF, 0x01 }));
  }

  // ============================================================
  // Decode hand-crafted spec-conformant byte sequences.
  // These bytes are what a libjxl/j40-class encoder would write
  // for the given dimensions. Decoding them must produce the
  // expected width/height — otherwise our SizeHeader parser is
  // not actually spec-conformant.
  // ============================================================

  [Test]
  [Category("Unit")]
  public void Decode_SpecBytes_ForSmall_8x8_ReturnsCorrectDimensions() {
    // Crafted from spec: 8x8 square in small encoding.
    var (w, h, _) = JpegXlSizeHeader.Decode(new byte[] { 0x41, 0x00 });
    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(8));
      Assert.That(h, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_SpecBytes_ForSmall_256x128_ReturnsCorrectDimensions() {
    // Crafted from spec: 256x128 (2:1 aspect, small=1).
    var (w, h, _) = JpegXlSizeHeader.Decode(new byte[] { 0xDF, 0x01 });
    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(256));
      Assert.That(h, Is.EqualTo(128));
    });
  }

  /// <summary>
  /// The first two SizeHeader bytes of a 40x24 image as libjxl writes it, via ImageMagick.
  /// </summary>
  /// <remarks>
  /// 40:24 is 5:3, which is not one of the seven ratios the small header can name, so the encoder
  /// writes ratio 0 and then spells the width out — five more bits of eighths, the same way it just
  /// spelled out the height. The decoder read the ratio, saw 0, and returned the height as the
  /// width, so this file measured 24x24; it also left the bit reader three bits short of where the
  /// image metadata begins. Round-trip tests could not see it, because nothing was writing this form
  /// either.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void Decode_SpecBytes_ForSmall_40x24_WithNoNameableRatio() {
    var (w, h, _) = JpegXlSizeHeader.Decode([0x05, 0xC8]);
    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(40));
      Assert.That(h, Is.EqualTo(24));
    });
  }

  /// <summary>The width spelled out in the small header is five bits, so it stops at 256.</summary>
  [Test]
  [Category("Unit")]
  [TestCase(40, 24)]
  [TestCase(32, 24)]
  [TestCase(24, 40)]
  [TestCase(8, 256)]
  [TestCase(256, 8)]
  public void Encode_ThenDecode_SmallWithNoNameableRatio(int width, int height) {
    var (w, h, _) = JpegXlSizeHeader.Decode(JpegXlSizeHeader.Encode(width, height));
    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(width));
      Assert.That(h, Is.EqualTo(height));
    });
  }

  // ============================================================
  // Full codestream-prefix tests: 0xFF 0x0A signature + SizeHeader.
  // This is the leading byte sequence of every real JPEG XL file.
  // Our reader must at least extract dimensions from this prefix.
  // ============================================================

  [Test]
  [Category("Unit")]
  public void Decode_AfterFf0aSignature_ExtractsCorrectDimensions() {
    // Real JPEG XL codestream layout: FF 0A | SizeHeader | ImageMetadata | …
    // We construct just FF 0A + SizeHeader for an 8x8 square and verify our
    // SizeHeader decoder advances the right number of bytes past the signature.
    var sizeHeader = JpegXlSizeHeader.Encode(8, 8);
    var codestream = new byte[2 + sizeHeader.Length];
    codestream[0] = 0xFF;
    codestream[1] = 0x0A;
    Array.Copy(sizeHeader, 0, codestream, 2, sizeHeader.Length);

    // Skip the signature; decode the SizeHeader.
    var (w, h, bytesConsumed) = JpegXlSizeHeader.Decode(codestream.AsSpan(2));

    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(8));
      Assert.That(h, Is.EqualTo(8));
      Assert.That(bytesConsumed, Is.GreaterThan(0));
      Assert.That(bytesConsumed, Is.LessThanOrEqualTo(sizeHeader.Length));
    });
  }

  // ============================================================
  // Aspect-ratio table conformance (spec §3.6.2 Table 3)
  // ratio=1 → 1:1, =2 → 12:10, =3 → 4:3, =4 → 3:2, =5 → 16:9, =6 → 5:4, =7 → 2:1
  // ============================================================

  [TestCase(8, 8, TestName = "ratio=1 (1:1)")]
  [TestCase(96, 80, TestName = "ratio=2 (12:10) — 80*12/10=96")]
  [TestCase(128, 96, TestName = "ratio=3 (4:3) — 96*4/3=128")]
  [TestCase(48, 32, TestName = "ratio=4 (3:2) — 32*3/2=48")]
  [TestCase(256, 144, TestName = "ratio=5 (16:9) — 144*16/9=256")]
  [TestCase(40, 32, TestName = "ratio=6 (5:4) — 32*5/4=40")]
  [TestCase(16, 8, TestName = "ratio=7 (2:1) — 8*2=16")]
  [Category("Unit")]
  public void EncodeDecode_AllSmallRatios_RoundTripCorrectly(int width, int height) {
    var encoded = JpegXlSizeHeader.Encode(width, height);
    var (w, h, _) = JpegXlSizeHeader.Decode(encoded);
    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(width), $"width mismatch for {width}x{height}");
      Assert.That(h, Is.EqualTo(height), $"height mismatch for {width}x{height}");
    });
  }
}
