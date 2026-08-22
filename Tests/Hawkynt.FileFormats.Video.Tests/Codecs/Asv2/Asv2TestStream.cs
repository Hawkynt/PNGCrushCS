using System.Collections.Generic;
using FileFormat.Codecs.Asv2;

namespace FileFormat.Codecs.Asv2.Tests;

/// <summary>
/// Writes an ASV2 picture a bit at a time, in the logical bit order a decoded stream reads once its
/// own byte-wide reversal has been undone, and turns it into the on-disk byte order a real packet
/// carries by running it through the same reversal <see cref="Asv2Bitstream.ReverseBits"/> uses to undo
/// it — self-inverse, so applying it here a second time is exactly what building a stream the way the
/// decoder reads it needs.
/// </summary>
internal sealed class Asv2TestStream {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  internal Asv2TestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  internal Asv2TestStream Code(string code) {
    foreach (var character in code)
      this._Bit(character == '1' ? 1 : 0);

    return this;
  }

  /// <summary>
  /// A fixed-width field — the coefficient group count, a DC field, an escaped level's raw byte — whose
  /// bits <see cref="Asv2Bitstream.ReadReversedBits"/> reverses a second time on the way in, so writing
  /// them here bit-reversed is what makes the decoder read the value stated.
  /// </summary>
  internal Asv2TestStream ReversedField(int value, int count) {
    var reversed = 0;
    for (var i = 0; i < count; ++i)
      reversed = (reversed << 1) | ((value >> i) & 1);

    return this.Bits(reversed, count);
  }

  /// <summary>A block with no coefficient groups after the DC field: count zero, DC, then the "no bits set" first pattern.</summary>
  internal Asv2TestStream DcOnlyBlock(int dc) => this
    .ReversedField(0, 4).ReversedField(dc, 8).Code("01"); // FirstCoefficientPattern "01" -> 0b0000

  /// <summary>One macroblock of six DC-only blocks: four luma quadrants and one chroma pair.</summary>
  internal Asv2TestStream FlatMacroblock(int lumaDc, int chromaDc = 128) => this
    .DcOnlyBlock(lumaDc).DcOnlyBlock(lumaDc).DcOnlyBlock(lumaDc).DcOnlyBlock(lumaDc)
    .DcOnlyBlock(chromaDc).DcOnlyBlock(chromaDc);

  /// <summary>The escape form of a coefficient's level: "00000" then an eight-bit two's-complement value.</summary>
  internal Asv2TestStream EscapedLevel(int value) => this.Code("00000").ReversedField(value & 0xFF, 8);

  /// <summary>Turns the bits written so far into the byte-reversed form a real packet is stored in.</summary>
  internal byte[] ToPacketBytes() {
    while (this._partialBits != 0)
      this._Bit(0);

    return Asv2Bitstream.ReverseBits(this._bytes.ToArray());
  }

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }
}
