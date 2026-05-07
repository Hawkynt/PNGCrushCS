using System;
using System.Collections.Generic;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for <see cref="JxlFrameQuantizer"/> (ISO/IEC
/// 18181-1 §G.6 / libjxl <c>lib/jxl/quant_weights.cc::DequantMatrices::Decode</c>
/// and <c>lib/jxl/quantizer.cc::Quantizer::Decode</c>).
/// </summary>
[TestFixture]
internal sealed class JxlFrameQuantizerTests {

  // ============================================================
  // Test helper: LSB-first bit packer matching JxlBitReader's
  // wire ordering (same approach used in JxlBlockContextMapTests).
  // ============================================================

  private sealed class BitsBuilder {
    private readonly List<byte> _bytes = new();
    private byte _current;
    private int _bitInByte;

    public BitsBuilder Add(uint value, int nBits) {
      for (var i = 0; i < nBits; ++i) {
        var bit = (value >> i) & 1u;
        _current |= (byte)(bit << _bitInByte);
        ++_bitInByte;
        if (_bitInByte == 8) {
          _bytes.Add(_current);
          _current = 0;
          _bitInByte = 0;
        }
      }
      return this;
    }

    public byte[] ToBytes() {
      var copy = new List<byte>(_bytes);
      if (_bitInByte != 0)
        copy.Add(_current);
      // Pad to a comfortable size so JxlBitReader has refill room.
      while (copy.Count < 32)
        copy.Add(0);
      return copy.ToArray();
    }
  }

  // ============================================================
  // ReadGlobalScale
  //
  // QuantizerParams::VisitFields (libjxl quantizer.cc):
  //   global_scale: U32(BitsOffset(11,1), BitsOffset(11,2049),
  //                     BitsOffset(12,4097), BitsOffset(16,8193))
  //   quant_dc:     U32(Val(16), BitsOffset(5,1),
  //                     BitsOffset(8,1), BitsOffset(16,1))
  //
  // U32 wire format: 2-bit selector, then payload of (u0|u1|u2|u3) bits.
  // ============================================================

  [Test]
  public void ReadGlobalScale_DefaultValueOne() {
    // global_scale = 1: selector 0 (BitsOffset(11, 1) → 1 + payload),
    // payload = 0 (11 bits). quant_dc = 16: selector 0 (Val(16)), no payload.
    var bits = new BitsBuilder()
      .Add(0u, 2)              // global_scale selector = 0
      .Add(0u, 11)             // payload = 0  → value = 1 + 0 = 1
      .Add(0u, 2)              // quant_dc selector = 0 (Val(16))
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var scale = JxlFrameQuantizer.ReadGlobalScale(reader);

    Assert.That(scale, Is.EqualTo(1));
  }

  [Test]
  public void ReadGlobalScale_Selector0_MaximumPayload() {
    // global_scale: selector 0, payload = 2047 (max 11-bit) → value = 1 + 2047 = 2048
    // quant_dc: selector 0 (Val(16)), no payload
    var bits = new BitsBuilder()
      .Add(0u, 2)
      .Add(2047u, 11)
      .Add(0u, 2)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var scale = JxlFrameQuantizer.ReadGlobalScale(reader);

    Assert.That(scale, Is.EqualTo(2048));
  }

  [Test]
  public void ReadGlobalScale_Selector1_AddsOffset2049() {
    // global_scale: selector 1 (BitsOffset(11, 2049)), payload = 0
    //   → value = 2049 + 0 = 2049
    var bits = new BitsBuilder()
      .Add(1u, 2)              // selector 1
      .Add(0u, 11)             // payload = 0
      .Add(0u, 2)              // quant_dc Val(16)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var scale = JxlFrameQuantizer.ReadGlobalScale(reader);

    Assert.That(scale, Is.EqualTo(2049));
  }

  [Test]
  public void ReadGlobalScale_Selector2_AddsOffset4097() {
    // global_scale: selector 2 (BitsOffset(12, 4097)), payload = 100
    //   → value = 4097 + 100 = 4197
    var bits = new BitsBuilder()
      .Add(2u, 2)              // selector 2
      .Add(100u, 12)           // payload = 100
      .Add(0u, 2)              // quant_dc Val(16)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var scale = JxlFrameQuantizer.ReadGlobalScale(reader);

    Assert.That(scale, Is.EqualTo(4197));
  }

  [Test]
  public void ReadGlobalScale_Selector3_AddsOffset8193() {
    // global_scale: selector 3 (BitsOffset(16, 8193)), payload = 1234
    //   → value = 8193 + 1234 = 9427
    var bits = new BitsBuilder()
      .Add(3u, 2)              // selector 3
      .Add(1234u, 16)          // payload = 1234
      .Add(0u, 2)              // quant_dc Val(16)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var scale = JxlFrameQuantizer.ReadGlobalScale(reader);

    Assert.That(scale, Is.EqualTo(9427));
  }

  [Test]
  public void ReadGlobalScale_AdvancesReaderPastQuantDc() {
    // After ReadGlobalScale, the reader should be positioned past BOTH
    // global_scale AND quant_dc. We craft a stream with a sentinel byte
    // immediately following the QuantizerParams bundle and verify we can
    // read it next.
    //
    // Layout:
    //   global_scale (selector 0, default 1)   = 2 + 11 = 13 bits
    //   quant_dc     (selector 0, Val(16))     = 2       bits  (no payload)
    // Total = 15 bits. The 16th bit + 8-bit sentinel that follows should be
    // readable.
    var bits = new BitsBuilder()
      .Add(0u, 2)              // global_scale selector 0
      .Add(0u, 11)             // payload 0
      .Add(0u, 2)              // quant_dc Val(16)
      // Sentinel: 1 bit set, then the byte 0xAB.
      .Add(1u, 1)              // 1-bit pad to round to 16 bits — also acts as marker
      .Add(0xABu, 8)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var scale = JxlFrameQuantizer.ReadGlobalScale(reader);

    Assert.That(scale, Is.EqualTo(1));
    Assert.That(reader.BitsRead, Is.EqualTo(15),
      "ReadGlobalScale should consume exactly the QuantizerParams bundle (13+2 bits).");

    // After consuming the bundle the next 1 bit should be the marker, then 0xAB.
    Assert.That(reader.ReadBits(1), Is.EqualTo(1u));
    Assert.That(reader.ReadBits(8), Is.EqualTo(0xABu));
  }

  [Test]
  public void ReadGlobalScale_QuantDcSelector1Variant() {
    // Sanity that quant_dc consumes its bits in non-default branches too.
    //   global_scale: selector 0, value = 1     → 13 bits
    //   quant_dc:     selector 1 (BitsOffset(5,1)), payload = 7
    //     → value = 8, bits consumed = 2 + 5 = 7
    // Total = 20 bits. After that, the reader should pick up our marker.
    var bits = new BitsBuilder()
      .Add(0u, 2)
      .Add(0u, 11)
      .Add(1u, 2)              // quant_dc selector 1
      .Add(7u, 5)              // payload 7
      .Add(0xCDu, 8)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var scale = JxlFrameQuantizer.ReadGlobalScale(reader);

    Assert.That(scale, Is.EqualTo(1));
    Assert.That(reader.BitsRead, Is.EqualTo(20));
    Assert.That(reader.ReadBits(8), Is.EqualTo(0xCDu));
  }

  [Test]
  public void ReadGlobalScale_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(() => JxlFrameQuantizer.ReadGlobalScale(null!));
  }

  // ============================================================
  // ReadDequantMatrices
  // ============================================================

  [Test]
  public void ReadDequantMatrices_AllDefaultBit_ReturnsDefaultTables() {
    // libjxl `DequantMatrices::Decode` reads 1-bit `all_default` first; when
    // 1 it returns the libjxl default XYB tables and reads no further bits.
    var bits = new BitsBuilder().Add(1u, 1).ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var set = JxlFrameQuantizer.ReadDequantMatrices(reader);

    Assert.That(set, Is.Not.Null);
    Assert.That(set.Tables.Length, Is.EqualTo(3));
    Assert.That(reader.BitsRead, Is.EqualTo(1),
      "all_default=1 short-circuits — only the 1-bit flag is read.");

    var reference = JxlVarDctQuant.DefaultTableSetXyb();
    for (var c = 0; c < reference.Tables.Length; c++) {
      Assert.That(set.Tables[c].Weights, Is.EqualTo(reference.Tables[c].Weights).AsCollection,
        $"Channel {c} weights should match the libjxl default tables.");
    }
  }

  [Test]
  public void ReadDequantMatrices_AllLibraryMode_ReturnsDefaultTables() {
    // With all_default=0, libjxl loops through 17 quant tables. Library mode
    // (= 0) reads no extra bits, so the bitstream is 1 (all_default=0) +
    // 17 × 3 = 52 bits, all zeros except for the first which is 0.
    var builder = new BitsBuilder().Add(0u, 1); // all_default=0
    for (var t = 0; t < 17; t++)
      builder.Add(0u, 3);       // mode = 0 (Library)
    var bits = builder.ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var set = JxlFrameQuantizer.ReadDequantMatrices(reader);

    Assert.That(set, Is.Not.Null);
    Assert.That(set.Tables.Length, Is.EqualTo(3));
    Assert.That(reader.BitsRead, Is.EqualTo(52),
      "all_default=0 + 17 Library-mode dispatches should consume 1 + 51 bits.");

    var reference = JxlVarDctQuant.DefaultTableSetXyb();
    for (var c = 0; c < reference.Tables.Length; c++) {
      Assert.That(set.Tables[c].Weights, Is.EqualTo(reference.Tables[c].Weights).AsCollection,
        $"Channel {c} weights should match the libjxl default tables.");
    }
  }

  [Test]
  public void ReadDequantMatrices_AllLibrary_MatchesIndependentDefaultCall() {
    var builder = new BitsBuilder().Add(0u, 1);
    for (var t = 0; t < 17; t++)
      builder.Add(0u, 3);
    var bits = builder.ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var fromReader = JxlFrameQuantizer.ReadDequantMatrices(reader);

    var reference = JxlVarDctQuant.DefaultTableSetXyb();

    Assert.That(fromReader.Tables.Length, Is.EqualTo(reference.Tables.Length));
    for (var c = 0; c < reference.Tables.Length; c++) {
      Assert.That(fromReader.Tables[c].Width, Is.EqualTo(reference.Tables[c].Width));
      Assert.That(fromReader.Tables[c].Height, Is.EqualTo(reference.Tables[c].Height));
      Assert.That(fromReader.Tables[c].Weights, Is.EqualTo(reference.Tables[c].Weights).AsCollection,
        $"Channel {c} weights should match the reference default table.");
    }
  }

  // Modes 1-6 now consume the correct number of bits per the libjxl spec
  // (rather than throwing NotImplementedException). The actual quant table
  // values aren't yet used downstream (DCT8 always falls back to defaults),
  // but bit alignment is preserved so subsequent reads stay in sync. Each
  // test pads the bitstream with enough zero bits for the mode + 16 zero
  // modes in the remaining 16 table slots.

  [Test]
  public void ReadDequantMatrices_NonDefault_Mode1_Identity_ConsumesNineF16() {
    // Mode 1 = Identity: 3 channels × 3 F16 weights = 9 × 16 = 144 bits.
    // Total = 1 (all_default=0) + 3 (mode) + 144 (Identity payload) + 48 (16 × 3-bit Library) = 196.
    var b = new BitsBuilder().Add(0u, 1).Add(1u, 3);
    for (var i = 0; i < 9; ++i) b.Add(0u, 16);
    for (var i = 0; i < 16; ++i) b.Add(0u, 3);
    var reader = new JxlBitReader(b.ToBytes(), 0);

    var set = JxlFrameQuantizer.ReadDequantMatrices(reader);
    Assert.Multiple(() => {
      Assert.That(set, Is.Not.Null);
      Assert.That(reader.BitsRead, Is.EqualTo(1 + 3 + 144 + 48));
    });
  }

  [Test]
  public void ReadDequantMatrices_NonDefault_Mode2_Dct2_ConsumesEighteenF16() {
    // Mode 2 = DCT2x2: 3 channels × 6 F16 = 18 × 16 = 288 bits.
    var b = new BitsBuilder().Add(0u, 1).Add(2u, 3);
    for (var i = 0; i < 18; ++i) b.Add(0u, 16);
    for (var i = 0; i < 16; ++i) b.Add(0u, 3);
    var reader = new JxlBitReader(b.ToBytes(), 0);

    var set = JxlFrameQuantizer.ReadDequantMatrices(reader);
    Assert.Multiple(() => {
      Assert.That(set, Is.Not.Null);
      Assert.That(reader.BitsRead, Is.EqualTo(1 + 3 + 288 + 48));
    });
  }

  [Test]
  public void ReadDequantMatrices_NonDefault_Mode3_Dct4_ConsumesSixF16PlusDctParams() {
    var b = new BitsBuilder().Add(0u, 1).Add(3u, 3);
    for (var i = 0; i < 6; ++i) b.Add(0u, 16);
    b.Add(0u, 4);
    for (var i = 0; i < 3; ++i) b.Add(0u, 16);
    for (var i = 0; i < 16; ++i) b.Add(0u, 3);
    var reader = new JxlBitReader(b.ToBytes(), 0);

    var set = JxlFrameQuantizer.ReadDequantMatrices(reader);
    Assert.That(set, Is.Not.Null);
    Assert.That(reader.BitsRead, Is.EqualTo(1 + 3 + 96 + 4 + 48 + 48));
  }

  [Test]
  public void ReadDequantMatrices_NonDefault_Mode4_Dct4x8_ConsumesThreeF16PlusDctParams() {
    var b = new BitsBuilder().Add(0u, 1).Add(4u, 3);
    for (var i = 0; i < 3; ++i) b.Add(0u, 16);
    b.Add(0u, 4);
    for (var i = 0; i < 3; ++i) b.Add(0u, 16);
    for (var i = 0; i < 16; ++i) b.Add(0u, 3);
    var reader = new JxlBitReader(b.ToBytes(), 0);

    var set = JxlFrameQuantizer.ReadDequantMatrices(reader);
    Assert.That(set, Is.Not.Null);
    Assert.That(reader.BitsRead, Is.EqualTo(1 + 3 + 48 + 4 + 48 + 48));
  }

  [Test]
  public void ReadDequantMatrices_NonDefault_Mode5_Afv_ConsumesTwentySevenF16PlusTwoDctParams() {
    var b = new BitsBuilder().Add(0u, 1).Add(5u, 3);
    for (var i = 0; i < 27; ++i) b.Add(0u, 16);
    for (var k = 0; k < 2; ++k) {
      b.Add(0u, 4);
      for (var i = 0; i < 3; ++i) b.Add(0u, 16);
    }
    for (var i = 0; i < 16; ++i) b.Add(0u, 3);
    var reader = new JxlBitReader(b.ToBytes(), 0);

    var set = JxlFrameQuantizer.ReadDequantMatrices(reader);
    Assert.That(set, Is.Not.Null);
    Assert.That(reader.BitsRead, Is.EqualTo(1 + 3 + 27 * 16 + 2 * (4 + 48) + 48));
  }

  [Test]
  public void ReadDequantMatrices_NonDefault_Mode6_Dct_ConsumesDctParams() {
    var b = new BitsBuilder().Add(0u, 1).Add(6u, 3);
    b.Add(0u, 4);
    for (var i = 0; i < 3; ++i) b.Add(0u, 16);
    for (var i = 0; i < 16; ++i) b.Add(0u, 3);
    var reader = new JxlBitReader(b.ToBytes(), 0);

    var set = JxlFrameQuantizer.ReadDequantMatrices(reader);
    Assert.That(set, Is.Not.Null);
    Assert.That(reader.BitsRead, Is.EqualTo(1 + 3 + 4 + 48 + 48));
  }

  [Test]
  public void ReadDequantMatrices_NonDefault_Mode7_Raw_Throws() {
    var bits = new BitsBuilder()
      .Add(0u, 1)               // all_default = 0
      .Add(7u, 3)               // table 0 mode = 7 (RAW)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);

    var ex = Assert.Throws<NotImplementedException>(
      () => JxlFrameQuantizer.ReadDequantMatrices(reader));

    Assert.That(ex!.Message, Does.Contain("mode 7"));
    Assert.That(ex.Message, Does.Contain("RAW"));
  }

  [Test]
  public void ReadDequantMatrices_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(() => JxlFrameQuantizer.ReadDequantMatrices(null!));
  }
}
