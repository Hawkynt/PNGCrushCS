using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Vp3;

/// <summary>Writes VP3 fields most-significant bit first, matching <see cref="Vp3BitReader"/>.</summary>
internal sealed class Vp3BitWriter {
  private readonly List<byte> _bytes = [];
  private int _position;

  internal int Position => this._position;

  internal void WriteBit(int value) {
    if ((value & ~1) != 0)
      throw new ArgumentOutOfRangeException(nameof(value));

    var byteIndex = this._position >> 3;
    if (byteIndex == this._bytes.Count)
      this._bytes.Add(0);

    if (value != 0)
      this._bytes[byteIndex] |= (byte)(1 << (7 - (this._position & 7)));

    ++this._position;
  }

  internal void WriteBits(uint value, int count) {
    if ((uint)count > 32)
      throw new ArgumentOutOfRangeException(nameof(count));
    if (count < 32 && value >= 1U << count)
      throw new ArgumentOutOfRangeException(nameof(value));

    for (var bit = count - 1; bit >= 0; --bit)
      this.WriteBit((int)(value >> bit & 1U));
  }

  internal byte[] ToArray() => [.. this._bytes];
}
