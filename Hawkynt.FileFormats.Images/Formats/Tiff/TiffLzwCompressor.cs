using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Tiff;

/// <summary>TIFF LZW codec (MSB-first, 9-bit initial, 12-bit max, CLEAR=256, EOI=257).</summary>
internal static class TiffLzwCompressor {

  private const int ClearCode = 256;
  private const int EoiCode = 257;
  private const int FirstCode = 258;
  private const int MaxBits = 12;
  private const int MaxTableSize = 1 << MaxBits; // 4096

  /// <summary>Decompresses TIFF LZW data.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> data, int expectedLength) {
    var output = new byte[expectedLength];
    var outPos = 0;
    var reader = new BitReader(data);

    var codeSize = 9;
    var table = new List<byte[]>(MaxTableSize);

    // Initialize table with single-byte entries
    _InitTable(table);

    byte[]? prevEntry = null;

    while (outPos < expectedLength) {
      var code = reader.ReadBits(codeSize);
      if (code < 0)
        break;

      if (code == EoiCode)
        break;

      if (code == ClearCode) {
        codeSize = 9;
        table.Clear();
        _InitTable(table);
        prevEntry = null;

        code = reader.ReadBits(codeSize);
        if (code < 0 || code == EoiCode)
          break;

        if (code >= table.Count)
          break;

        var entry = table[code];
        _WriteBytes(output, ref outPos, entry, expectedLength);
        prevEntry = entry;
        continue;
      }

      byte[] current;
      if (code < table.Count) {
        current = table[code];
      } else if (code == table.Count && prevEntry != null) {
        // KwKwK special case
        current = new byte[prevEntry.Length + 1];
        prevEntry.CopyTo(current, 0);
        current[^1] = prevEntry[0];
      } else {
        break; // Invalid code
      }

      _WriteBytes(output, ref outPos, current, expectedLength);

      if (prevEntry != null && table.Count < MaxTableSize) {
        var newEntry = new byte[prevEntry.Length + 1];
        prevEntry.CopyTo(newEntry, 0);
        newEntry[^1] = current[0];
        table.Add(newEntry);

        // Increase code size when table reaches next power of 2
        if (table.Count > (1 << codeSize) && codeSize < MaxBits)
          ++codeSize;
      }

      prevEntry = current;
    }

    if (outPos < expectedLength)
      return output[..outPos]; // Return what we got

    return output;
  }

  /// <summary>Compresses data using TIFF LZW.</summary>
  public static byte[] Compress(ReadOnlySpan<byte> data) {
    if (data.Length == 0)
      return [];

    var writer = new BitWriter();
    var codeSize = 9;
    var nextCode = FirstCode;

    // Hash table for string matching
    var table = new Dictionary<long, int>(MaxTableSize);

    // Write initial CLEAR code
    writer.WriteBits(ClearCode, codeSize);

    var prefix = (int)data[0];
    for (var i = 1; i < data.Length; ++i) {
      var c = data[i];
      var key = ((long)prefix << 8) | c;

      if (table.TryGetValue(key, out var existing)) {
        prefix = existing;
      } else {
        writer.WriteBits(prefix, codeSize);

        if (nextCode < MaxTableSize) {
          table[key] = nextCode;
          ++nextCode;

          // Check if we need to increase code size
          if (nextCode > (1 << codeSize) && codeSize < MaxBits)
            ++codeSize;
        } else {
          // Table full — emit CLEAR and reset
          writer.WriteBits(ClearCode, codeSize);
          table.Clear();
          codeSize = 9;
          nextCode = FirstCode;
        }

        prefix = c;
      }
    }

    // Write final prefix
    writer.WriteBits(prefix, codeSize);

    // Write EOI
    writer.WriteBits(EoiCode, codeSize);

    return writer.ToArray();
  }

  private static void _InitTable(List<byte[]> table) {
    for (var i = 0; i < 258; ++i)
      table.Add(i < 256 ? [(byte)i] : []); // 256=CLEAR, 257=EOI (placeholders)
  }

  private static void _WriteBytes(byte[] output, ref int outPos, byte[] entry, int maxLen) {
    var count = Math.Min(entry.Length, maxLen - outPos);
    if (count > 0) {
      entry.AsSpan(0, count).CopyTo(output.AsSpan(outPos));
      outPos += count;
    }
  }

  /// <summary>MSB-first bit reader for TIFF LZW.</summary>
  private ref struct BitReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _bytePos;
    private int _bitPos; // Bits remaining in current byte (MSB-first)

    public BitReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._bytePos = 0;
      this._bitPos = 8;
    }

    public int ReadBits(int count) {
      var result = 0;
      var bitsNeeded = count;

      while (bitsNeeded > 0) {
        if (this._bytePos >= this._data.Length)
          return -1;

        var bitsAvailable = this._bitPos;
        var bitsToRead = Math.Min(bitsNeeded, bitsAvailable);

        // Extract bits from MSB side
        var shift = bitsAvailable - bitsToRead;
        var mask = ((1 << bitsToRead) - 1) << shift;
        var bits = (this._data[this._bytePos] & mask) >> shift;

        result = (result << bitsToRead) | bits;
        bitsNeeded -= bitsToRead;
        this._bitPos -= bitsToRead;

        if (this._bitPos == 0) {
          ++this._bytePos;
          this._bitPos = 8;
        }
      }

      return result;
    }
  }

  /// <summary>MSB-first bit writer for TIFF LZW.</summary>
  private sealed class BitWriter {
    private readonly MemoryStream _ms = new();
    private byte _currentByte;
    private int _bitsUsed; // How many bits filled from MSB side

    public void WriteBits(int value, int count) {
      var bitsRemaining = count;
      while (bitsRemaining > 0) {
        var bitsAvailable = 8 - this._bitsUsed;
        var bitsToWrite = Math.Min(bitsRemaining, bitsAvailable);

        // Extract the top `bitsToWrite` bits from the remaining value
        var shift = bitsRemaining - bitsToWrite;
        var bits = (value >> shift) & ((1 << bitsToWrite) - 1);

        // Place into current byte at MSB position
        this._currentByte |= (byte)(bits << (bitsAvailable - bitsToWrite));
        this._bitsUsed += bitsToWrite;
        bitsRemaining -= bitsToWrite;

        if (this._bitsUsed == 8) {
          this._ms.WriteByte(this._currentByte);
          this._currentByte = 0;
          this._bitsUsed = 0;
        }
      }
    }

    public byte[] ToArray() {
      // Flush partial byte
      if (this._bitsUsed > 0)
        this._ms.WriteByte(this._currentByte);

      return this._ms.ToArray();
    }
  }
}
