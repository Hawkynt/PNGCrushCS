using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Indeo;

/// <summary>Writes Indeo fields in the same least-significant-bit-first order <see cref="IviBitReader"/> reads.</summary>
internal sealed class IviBitWriter {

  private readonly List<byte> _bytes = [];
  private int _position;

  internal int Position => this._position;

  internal void WriteBit(int value) {
    var byteIndex = this._position >> 3;
    if (byteIndex == this._bytes.Count)
      this._bytes.Add(0);

    if ((value & 1) != 0)
      this._bytes[byteIndex] |= (byte)(1 << (this._position & 7));

    ++this._position;
  }

  internal void WriteFlag(bool value) => this.WriteBit(value ? 1 : 0);

  internal void Write(uint value, int count) {
    if ((uint)count > 32)
      throw new ArgumentOutOfRangeException(nameof(count));

    for (var bit = 0; bit < count; ++bit)
      this.WriteBit((int)(value >> bit));
  }

  internal void Align() {
    while ((this._position & 7) != 0)
      this.WriteBit(0);
  }

  internal void WriteBytes(ReadOnlySpan<byte> bytes) {
    if ((this._position & 7) != 0)
      throw new InvalidOperationException("Whole Indeo bytes can only be appended at a byte boundary.");

    foreach (var value in bytes) {
      this._bytes.Add(value);
      this._position += 8;
    }
  }

  internal byte[] ToArray() => [.. this._bytes];
}
