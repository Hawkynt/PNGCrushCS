using System;
using System.Buffers.Binary;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>
/// Writes a HuffYUV frame's bits: most significant first, then turned round into the little-endian
/// thirty-two bit words the format stores them as.
/// </summary>
/// <remarks>
/// The mirror of <see cref="HuffYuvBitReader"/>. The bits go into bytes in the order they are
/// written, and only when the frame is finished is every four bytes reversed — which is the same
/// thing the reference coder does by writing machine words on a little-endian machine. The frame
/// is padded with zero bits to a whole number of words first, because the coder wrote whole words
/// and a reader pads a short frame the same way before swapping it.
/// </remarks>
internal sealed class HuffYuvBitWriter {

  private byte[] _bytes;
  private int _length;
  private ulong _accumulator;
  private int _accumulatedBits;

  internal HuffYuvBitWriter(int capacity) {
    this._bytes = new byte[Math.Max(16, capacity)];
  }

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal void Write(uint value, int count) {
    this._accumulator = (this._accumulator << count) | (value & ((1UL << count) - 1));
    this._accumulatedBits += count;

    while (this._accumulatedBits >= 8) {
      this._accumulatedBits -= 8;
      this._Append((byte)(this._accumulator >> this._accumulatedBits));
    }
  }

  /// <summary>Finishes the frame: pads it to whole words and turns every word round.</summary>
  internal byte[] End() {
    if (this._accumulatedBits > 0)
      this._Append((byte)(this._accumulator << (8 - this._accumulatedBits)));

    this._accumulatedBits = 0;
    this._accumulator = 0;

    var words = (this._length + 3) / 4;
    var frame = new byte[words * 4];
    this._bytes.AsSpan(0, this._length).CopyTo(frame);

    for (var i = 0; i < frame.Length; i += 4)
      BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(i, 4), BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(i, 4)));

    return frame;
  }

  private void _Append(byte value) {
    if (this._length == this._bytes.Length)
      Array.Resize(ref this._bytes, this._bytes.Length * 2);

    this._bytes[this._length++] = value;
  }
}
