using System;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Reads JPEG 2000 packet-header bits MSB first with the B.10.1 bit-stuffing rule.</summary>
internal sealed class BitReader {

  private readonly byte[] _data;
  private int _pos;
  private readonly int _end;
  private int _bitsLeft;
  private int _currentByte;

  /// <summary>Current byte position in the source, including a byte already loaded for bit access.</summary>
  public int Position => _pos;

  public BitReader(byte[] data, int offset, int length) {
    ArgumentNullException.ThrowIfNull(data);
    if ((uint)offset > (uint)data.Length)
      throw new ArgumentOutOfRangeException(nameof(offset));
    if (length < 0 || offset + length > data.Length)
      throw new ArgumentOutOfRangeException(nameof(length));

    _data = data;
    _pos = offset;
    _end = offset + length;
  }

  public int ReadBit() {
    if (_bitsLeft == 0)
      _LoadByte();

    --_bitsLeft;
    return (_currentByte >> _bitsLeft) & 1;
  }

  public int ReadBits(int count) {
    if (count < 0 || count > 31)
      throw new ArgumentOutOfRangeException(nameof(count));

    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | ReadBit();
    return value;
  }

  /// <summary>
  /// Discards packet-header padding and positions <see cref="Position"/> at the first packet-body
  /// byte. If the last full header byte was FF, this also consumes the mandatory stuffed-zero byte.
  /// </summary>
  public void AlignToByte() {
    if (_bitsLeft > 0) {
      _bitsLeft = 0;
      _currentByte = 0;
      return;
    }

    if (_pos == 0 || _pos >= _end || _data[_pos - 1] != 0xFF)
      return;

    if ((_data[_pos] & 0x80) != 0)
      throw new InvalidDataException("JPEG 2000 packet header has a one in the stuffed bit following 0xFF.");

    ++_pos;
  }

  private void _LoadByte() {
    if (_pos >= _end)
      throw new InvalidDataException("JPEG 2000 packet header ends in the middle of a syntax element.");

    _currentByte = _data[_pos++];

    // The most-significant bit of a byte following FF is the stuffed zero and is not syntax.
    _bitsLeft = _pos >= 2 && _data[_pos - 2] == 0xFF ? 7 : 8;
  }
}
