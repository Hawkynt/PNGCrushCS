using System;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Writes individual bits to a byte stream (MSB-first), applying JPEG 2000 marker byte-stuffing.</summary>
internal sealed class BitWriter {

  private readonly MemoryStream _output;
  private int _currentByte;
  private int _bitsUsed;
  private bool _lastByteWasFF;

  public BitWriter() {
    _output = new MemoryStream();
    _currentByte = 0;
    _bitsUsed = 0;
    _lastByteWasFF = false;
  }

  /// <summary>Write a single bit (0 or 1).</summary>
  public void WriteBit(int bit) {
    var maxBits = _lastByteWasFF ? 7 : 8;
    _currentByte = (_currentByte << 1) | (bit & 1);
    ++_bitsUsed;
    if (_bitsUsed == maxBits)
      _FlushByte();
  }

  /// <summary>Write <paramref name="count"/> bits from an integer value (MSB-first).</summary>
  public void WriteBits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      WriteBit((value >> i) & 1);
  }

  /// <summary>Flush any remaining partial byte and return the encoded data.</summary>
  public byte[] Flush() {
    if (_bitsUsed > 0) {
      var maxBits = _lastByteWasFF ? 7 : 8;
      _currentByte <<= (maxBits - _bitsUsed);
      _output.WriteByte((byte)_currentByte);
      _lastByteWasFF = (byte)_currentByte == 0xFF;
      _bitsUsed = 0;
      _currentByte = 0;
    }
    return _output.ToArray();
  }

  private void _FlushByte() {
    _output.WriteByte((byte)_currentByte);
    _lastByteWasFF = (byte)_currentByte == 0xFF;
    _currentByte = 0;
    _bitsUsed = 0;
  }
}
