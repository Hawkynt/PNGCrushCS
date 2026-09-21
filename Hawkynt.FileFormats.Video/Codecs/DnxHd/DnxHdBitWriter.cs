using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>Writes bits most-significant first, which is the bit order VC-3 uses inside coding units.</summary>
internal sealed class DnxHdBitWriter {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  internal int Length => this._bytes.Count + (this._partialBits == 0 ? 0 : 1);

  internal void WriteBit(bool value) {
    this._partial = (this._partial << 1) | (value ? 1 : 0);
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  internal void WriteBits(uint value, int count) {
    if (count is < 0 or > 32)
      throw new ArgumentOutOfRangeException(nameof(count));

    for (var bit = count - 1; bit >= 0; --bit)
      this.WriteBit(((value >> bit) & 1) != 0);
  }

  internal void AlignToByte() {
    while (this._partialBits != 0)
      this.WriteBit(false);
  }

  internal void AlignToFourBytes() {
    this.AlignToByte();
    while ((this._bytes.Count & 3) != 0)
      this._bytes.Add(0);
  }

  internal byte[] ToArray() {
    this.AlignToByte();
    return [.. this._bytes];
  }
}
