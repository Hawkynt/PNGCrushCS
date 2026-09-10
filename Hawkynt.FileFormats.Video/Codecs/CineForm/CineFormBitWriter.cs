using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.CineForm;

/// <summary>MSB-first writer for the prefix codewords in one CineForm highpass subband.</summary>
internal sealed class CineFormBitWriter {
  private readonly List<byte> _bytes = [];
  private byte _current;
  private int _bitCount;

  internal int BitCount => this._bytes.Count * 8 + this._bitCount;

  internal void WriteBits(uint value, int count) {
    if ((uint)count > 32)
      throw new ArgumentOutOfRangeException(nameof(count));

    for (var shift = count - 1; shift >= 0; --shift) {
      this._current = (byte)((this._current << 1) | ((value >> shift) & 1));
      if (++this._bitCount != 8)
        continue;

      this._bytes.Add(this._current);
      this._current = 0;
      this._bitCount = 0;
    }
  }

  internal byte[] ToSegmentAlignedArray() {
    if (this._bitCount != 0) {
      this._bytes.Add((byte)(this._current << (8 - this._bitCount)));
      this._current = 0;
      this._bitCount = 0;
    }

    while ((this._bytes.Count & 3) != 0)
      this._bytes.Add(0);

    return [.. this._bytes];
  }
}
