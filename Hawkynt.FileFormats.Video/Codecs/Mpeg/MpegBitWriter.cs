using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Mpeg;

/// <summary>Writes an MPEG video bitstream most-significant bit first.</summary>
/// <remarks>
/// MPEG-1 puts no escaping between the syntax bits and the bytes. Start codes are the only byte-level
/// structure: they are aligned, followed by <c>00 00 01</c>, and then the code byte. Keeping that
/// operation here makes it impossible for a picture writer to accidentally emit a start code through
/// a half-filled byte.
/// </remarks>
internal sealed class MpegBitWriter {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/>, high bit first.</summary>
  internal void Write(int value, int count) {
    if ((uint)count > 32)
      throw new ArgumentOutOfRangeException(nameof(count));

    for (var bit = count - 1; bit >= 0; --bit)
      this.WriteBit((value >> bit) & 1);
  }

  /// <summary>Writes one bit, whose value must be zero or one.</summary>
  internal void WriteBit(int value) {
    if ((uint)value > 1)
      throw new ArgumentOutOfRangeException(nameof(value));

    this._partial = (this._partial << 1) | value;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  /// <summary>Writes a VLC exactly as Annex B prints it; spaces are visual grouping only.</summary>
  internal void WriteCode(string code) {
    ArgumentNullException.ThrowIfNull(code);

    foreach (var character in code)
      switch (character) {
        case '0': this.WriteBit(0); break;
        case '1': this.WriteBit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }
  }

  /// <summary>Aligns to a byte with zero bits and writes an MPEG start code.</summary>
  internal void StartCode(byte code) {
    this.AlignToByte();
    this._bytes.Add(0x00);
    this._bytes.Add(0x00);
    this._bytes.Add(0x01);
    this._bytes.Add(code);
  }

  /// <summary>Pads the current byte with zeroes.</summary>
  internal void AlignToByte() {
    while (this._partialBits != 0)
      this.WriteBit(0);
  }

  /// <summary>Finishes the last byte with zeroes and returns the written stream.</summary>
  internal byte[] ToArray() {
    this.AlignToByte();
    return this._bytes.ToArray();
  }
}
