using System;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Writes JPEG 2000 packet-header bits MSB first with the B.10.1 bit-stuffing rule.</summary>
internal sealed class BitWriter {

  private readonly MemoryStream _output = new();
  private int _currentByte;
  private int _bitsUsed;
  private bool _lastByteWasFF;

  /// <summary>Write a single bit.</summary>
  public void WriteBit(int bit) {
    var capacity = _lastByteWasFF ? 7 : 8;
    _currentByte = (_currentByte << 1) | (bit & 1);
    ++_bitsUsed;

    if (_bitsUsed == capacity)
      _FlushByte();
  }

  /// <summary>Write <paramref name="count"/> bits, most significant bit first.</summary>
  public void WriteBits(int value, int count) {
    if (count < 0 || count > 31)
      throw new ArgumentOutOfRangeException(nameof(count));

    for (var bit = count - 1; bit >= 0; --bit)
      WriteBit((value >> bit) & 1);
  }

  /// <summary>
  /// Pads the packet header to a byte boundary and returns it.
  /// </summary>
  /// <remarks>
  /// T.800 B.10.1 has one non-obvious edge case: a packet header is not allowed to end on FF. If a
  /// fully occupied byte is FF, the stuffed zero bit that belongs to the following byte still has to
  /// be present even when there are no more syntax bits. The previous writer omitted that byte, so a
  /// decoder began the packet body where it was still expecting the stuffed bit.
  /// </remarks>
  public byte[] Flush() {
    if (_bitsUsed > 0) {
      var capacity = _lastByteWasFF ? 7 : 8;
      _currentByte <<= capacity - _bitsUsed;
      _FlushByte();
    }

    if (_lastByteWasFF) {
      _output.WriteByte(0);
      _lastByteWasFF = false;
    }

    return _output.ToArray();
  }

  private void _FlushByte() {
    var value = (byte)_currentByte;
    _output.WriteByte(value);
    _lastByteWasFF = value == 0xFF;
    _currentByte = 0;
    _bitsUsed = 0;
  }
}
