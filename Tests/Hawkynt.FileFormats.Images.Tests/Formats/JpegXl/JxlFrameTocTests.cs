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
  // ============================================================

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector0() {
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)
      .Add(0u, 2)
      .Add(42u, 10)
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
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)
      .Add(0u, 2)
      .Add(1023u, 10)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(1023));
  }

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector1_AddsOffset1024() {
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)
      .Add(1u, 2)
      .Add(100u, 14)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(1124));
  }

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector2_AddsOffset17408() {
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)
      .Add(2u, 2)
      .Add(5u, 22)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(17413));
  }

  [Test]
  public void Decode_SingleGroup_NoPermutation_Selector3_AddsOffset4211712() {
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)
      .Add(3u, 2)
      .Add(1_000_000u, 30)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(5_211_712));
  }

  [Test]
  public void Decode_SingleGroup_LeavesReaderByteAligned() {
    var bits = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7)
      .Add(0u, 2)
      .Add(7u, 10)
      .Add(0u, 4)
      .Add(0xCDu, 8)
      .ToBytes();

    var reader = new JxlBitReader(bits, 0);
    var toc = JxlFrameToc.Decode(reader, 1, 1);

    Assert.That(toc.SectionSizes[0], Is.EqualTo(7));
    Assert.That(reader.BitsRead % 8, Is.EqualTo(0),
      "Decode should leave the reader byte-aligned.");
    Assert.That(reader.ReadBits(8), Is.EqualTo(0xCDu));
  }

  // ============================================================
  // Decode — entropy-coded Lehmer permutation
  // ============================================================

  [Test]
  public void Decode_Permuted_RemapsPhysicalSectionsToCanonicalOrder() {
    // Five canonical sections. Lehmer [1,0,0,0,0] is permutation
    // [1,0,2,3,4]: canonical section 0 is physically second, section 1 first.
    // DecodePermutation stores only through the last non-zero Lehmer digit, so
    // the entropy block carries end=1 followed by digit 1.
    var writer = new JxlBitWriter();
    writer.WriteBool(true);
    var permutationTokens = new JxlTokenStream();
    permutationTokens.Add(1); // end
    permutationTokens.Add(1); // lehmer[0]
    permutationTokens.WriteHeader(writer, JxlCoeffOrderDecoder.PermutationContexts);
    permutationTokens.WriteTokens(writer);
    writer.ZeroPadToByte();

    int[] physicalSizes = [10, 20, 30, 40, 50];
    foreach (var size in physicalSizes)
      writer.WriteU32((uint)size, 0, 10, 1024, 14, 17408, 22, 4211712, 30);
    writer.ZeroPadToByte();

    var reader = new JxlBitReader(writer.ToArray(), 0);
    var toc = JxlFrameToc.Decode(reader, numGroups: 1, numPasses: 2, numDcGroups: 1);

    Assert.Multiple(() => {
      Assert.That(toc.Permuted, Is.True);
      Assert.That(toc.Permutation, Is.EqualTo(new[] { 1, 0, 2, 3, 4 }));
      Assert.That(toc.SectionSizes, Is.EqualTo(new[] { 20, 10, 30, 40, 50 }));
      Assert.That(toc.SectionOffsets, Is.EqualTo(new[] { 10, 0, 30, 60, 100 }));
      Assert.That(reader.BitsRead % 8, Is.Zero);
    });
  }

  // ============================================================
  // Decode — multi-section TOC
  // ============================================================

  [Test]
  public void Decode_MultiGroup_ReadsAllSectionSizes() {
    var b = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7);
    for (var i = 1; i <= 7; ++i) {
      b.Add(0u, 2);
      b.Add((uint)i, 10);
    }
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
    var b = new BitsBuilder()
      .Add(0u, 1)
      .Add(0u, 7);
    for (var i = 1; i <= 5; ++i) {
      b.Add(0u, 2);
      b.Add((uint)i * 10u, 10);
    }
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
    var b = new BitsBuilder()
      .Add(0u, 1).Add(0u, 7);
    int[] sizes = [100, 200, 300, 50, 75, 25, 1];
    foreach (var s in sizes) {
      b.Add(0u, 2);
      b.Add((uint)s, 10);
    }
    b.Add(0u, 4);
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
