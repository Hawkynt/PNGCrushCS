using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H264;

/// <summary>MSB-first writer for the H.264 syntax elements emitted by the managed encoder.</summary>
internal sealed class H264BitWriter {
  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  internal void WriteBit(bool value) {
    this._partial = (this._partial << 1) | (value ? 1 : 0);
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  internal void WriteBits(int value, int count) => this.WriteBits(unchecked((uint)value), count);

  internal void WriteBits(uint value, int count) {
    if ((uint)count > 32)
      throw new ArgumentOutOfRangeException(nameof(count));

    for (var bit = count - 1; bit >= 0; --bit)
      this.WriteBit(((value >> bit) & 1u) != 0);
  }

  internal void WriteUnsignedExpGolomb(int value) {
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value));

    var codeNum = checked(value + 1);
    var leadingZeroBits = 0;
    for (var valueBits = codeNum; valueBits > 1; valueBits >>= 1)
      ++leadingZeroBits;

    for (var i = 0; i < leadingZeroBits; ++i)
      this.WriteBit(false);
    this.WriteBits(codeNum, leadingZeroBits + 1);
  }

  internal void WriteSignedExpGolomb(int value)
    => this.WriteUnsignedExpGolomb(value > 0 ? checked(2 * value - 1) : checked(-2 * value));

  internal void AlignWithZeroBits() {
    while (this._partialBits != 0)
      this.WriteBit(false);
  }

  internal void WriteAlignedByte(byte value) {
    if (this._partialBits != 0)
      throw new InvalidOperationException("H.264 PCM samples must begin on a byte boundary.");
    this._bytes.Add(value);
  }

  internal byte[] FinishRbsp() {
    this.WriteBit(true);
    while (this._partialBits != 0)
      this.WriteBit(false);
    return [.. this._bytes];
  }
}
