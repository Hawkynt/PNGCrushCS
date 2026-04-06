using System;

namespace FileFormat.Jpeg;

/// <summary>MSB-first bit reader with JPEG 0xFF/0x00 byte-stuffing and RST marker handling.</summary>
internal sealed class JpegBitReader {
  private readonly byte[] _data;
  private int _pos;
  private int _bitBuffer;
  private int _bitsLeft;
  private bool _atEnd;

  public JpegBitReader(byte[] data, int offset) {
    this._data = data;
    this._pos = offset;
  }

  public int Position => this._pos;
  public int BitsLeft => this._bitsLeft;
  public bool IsAtEnd => this._atEnd;

  public int ReadBits(int count) {
    while (this._bitsLeft < count) {
      var b = _NextByte();
      this._bitBuffer = (this._bitBuffer << 8) | b;
      this._bitsLeft += 8;
    }

    this._bitsLeft -= count;
    return (this._bitBuffer >> this._bitsLeft) & ((1 << count) - 1);
  }

  public int ReadBit() => ReadBits(1);

  /// <summary>Reads a signed value using JPEG's extend convention.</summary>
  public int Receive(int nbits) {
    if (nbits == 0)
      return 0;

    var value = ReadBits(nbits);
    return Extend(value, nbits);
  }

  /// <summary>JPEG extend function: if MSB is 0, value is negative.</summary>
  public static int Extend(int value, int nbits) {
    if (value < (1 << (nbits - 1)))
      value += (-1 << nbits) + 1;
    return value;
  }

  /// <summary>Decodes one Huffman symbol using the table's 8-bit lookup then falling back to bit-by-bit.</summary>
  public int DecodeHuffman(JpegHuffmanTable table) {
    if (this._atEnd)
      return 0;

    // Fast path: 8-bit lookup table
    var lookup = table.LookupTable;
    if (lookup != null) {
      // Peek 8 bits without consuming
      while (this._bitsLeft < 8 && !this._atEnd) {
        var b = _NextByte();
        this._bitBuffer = (this._bitBuffer << 8) | b;
        this._bitsLeft += 8;
      }

      if (this._bitsLeft >= 8) {
        var peek = (this._bitBuffer >> (this._bitsLeft - 8)) & 0xFF;
        var (symbol, bitsUsed) = lookup[peek];
        if (bitsUsed > 0) {
          this._bitsLeft -= bitsUsed;
          return symbol;
        }
      }
    }

    // Slow path: bit-by-bit for codes > 8 bits or when lookup misses
    var code = ReadBit();
    for (var len = 1; len <= 16; ++len) {
      if (this._atEnd)
        return 0;
      if (code <= table.MaxCode[len]) {
        var idx = code + table.ValOffset[len];
        return idx >= 0 && idx < table.Values.Length ? table.Values[idx] : 0;
      }
      code = (code << 1) | ReadBit();
    }

    return 0;
  }

  /// <summary>Aligns reader to byte boundary (discards remaining bits in current byte).</summary>
  public void AlignToByte() {
    this._bitsLeft = 0;
    this._bitBuffer = 0;
  }

  /// <summary>Checks if a restart marker is at the current byte position. If so, consumes it.</summary>
  public bool TryConsumeRestart(int expectedRstIndex) {
    AlignToByte();
    // Look for 0xFF RST marker
    while (this._pos < this._data.Length - 1) {
      if (this._data[this._pos] == 0xFF) {
        var marker = this._data[this._pos + 1];
        if (JpegMarker.IsRst(marker)) {
          this._pos += 2;
          return (marker & 0x07) == (expectedRstIndex & 0x07);
        }

        if (marker == 0x00) {
          // Stuffed byte - not a marker
          break;
        }

        if (marker == 0xFF) {
          // Fill byte
          ++this._pos;
          continue;
        }

        break;
      }

      break;
    }

    return false;
  }

  private byte _NextByte() {
    if (this._pos >= this._data.Length) {
      this._atEnd = true;
      return 0;
    }

    var b = this._data[this._pos++];
    if (b == 0xFF) {
      if (this._pos < this._data.Length) {
        var next = this._data[this._pos];
        if (next == 0x00)
          ++this._pos; // Byte-stuffing: skip stuffed zero
        else if (next != 0xFF) {
          // Non-stuffed marker: end of entropy data
          --this._pos; // Back up so marker can be found by caller
          this._atEnd = true;
          return 0;
        }
      } else {
        this._atEnd = true;
        return 0;
      }
    }

    return b;
  }
}
