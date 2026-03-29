using System;
using System.Runtime.CompilerServices;

namespace FileFormat.JpegXr.Codec;

/// <summary>Bit-level reader for JPEG XR compressed data with byte-stuffing support.</summary>
internal sealed class JxrBitReader {

  private readonly byte[] _data;
  private int _byteOffset;
  private int _bitOffset;

  /// <summary>Current byte position in the data stream.</summary>
  public int ByteOffset => _byteOffset;

  /// <summary>Current bit position within the current byte (0..7).</summary>
  public int BitOffset => _bitOffset;

  /// <summary>Whether the reader has reached or exceeded the end of the data.</summary>
  public bool IsEof => _byteOffset >= _data.Length;

  public JxrBitReader(byte[] data, int startOffset = 0) {
    ArgumentNullException.ThrowIfNull(data);
    _data = data;
    _byteOffset = startOffset;
    _bitOffset = 0;
  }

  /// <summary>Reads a single bit, returning 0 or 1.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int ReadBit() {
    if (_byteOffset >= _data.Length)
      return 0;

    var bit = (_data[_byteOffset] >> (7 - _bitOffset)) & 1;
    if (++_bitOffset >= 8) {
      _bitOffset = 0;
      ++_byteOffset;
      _HandleByteStuffing();
    }
    return bit;
  }

  /// <summary>Reads up to 32 bits as an unsigned integer, MSB first.</summary>
  public uint ReadBits(int count) {
    if (count <= 0 || count > 32)
      throw new ArgumentOutOfRangeException(nameof(count), count, "Must be 1..32.");

    var result = 0u;
    for (var i = 0; i < count; ++i)
      result = (result << 1) | (uint)ReadBit();

    return result;
  }

  /// <summary>Reads a signed value using two's complement.</summary>
  public int ReadSignedBits(int count) {
    if (count <= 0 || count > 32)
      throw new ArgumentOutOfRangeException(nameof(count), count, "Must be 1..32.");

    var value = ReadBits(count);
    var signBit = 1u << (count - 1);
    if ((value & signBit) != 0)
      return (int)(value | ~(signBit - 1));

    return (int)value;
  }

  /// <summary>Reads an unsigned 8-bit value.</summary>
  public byte ReadByte() => (byte)ReadBits(8);

  /// <summary>Reads an unsigned 16-bit value (big-endian bit order).</summary>
  public ushort ReadUInt16() => (ushort)ReadBits(16);

  /// <summary>Reads a unary-coded value (count of 1-bits before first 0-bit).</summary>
  public int ReadUnary() {
    var count = 0;
    while (ReadBit() == 1)
      ++count;
    return count;
  }

  /// <summary>Reads a variable-length code from an adaptive VLC table.</summary>
  public int ReadAdaptiveVlc(JxrVlcTable table) {
    ArgumentNullException.ThrowIfNull(table);
    return table.Decode(this);
  }

  /// <summary>Aligns the reader to the next byte boundary.</summary>
  public void AlignToByte() {
    if (_bitOffset <= 0)
      return;

    _bitOffset = 0;
    ++_byteOffset;
    _HandleByteStuffing();
  }

  /// <summary>Skips the given number of bytes, aligning first if needed.</summary>
  public void SkipBytes(int count) {
    AlignToByte();
    _byteOffset += count;
  }

  /// <summary>Peeks at the next N bits without advancing the position.</summary>
  public uint PeekBits(int count) {
    var savedByte = _byteOffset;
    var savedBit = _bitOffset;
    var value = ReadBits(count);
    _byteOffset = savedByte;
    _bitOffset = savedBit;
    return value;
  }

  /// <summary>
  /// Handles JPEG XR byte stuffing: after reading 0xFF, if the next byte is 0x00, skip it.
  /// This prevents confusion with marker bytes.
  /// </summary>
  private void _HandleByteStuffing() {
    if (_byteOffset < 2 || _byteOffset >= _data.Length)
      return;

    if (_data[_byteOffset - 1] == 0xFF && _data[_byteOffset] == 0x00)
      ++_byteOffset;
  }
}

/// <summary>Adaptive variable-length code table for JPEG XR entropy coding.</summary>
internal sealed class JxrVlcTable {

  /// <summary>Table entries: each is (codeLength, value). Indexed by up to 8-bit prefix.</summary>
  private readonly (int Length, int Value)[] _entries;

  /// <summary>Maximum lookup bits for this table.</summary>
  private readonly int _maxBits;

  /// <summary>Discrimination threshold for table adaptation.</summary>
  private int _discriminant;

  /// <summary>Which sub-table to use (adapted based on statistics).</summary>
  private int _tableIndex;

  /// <summary>Count of symbols decoded since last adaptation.</summary>
  private int _symbolCount;

  /// <summary>Sum of decoded values for adaptation.</summary>
  private long _valueSum;

  private static readonly (int Length, int Value)[][] _DefaultTables = _BuildDefaultTables();

  public JxrVlcTable(int initialTableIndex = 0) {
    _tableIndex = Math.Clamp(initialTableIndex, 0, _DefaultTables.Length - 1);
    _entries = _DefaultTables[_tableIndex];
    _maxBits = 8;
    _discriminant = 0;
    _symbolCount = 0;
    _valueSum = 0;
  }

  /// <summary>Decodes one symbol from the bit reader and adapts the table.</summary>
  public int Decode(JxrBitReader reader) {
    // Try table-based decode with up to _maxBits prefix
    var prefix = (int)reader.PeekBits(_maxBits);
    var entry = _entries[prefix & ((_entries.Length) - 1)];

    int value;
    if (entry.Length > 0) {
      reader.ReadBits(entry.Length);
      value = entry.Value;
    } else {
      // Fallback: read as unary + suffix
      var unary = reader.ReadUnary();
      if (unary < 4)
        value = unary;
      else {
        var extra = reader.ReadBits(unary - 3);
        value = (1 << (unary - 3)) + (int)extra + 3;
      }
    }

    _Adapt(value);
    return value;
  }

  /// <summary>Adapts the VLC table based on the distribution of decoded values.</summary>
  private void _Adapt(int value) {
    _valueSum += Math.Abs(value);
    ++_symbolCount;

    // Adapt every 16 symbols
    if (_symbolCount < 16)
      return;

    var average = _valueSum / _symbolCount;
    var newIndex = average switch {
      < 2 => 0,
      < 4 => 1,
      < 8 => 2,
      _ => 3
    };

    if (newIndex != _tableIndex) {
      _tableIndex = Math.Clamp((int)newIndex, 0, _DefaultTables.Length - 1);
      Array.Copy(_DefaultTables[_tableIndex], _entries, Math.Min(_DefaultTables[_tableIndex].Length, _entries.Length));
    }

    _discriminant = (int)newIndex;
    _symbolCount = 0;
    _valueSum = 0;
  }

  private static (int Length, int Value)[][] _BuildDefaultTables() {
    // Build 4 default VLC tables for different value distributions
    // Table 0: biased toward small values (0, 1)
    // Table 1: moderate values
    // Table 2: larger values
    // Table 3: wide range
    var tables = new (int Length, int Value)[4][];

    for (var t = 0; t < 4; ++t) {
      tables[t] = new (int, int)[256];
      for (var i = 0; i < 256; ++i) {
        // Prefix-based decode: leading zeros give the magnitude
        var leadingOnes = 0;
        for (var b = 7; b >= 0; --b) {
          if ((i & (1 << b)) != 0)
            ++leadingOnes;
          else
            break;
        }

        var shift = t + 1;
        tables[t][i] = leadingOnes switch {
          0 => (1, 0),
          1 => (2, 1),
          2 => (3, 2 + (i >> 5 & 1)),
          3 => (4 + shift / 2, 4 + ((i >> (4 - shift / 2)) & ((1 << (shift / 2 + 1)) - 1))),
          _ => (0, 0) // use fallback
        };
      }
    }

    return tables;
  }

  /// <summary>Resets adaptation state.</summary>
  public void Reset() {
    _symbolCount = 0;
    _valueSum = 0;
    _discriminant = 0;
    _tableIndex = 0;
    Array.Copy(_DefaultTables[0], _entries, Math.Min(_DefaultTables[0].Length, _entries.Length));
  }
}

/// <summary>Bit-level writer for JPEG XR compressed data with byte-stuffing support.</summary>
internal sealed class JxrBitWriter {

  private byte[] _buffer;
  private int _byteOffset;
  private int _bitOffset;
  private byte _current;

  /// <summary>Number of bytes written so far (excluding partial byte).</summary>
  public int BytesWritten => _byteOffset + (_bitOffset > 0 ? 1 : 0);

  public JxrBitWriter(int initialCapacity = 4096) {
    _buffer = new byte[initialCapacity];
    _byteOffset = 0;
    _bitOffset = 0;
    _current = 0;
  }

  /// <summary>Writes a single bit (0 or 1).</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void WriteBit(int bit) {
    _current |= (byte)((bit & 1) << (7 - _bitOffset));
    if (++_bitOffset >= 8)
      _FlushByte();
  }

  /// <summary>Writes up to 32 bits from an unsigned integer, MSB first.</summary>
  public void WriteBits(uint value, int count) {
    for (var i = count - 1; i >= 0; --i)
      WriteBit((int)((value >> i) & 1));
  }

  /// <summary>Writes a signed value in two's complement.</summary>
  public void WriteSignedBits(int value, int count) => WriteBits((uint)value & ((1u << count) - 1), count);

  /// <summary>Writes a unary code (N ones followed by a zero).</summary>
  public void WriteUnary(int count) {
    for (var i = 0; i < count; ++i)
      WriteBit(1);
    WriteBit(0);
  }

  /// <summary>Aligns output to the next byte boundary, padding with zeros.</summary>
  public void AlignToByte() {
    if (_bitOffset > 0)
      _FlushByte();
  }

  /// <summary>Writes a raw byte.</summary>
  public void WriteByte(byte value) {
    AlignToByte();
    _EnsureCapacity(_byteOffset + 1);
    _buffer[_byteOffset++] = value;
    _HandleByteStuffing();
  }

  /// <summary>Returns the written data as a byte array.</summary>
  public byte[] ToArray() {
    var length = _byteOffset;
    if (_bitOffset > 0) {
      _EnsureCapacity(_byteOffset + 1);
      _buffer[_byteOffset] = _current;
      ++length;
    }
    var result = new byte[length];
    Array.Copy(_buffer, result, length);
    return result;
  }

  private void _FlushByte() {
    _EnsureCapacity(_byteOffset + 1);
    _buffer[_byteOffset++] = _current;
    _HandleByteStuffing();
    _current = 0;
    _bitOffset = 0;
  }

  private void _HandleByteStuffing() {
    if (_byteOffset < 1)
      return;

    // After writing 0xFF, insert a 0x00 stuff byte
    if (_buffer[_byteOffset - 1] != 0xFF)
      return;

    _EnsureCapacity(_byteOffset + 1);
    _buffer[_byteOffset++] = 0x00;
  }

  private void _EnsureCapacity(int required) {
    if (required <= _buffer.Length)
      return;

    var newSize = Math.Max(_buffer.Length * 2, required);
    var newBuffer = new byte[newSize];
    Array.Copy(_buffer, newBuffer, _byteOffset);
    _buffer = newBuffer;
  }
}
