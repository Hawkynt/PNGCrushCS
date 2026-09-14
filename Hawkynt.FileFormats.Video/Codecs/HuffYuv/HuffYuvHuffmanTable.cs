using System;
using System.IO;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>One HuffYUV/FFVHUFF decoder table.</summary>
/// <remarks>
/// The original codec has 256 residual symbols. FFVHUFF version 3 widens the alphabet with the
/// sample depth, up to 16 384 symbols; sixteen-bit samples still use that 14-bit alphabet and carry
/// the two low residual bits literally. Only code lengths are stored in version 2/3 descriptions;
/// codes are assigned by HuffYUV's longest-length-first rule.
/// </remarks>
internal sealed class HuffYuvHuffmanTable {

  internal const int SYMBOL_COUNT = 256;
  internal const int MAX_SYMBOL_COUNT = 16384;
  private const int _MAX_LENGTH = 32;

  private readonly int[] _firstCode = new int[_MAX_LENGTH + 1];
  private readonly int[] _count = new int[_MAX_LENGTH + 1];
  private readonly int[] _firstSymbol = new int[_MAX_LENGTH + 1];
  private readonly int[] _symbols;
  private readonly int _shortest;
  private readonly int _longest;

  private HuffYuvHuffmanTable(ReadOnlySpan<byte> lengths, int plane) {
    if (lengths.IsEmpty || lengths.Length > MAX_SYMBOL_COUNT)
      throw new ArgumentOutOfRangeException(nameof(lengths));

    this._symbols = new int[lengths.Length];
    var used = 0;
    for (var i = 0; i < lengths.Length; ++i) {
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
      shortest = Math.Min(shortest, length);
      longest = length;
    }
    this._shortest = shortest;
    this._longest = longest;

    var next = 0;
    var symbolsPlaced = lengths.Length;
    for (var length = _MAX_LENGTH; length >= 1; --length) {
      if (this._count[length] > 0) {
        this._firstCode[length] = next;
        symbolsPlaced -= this._count[length];
        this._firstSymbol[length] = symbolsPlaced;
        var at = symbolsPlaced;
        for (var symbol = 0; symbol < lengths.Length; ++symbol)
          if (lengths[symbol] == length)
            this._symbols[at++] = symbol;
        next += this._count[length];
      }

      if (length <= 1)
        continue;
      if ((next & 1) != 0)
        throw new InvalidDataException($"Table {plane} does not describe a complete code: the lengths leave a code of {length - 1} bits half assigned.");
      next >>= 1;
    }

    if (next > 2)
      throw new InvalidDataException($"Table {plane} describes more codes than the lengths have room for.");
  }

  internal int Read(HuffYuvBitReader bits) {
    var code = bits.Bits(this._shortest);
    for (var length = this._shortest; length <= this._longest; ++length) {
      var count = this._count[length];
      if (count > 0) {
        var offset = code - this._firstCode[length];
        if ((uint)offset < (uint)count)
          return this._symbols[this._firstSymbol[length] + offset];
      }
      code = (code << 1) | bits.Bit();
    }
    throw new InvalidDataException($"A code longer than {this._longest} bits, which no table entry is.");
  }

  internal static int ReadLengths(ReadOnlySpan<byte> source, int offset, Span<byte> lengths, int plane) {
    var written = 0;
    while (written < lengths.Length) {
      if (offset >= source.Length)
        throw new InvalidDataException($"The Huffman tables end after {written} of {lengths.Length} lengths of table {plane}.");

      var packed = source[offset++];
      var length = packed & 0x1F;
      var repeat = packed >> 5;
      if (repeat == 0) {
        if (offset >= source.Length)
          throw new InvalidDataException($"A run of table {plane} states its count in a byte the tables end before.");
        repeat = source[offset++];
      }
      if (repeat == 0 || written + repeat > lengths.Length)
        throw new InvalidDataException($"A run of table {plane} states {repeat} symbol(s) of length {length} where {lengths.Length - written} are left.");
      lengths.Slice(written, repeat).Fill((byte)length);
      written += repeat;
    }
    return offset;
  }

  internal static HuffYuvHuffmanTable[] ReadAll(ReadOnlySpan<byte> source, int offset, int count, out int end)
    => ReadAll(source, offset, count, SYMBOL_COUNT, out end);

  internal static HuffYuvHuffmanTable[] ReadAll(ReadOnlySpan<byte> source, int offset, int count, int symbolCount, out int end) {
    if (symbolCount is < 1 or > MAX_SYMBOL_COUNT)
      throw new ArgumentOutOfRangeException(nameof(symbolCount));

    var tables = new HuffYuvHuffmanTable[count];
    var lengths = new byte[symbolCount];
    for (var plane = 0; plane < count; ++plane) {
      offset = ReadLengths(source, offset, lengths, plane);
      tables[plane] = new(lengths, plane);
    }
    end = offset;
    return tables;
  }
}
