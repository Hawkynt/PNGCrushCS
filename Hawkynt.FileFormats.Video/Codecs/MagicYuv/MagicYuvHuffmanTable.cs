using System;
using System.IO;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>
/// One of MagicYUV's Huffman tables: 256 code lengths, and the codes those lengths imply.
/// </summary>
/// <remarks>
/// The lengths are in the frame one byte a symbol, uncoded — 256 bytes a table, with a length of
/// nought meaning the symbol does not occur. There is one table a plane and every slice of that
/// plane uses it, which is what lets the slices be decoded in any order.
/// <para/>
/// <b>Codes are handed out from the longest length to the shortest.</b> Starting at zero, every
/// symbol of the current length takes the next number in ascending symbol order; the running number
/// is then halved and the next shorter length carries on from there. The consequence is that the
/// shortest code is all ones rather than all zeroes, which is how it was found: a picture of flat
/// colour codes almost entirely as its commonest symbol, and its slice data is a run of
/// <c>0xFF</c> bytes. Under the assignment a reader reaches for first — shortest length up from
/// zero — that same slice would have been a run of <c>0x00</c>.
/// <para/>
/// Within one length the symbols run <b>ascending</b>, which is where this parts company with Ut
/// Video's otherwise identical construction. The two differ on every plane that has more than one
/// symbol at some length, so it is not a detail: reading them the other way round decodes a plane's
/// commonest symbol correctly and almost nothing else.
/// </remarks>
internal sealed class MagicYuvHuffmanTable {

  /// <summary>How many symbols a table describes, one for every value a byte can take.</summary>
  internal const int SYMBOL_COUNT = 256;

  /// <summary>The longest code the construction can carry.</summary>
  private const int _MAX_LENGTH = 32;

  private readonly int[] _count = new int[_MAX_LENGTH + 2];
  private readonly int[] _firstCode = new int[_MAX_LENGTH + 2];
  private readonly int[] _firstSymbol = new int[_MAX_LENGTH + 2];
  private readonly byte[] _symbols = new byte[SYMBOL_COUNT];
  private readonly int _shortest;
  private readonly int _longest;

  internal MagicYuvHuffmanTable(ReadOnlySpan<byte> lengths, int plane, int longestAllowed) {
    var used = 0;
    for (var i = 0; i < SYMBOL_COUNT; ++i) {
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

      if (length < shortest)
        shortest = length;
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

        for (var symbol = 0; symbol < SYMBOL_COUNT; ++symbol)
          if (lengths[symbol] == length)
            this._symbols[placed++] = (byte)symbol;

        next += this._count[length];
      }

      if (length > 1) {
        if ((next & 1) != 0)
          throw new InvalidDataException(
            $"Table {plane} does not describe a complete code: the lengths leave a code of {length - 1} bits half assigned.");

        next >>= 1;
      }
    }

    if (next > 2)
      throw new InvalidDataException($"Table {plane} describes more codes than the lengths have room for.");
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
