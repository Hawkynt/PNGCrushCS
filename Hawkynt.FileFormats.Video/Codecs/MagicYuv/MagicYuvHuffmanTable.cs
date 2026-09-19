using System;
using System.IO;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>One MagicYUV Huffman table, from 8 through 14-bit alphabets.</summary>
/// <remarks>
/// MagicYUV assigns codes from the longest length down and symbols of one length in ascending
/// order. Higher-depth streams run-length encode their length descriptors: a descriptor byte with
/// bit 7 clear is one literal length; with bit 7 set its low seven bits are the length and the next
/// byte adds that many repetitions. FFmpeg's LGPL decoder and OxideAV's MIT clean-room decoder agree
/// on that representation.
/// </remarks>
internal sealed class MagicYuvHuffmanTable {
  private const int _MAX_LENGTH = 32;

  private readonly int[] _count = new int[_MAX_LENGTH + 2];
  private readonly int[] _firstCode = new int[_MAX_LENGTH + 2];
  private readonly int[] _firstSymbol = new int[_MAX_LENGTH + 2];
  private readonly ushort[] _symbols;
  private readonly int _shortest;
  private readonly int _longest;

  internal MagicYuvHuffmanTable(ReadOnlySpan<byte> lengths, int plane, int longestAllowed) {
    if (lengths.IsEmpty)
      throw new InvalidDataException($"Table {plane} describes no symbols.");

    this._symbols = new ushort[lengths.Length];
    var used = 0;
    for (var i = 0; i < lengths.Length; ++i) {
      var length = lengths[i];
      if (length == 0)
        continue;

      if (length > _MAX_LENGTH || length > longestAllowed)
        throw new InvalidDataException(
          $"Table {plane} gives symbol {i} a code {length} bits long, where the frame states {longestAllowed} as the longest it uses.");

      ++this._count[length];
      ++used;
    }

    if (used == 0)
      throw new InvalidDataException($"Table {plane} gives no symbol a code, so it codes nothing.");

    var shortest = _MAX_LENGTH;
    var longest = 1;
    for (var length = 1; length <= _MAX_LENGTH; ++length) {
      if (this._count[length] == 0)
        continue;

      shortest = Math.Min(shortest, length);
      longest = length;
    }

    this._shortest = shortest;
    this._longest = longest;

    var next = 0;
    var placed = 0;
    for (var length = _MAX_LENGTH; length >= 1; --length) {
      if (this._count[length] > 0) {
        this._firstCode[length] = next;
        this._firstSymbol[length] = placed;

        for (var symbol = 0; symbol < lengths.Length; ++symbol)
          if (lengths[symbol] == length)
            this._symbols[placed++] = checked((ushort)symbol);

        next += this._count[length];
      }

      if (length <= 1)
        continue;

      if ((next & 1) != 0)
        throw new InvalidDataException(
          $"Table {plane} does not describe a complete code: the lengths leave a code of {length - 1} bits half assigned.");

      next >>= 1;
    }

    if (next > 2)
      throw new InvalidDataException($"Table {plane} describes more codes than the lengths have room for.");
  }

  /// <summary>Reads and expands one on-wire length descriptor.</summary>
  internal static MagicYuvHuffmanTable Read(
    ReadOnlySpan<byte> data,
    ref int at,
    int end,
    int plane,
    int symbolCount,
    int longestAllowed
  ) {
    if (symbolCount is <= 0 or > 1 << 14)
      throw new ArgumentOutOfRangeException(nameof(symbolCount));

    var lengths = new byte[symbolCount];
    var symbol = 0;
    while (symbol < symbolCount) {
      if (at >= end)
        throw new InvalidDataException($"A frame ends inside Huffman descriptor {plane} after {symbol} of {symbolCount} symbols.");

      var descriptor = data[at++];
      var length = descriptor & 0x7F;
      var run = 1;
      if ((descriptor & 0x80) != 0) {
        if (at >= end)
          throw new InvalidDataException($"Huffman descriptor {plane} ends after a run marker without its count.");

        run += data[at++];
      }

      if (length > longestAllowed || length > _MAX_LENGTH)
        throw new InvalidDataException(
          $"Table {plane} gives a run a code length of {length}, where this format allows at most {longestAllowed}.");

      if (symbol + run > symbolCount)
        throw new InvalidDataException(
          $"Huffman descriptor {plane} expands past its {symbolCount} symbols.");

      lengths.AsSpan(symbol, run).Fill((byte)length);
      symbol += run;
    }

    return new(lengths, plane, longestAllowed);
  }

  /// <summary>Reads one symbol.</summary>
  internal int Read(MagicYuvBitReader bits) {
    var code = 0;
    for (var length = 1; length <= this._longest; ++length) {
      code = (code << 1) | bits.Bit();
      if (length < this._shortest)
        continue;

      var count = this._count[length];
      if (count <= 0)
        continue;

      var offset = code - this._firstCode[length];
      if (offset >= 0 && offset < count)
        return this._symbols[this._firstSymbol[length] + offset];
    }

    throw new InvalidDataException($"A code longer than {this._longest} bits, which no table entry is.");
  }
}
