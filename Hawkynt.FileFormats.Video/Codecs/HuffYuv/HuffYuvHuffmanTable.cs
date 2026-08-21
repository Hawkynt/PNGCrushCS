using System;
using System.IO;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>
/// One of HuffYUV's Huffman tables: 256 code lengths, and the codes those lengths imply.
/// </summary>
/// <remarks>
/// Only the lengths are in the file. The codes are worked out from them, which is what makes a table
/// thirty-odd bytes instead of a kilobyte, and the rule for working them out is part of the format
/// rather than a choice — it is not the canonical assignment a reader would reach for.
/// <para/>
/// Codes are handed out from the <b>longest</b> length to the shortest. Starting at zero, every
/// symbol of the current length takes the next number in index order; the running number is then
/// halved and the next shorter length carries on from there. A table of lengths 1, 2, 2 comes out as
/// <c>1</c>, <c>00</c>, <c>01</c> under that rule, where the ordinary canonical assignment would give
/// <c>0</c>, <c>10</c>, <c>11</c>. Both are prefix codes and only one of them decodes a HuffYUV file.
/// <para/>
/// Because each length's codes are consecutive, decoding needs no tree: read bits until the number
/// so far falls inside the range that length was given, and the symbol is that length's list at that
/// offset.
/// </remarks>
internal sealed class HuffYuvHuffmanTable {

  internal const int SYMBOL_COUNT = 256;
  private const int _MAX_LENGTH = 32;

  private readonly int[] _firstCode = new int[_MAX_LENGTH + 1];
  private readonly int[] _count = new int[_MAX_LENGTH + 1];
  private readonly int[] _firstSymbol = new int[_MAX_LENGTH + 1];
  private readonly byte[] _symbols = new byte[SYMBOL_COUNT];
  private readonly int _shortest;
  private readonly int _longest;

  private HuffYuvHuffmanTable(ReadOnlySpan<byte> lengths, int plane) {
    var used = 0;
    for (var i = 0; i < SYMBOL_COUNT; ++i) {
      var length = lengths[i];
      if (length == 0)
        continue;

      if (length > _MAX_LENGTH)
        throw new InvalidDataException($"Table {plane} gives symbol {i} a code {length} bits long, where {_MAX_LENGTH} is the most a code can be.");

      ++this._count[length];
      ++used;
    }

    if (used == 0)
      throw new InvalidDataException($"Table {plane} gives every symbol a length of zero, so it codes nothing.");

    var shortest = _MAX_LENGTH;
    var longest = 1;
    for (var length = 1; length <= _MAX_LENGTH; ++length) {
      if (this._count[length] == 0)
        continue;

      if (length < shortest)
        shortest = length;
      longest = length;
    }

    this._shortest = shortest;
    this._longest = longest;

    // Longest first, as the format has it, with the running number halved at every step down.
    var next = 0;
    var symbolsPlaced = SYMBOL_COUNT;
    for (var length = _MAX_LENGTH; length >= 1; --length) {
      if (this._count[length] > 0) {
        this._firstCode[length] = next;
        symbolsPlaced -= this._count[length];
        this._firstSymbol[length] = symbolsPlaced;

        var at = symbolsPlaced;
        for (var symbol = 0; symbol < SYMBOL_COUNT; ++symbol)
          if (lengths[symbol] == length)
            this._symbols[at++] = (byte)symbol;

        next += this._count[length];
      }

      if (length > 1) {
        if ((next & 1) != 0)
          throw new InvalidDataException($"Table {plane} does not describe a complete code: the lengths leave a code of {length - 1} bits half assigned.");

        next >>= 1;
      }
    }

    if (next > 2)
      throw new InvalidDataException($"Table {plane} describes more codes than the lengths have room for.");
  }

  /// <summary>Reads one symbol.</summary>
  internal int Read(HuffYuvBitReader bits) {
    var code = bits.Bits(this._shortest);
    for (var length = this._shortest; length <= this._longest; ++length) {
      var count = this._count[length];
      if (count > 0) {
        var offset = code - this._firstCode[length];
        if (offset >= 0 && offset < count)
          return this._symbols[this._firstSymbol[length] + offset];
      }

      code = (code << 1) | bits.Bit();
    }

    throw new InvalidDataException($"A code longer than {this._longest} bits, which no table entry is.");
  }

  // ============================================================================================
  // The lengths, as the file carries them
  // ============================================================================================

  /// <summary>
  /// Reads one table's 256 code lengths out of their run-length coded form.
  /// </summary>
  /// <remarks>
  /// A byte at a time. The low five bits are a length; the top three are how many symbols in a row
  /// have it, and where those three are zero the count is the whole of the next byte instead. Three
  /// bits reach seven, and a table of 8-bit samples routinely has seventy-two symbols sharing a
  /// length, so the escape is not an edge case — it is most of a real table.
  /// </remarks>
  internal static int ReadLengths(ReadOnlySpan<byte> source, int offset, Span<byte> lengths, int plane) {
    var written = 0;
    while (written < SYMBOL_COUNT) {
      if (offset >= source.Length)
        throw new InvalidDataException($"The Huffman tables end after {written} of {SYMBOL_COUNT} lengths of table {plane}.");

      var packed = source[offset++];
      var length = packed & 0x1F;
      var repeat = packed >> 5;
      if (repeat == 0) {
        if (offset >= source.Length)
          throw new InvalidDataException($"A run of table {plane} states its count in a byte the tables end before.");

        repeat = source[offset++];
      }

      if (repeat == 0 || written + repeat > SYMBOL_COUNT)
        throw new InvalidDataException(
          $"A run of table {plane} states {repeat} symbol(s) of length {length} where {SYMBOL_COUNT - written} are left.");

      lengths.Slice(written, repeat).Fill((byte)length);
      written += repeat;
    }

    return offset;
  }

  /// <summary>
  /// Builds the tables a stream carries, one after another, and says where they end.
  /// </summary>
  /// <remarks>
  /// Where they end matters for a stream whose tables are in every frame rather than in the stream
  /// description: the picture begins at the next byte, and the word swapping the bit reader does
  /// starts there and not at the front of the frame.
  /// </remarks>
  internal static HuffYuvHuffmanTable[] ReadAll(ReadOnlySpan<byte> source, int offset, int count, out int end) {
    var tables = new HuffYuvHuffmanTable[count];
    Span<byte> lengths = stackalloc byte[SYMBOL_COUNT];

    for (var plane = 0; plane < count; ++plane) {
      offset = ReadLengths(source, offset, lengths, plane);
      tables[plane] = new(lengths, plane);
    }

    end = offset;
    return tables;
  }
}
