using System;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Reads packet-header bits most significant first with the B.10.1 bit-stuffing rule.</summary>
/// <remarks>
/// The bit after an 0xFF byte is a stuffed zero and carries no syntax, which also means a header
/// whose last byte is 0xFF still owns the byte after it even if only part of that 0xFF was read.
/// Whether the previous byte was 0xFF is tracked as state rather than read back out of the buffer,
/// because at the start of a packet the byte before it belongs to the previous packet's body and
/// may be anything at all.
/// </remarks>
internal sealed class BitReader {

  private readonly byte[] _data;
  private readonly int _end;
  private int _position;
  private int _bitsLeft;
  private int _current;

  /// <summary>Byte position just past everything consumed so far.</summary>
  public int Position => this._position;

  public BitReader(byte[] data, int offset, int length) {
    ArgumentNullException.ThrowIfNull(data);
    if ((uint)offset > (uint)data.Length)
      throw new ArgumentOutOfRangeException(nameof(offset));
    if (length < 0 || offset + length > data.Length)
      throw new ArgumentOutOfRangeException(nameof(length));

    this._data = data;
    this._position = offset;
    this._end = offset + length;
  }

  public int ReadBit() {
    if (this._bitsLeft == 0)
      this._LoadByte();

    --this._bitsLeft;
    return (this._current >> this._bitsLeft) & 1;
  }

  public int ReadBits(int count) {
    if (count < 0 || count > 31)
      throw new ArgumentOutOfRangeException(nameof(count));

    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.ReadBit();

    return value;
  }

  /// <summary>
  /// Drops the remaining bits of the current byte and leaves <see cref="Position"/> on the first
  /// byte after the header, consuming the stuffed byte when the last header byte was 0xFF.
  /// </summary>
  public void AlignToByte() {
    if (this._current == 0xFF && this._position < this._end) {
      if ((this._data[this._position] & 0x80) != 0)
        throw new InvalidDataException("JPEG 2000 packet header has a one in the stuffed bit after 0xFF.");

      ++this._position;
    }

    this._bitsLeft = 0;
    this._current = 0;
  }

  private void _LoadByte() {
    if (this._position >= this._end)
      throw new InvalidDataException("JPEG 2000 packet header ends in the middle of a syntax element.");

    var stuffed = this._current == 0xFF;
    this._current = this._data[this._position++];
    this._bitsLeft = stuffed ? 7 : 8;
  }
}
