using System.Collections.Generic;
using FileFormat.Codecs.Asv1;

namespace FileFormat.Codecs.Asv1.Tests;

/// <summary>
/// Writes an ASV1 picture a bit at a time, in the logical bit order asv1.txt describes, and turns it
/// into the on-disk byte order a real packet carries by running it through the same word byte-swap
/// <see cref="Asv1Bitstream.SwapWords"/> uses to undo it — so a test builds a stream the way the
/// document reads and a real packet reads the same document the decoder does, and the one piece of
/// bit-packing machinery is exercised by both directions at once.
/// </summary>
internal sealed class Asv1TestStream {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  internal Asv1TestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  internal Asv1TestStream Code(string code) {
    foreach (var character in code)
      this._Bit(character == '1' ? 1 : 0);

    return this;
  }

  /// <summary>An eight-bit DC field followed immediately by End Of Block: a block with no AC coefficients.</summary>
  internal Asv1TestStream DcOnlyBlock(int dc) => this.Bits(dc, 8).Code("01111");

  /// <summary>The escape form of a coefficient's level: "000" then an eight-bit two's-complement value.</summary>
  internal Asv1TestStream EscapedLevel(int value) => this.Code("000").Bits(value & 0xFF, 8);

  /// <summary>One macroblock of six DC-only blocks: four luma quadrants and one chroma pair.</summary>
  internal Asv1TestStream FlatMacroblock(int lumaDc, int chromaDc = 128) => this
    .DcOnlyBlock(lumaDc).DcOnlyBlock(lumaDc).DcOnlyBlock(lumaDc).DcOnlyBlock(lumaDc)
    .DcOnlyBlock(chromaDc).DcOnlyBlock(chromaDc);

  /// <summary>Turns the bits written so far into the byte-swapped form a real packet is stored in.</summary>
  internal byte[] ToPacketBytes() {
    while (this._partialBits != 0)
      this._Bit(0);

    return Asv1Bitstream.SwapWords(this._bytes.ToArray());
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
