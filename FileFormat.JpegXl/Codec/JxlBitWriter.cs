using System;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Bit writer for JPEG XL codestreams. Writes bits LSB-first within bytes,
/// buffering up to 64 bits for efficient multi-bit writes.
/// </summary>
internal sealed class JxlBitWriter {

  private readonly MemoryStream _stream;
  private ulong _buffer;
  private int _bitsInBuffer;

  public JxlBitWriter(int capacity = 256) {
    _stream = new MemoryStream(capacity);
    _buffer = 0;
    _bitsInBuffer = 0;
  }

  /// <summary>Total number of bits written so far.</summary>
  public long BitsWritten => _stream.Length * 8 + _bitsInBuffer;

  /// <summary>Write 1..32 bits, LSB first.</summary>
  public void WriteBits(uint value, int n) {
    if (n == 0)
      return;
    if (n < 0 || n > 32)
      throw new ArgumentOutOfRangeException(nameof(n));

    _buffer |= ((ulong)value & ((1UL << n) - 1)) << _bitsInBuffer;
    _bitsInBuffer += n;
    _Flush();
  }

  /// <summary>Write 1..56 bits as ulong, LSB first.</summary>
  public void WriteBits64(ulong value, int n) {
    if (n == 0)
      return;
    if (n < 0 || n > 56)
      throw new ArgumentOutOfRangeException(nameof(n));

    _buffer |= (value & ((1UL << n) - 1)) << _bitsInBuffer;
    _bitsInBuffer += n;
    _Flush();
  }

  /// <summary>Write a single bit.</summary>
  public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

  /// <summary>
  /// Write a JPEG XL U32 using the specified distribution parameters.
  /// Selects the smallest encoding that can represent the value.
  /// </summary>
  public void WriteU32(uint value, uint c0, uint u0, uint c1, uint u1, uint c2, uint u2, uint c3, uint u3) {
    if (u0 == 0 && value == c0) {
      WriteBits(0, 2);
      return;
    }
    if (value >= c1 && value < c1 + (1u << (int)u1)) {
      WriteBits(1, 2);
      WriteBits(value - c1, (int)u1);
      return;
    }
    if (value >= c2 && value < c2 + (1u << (int)u2)) {
      WriteBits(2, 2);
      WriteBits(value - c2, (int)u2);
      return;
    }
    if (value >= c3 && (u3 == 32 || value < c3 + (1u << (int)u3))) {
      WriteBits(3, 2);
      WriteBits(value - c3, (int)u3);
      return;
    }
    // Fallback: try selector 0 if it has bits
    if (u0 > 0 && value >= c0 && value < c0 + (1u << (int)u0)) {
      WriteBits(0, 2);
      WriteBits(value - c0, (int)u0);
      return;
    }
    throw new ArgumentOutOfRangeException(nameof(value), $"Cannot encode {value} with the given U32 distribution.");
  }

  /// <summary>Write a JPEG XL U64 (variable-length uint64).</summary>
  public void WriteU64(ulong value) {
    if (value == 0) {
      WriteBits(0, 2);
      return;
    }
    if (value <= 16) {
      WriteBits(1, 2);
      WriteBits((uint)(value - 1), 4);
      return;
    }
    if (value <= 272) {
      WriteBits(2, 2);
      WriteBits((uint)(value - 17), 8);
      return;
    }
    // selector 3: 12 bits, then 8-bit groups with continuation bit
    WriteBits(3, 2);
    WriteBits((uint)(value & 0xFFF), 12);
    value >>= 12;
    var shift = 0;
    while (shift < 48) {
      if (value == 0) {
        WriteBool(false);
        break;
      }
      WriteBool(true);
      WriteBits((uint)(value & 0xFF), 8);
      value >>= 8;
      shift += 8;
    }
  }

  /// <summary>Pad with zero bits to align to the next byte boundary.</summary>
  public void ZeroPadToByte() {
    var remainder = _bitsInBuffer % 8;
    if (remainder == 0)
      return;
    WriteBits(0, 8 - remainder);
  }

  /// <summary>Write a single byte (8 bits).</summary>
  public void WriteByte(byte value) => WriteBits(value, 8);

  /// <summary>Finalize and return the written bytes.</summary>
  public byte[] ToArray() {
    // Flush any remaining bits
    while (_bitsInBuffer > 0) {
      _stream.WriteByte((byte)(_buffer & 0xFF));
      _buffer >>= 8;
      _bitsInBuffer -= Math.Min(_bitsInBuffer, 8);
    }
    return _stream.ToArray();
  }

  private void _Flush() {
    while (_bitsInBuffer >= 8) {
      _stream.WriteByte((byte)(_buffer & 0xFF));
      _buffer >>= 8;
      _bitsInBuffer -= 8;
    }
  }
}
