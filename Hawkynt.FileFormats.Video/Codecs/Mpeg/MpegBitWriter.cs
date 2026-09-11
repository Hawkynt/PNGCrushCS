using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Mpeg;

/// <summary>Writes the most-significant-bit-first syntax of an MPEG-1 or MPEG-2 video elementary stream.</summary>
/// <remarks>
/// Neither standard escapes anything between the syntax bits and the bytes. Start codes are the only
/// byte-level structure: they are aligned, then <c>00 00 01</c>, then the code byte. Keeping that
/// operation in here makes it impossible for a picture writer to emit a start code by accident out of
/// a half-filled byte.
/// </remarks>
internal sealed class MpegBitWriter {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>How many syntax bits have been written, excluding alignment padding.</summary>
  internal int BitCount => this._bytes.Count * 8 + this._partialBits;

  /// <summary>Appends a non-negative integer in exactly <paramref name="length"/> bits.</summary>
  internal void WriteBits(int value, int length) {
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value), value, "An MPEG unsigned bit field cannot be negative.");

    this.WriteBits((uint)value, length);
  }

  /// <summary>Appends the low <paramref name="length"/> bits of <paramref name="value"/>, most significant first.</summary>
  internal void WriteBits(uint value, int length) {
    if ((uint)length > 32)
      throw new ArgumentOutOfRangeException(nameof(length), length, "An MPEG bit field cannot be wider than 32 bits here.");

    if (length < 32 && value >= (1u << length))
      throw new ArgumentOutOfRangeException(nameof(value), value, $"The value does not fit in {length} bits.");

    for (var bit = length - 1; bit >= 0; --bit)
      this.WriteBit((int)(value >> bit) & 1);
  }

  /// <summary>Appends one bit.</summary>
  internal void WriteBit(int bit) {
    this._partial = (this._partial << 1) | (bit & 1);
    if (++this._partialBits < 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  /// <summary>Appends one codeword as printed in the standard; spaces are grouping and ignored.</summary>
  internal void WriteCode(string code) {
    ArgumentNullException.ThrowIfNull(code);

    foreach (var c in code)
      switch (c) {
        case '0':
          this.WriteBit(0);
          break;
        case '1':
          this.WriteBit(1);
          break;
        case ' ':
          break;
        default:
          throw new ArgumentException($"'{c}' is not a bit in MPEG codeword '{code}'.", nameof(code));
      }
  }

  /// <summary>Zero-pads to the next byte boundary.</summary>
  internal void AlignToByte() {
    if (this._partialBits == 0)
      return;

    this._bytes.Add((byte)(this._partial << (8 - this._partialBits)));
    this._partial = 0;
    this._partialBits = 0;
  }

  /// <summary>Writes an aligned 00 00 01 xx MPEG start code.</summary>
  internal void WriteStartCode(byte code) {
    this.AlignToByte();
    this._bytes.Add(0);
    this._bytes.Add(0);
    this._bytes.Add(1);
    this._bytes.Add(code);
  }

  /// <summary>Returns the stream so far, padding the last byte with zero bits when necessary.</summary>
  internal byte[] ToArray() {
    this.AlignToByte();
    return [.. this._bytes];
  }
}
