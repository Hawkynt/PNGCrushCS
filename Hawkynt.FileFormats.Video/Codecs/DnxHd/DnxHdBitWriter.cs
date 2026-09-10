using System.Collections.Generic;

namespace FileFormat.Codecs.DnxHd;

/// <summary>Writes the MSB-first bitstream used by one VC-3 compressed payload.</summary>
internal sealed class DnxHdBitWriter {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>The number of complete bytes written so far.</summary>
  internal int BytePosition => this._bytes.Count;

  /// <summary>Writes one bit.</summary>
  internal void Bit(int value) {
    this._partial = (this._partial << 1) | (value & 1);
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/>, most significant first.</summary>
  internal void Bits(int value, int count) {
    for (var bit = count - 1; bit >= 0; --bit)
      this.Bit(value >> bit);
  }

  /// <summary>Pads the current byte, and then the current macroblock scan line, with zeroes to a four-byte boundary.</summary>
  internal void AlignToFourBytes() {
    while (this._partialBits != 0)
      this.Bit(0);

    while ((this._bytes.Count & 3) != 0)
      this._bytes.Add(0);
  }

  /// <summary>Finishes the last byte with zeroes and returns the written bytes.</summary>
  internal byte[] ToArray() {
    while (this._partialBits != 0)
      this.Bit(0);

    return this._bytes.ToArray();
  }
}
