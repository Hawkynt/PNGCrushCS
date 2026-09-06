using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// Writes the bits of one colour component, most significant bit of each byte first.
/// </summary>
/// <remarks>
/// The mirror of <see cref="ProResBitReader"/> and bound by the same convention: RDD 36:2022, 5 puts
/// bit strings and variable-length codes into the stream left bit first, which for a big-endian
/// writer is simply the order they are written in.
/// <para/>
/// <b>The padding at the end is part of the syntax, not decoration.</b> A component's coded data is a
/// whole number of bytes, and 5.3.2's <c>endOfData()</c> is what tells a decoder its run-and-level
/// loop has finished: fewer than thirty-two bits left and every one of them zero. Padding with zeroes
/// to the next byte boundary leaves at most seven, so the test can never be satisfied early and can
/// never fail to be satisfied at the end. Padding with anything else, or leaving four spare bytes of
/// zeroes, would each break one half of it.
/// </remarks>
internal sealed class ProResBitWriter {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>The bits written so far, which is what a rate decision is made on.</summary>
  internal int BitCount => this._bytes.Count * 8 + this._partialBits;

  /// <summary>Writes one bit.</summary>
  internal void Bit(int bit) {
    this._partial = (this._partial << 1) | (bit & 1);
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  /// <summary>Writes the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal void Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this.Bit((value >> i) & 1);
  }

  /// <summary>Writes <paramref name="count"/> '0' bits followed by the '1' that separates a Golomb prefix from its suffix.</summary>
  internal void UnaryPrefix(int count) {
    for (var i = 0; i < count; ++i)
      this.Bit(0);

    this.Bit(1);
  }

  /// <summary>Finishes the component, padding with zero bits to the next byte boundary.</summary>
  internal byte[] ToArray() {
    while (this._partialBits != 0)
      this.Bit(0);

    return this._bytes.ToArray();
  }

  /// <summary>Discards everything written, so one buffer can serve a rate search.</summary>
  internal void Reset() {
    this._bytes.Clear();
    this._partial = 0;
    this._partialBits = 0;
  }
}
