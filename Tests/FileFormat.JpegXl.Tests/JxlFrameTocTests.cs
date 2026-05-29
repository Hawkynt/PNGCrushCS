using System;
using System.Collections.Generic;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for <see cref="JxlFrameToc"/> (ISO/IEC 18181-1 §G.5
/// / libjxl <c>lib/jxl/dec_frame.cc::ReadGroupOffsets</c>).
/// </summary>
[TestFixture]
internal sealed class JxlFrameTocTests {

  // ============================================================
  // Test helper: LSB-first bit packer matching JxlBitReader's wire
  // ordering (same approach used in JxlFrameQuantizerTests).
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
  // Decode — single group, no permutation (1-section TOC)
  //
  // libjxl ReadGroupOffsets layout for the 1-section case:
  //   1 bit  permuted = 0
  //   U32    section size: U32(0+u(10), 1024+u(14), 17408+u(22), Bits(30))
  //   ZeroPadToByte
  // ============================================================

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector0() {
    // libjxl ReadToc layout: permuted (1) | byte-align | U32 size | byte-align.
    // permuted = 0, section size selector 0 = 0 + u(10), payload = 42 → size = 42.
    var bits = new BitsBuilder()
      .Add(0u, 1)               // permuted = 0
      .Add(0u, 7)               // byte-align (libjxl JumpToByteBoundary)
      .Add(0u, 2)               // U32 selector 0
      .Add(42u, 10)             // payload = 42 → size = 0 + 42 = 42
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, numGroups: 1, numPasses: 1);

    Assert.That(toc, Is.Not.Null);
    Assert.That(toc.Permuted, Is.False);
    Assert.That(toc.Permutation, Has.Length.EqualTo(0));
    Assert.That(toc.SectionSizes.Length, Is.EqualTo(1));
    Assert.That(toc.SectionOffsets.Length, Is.EqualTo(1));
    Assert.That(toc.SectionSizes[0], Is.EqualTo(42));
    Assert.That(toc.SectionOffsets[0], Is.EqualTo(0));
  }

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector0_MaxPayload() {
    // selector 0 max: payload = 1023 → size = 1023.
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)               // byte-align
      .Add(0u, 2)
      .Add(1023u, 10)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(1023));
  }

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector1_AddsOffset1024() {
    // selector 1: BitsOffset(14, 1024) → size = 1024 + payload. payload = 100 → 1124.
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)               // byte-align
      .Add(1u, 2)
      .Add(100u, 14)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(1124));
  }

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector2_AddsOffset17408() {
    // selector 2: BitsOffset(22, 17408) → size = 17408 + payload. payload = 5 → 17413.
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)               // byte-align
      .Add(2u, 2)
      .Add(5u, 22)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(17413));
  }

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector3_FullBits30() {
    // selector 3: Bits(30) → size = 0 + payload. payload = 1_000_000 → 1_000_000.
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)               // byte-align
      .Add(3u, 2)
      .Add(1_000_000u, 30)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(1_000_000));
  }

  [Test]
  public void Decode_SingleGroup_LeavesReaderByteAligned() {
    // After Decode the reader should be byte-aligned (libjxl calls
    // JumpToByteBoundary at the end of ReadGroupOffsets, AND between the
    // permutation flag and the size U32s). Layout:
    //   permuted (1) + byte-align(7) + selector (2) + payload10 (10)
    //   + final byte-align (4) = 24 bits, then sentinel byte.
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)               // byte-align after permuted
      .Add(0u, 2)
      .Add(7u, 10)              // size = 7
      .Add(0u, 4)               // final byte-align (8+2+10 = 20, +4 = 24)
      .Add(0xCDu, 8)            // sentinel
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(7));
    Assert.That(reader.BitsRead % 8, Is.EqualTo(0),
      "Decode should leave the reader byte-aligned.");
    Assert.That(reader.ReadBits(8), Is.EqualTo(0xCDu));
  }

  // ============================================================
  // Decode — permuted TOC (deferred)
  // ============================================================

  [Test]
  public void Decode_Permuted_ThrowsNotImplemented() {
    // permuted = 1 routes us into the Lehmer-code permutation reader, which
    // is deferred (requires the ANS pipeline + context map). Confirm we
    // throw NotImplementedException with a clear message.
    var bits = new BitsBuilder()
      .Add(1u, 1)               // permuted = 1
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);

    var ex = Assert.Throws<NotImplementedException>(
      () => JxlFrameToc.Decode(reader, 1, 1));

    Assert.That(ex!.Message, Does.Contain("permuted"),
      "Error message should mention the permuted flag.");
    Assert.That(ex.Message, Does.Contain("Lehmer").Or.Contains("DecodePermutation"),
      "Error message should mention the Lehmer-code reader or DecodePermutation.");
  }

  // ============================================================
  // Decode — multi-section TOC
  //
  // libjxl `NumTocEntries(num_groups, num_dc_groups, num_passes)`:
  //   (numGroups == 1 && numPasses == 1) ? 1
  //                                      : 2 + numDcGroups + numGroups * numPasses
  // ============================================================

  [Test]
  public void Decode_MultiGroup_ReadsAllSectionSizes() {
    // numGroups=4, numPasses=1, numDcGroups=1 → 2+1+4 = 7 sections.
    // Each size selector 0 = 12 bits (2 sel + 10 payload), 7 of those fit
    // in 84 bits + alignment.
    var b = new BitsBuilder()
      .Add(0u, 1)            // permuted=0
      .Add(0u, 7);           // byte-align after permuted
    // Add 7 size U32s, sizes 1..7.
    for (var i = 1; i <= 7; ++i) {
      b.Add(0u, 2);          // selector 0
      b.Add((uint)i, 10);    // payload
    }
    // Final byte-align: 8+84=92 bits, need 4 more bits.
    b.Add(0u, 4);
    var bits = b.ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, numGroups: 4, numPasses: 1);

    Assert.That(toc.SectionSizes.Length, Is.EqualTo(7));
    for (var i = 0; i < 7; ++i)
      Assert.That(toc.SectionSizes[i], Is.EqualTo(i + 1), $"Section {i} size");
  }

  [Test]
  public void Decode_MultiPass_ReadsAllSectionSizes() {
    // numGroups=1, numPasses=2, numDcGroups=1 → 2+1+1*2 = 5 sections.
    var b = new BitsBuilder()
      .Add(0u, 1)            // permuted=0
      .Add(0u, 7);           // byte-align
    for (var i = 1; i <= 5; ++i) {
      b.Add(0u, 2);
      b.Add((uint)i * 10u, 10);
    }
    // 8 + 60 = 68 bits → align needs 4.
    b.Add(0u, 4);
    var bits = b.ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, numGroups: 1, numPasses: 2);

    Assert.That(toc.SectionSizes.Length, Is.EqualTo(5));
    Assert.That(toc.SectionSizes[0], Is.EqualTo(10));
    Assert.That(toc.SectionSizes[4], Is.EqualTo(50));
  }

  [Test]
  public void Decode_MultiGroup_OffsetsAreCumulative() {
    // Verify SectionOffsets[i] = sum(SectionSizes[0..i-1]).
    var b = new BitsBuilder()
      .Add(0u, 1).Add(0u, 7);
    var sizes = new int[] { 100, 200, 300, 50, 75, 25, 1 };
    foreach (var s in sizes) {
      b.Add(0u, 2);
      b.Add((uint)s, 10);
    }
    b.Add(0u, 4); // byte-align
    var bits = b.ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, numGroups: 4, numPasses: 1);

    var expectedOffset = 0;
    for (var i = 0; i < sizes.Length; ++i) {
      Assert.That(toc.SectionOffsets[i], Is.EqualTo(expectedOffset),
        $"Offset {i} cumulative");
      expectedOffset += sizes[i];
    }
  }

  // ============================================================
  // Argument validation
  // ============================================================

  [Test]
  public void Decode_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(
      () => JxlFrameToc.Decode(null!, 1, 1));
  }

  [Test]
  public void Decode_ZeroGroups_Throws() {
    var bits = new BitsBuilder().Add(0u, 1).ToBytes();
    var reader = new JxlBitReader(bits, 0);
    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlFrameToc.Decode(reader, numGroups: 0, numPasses: 1));
  }

  [Test]
  public void Decode_ZeroPasses_Throws() {
    var bits = new BitsBuilder().Add(0u, 1).ToBytes();
    var reader = new JxlBitReader(bits, 0);
    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlFrameToc.Decode(reader, numGroups: 1, numPasses: 0));
  }
}
