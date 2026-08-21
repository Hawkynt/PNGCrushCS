using System;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>
/// Reads a MagicYUV slice's bits: most significant first, straight out of the bytes.
/// </summary>
/// <remarks>
/// Plainly, in other words — which is worth saying because the two codecs this one most resembles
/// both do something else. HuffYUV and Ut Video were written for Video for Windows and their coders
/// wrote whole machine words, so every four bytes of a frame sit back to front; this one does not,
/// and reading it as though it did decodes nothing. Nothing states which of the two it is, so it was
/// measured: the codes line up byte for byte with the differences a known picture implies, and they
/// do not line up at all under the swap.
/// <para/>
/// One reader is built per slice, because a slice is where the coding starts again.
/// </remarks>
internal sealed class MagicYuvBitReader {

  private readonly ReadOnlyMemory<byte> _data;
  private readonly int _bitLength;
  private int _position;

  internal MagicYuvBitReader(ReadOnlyMemory<byte> slice) {
    this._data = slice;
    this._bitLength = slice.Length * 8;
  }

  /// <summary>The next bit, or zero once the slice has run out.</summary>
  /// <remarks>
  /// Zero rather than a throw, because a slice ends on a byte boundary and its last byte is padding
  /// whose bits a decoder may legitimately look at while finishing the last code of the last row.
  /// Running out for real is caught where it means something — a code no table entry completes.
  /// </remarks>
  internal int Bit() {
    if (this._position >= this._bitLength)
      return 0;

    var bit = (this._data.Span[this._position >> 3] >> (7 - (this._position & 7))) & 1;
    ++this._position;
    return bit;
  }
}
