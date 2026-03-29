using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Avif.Codec;

/// <summary>Bit-level reader for AV1 OBU (Open Bitstream Unit) parsing with leb128, uvlc, ns, and su coding.</summary>
internal sealed class Av1BitReader {

  private readonly byte[] _data;
  private int _byteOffset;
  private int _bitOffset;
  private readonly int _endByte;

  public Av1BitReader(byte[] data, int offset, int length) {
    _data = data ?? throw new ArgumentNullException(nameof(data));
    _byteOffset = offset;
    _bitOffset = 0;
    _endByte = offset + length;
  }

  public Av1BitReader(byte[] data) : this(data, 0, data.Length) { }

  /// <summary>Current bit position within the stream.</summary>
  public int BitPosition => (_byteOffset * 8) + _bitOffset;

  /// <summary>Number of bits remaining.</summary>
  public int BitsRemaining => ((_endByte - _byteOffset) * 8) - _bitOffset;

  /// <summary>Whether the reader has reached the end of the data.</summary>
  public bool IsAtEnd => _byteOffset >= _endByte && _bitOffset == 0;

  /// <summary>Reads a single bit.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int ReadBit() {
    if (_byteOffset >= _endByte)
      throw new InvalidOperationException("AV1 bitstream: read past end of data.");

    var bit = (_data[_byteOffset] >> (7 - _bitOffset)) & 1;
    if (++_bitOffset == 8) {
      _bitOffset = 0;
      ++_byteOffset;
    }
    return bit;
  }

  /// <summary>Reads up to 32 bits as an unsigned integer (MSB first).</summary>
  public uint ReadBits(int n) {
    if (n == 0)
      return 0;
    if (n > 32)
      throw new ArgumentOutOfRangeException(nameof(n));

    var result = 0u;
    for (var i = 0; i < n; ++i)
      result = (result << 1) | (uint)ReadBit();

    return result;
  }

  /// <summary>Reads a boolean (1 bit).</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool ReadBool() => ReadBit() != 0;

  /// <summary>Reads an unsigned value encoded using leb128 (AV1 spec 4.10.5).</summary>
  public ulong ReadLeb128() {
    var value = 0UL;
    for (var i = 0; i < 8; ++i) {
      var b = ReadBits(8);
      value |= ((ulong)(b & 0x7F)) << (i * 7);
      if ((b & 0x80) == 0)
        break;
    }
    return value;
  }

  /// <summary>Reads an unsigned variable-length code (AV1 spec 4.10.3).</summary>
  public uint ReadUvlc() {
    var leadingZeros = 0;
    while (!ReadBool()) {
      ++leadingZeros;
      if (leadingZeros >= 32)
        return uint.MaxValue;
    }
    if (leadingZeros >= 32)
      return uint.MaxValue;

    var value = ReadBits(leadingZeros);
    return value + (1u << leadingZeros) - 1;
  }

  /// <summary>Reads a signed value encoded in su(n) format (AV1 spec 4.10.6).</summary>
  public int ReadSu(int n) {
    var value = (int)ReadBits(n);
    var signMask = 1 << (n - 1);
    if ((value & signMask) != 0)
      value -= 2 * signMask;

    return value;
  }

  /// <summary>Reads a non-symmetric unsigned value in the range [0, n) (AV1 spec 4.10.7).</summary>
  public uint ReadNs(uint n) {
    if (n <= 1)
      return 0;

    var w = _FloorLog2(n) + 1;
    var m = (1u << w) - n;
    var v = ReadBits(w - 1);
    if (v < m)
      return v;

    var extraBit = ReadBit();
    return (v << 1) - m + (uint)extraBit;
  }

  /// <summary>Reads a delta-coded quantizer value (AV1 spec).</summary>
  public int ReadDeltaQ() {
    if (ReadBool())
      return ReadSu(7);
    return 0;
  }

  /// <summary>Aligns to the next byte boundary.</summary>
  public void ByteAlign() {
    if (_bitOffset != 0) {
      _bitOffset = 0;
      ++_byteOffset;
    }
  }

  /// <summary>Skips a specified number of bits.</summary>
  public void Skip(int bits) {
    var totalBits = (_bitOffset + bits);
    _byteOffset += totalBits / 8;
    _bitOffset = totalBits % 8;
  }

  /// <summary>Gets the current byte offset (after alignment).</summary>
  public int ByteOffset {
    get {
      if (_bitOffset != 0)
        return _byteOffset + 1;
      return _byteOffset;
    }
  }

  /// <summary>Creates a sub-reader for a byte range within the current data.</summary>
  public Av1BitReader CreateSubReader(int byteOffset, int length)
    => new(_data, byteOffset, length);

  private static int _FloorLog2(uint n) {
    var s = 0;
    while ((1u << (s + 1)) <= n)
      ++s;
    return s;
  }
}
