using System;

namespace FileFormat.Codecs.Vc1;

/// <summary>Writes the most-significant-bit-first fields and VLC entries used by VC-1.</summary>
/// <remarks>
/// VC-1's syntax diagrams state fields from their most significant bit down, and the tables in
/// <see cref="Vc1Tables"/> keep every codeword beside its length in exactly that order. Keeping the
/// writer at that same level makes an encoder the literal inverse of <see cref="Vc1BitReader"/> rather
/// than a second representation of the bitstream.
/// </remarks>
internal sealed class Vc1BitWriter {

  private byte[] _buffer = new byte[256];
  private int _length;
  private int _partial;
  private int _used;

  internal int BitLength => (this._length << 3) + this._used;

  internal void WriteBit(bool value) {
    this._partial = (this._partial << 1) | (value ? 1 : 0);
    if (++this._used != 8)
      return;

    this._Append((byte)this._partial);
    this._partial = 0;
    this._used = 0;
  }

  internal void WriteBits(int value, int count) {
    if ((uint)count > 31u)
      throw new ArgumentOutOfRangeException(nameof(count), count, "A VC-1 integer field is at most thirty-one bits here.");
    if (count < 31 && (uint)value >= 1u << count)
      throw new ArgumentOutOfRangeException(nameof(value), value, $"The value does not fit in {count} bit(s).");

    for (var bit = count - 1; bit >= 0; --bit)
      this.WriteBit(((value >> bit) & 1) != 0);
  }

  /// <summary>Writes one index from a table stored as codeword/length pairs.</summary>
  internal void WriteCode(ReadOnlySpan<int> table, int index) {
    var at = checked(index * 2);
    if ((uint)(at + 1) >= (uint)table.Length)
      throw new ArgumentOutOfRangeException(nameof(index), index, "The VLC table has no such entry.");

    this.WriteBits(table[at], table[at + 1]);
  }

  /// <summary>Returns the bytes written, padding the last one with zero bits as VC-1 packets do.</summary>
  internal byte[] ToArray() {
    var length = this._length + (this._used == 0 ? 0 : 1);
    var result = new byte[length];
    this._buffer.AsSpan(0, this._length).CopyTo(result);
    if (this._used != 0)
      result[^1] = (byte)(this._partial << (8 - this._used));

    return result;
  }

  private void _Append(byte value) {
    if (this._length == this._buffer.Length)
      Array.Resize(ref this._buffer, checked(this._buffer.Length * 2));

    this._buffer[this._length++] = value;
  }
}
