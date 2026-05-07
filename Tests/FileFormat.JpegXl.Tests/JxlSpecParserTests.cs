using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for the new bit-level parsers (ISO/IEC 18181-1 §3.6.3, §3.6.5).
/// Validates ImageMetadata + FrameHeader against hand-computed bit patterns.
///
/// The test approach: construct minimal valid bit sequences from the spec, decode
/// them with our parsers, and verify field values. The spec itself is the reference;
/// no external libjxl test fixtures required.
/// </summary>
[TestFixture]
public sealed class JxlSpecParserTests {

  // ============================================================
  // ImageMetadata all_default fast path
  // ============================================================

  [Test]
  public void ImageMetadata_AllDefaultBit_ProducesDefaultBundle() {
    // all_default = 1 (single bit), packed LSB-first: byte 0 = 0x01
    var data = new byte[] { 0x01 };
    var reader = new JxlBitReader(data, 0);
    var meta = JxlImageMetadata.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(meta.AllDefault, Is.True);
      Assert.That(meta.Orientation, Is.EqualTo(JxlOrientation.Identity));
      Assert.That(meta.NumExtraChannels, Is.EqualTo(0u));
      Assert.That(meta.XybEncoded, Is.True);
      Assert.That(meta.BitDepth.FloatingPoint, Is.False);
      Assert.That(meta.BitDepth.BitsPerSample, Is.EqualTo(8u));
      Assert.That(meta.Modular16BitBuffers, Is.True);
      Assert.That(meta.ColorEncoding.AllDefault, Is.True);
      Assert.That(meta.ToneMapping.AllDefault, Is.True);
      Assert.That(meta.ToneMapping.IntensityTarget, Is.EqualTo(255f));
    });
  }

  // ============================================================
  // FrameHeader all_default fast path
  // ============================================================

  [Test]
  public void FrameHeader_AllDefaultBit_ProducesDefaultBundle() {
    var data = new byte[] { 0x01 };
    var reader = new JxlBitReader(data, 0);
    var frame = JxlSpecFrameHeader.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(frame.AllDefault, Is.True);
      Assert.That(frame.FrameType, Is.EqualTo(JxlFrameType.Regular));
      Assert.That(frame.Encoding, Is.EqualTo(JxlFrameEncoding.VarDct));
      Assert.That(frame.IsLast, Is.True);
      Assert.That(frame.NumPasses, Is.EqualTo(1u));
    });
  }

  // ============================================================
  // SizeHeader → ImageMetadata chain via JxlBitReader
  // ============================================================

  [Test]
  public void SizeHeader_Then_ImageMetadata_AllDefault_AdvancesBitPositionCorrectly() {
    // 8x8 SizeHeader (small=1 per libjxl spec, h_div8=0, ratio=1) + ImageMetadata all_default=1.
    // SizeHeader = 9 bits: 1|00000|001 packed LSB-first → byte 0 = 0x41, byte 1 bit 0 = 0.
    // Then ImageMetadata all_default at bit 9 = byte 1 bit 1.
    // Putting the all_default bit (=1) at bit 1 of byte 1 sets byte 1 = 0x02.
    var data = new byte[] { 0x41, 0x02 };
    var reader = new JxlBitReader(data, 0);

    var (w, h) = JxlSizeHeader.Decode(reader);
    var meta = JxlImageMetadata.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(8));
      Assert.That(h, Is.EqualTo(8));
      Assert.That(meta.AllDefault, Is.True);
    });
  }

  [Test]
  public void SizeHeader_Then_ImageMetadata_Then_FrameHeader_AllDefault_Decodes() {
    // 8x8 SizeHeader = 9 bits ending at bit 9 (per libjxl spec: small=1).
    // ImageMetadata all_default = 1 bit at position 9.
    // FrameHeader all_default = 1 bit at position 10.
    //
    // bit 0..8 = SizeHeader (1|00000|001)
    // bit 9 = ImageMetadata all_default (=1)
    // bit 10 = FrameHeader all_default (=1)
    //
    // byte 0 (bits 0..7) = 0b01000001 = 0x41
    // byte 1 (bits 8..15) = bit 8 = 0, bit 9 = 1, bit 10 = 1, rest 0 → 0b00000110 = 0x06
    var data = new byte[] { 0x41, 0x06 };
    var reader = new JxlBitReader(data, 0);

    var (w, h) = JxlSizeHeader.Decode(reader);
    var meta = JxlImageMetadata.Decode(reader);
    var frame = JxlSpecFrameHeader.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(w, Is.EqualTo(8));
      Assert.That(h, Is.EqualTo(8));
      Assert.That(meta.AllDefault, Is.True);
      Assert.That(frame.AllDefault, Is.True);
    });
  }

  // ============================================================
  // BitDepth bundle (§3.6.3)
  // ============================================================

  [Test]
  public void BitDepth_Default_Integer8Bit() {
    // float_sample = 0 (1 bit), bits_per_sample u32 selector 0 (2 bits) → returns 8.
    // Total: 3 bits = 0|00 → byte 0 = 0x00.
    var data = new byte[] { 0x00 };
    var reader = new JxlBitReader(data, 0);
    var bd = JxlBitDepth.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(bd.FloatingPoint, Is.False);
      Assert.That(bd.BitsPerSample, Is.EqualTo(8u));
    });
  }

  [Test]
  public void BitDepth_Integer16Bit_DecodesCorrectly() {
    // ReadU32(8, 0, 10, 0, 12, 0, 1, 6): selectors map to {8, 10, 12, 1+u(6)}. To get 16 we
    // use selector 3 with 6-bit payload 15 (since 1 + 15 = 16).
    // Stream layout (LSB-first):
    //   bit 0    = 0  (float_sample = false → integer)
    //   bits 1-2 = 11 (selector 3 LSB-first → bit1=1, bit2=1)
    //   bits 3-8 = 6-bit value 15 = 001111 LSB-first → bits 3..8 = 1,1,1,1,0,0
    // Byte 0 (bits 7..0) = bit7=0, bit6=1, bit5=1, bit4=1, bit3=1, bit2=1, bit1=1, bit0=0
    //                    = 0b01111110 = 0x7E
    // Byte 1 (bit 8 only) = 0 → 0x00
    var data = new byte[] { 0x7E, 0x00 };
    var reader = new JxlBitReader(data, 0);
    var bd = JxlBitDepth.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(bd.FloatingPoint, Is.False);
      Assert.That(bd.BitsPerSample, Is.EqualTo(16u));
    });
  }

  // ============================================================
  // Edge: zero-length data must not crash
  // ============================================================

  [Test]
  public void ImageMetadata_EmptyData_Throws() {
    var reader = new JxlBitReader(Array.Empty<byte>(), 0);
    Assert.Throws<InvalidOperationException>(() => JxlImageMetadata.Decode(reader));
  }

  // ============================================================
  // ColorEncoding all_default
  // ============================================================

  [Test]
  public void ColorEncoding_AllDefaultBit_ProducesSrgbDefaults() {
    // all_default = 1 (single bit) → byte 0 = 0x01
    var data = new byte[] { 0x01 };
    var reader = new JxlBitReader(data, 0);
    var ce = JxlColorEncoding.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(ce.AllDefault, Is.True);
      Assert.That(ce.ColorSpace, Is.EqualTo(0u));   // RGB
      Assert.That(ce.WhitePoint, Is.EqualTo(1u));   // D65
      Assert.That(ce.Primaries, Is.EqualTo(1u));    // sRGB
      Assert.That(ce.TransferFunction, Is.EqualTo(13u)); // sRGB transfer
    });
  }
}
