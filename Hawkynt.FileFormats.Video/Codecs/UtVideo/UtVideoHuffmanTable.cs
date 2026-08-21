using System;
using System.IO;

namespace FileFormat.Codecs.UtVideo;

/// <summary>
/// One of Ut Video's Huffman tables: 256 code lengths, and the codes those lengths imply.
/// </summary>
/// <remarks>
/// The lengths are in the file one byte a symbol, uncoded — unlike HuffYUV, which run-length codes
/// the same 256 numbers. A length of 255 means the symbol does not occur in this plane and gets no
/// code at all.
/// <para/>
/// <b>Codes are handed out from the longest length to the shortest</b>, which is the one part of the
/// construction the format's own description states: "the longest code has zero prefix, the shortest
/// code is all ones". Starting at zero, every symbol of the current length takes the next number;
/// the running number is then halved and the next shorter length carries on from there.
/// <para/>
/// <b>Within one length the symbols are taken in descending order</b>, and that part is stated
/// nowhere. It was measured: a plane whose length-5 symbols are 127, 253, 254 and 255 gives the code
/// <c>00011</c> to 254, where taking them in ascending order gives it to 253. Ascending order
/// decodes the first eleven samples of that plane correctly — every one of them a length-1 code —
/// and then hands back 253 where the picture has 254, for every sample after it. That is the whole
/// of the difference between the two readings, and it is why this is a table rather than a loop.
/// <para/>
/// A plane in which only one symbol occurs is a table with a single entry, and it codes no bits at
/// all: every sample of the plane is that symbol and the slice carries nothing. A run of flat colour
/// produces one, so it is not a curiosity.
/// </remarks>
internal sealed class UtVideoHuffmanTable {

  /// <summary>How many symbols a table describes, one for every value a byte can take.</summary>
  internal const int SYMBOL_COUNT = 256;

  /// <summary>The length a symbol that does not occur is given.</summary>
  private const byte _UNUSED = 0xFF;

  /// <summary>
  /// The longest code the format allows, which its author states as twenty-four bits.
  /// </summary>
  private const int _MAX_LENGTH = 24;

  private readonly int[] _count = new int[_MAX_LENGTH + 2];
  private readonly int[] _firstCode = new int[_MAX_LENGTH + 2];
  private readonly int[] _firstSymbol = new int[_MAX_LENGTH + 2];
  private readonly byte[] _symbols = new byte[SYMBOL_COUNT];
  private readonly int _shortest;
  private readonly int _longest;

  /// <summary>The one symbol a plane uses, where it uses only one, and no code stands for it.</summary>
  internal int SingleSymbol { get; } = -1;

  internal UtVideoHuffmanTable(ReadOnlySpan<byte> lengths, int plane) {
    var used = 0;
    var last = -1;
    for (var i = 0; i < SYMBOL_COUNT; ++i) {
      if (lengths[i] == _UNUSED)
        continue;

      ++used;
      last = i;
    }

    if (used == 0)
      throw new InvalidDataException($"Table {plane} gives no symbol a code, so it codes nothing.");

    // A plane in which one symbol occurs and no other gives that symbol a length of nought, and its
    // slices carry no bits at all: there is nothing to tell apart, so there is nothing to code. A
    // flat alpha channel produces one on every frame, which is how this came to light — reading the
    // nought as a malformed length refuses a picture that is merely opaque.
    if (used == 1) {
      this.SingleSymbol = last;
      this._shortest = 1;
      this._longest = 1;
      return;
    }

    for (var i = 0; i < SYMBOL_COUNT; ++i) {
      var length = lengths[i];
      if (length == _UNUSED)
        continue;

      if (length == 0 || length > _MAX_LENGTH)
        throw new InvalidDataException(
          $"Table {plane} gives symbol {i} a code {length} bits long, where a code is between 1 and {_MAX_LENGTH} bits and only a table with one symbol in it may say nought.");

      ++this._count[length];
    }

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

    // Longest first, as the format has it, with the running number halved at every step down, and
    // the symbols of one length taken from the highest down as well.
    var next = 0;
    var placed = SYMBOL_COUNT;
    for (var length = _MAX_LENGTH; length >= 1; --length) {
      if (this._count[length] > 0) {
        this._firstCode[length] = next;
        placed -= this._count[length];
        this._firstSymbol[length] = placed;

        var at = placed;
        for (var symbol = SYMBOL_COUNT - 1; symbol >= 0; --symbol)
          if (lengths[symbol] == length)
            this._symbols[at++] = (byte)symbol;

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
  internal int Read(UtVideoBitReader bits) {
    if (this.SingleSymbol >= 0)
      return this.SingleSymbol;

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
