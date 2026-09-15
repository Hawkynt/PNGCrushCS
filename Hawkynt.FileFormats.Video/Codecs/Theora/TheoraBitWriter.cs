using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Theora;

/// <summary>Writes Theora bit fields most-significant bit first, section 5.2 of the specification.</summary>
internal sealed class TheoraBitWriter {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _bitCount;

  internal void WriteBit(int value) => this.WriteBits(1, (uint)value);

  internal void WriteBits(int count, uint value) {
    ArgumentOutOfRangeException.ThrowIfNegative(count);
    if (count > 32)
      throw new ArgumentOutOfRangeException(nameof(count), count, "A Theora bit field cannot be wider than the value supplied here.");

    for (var bit = count - 1; bit >= 0; --bit) {
      this._partial = (this._partial << 1) | (int)((value >> bit) & 1);
      if (++this._bitCount != 8)
        continue;

      this._bytes.Add((byte)this._partial);
      this._partial = 0;
      this._bitCount = 0;
    }
  }

  internal void WriteBytes(ReadOnlySpan<byte> bytes) {
    foreach (var value in bytes)
      this.WriteBits(8, value);
  }

  internal byte[] Finish() {
    if (this._bitCount != 0) {
      this._bytes.Add((byte)(this._partial << (8 - this._bitCount)));
      this._partial = 0;
      this._bitCount = 0;
    }

    return this._bytes.ToArray();
  }
}
