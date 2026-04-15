using System;

namespace FileFormat.WebP.Vp8;

/// <summary>
/// VP8 boolean arithmetic bit writer — inverse of <see cref="Vp8Partition"/>.
/// Port of libwebp's <c>VP8BitWriter</c> (src/utils/bit_writer_utils.c) by Skal / Pascal Massimino.
/// </summary>
internal sealed class Vp8BitWriter {

  // renorm_sizes[i] = 8 - log2(i); used when range falls below 127.
  private static readonly byte[] _KNorm = [
    7, 6, 6, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3,
    3, 3, 3, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
    2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0,
  ];

  // range = ((range + 1) << kVP8Log2Range[range]) - 1
  private static readonly byte[] _KNewRange = [
    127, 127, 191, 127, 159, 191, 223, 127, 143, 159, 175, 191, 207, 223, 239,
    127, 135, 143, 151, 159, 167, 175, 183, 191, 199, 207, 215, 223, 231, 239,
    247, 127, 131, 135, 139, 143, 147, 151, 155, 159, 163, 167, 171, 175, 179,
    183, 187, 191, 195, 199, 203, 207, 211, 215, 219, 223, 227, 231, 235, 239,
    243, 247, 251, 127, 129, 131, 133, 135, 137, 139, 141, 143, 145, 147, 149,
    151, 153, 155, 157, 159, 161, 163, 165, 167, 169, 171, 173, 175, 177, 179,
    181, 183, 185, 187, 189, 191, 193, 195, 197, 199, 201, 203, 205, 207, 209,
    211, 213, 215, 217, 219, 221, 223, 225, 227, 229, 231, 233, 235, 237, 239,
    241, 243, 245, 247, 249, 251, 253, 127,
  ];

  private int _range;    // range - 1
  private int _value;
  private int _run;      // number of outstanding 0xff bytes awaiting carry resolution
  private int _nbBits;   // number of pending bits; starts at -8

  private byte[] _buf;
  private int _pos;

  public Vp8BitWriter(int expectedSize = 4096) {
    _range = 254;
    _value = 0;
    _run = 0;
    _nbBits = -8;
    _buf = new byte[Math.Max(1024, expectedSize)];
    _pos = 0;
  }

  /// <summary>Total bytes written so far (after Finish).</summary>
  public int Position => _pos;

  /// <summary>Approximate bit-position for cost/pending calculations.</summary>
  public long BitPosition {
    get {
      long nbBits = 8 + _nbBits;
      return (long)(_pos + _run) * 8 + nbBits;
    }
  }

  /// <summary>Write one boolean bit with 0-probability = prob/256.</summary>
  public int PutBit(int bit, int prob) {
    var split = _range * prob >> 8;
    if (bit != 0) {
      _value += split + 1;
      _range -= split + 1;
    } else {
      _range = split;
    }
    if (_range < 127) {
      var shift = _KNorm[_range];
      _range = _KNewRange[_range];
      _value <<= shift;
      _nbBits += shift;
      if (_nbBits > 0) _Flush();
    }
    return bit;
  }

  /// <summary>Write one uniform (prob=128) bit — slightly faster variant using split = range/2.</summary>
  public int PutBitUniform(int bit) {
    var split = _range >> 1;
    if (bit != 0) {
      _value += split + 1;
      _range -= split + 1;
    } else {
      _range = split;
    }
    if (_range < 127) {
      _range = _KNewRange[_range];
      _value <<= 1;
      _nbBits += 1;
      if (_nbBits > 0) _Flush();
    }
    return bit;
  }

  /// <summary>Write <paramref name="nBits"/> bits MSB-first as uniform bits.</summary>
  public void PutBits(uint value, int nBits) {
    for (var mask = 1u << nBits - 1; mask != 0; mask >>= 1)
      PutBitUniform((value & mask) != 0 ? 1 : 0);
  }

  /// <summary>Write a signed integer with flag/sign encoding: flag + (n+1) bits.</summary>
  public void PutSignedBits(int value, int nBits) {
    if (PutBitUniform(value != 0 ? 1 : 0) == 0) return;
    if (value < 0) PutBits((uint)(-value << 1 | 1), nBits + 1);
    else PutBits((uint)(value << 1), nBits + 1);
  }

  /// <summary>Append raw bytes (must have called <see cref="Finish"/> first).</summary>
  public void Append(byte[] data, int offset, int size) {
    if (_nbBits != -8) throw new InvalidOperationException("Append requires prior Finish()");
    _EnsureCapacity(size);
    Buffer.BlockCopy(data, offset, _buf, _pos, size);
    _pos += size;
  }

  /// <summary>Pad with zeros and flush. Returns the final byte array slice.</summary>
  public byte[] Finish() {
    PutBits(0, 9 - _nbBits);
    _nbBits = 0;
    _Flush();
    var result = new byte[_pos];
    Buffer.BlockCopy(_buf, 0, result, 0, _pos);
    return result;
  }

  // Emit one byte (or the carry resolution of a pending run of 0xff bytes).
  private void _Flush() {
    var s = 8 + _nbBits;
    var bits = _value >> s;
    _value -= bits << s;
    _nbBits -= 8;
    if ((bits & 0xff) != 0xff) {
      var pos = _pos;
      _EnsureCapacity(_run + 1);
      if ((bits & 0x100) != 0) {
        // Overflow: propagate carry over pending 0xff bytes.
        if (pos > 0) ++_buf[pos - 1];
      }
      if (_run > 0) {
        var value = (bits & 0x100) != 0 ? (byte)0x00 : (byte)0xff;
        for (; _run > 0; --_run)
          _buf[pos++] = value;
      }
      _buf[pos++] = (byte)(bits & 0xff);
      _pos = pos;
    } else {
      // Delay writing 0xff: it may need to become 0x00 if a later carry propagates.
      ++_run;
    }
  }

  private void _EnsureCapacity(int extra) {
    var needed = _pos + extra;
    if (needed <= _buf.Length) return;
    var newSize = _buf.Length * 2;
    if (newSize < needed) newSize = needed;
    if (newSize < 1024) newSize = 1024;
    var newBuf = new byte[newSize];
    Buffer.BlockCopy(_buf, 0, newBuf, 0, _pos);
    _buf = newBuf;
  }
}
