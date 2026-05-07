using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Bit reader for JPEG XL codestreams. Reads bits LSB-first within bytes,
/// buffering up to 64 bits for efficient multi-bit reads.
/// </summary>
internal sealed class JxlBitReader {

  private readonly byte[] _data;
  private int _bytePos;
  private ulong _buffer;
  private int _bitsInBuffer;

  public JxlBitReader(byte[] data, int offset) {
    _data = data ?? throw new ArgumentNullException(nameof(data));
    _bytePos = offset;
    _buffer = 0;
    _bitsInBuffer = 0;
    _Refill();
  }

  /// <summary>Current byte-aligned position in the source data.</summary>
  public int Position => _bytePos - _bitsInBuffer / 8;

  /// <summary>Total number of bits consumed so far.</summary>
  public long BitsRead => (long)_bytePos * 8 - _bitsInBuffer;

  /// <summary>Read 1..32 bits, LSB first.</summary>
  public uint ReadBits(int n) {
    if (n == 0)
      return 0;
    if (n < 0 || n > 32)
      throw new ArgumentOutOfRangeException(nameof(n), "Must be 0..32.");

    _EnsureBits(n);
    var result = (uint)(_buffer & ((1UL << n) - 1));
    _buffer >>= n;
    _bitsInBuffer -= n;
    return result;
  }

  /// <summary>Read 1..56 bits as ulong, LSB first.</summary>
  public ulong ReadBits64(int n) {
    if (n == 0)
      return 0;
    if (n < 0 || n > 56)
      throw new ArgumentOutOfRangeException(nameof(n), "Must be 0..56.");

    _EnsureBits(n);
    var result = _buffer & ((1UL << n) - 1);
    _buffer >>= n;
    _bitsInBuffer -= n;
    return result;
  }

  /// <summary>Read a single bit as boolean.</summary>
  public bool ReadBool() => ReadBits(1) != 0;

  /// <summary>
  /// Read a JPEG XL U32 (variable-length unsigned int).
  /// Uses a 2-bit selector followed by a variable-length payload.
  /// Each of the 4 selector values has a constant offset and a number of extra bits.
  /// </summary>
  public uint ReadU32(uint c0, uint u0, uint c1, uint u1, uint c2, uint u2, uint c3, uint u3) {
    var selector = ReadBits(2);
    return selector switch {
      0 => c0 + ReadBits((int)u0),
      1 => c1 + ReadBits((int)u1),
      2 => c2 + ReadBits((int)u2),
      3 => c3 + ReadBits((int)u3),
      _ => 0
    };
  }

  /// <summary>
  /// Read a JPEG XL U64 (variable-length uint64). Encoding per ISO/IEC 18181-1 §3.4.1:
  /// 2-bit selector, then variable payload.
  /// Selector 0 -> 0; selector 1 -> 1 + u(4); selector 2 -> 17 + u(8); selector 3 -> u(12)
  /// followed by, while the next continuation bit is 1, an additional 8-bit group placed
  /// at the current shift (12, 20, 28, …, 52). When shift == 60, the final group is 4 bits
  /// (since 60 + 4 = 64) and the loop terminates unconditionally afterwards.
  /// </summary>
  public ulong ReadU64() {
    var selector = ReadBits(2);
    if (selector == 0)
      return 0;
    if (selector == 1)
      return 1 + ReadBits(4);
    if (selector == 2)
      return 17 + ReadBits(8);

    // selector == 3: read 12 bits, then continuation-prefixed 8-bit (or final 4-bit) groups.
    var result = (ulong)ReadBits(12);
    var shift = 12;
    while (ReadBool()) {
      if (shift == 60) {
        result |= (ulong)ReadBits(4) << shift;
        break;
      }
      result |= (ulong)ReadBits(8) << shift;
      shift += 8;
    }
    return result;
  }

  /// <summary>Skip bits to align to the next byte boundary, verifying skipped bits are zero.</summary>
  public void ZeroPadToByte() {
    var remainder = _bitsInBuffer % 8;
    if (remainder == 0)
      return;

    var pad = ReadBits(remainder);
    if (pad != 0)
      throw new InvalidOperationException("Non-zero bits in zero-pad-to-byte.");
  }

  /// <summary>Read a single uncompressed byte (8 bits).</summary>
  public byte ReadByte() => (byte)ReadBits(8);

  /// <summary>Whether there are at least <paramref name="n"/> bits remaining.</summary>
  public bool HasBits(int n) {
    if (_bitsInBuffer >= n)
      return true;
    return (_data.Length - _bytePos) * 8 + _bitsInBuffer >= n;
  }

  /// <summary>Total number of bits still available in the source (buffered + unread bytes).</summary>
  public long BitsAvailable => (long)_bitsInBuffer + (long)(_data.Length - _bytePos) * 8;

  /// <summary>
  /// Advance the bit position by <paramref name="bitCount"/> bits without decoding them.
  /// Used to skip over opaque payloads (e.g., unknown extension bodies). If
  /// <paramref name="bitCount"/> exceeds the remaining bits in the stream, the position
  /// is clamped at end-of-stream and the actual number of bits skipped is returned. This
  /// matches libjxl's <c>BitReader::SkipBits</c> over-read semantics — it is the caller's
  /// responsibility to validate against EOF when the payload is meant to be consumable.
  /// </summary>
  public long Skip(long bitCount) {
    if (bitCount <= 0)
      return 0;

    var remaining = bitCount;

    // Drain the in-memory buffer first.
    if (_bitsInBuffer > 0) {
      var take = (int)Math.Min(remaining, _bitsInBuffer);
      _buffer >>= take;
      _bitsInBuffer -= take;
      remaining -= take;
      if (remaining == 0)
        return bitCount;
    }

    // Buffer is now empty — skip whole bytes directly.
    var bytesAvailable = _data.Length - _bytePos;
    var wholeBytes = remaining / 8;
    if (wholeBytes >= bytesAvailable) {
      // Clamp at EOF: consume everything that's left.
      var skipped = bitCount - remaining + (long)bytesAvailable * 8;
      _bytePos = _data.Length;
      _buffer = 0;
      _bitsInBuffer = 0;
      return skipped;
    }

    _bytePos += (int)wholeBytes;
    remaining -= wholeBytes * 8;

    // Refill and consume the trailing sub-byte remainder, if any.
    _Refill();
    if (remaining > 0) {
      // remaining is < 8 here, and _Refill always loads at least 8 bits when data exists.
      var take = (int)Math.Min(remaining, _bitsInBuffer);
      _buffer >>= take;
      _bitsInBuffer -= take;
      remaining -= take;
    }
    return bitCount - remaining;
  }

  private void _EnsureBits(int n) {
    if (_bitsInBuffer >= n)
      return;
    _Refill();
    if (_bitsInBuffer < n)
      throw new InvalidOperationException($"Not enough data: need {n} bits but only {_bitsInBuffer} available.");
  }

  private void _Refill() {
    while (_bitsInBuffer <= 56 && _bytePos < _data.Length) {
      _buffer |= (ulong)_data[_bytePos] << _bitsInBuffer;
      ++_bytePos;
      _bitsInBuffer += 8;
    }
  }
}
