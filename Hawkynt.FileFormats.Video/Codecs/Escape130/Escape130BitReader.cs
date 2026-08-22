using System;
using System.IO;

namespace FileFormat.Codecs.Escape130;

/// <summary>
/// Reads Escape 130's bitstream the way its own specification states it: little-endian throughout,
/// and bit-oriented with every multi-bit quantity stored smallest bit first — the first bit taken off
/// the stream becomes the lowest bit of whatever value is being read, exactly as a byte's own bits are
/// numbered from bit 0 upward.
/// </summary>
internal ref struct Escape130BitReader {

  private readonly ReadOnlySpan<byte> _data;
  private readonly int _totalBits;
  private int _bitPosition;

  internal Escape130BitReader(ReadOnlySpan<byte> data) {
    this._data = data;
    this._totalBits = data.Length * 8;
    this._bitPosition = 0;
  }

  internal readonly int BitPosition => this._bitPosition;

  internal readonly int RemainingBits => this._totalBits - this._bitPosition;

  internal int ReadBit() {
    if (this._bitPosition >= this._totalBits)
      throw new InvalidDataException("An Escape 130 frame's bitstream ran out of bits before its block loop finished.");

    var byteIndex = this._bitPosition >> 3;
    var bitIndex = this._bitPosition & 7;
    this._bitPosition++;
    return (this._data[byteIndex] >> bitIndex) & 1;
  }

  /// <summary>Reads <paramref name="count"/> bits, the first one read becoming the result's bit 0.</summary>
  internal int ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value |= this.ReadBit() << i;

    return value;
  }
}
