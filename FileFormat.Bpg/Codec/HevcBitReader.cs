using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Bpg.Codec;

/// <summary>Bit-level reader for HEVC NAL unit parsing with Exp-Golomb and fixed-length codes.</summary>
internal sealed class HevcBitReader {

  private readonly byte[] _data;
  private int _byteOffset;
  private int _bitOffset;

  public HevcBitReader(byte[] data, int offset = 0) {
    _data = data ?? throw new ArgumentNullException(nameof(data));
    _byteOffset = offset;
    _bitOffset = 0;
  }

  /// <summary>Current bit position in the stream.</summary>
  public int BitPosition => _byteOffset * 8 + _bitOffset;

  /// <summary>Number of remaining bits available.</summary>
  public int BitsRemaining => (_data.Length - _byteOffset) * 8 - _bitOffset;

  /// <summary>Whether more data is available.</summary>
  public bool HasMoreData => _byteOffset < _data.Length;

  /// <summary>Reads a single bit.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int ReadBit() {
    if (_byteOffset >= _data.Length)
      throw new InvalidOperationException("End of bitstream reached.");

    var bit = (_data[_byteOffset] >> (7 - _bitOffset)) & 1;
    if (++_bitOffset >= 8) {
      _bitOffset = 0;
      ++_byteOffset;
    }
    return bit;
  }

  /// <summary>Reads <paramref name="count"/> bits as an unsigned integer (MSB first).</summary>
  public uint ReadBits(int count) {
    if (count < 0 || count > 32)
      throw new ArgumentOutOfRangeException(nameof(count));
    if (count == 0)
      return 0;

    var result = 0u;
    for (var i = 0; i < count; ++i)
      result = (result << 1) | (uint)ReadBit();

    return result;
  }

  /// <summary>Reads a signed value using two's complement with <paramref name="count"/> bits.</summary>
  public int ReadSignedBits(int count) {
    var val = ReadBits(count);
    if ((val & (1u << (count - 1))) != 0)
      return (int)(val | (~0u << count));

    return (int)val;
  }

  /// <summary>Reads an unsigned Exp-Golomb coded value (ue(v) in the HEVC spec).</summary>
  public uint ReadUe() {
    var leadingZeros = 0;
    while (ReadBit() == 0) {
      ++leadingZeros;
      if (leadingZeros > 31)
        throw new InvalidOperationException("Exp-Golomb code too large.");
    }

    if (leadingZeros == 0)
      return 0;

    var suffix = ReadBits(leadingZeros);
    return (1u << leadingZeros) - 1 + suffix;
  }

  /// <summary>Reads a signed Exp-Golomb coded value (se(v) in the HEVC spec).</summary>
  public int ReadSe() {
    var codeNum = ReadUe();
    var sign = (codeNum & 1) != 0 ? 1 : -1;
    return sign * (int)((codeNum + 1) >> 1);
  }

  /// <summary>Reads a single flag bit as boolean.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool ReadFlag() => ReadBit() != 0;

  /// <summary>Reads a byte (8 bits).</summary>
  public byte ReadByte() => (byte)ReadBits(8);

  /// <summary>Aligns to byte boundary by skipping remaining bits in the current byte.</summary>
  public void AlignToByte() {
    if (_bitOffset != 0) {
      _bitOffset = 0;
      ++_byteOffset;
    }
  }

  /// <summary>Checks for RBSP trailing bits and consumes them.</summary>
  public void ReadTrailingBits() {
    ReadBit(); // rbsp_stop_one_bit = 1
    while (_bitOffset != 0)
      ReadBit(); // alignment zero bits
  }

  /// <summary>Checks if there is more RBSP data (not just trailing bits).</summary>
  public bool MoreRbspData() {
    if (!HasMoreData)
      return false;

    // Save position
    var savedByte = _byteOffset;
    var savedBit = _bitOffset;

    // Check if remaining bits are just trailing bits (1 followed by zeros to byte boundary)
    if (BitsRemaining <= 0)
      return false;

    // Look for the stop bit pattern
    var remaining = BitsRemaining;
    if (remaining > 8)
      return true;

    // Check if what's left could be trailing bits
    var firstBit = ReadBit();
    var allZeros = true;
    var bitsAfterFirst = 0;
    while (_bitOffset != 0 && HasMoreData) {
      if (ReadBit() != 0)
        allZeros = false;
      ++bitsAfterFirst;
    }

    // Restore position
    _byteOffset = savedByte;
    _bitOffset = savedBit;

    // It is NOT trailing bits if either the stop bit is 0 or there are non-zero alignment bits
    return firstBit != 1 || !allZeros || _byteOffset < _data.Length - 1;
  }

  /// <summary>Skips <paramref name="count"/> bits.</summary>
  public void SkipBits(int count) {
    for (var i = 0; i < count; ++i)
      ReadBit();
  }

  /// <summary>Returns the current byte offset (for sub-stream creation).</summary>
  public int ByteOffset => _byteOffset;

  /// <summary>Creates a sub-reader starting at <paramref name="offset"/> for <paramref name="length"/> bytes.</summary>
  public static HevcBitReader CreateSubReader(byte[] data, int offset, int length) {
    var subData = new byte[length];
    Buffer.BlockCopy(data, offset, subData, 0, length);
    return new(subData);
  }
}
