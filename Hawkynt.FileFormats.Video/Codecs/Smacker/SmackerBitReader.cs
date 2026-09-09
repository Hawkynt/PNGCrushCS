using System;

namespace FileFormat.Codecs.Smacker;

/// <summary>
/// Reads a Smacker bitstream a bit at a time, least significant bit of each new byte first, over a
/// buffer treated as though zero bytes followed it forever.
/// </summary>
/// <remarks>
/// RAD's own description states the convention in one sentence — "bits counted from the lower bit of
/// each new byte" — and offers a worked example that does not reproduce that rule: reading 5, 6 and 7
/// bits from the byte sequence <c>0x5C, 0x96, 0xEF</c> the way the sentence describes gives
/// <c>0x1C, 0x32, 0x72</c>, not the <c>0x1C, 0x1A, 0x79</c> the text states — the first value agrees and
/// the other two do not, under any of the four combinations of bit order within a byte and of which end
/// of a multi-bit read is significant. The sentence is what real files bear out, and it is the ordinary
/// least-significant-bit-first convention several other codecs in this package already read.
/// <para/>
/// <b>Why running off the end returns zeroes rather than throwing.</b> A Smacker tree section ends on
/// whatever bit its last leaf ended on, and both the tree walk and the per-symbol decode read ahead of
/// themselves: a table can legitimately finish with its last few reads landing past the final byte, and
/// a file whose tree section is exactly as long as its trees need is the normal case rather than a
/// damaged one. Reads past the end therefore yield zero bits, and <see cref="BitsRemaining"/> — which
/// goes negative once that has happened — is what the two places that must not run on regardless check
/// instead, so a genuinely truncated file is refused by the structure it fails to produce rather than by
/// an exception thrown one bit early on a sound one.
/// </remarks>
internal ref struct SmackerBitReader {
  private readonly ReadOnlySpan<byte> _data;
  private int _bitOffset;

  internal SmackerBitReader(ReadOnlySpan<byte> data) {
    this._data = data;
    this._bitOffset = 0;
  }

  /// <summary>How many bits are left before the buffer's own end, negative once reads have run past
  /// it.</summary>
  internal readonly long BitsRemaining => (long)this._data.Length * 8 - this._bitOffset;

  internal int ReadBit() {
    var byteIndex = this._bitOffset >> 3;
    var bit = byteIndex < this._data.Length ? (this._data[byteIndex] >> (this._bitOffset & 7)) & 1 : 0;
    ++this._bitOffset;
    return bit;
  }

  internal void SkipBit() => ++this._bitOffset;

  /// <summary>Reads <paramref name="count"/> bits, the first one read becoming the result's least
  /// significant bit.</summary>
  internal uint ReadBits(int count) {
    uint value = 0;
    for (var i = 0; i < count; ++i)
      value |= (uint)this.ReadBit() << i;

    return value;
  }

  /// <summary>Reads a whole byte, least significant bit first — the same as <see cref="ReadBits"/>
  /// with a count of eight, spelled out for the common case.</summary>
  internal byte ReadByte() => (byte)this.ReadBits(8);
}
