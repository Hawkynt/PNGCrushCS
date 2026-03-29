using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Reads individual bits from a byte array (MSB-first), skipping JPEG 2000 marker byte-stuffing.</summary>
internal sealed class BitReader {

  private readonly byte[] _data;
  private int _pos;
  private readonly int _end;
  private int _bitsLeft;
  private int _currentByte;

  /// <summary>Current byte position in the data array.</summary>
  public int Position => _pos;

  public BitReader(byte[] data, int offset, int length) {
    _data = data;
    _pos = offset;
    _end = offset + length;
    _bitsLeft = 0;
    _currentByte = 0;
  }

  /// <summary>Read a single bit (0 or 1).</summary>
  public int ReadBit() {
    if (_bitsLeft == 0)
      _LoadByte();

    --_bitsLeft;
    return (_currentByte >> _bitsLeft) & 1;
  }

  /// <summary>Read <paramref name="count"/> bits as an unsigned integer (MSB-first).</summary>
  public int ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | ReadBit();

    return value;
  }

  private void _LoadByte() {
    if (_pos >= _end) {
      _currentByte = 0;
      _bitsLeft = 8;
      return;
    }

    _currentByte = _data[_pos];
    ++_pos;

    // After 0xFF, only 7 bits are available (bit-stuffing per ITU-T T.800)
    if (_pos >= 2 && _data[_pos - 2] == 0xFF)
      _bitsLeft = 7;
    else
      _bitsLeft = 8;
  }
}
