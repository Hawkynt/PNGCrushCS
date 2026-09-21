using System;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>Reads MagicYUV slice bits most-significant first, straight out of the bytes.</summary>
internal sealed class MagicYuvBitReader {
  private readonly ReadOnlyMemory<byte> _data;
  private readonly int _bitLength;
  private int _position;

  internal MagicYuvBitReader(ReadOnlyMemory<byte> slice) {
    this._data = slice;
    this._bitLength = slice.Length * 8;
  }

  internal int BitsRemaining => this._bitLength - this._position;

  /// <summary>The next bit, or zero once padding beyond the slice is inspected.</summary>
  internal int Bit() {
    if (this._position >= this._bitLength)
      return 0;

    var bit = (this._data.Span[this._position >> 3] >> (7 - (this._position & 7))) & 1;
    ++this._position;
    return bit;
  }

  /// <summary>Reads up to 16 bits as one unsigned value.</summary>
  internal int Bits(int count) {
    if (count is < 0 or > 16)
      throw new ArgumentOutOfRangeException(nameof(count));

    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.Bit();

    return value;
  }
}
