using System;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// Reads the entropy-coded bits of one highpass codeblock, most significant bit first.
/// </summary>
/// <remarks>
/// SMPTE ST 2073-1:2017, Annex G.9 describes the bitstream as read through a single sequential
/// function, <c>getrun()</c>, that returns a variable-length codeword's meaning without the caller
/// ever seeing the bits themselves. This reader is the thing underneath that function: a peek of up
/// to twenty-six bits — the codebook's longest codeword, Annex C.1 — so the codebook can try
/// candidate lengths without committing to one, and an advance once the true length is known.
/// </remarks>
internal sealed class CineFormBitReader(ReadOnlyMemory<byte> data, int startByte) {

  private readonly ReadOnlyMemory<byte> _data = data;
  private int _bitPosition = startByte * 8;

  /// <summary>The bit position the next read would start at.</summary>
  internal int BitPosition => this._bitPosition;

  /// <summary>The byte position at or after <see cref="BitPosition"/>, rounded up to a whole byte.</summary>
  internal int ByteBoundaryPosition => (this._bitPosition + 7) >> 3;

  /// <summary>
  /// Reads up to twenty-six bits without advancing, padding with zero bits past the end of the data.
  /// </summary>
  /// <remarks>
  /// Padding rather than throwing because a codeblock's last codeword may be followed by fewer than
  /// twenty-six real bits before the segment padding of 10.4's own zero fill; a peek that refused to
  /// look past the data would make reading the band end marker there impossible.
  /// </remarks>
  internal uint Peek(int bitCount) {
    var span = this._data.Span;
    var bitPos = this._bitPosition;
    var value = 0u;
    for (var i = 0; i < bitCount; ++i) {
      var bit = bitPos + i;
      var byteIndex = bit >> 3;
      var bitInByte = 7 - (bit & 7);
      var bitValue = byteIndex < span.Length ? (uint)(span[byteIndex] >> bitInByte) & 1u : 0u;
      value = (value << 1) | bitValue;
    }

    return value;
  }

  /// <summary>Advances over bits already accounted for by a <see cref="Peek"/>.</summary>
  internal void Advance(int bitCount) => this._bitPosition += bitCount;
}
