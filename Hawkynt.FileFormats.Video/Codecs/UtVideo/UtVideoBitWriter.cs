using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.UtVideo;

/// <summary>
/// Writes a Ut Video slice's bits: most significant first, into a stream of little-endian
/// thirty-two bit words.
/// </summary>
/// <remarks>
/// The mirror of <see cref="UtVideoBitReader"/>. Codes go in most significant bit first, and every
/// thirty-two bits are written out as one little-endian word, which is what turns every four bytes
/// of a slice back to front. A slice ends on a whole word: whatever the last code leaves over is
/// padded with zeroes, since the decoder reads words and looks no further than the last code.
/// <para/>
/// One writer serves one slice, because a slice's first code begins at the first bit of its first
/// word and the padding of the slice before it is not part of it.
/// </remarks>
internal sealed class UtVideoBitWriter {

  private readonly List<byte> _bytes;
  private ulong _accumulator;
  private int _pending;

  internal UtVideoBitWriter(int expectedBytes) {
    this._bytes = new(Math.Max(4, expectedBytes));
  }

  /// <summary>Appends the low <paramref name="length"/> bits of a code, most significant first.</summary>
  internal void Write(uint code, int length) {
    this._accumulator = (this._accumulator << length) | code;
    this._pending += length;

    while (this._pending >= 32) {
      this._pending -= 32;
      this._Emit((uint)(this._accumulator >> this._pending));
    }

    this._accumulator &= (1UL << this._pending) - 1;
  }

  /// <summary>Pads the last word with zeroes and hands back the slice's bytes.</summary>
  internal byte[] End() {
    if (this._pending > 0) {
      this._Emit((uint)(this._accumulator << (32 - this._pending)));
      this._pending = 0;
      this._accumulator = 0;
    }

    return this._bytes.ToArray();
  }

  private void _Emit(uint word) {
    this._bytes.Add((byte)word);
    this._bytes.Add((byte)(word >> 8));
    this._bytes.Add((byte)(word >> 16));
    this._bytes.Add((byte)(word >> 24));
  }
}
