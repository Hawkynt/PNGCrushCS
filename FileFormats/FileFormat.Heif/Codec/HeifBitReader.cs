using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Heif.Codec;

/// <summary>Bit-level reader for HEVC RBSP (Raw Byte Sequence Payload) data.
/// Supports Exp-Golomb coded integers and signed/unsigned variants.</summary>
internal sealed class HeifBitReader {

  private readonly byte[] _data;
  private int _byteOffset;
  private int _bitOffset;
  private readonly int _endByte;

  public HeifBitReader(byte[] data, int offset, int length) {
    _data = data ?? throw new ArgumentNullException(nameof(data));
    _byteOffset = offset;
    _bitOffset = 0;
    _endByte = offset + length;
  }

  public HeifBitReader(byte[] data) : this(data, 0, data.Length) { }

  /// <summary>Bits remaining in the stream.</summary>
  public int BitsRemaining => ((_endByte - _byteOffset) * 8) - _bitOffset;

  /// <summary>Whether the reader has been exhausted.</summary>
  public bool IsAtEnd => _byteOffset >= _endByte;

  /// <summary>Reads a single bit.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int ReadBit() {
    if (_byteOffset >= _endByte)
      throw new InvalidOperationException("HEVC: read past end of RBSP.");

    var bit = (_data[_byteOffset] >> (7 - _bitOffset)) & 1;
    if (++_bitOffset == 8) {
      _bitOffset = 0;
      ++_byteOffset;
    }
    return bit;
  }

  /// <summary>Reads up to 32 unsigned bits.</summary>
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

  /// <summary>Reads a boolean.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool ReadBool() => ReadBit() != 0;

  /// <summary>Reads an unsigned Exp-Golomb coded integer (ue(v)).</summary>
  public uint ReadUe() {
    var leadingZeros = 0;
    while (ReadBit() == 0) {
      ++leadingZeros;
      if (leadingZeros > 31)
        return uint.MaxValue;
    }
    if (leadingZeros == 0)
      return 0;

    var value = ReadBits(leadingZeros);
    return (1u << leadingZeros) - 1 + value;
  }

  /// <summary>Reads a signed Exp-Golomb coded integer (se(v)).</summary>
  public int ReadSe() {
    var codeNum = ReadUe();
    var value = (int)((codeNum + 1) >> 1);
    return (codeNum & 1) == 0 ? -value : value;
  }

  /// <summary>Aligns to the next byte boundary.</summary>
  public void ByteAlign() {
    if (_bitOffset != 0) {
      _bitOffset = 0;
      ++_byteOffset;
    }
  }

  /// <summary>Checks for RBSP trailing bits (used to verify end of NAL unit).</summary>
  public bool HasMoreRbspData() {
    if (_byteOffset >= _endByte)
      return false;

    if (_byteOffset == _endByte - 1) {
      // Check if remaining bits are just stop bit + alignment zeros
      var remainingBits = 8 - _bitOffset;
      if (remainingBits <= 0)
        return false;

      var lastByte = _data[_byteOffset];
      var mask = (1 << remainingBits) - 1;
      var remaining = lastByte & mask;
      // If remaining is just a single 1 bit followed by zeros, it's trailing bits
      return remaining != (1 << (remainingBits - 1));
    }

    return true;
  }
}
