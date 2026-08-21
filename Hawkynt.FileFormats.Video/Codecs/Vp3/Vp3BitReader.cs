using System;
using System.IO;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Reads the bit fields of a VP3 packet, most significant bit of each byte first.
/// </summary>
/// <remarks>
/// The convention is the one Section 5.2 of the Theora specification describes, which VP3 shares:
/// the first bit of a packet is the top bit of its first byte, fields are not aligned to anything,
/// and a field wider than what is left of a byte simply continues into the next one. It is the
/// opposite of the convention Vorbis uses, and the opposite of what most bit readers in this library
/// do, which is reason enough for it to be its own type rather than a few lines inlined somewhere.
/// <para/>
/// Reading past the end of the packet throws rather than returning zeroes. The specification allows a
/// decoder to keep what it read and carry on, but there is nothing useful to carry on with: a VP3
/// frame is a single arithmetic-free bit stream whose fields are positional, so the first read past
/// the end means the position was already wrong, and everything after it is noise that would still
/// produce a picture.
/// </remarks>
internal sealed class Vp3BitReader(ReadOnlyMemory<byte> data) {

  private readonly ReadOnlyMemory<byte> _data = data;
  private int _position;

  /// <summary>How many bits have been consumed, which is what says whether a frame read its packet.</summary>
  internal int Position => this._position;

  /// <summary>How many bits the packet holds.</summary>
  internal int Length => this._data.Length * 8;

  internal int ReadBit() {
    var position = this._position;
    if (position >= this._data.Length * 8)
      throw new InvalidDataException(
        $"A VP3 frame ran off the end of its {this._data.Length}-byte packet. The packet is either truncated or "
        + "was read out of step, and every field after this point would be noise.");

    this._position = position + 1;
    return (this._data.Span[position >> 3] >> (7 - (position & 7))) & 1;
  }

  internal int ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.ReadBit();

    return value;
  }
}
