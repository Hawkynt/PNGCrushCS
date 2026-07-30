using System;
using System.Collections.Generic;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for <see cref="JxlEntropyDecoder"/>'s
/// <c>_ReadPrefixCode</c> (ISO/IEC 18181-1 §C.5; libjxl
/// <c>HuffmanDecodingData::ReadFromBitStream</c> in
/// <c>lib/jxl/dec_huffman.cc</c>).
///
/// The reader is LSB-first: the first bit on the wire is bit 0 of the first
/// byte. Tests construct synthetic bitstreams via <see cref="BitsBuilder"/>
/// that pack named bit sequences into bytes preserving that order.
/// </summary>
[TestFixture]
public sealed class PrefixCodeReaderTests {

  /// <summary>
  /// Helper that packs bits LSB-first into bytes, matching the
  /// <see cref="JxlBitReader"/> wire ordering.
  /// </summary>
  private sealed class BitsBuilder {
    private readonly List<byte> _bytes = new();
    private byte _current;
    private int _bitInByte;

    public BitsBuilder Add(int value, int nBits) {
      for (var i = 0; i < nBits; ++i) {
        var bit = (value >> i) & 1;
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
      // Pad to a comfortable size so the reader can refill without overrunning.
      while (copy.Count < 16)
        copy.Add(0);
      return copy.ToArray();
    }
  }

  // ============================================================
  // SIMPLE PREFIX CODE
  // ============================================================

  /// <summary>
  /// Simple 1-symbol case: alphabet={a,b,c,d}, simple_code_or_skip=1,
  /// nsym_field=0 (=> 1 symbol), symbol_index = 2. Result: lengths[2] = 0,
  /// every other length 0 — single-symbol code consumes no bits at decode
  /// time.
  /// </summary>
  [Test]
  public void ReadPrefixCode_SimpleOneSymbol_ReturnsAllZeroLengths() {
    const int alphabetSize = 4; // max_bits = floor(log2(3)) + 1 = 2
    var bits = new BitsBuilder()
      .Add(1, 2)  // simple_code_or_skip = 1 (simple code)
      .Add(0, 2)  // nsym field = 0 (=> 1 symbol)
      .Add(2, 2); // symbol index = 2
    var reader = new JxlBitReader(bits.ToBytes(), 0);

    var (lengths, symbols) = JxlEntropyDecoder._ReadPrefixCode(reader, alphabetSize);

    Assert.Multiple(() => {
      Assert.That(lengths.Length, Is.EqualTo(alphabetSize));
      // For nsym=1 the encoded symbol value (here 2) is communicated to the
      // canonical-table builder via symbols[0]. libjxl dec_huffman.cc::
      // ReadSimpleCode case 1 → table[0] = {bits=0, value=sym[0]}.
      Assert.That(symbols[0], Is.EqualTo(2), "symbols[0] carries the encoded value for nsym=1.");
      // 1-symbol simple code: the lone symbol still has length 0 — decoding
      // produces it without consuming any bits.
      Assert.That(lengths, Is.EqualTo(new[] { 0, 0, 0, 0 }));
    });
  }

  /// <summary>
  /// Simple 2-symbol case: alphabet={a,b,c,d}, two distinct symbols, both with
  /// length 1 ({1,1}).
  /// </summary>
  [Test]
  public void ReadPrefixCode_SimpleTwoSymbol_ReturnsLengthsOneOne() {
    const int alphabetSize = 4;
    var bits = new BitsBuilder()
      .Add(1, 2)  // simple_code_or_skip = 1
      .Add(1, 2)  // nsym field = 1 (=> 2 symbols)
      .Add(0, 2)  // symbol[0] = 0
      .Add(3, 2); // symbol[1] = 3
    var reader = new JxlBitReader(bits.ToBytes(), 0);

    var (lengths, _) = JxlEntropyDecoder._ReadPrefixCode(reader, alphabetSize);

    Assert.That(lengths, Is.EqualTo(new[] { 1, 0, 0, 1 }),
      "2-symbol simple code: both selected symbols get length 1; rest stay 0.");
  }

  /// <summary>
  /// Simple 4-symbol case with tree_select=0: lengths {2,2,2,2}.
  /// </summary>
  [Test]
  public void ReadPrefixCode_SimpleFourSymbol_TreeSelectZero_ReturnsAllTwos() {
    const int alphabetSize = 4;
    var bits = new BitsBuilder()
      .Add(1, 2)  // simple_code_or_skip = 1
      .Add(3, 2)  // nsym field = 3 (=> 4 symbols)
      .Add(0, 2)  // symbol[0] = 0
      .Add(1, 2)  // symbol[1] = 1
      .Add(2, 2)  // symbol[2] = 2
      .Add(3, 2)  // symbol[3] = 3
      .Add(0, 1); // tree_select = 0 -> {2,2,2,2}
    var reader = new JxlBitReader(bits.ToBytes(), 0);

    var (lengths, _) = JxlEntropyDecoder._ReadPrefixCode(reader, alphabetSize);

    Assert.That(lengths, Is.EqualTo(new[] { 2, 2, 2, 2 }));
  }

  /// <summary>
  /// Simple 4-symbol case with tree_select=1: lengths {1,2,3,3} assigned in
  /// the order symbols are read (libjxl swaps to canonical order before
  /// emitting the table — but our return contract pins
  /// <c>lengths[i]</c> to symbol <c>i</c>, so the bits map to whichever
  /// symbols were named in slots 0..3).
  /// </summary>
  [Test]
  public void ReadPrefixCode_SimpleFourSymbol_TreeSelectOne_ReturnsSkewedLengths() {
    const int alphabetSize = 4;
    var bits = new BitsBuilder()
      .Add(1, 2)  // simple_code_or_skip = 1
      .Add(3, 2)  // nsym = 4
      .Add(0, 2)  // symbol[0] = 0  -> length 1
      .Add(1, 2)  // symbol[1] = 1  -> length 2
      .Add(2, 2)  // symbol[2] = 2  -> length 3
      .Add(3, 2)  // symbol[3] = 3  -> length 3
      .Add(1, 1); // tree_select = 1 -> {1,2,3,3}
    var reader = new JxlBitReader(bits.ToBytes(), 0);

    var (lengths, _) = JxlEntropyDecoder._ReadPrefixCode(reader, alphabetSize);

    Assert.That(lengths, Is.EqualTo(new[] { 1, 2, 3, 3 }));
  }

  /// <summary>
  /// Alphabet size 1 short-circuit: returns a single-symbol length-0 code with
  /// no bits consumed, regardless of input.
  /// </summary>
  [Test]
  public void ReadPrefixCode_AlphabetSizeOne_ReturnsLengthZeroAndConsumesNoBits() {
    var bits = new BitsBuilder().Add(0xFF, 8); // payload — must not be touched
    var data = bits.ToBytes();
    var reader = new JxlBitReader(data, 0);

    var (lengths, symbols) = JxlEntropyDecoder._ReadPrefixCode(reader, alphabetSize: 1);

    Assert.Multiple(() => {
      Assert.That(lengths, Is.EqualTo(new[] { 0 }));
      Assert.That(symbols, Is.EqualTo(new[] { 0 }));
      Assert.That(reader.BitsRead, Is.EqualTo(0L), "Single-symbol alphabet must not consume bits.");
    });
  }

  // ============================================================
  // COMPLEX PREFIX CODE
  // ============================================================

  /// <summary>
  /// Complex code with simple_code_or_skip=3 (skip the first three
  /// length-of-length entries — i.e. for symbols 1, 2, 3 in
  /// <c>kCodeLengthCodeOrder</c>) and the static-Huffman length stream
  /// supplying all zeros. With every code length 0, the resulting tree is
  /// empty, which is invalid (no symbols can be coded). The decoder must
  /// reject this.
  /// </summary>
  [Test]
  public void ReadPrefixCode_ComplexAllSkippedAllZeroLengths_Throws() {
    const int alphabetSize = 4;
    var bb = new BitsBuilder()
      .Add(3, 2); // simple_code_or_skip = 3 (skip 3 entries)
    // Remaining 18 - 3 = 15 length-of-length values, all "00" (value 0)
    // in the static huff[16] table -> 2 bits per zero.
    for (var i = 0; i < 15; ++i)
      bb.Add(0, 2);
    var reader = new JxlBitReader(bb.ToBytes(), 0);

    // numCodes == 0 and space == 32 (untouched) -> invalid prefix code.
    Assert.Throws<System.IO.InvalidDataException>(
      () => JxlEntropyDecoder._ReadPrefixCode(reader, alphabetSize),
      "All-zero code-length-code-lengths must be rejected.");
  }

  /// <summary>
  /// Sanity: passing an invalid <c>alphabetSize &lt;= 0</c> throws
  /// <see cref="ArgumentOutOfRangeException"/>.
  /// </summary>
  [Test]
  public void ReadPrefixCode_NonPositiveAlphabet_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlEntropyDecoder._ReadPrefixCode(reader, 0));
  }
}
