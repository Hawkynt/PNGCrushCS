using System;
using System.Collections.Generic;

namespace FileFormat.Avif.Codec;

/// <summary>Most-significant-bit-first writer for the uncompressed parts of AV1 OBU syntax.</summary>
internal sealed class Av1BitWriter {

  private readonly List<byte> _bytes = [];
  private int _bitsInCurrent;
  private byte _current;

  public void WriteBit(int bit) {
    if ((uint)bit > 1)
      throw new ArgumentOutOfRangeException(nameof(bit));

    this._current |= (byte)(bit << (7 - this._bitsInCurrent));
    if (++this._bitsInCurrent == 8) {
      this._bytes.Add(this._current);
      this._current = 0;
      this._bitsInCurrent = 0;
    }
  }

  public void Write(uint value, int count) {
    if (count is < 0 or > 32)
      throw new ArgumentOutOfRangeException(nameof(count));
    if (count < 32 && value >= 1u << count)
      throw new ArgumentOutOfRangeException(nameof(value));

    for (var bit = count - 1; bit >= 0; --bit)
      this.WriteBit((int)((value >> bit) & 1));
  }

  /// <summary>AV1 5.3.4 trailing_bits(): a one bit and then zeroes to the next byte boundary.</summary>
  public void WriteTrailingBits() {
    this.WriteBit(1);
    while (this._bitsInCurrent != 0)
      this.WriteBit(0);
  }

  /// <summary>Pads with zeroes to the next byte boundary, as byte_alignment() does.</summary>
  public void ByteAlign() {
    while (this._bitsInCurrent != 0)
      this.WriteBit(0);
  }

  public byte[] ToArray() {
    if (this._bitsInCurrent != 0)
      throw new InvalidOperationException("AV1: the OBU payload was not byte-aligned before being taken.");
    return this._bytes.ToArray();
  }
}
