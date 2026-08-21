using System;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// One of Annex E's variable-length code tables, prepared for decoding.
/// </summary>
/// <remarks>
/// Every code in SMPTE ST 2019-1:2016, Annex E is canonical: sorted by length and then by value, the
/// codewords are consecutive within each length and shift left by one at each step to the next.
/// That means a table is fully described by its codeword lengths in that order — which is how
/// <see cref="DnxHdVlcTables"/> stores them — and decoding is a running comparison rather than a
/// walk down a tree or a lookup into a table of 65536 entries.
/// <para/>
/// Decoding reads one bit at a time into an accumulator. At each length, the codewords of that
/// length occupy a contiguous run of values starting at <c>_firstCode</c>; if the accumulator has
/// reached that run, the symbol is at the matching offset and the codeword is complete. Since the
/// code is complete — Kraft's sum over all eighteen tables is exactly one — a valid bitstream always
/// terminates, and one that does not is refused rather than read past the end.
/// </remarks>
internal sealed class DnxHdVlcTable {

  private readonly int[] _firstCode;
  private readonly int[] _firstIndex;
  private readonly int[] _count;
  private readonly int _shortest;
  private readonly int _longest;

  /// <summary>What each codeword stands for, in the canonical order of the codewords.</summary>
  private readonly int[] _symbols;

  /// <summary>
  /// Builds a table from Annex E's codeword lengths and the meanings that go with them.
  /// </summary>
  /// <remarks>
  /// The two arrays are parallel and in canonical order, so entry <c>i</c> of one belongs with entry
  /// <c>i</c> of the other. The lengths are checked to describe a complete code — Kraft's inequality
  /// met with equality — which catches a mistranscribed table immediately instead of letting it
  /// decode most of a picture and then diverge.
  /// </remarks>
  internal DnxHdVlcTable(ReadOnlySpan<byte> lengths, ReadOnlySpan<int> symbols) {
    if (lengths.Length != symbols.Length)
      throw new InvalidDataException("A VC-3 code table has a different number of lengths and symbols.");

    var shortest = int.MaxValue;
    var longest = 0;
    foreach (var length in lengths) {
      if (length < shortest)
        shortest = length;

      if (length > longest)
        longest = length;
    }

    this._shortest = shortest;
    this._longest = longest;
    this._count = new int[longest + 2];
    this._firstCode = new int[longest + 2];
    this._firstIndex = new int[longest + 2];

    foreach (var length in lengths)
      ++this._count[length];

    // The canonical assignment, and the check that it closes. A complete code uses every value at
    // its longest length exactly once, so the running code ends at 1 << longest.
    var code = 0;
    var index = 0;
    for (var length = shortest; length <= longest; ++length) {
      this._firstCode[length] = code;
      this._firstIndex[length] = index;
      code += this._count[length];
      index += this._count[length];
      code <<= 1;
    }

    if (code != 1 << (longest + 1))
      throw new InvalidDataException(
        "A VC-3 code table's lengths do not describe a complete code, so it has been transcribed wrongly.");

    this._symbols = new int[symbols.Length];
    var at = new int[longest + 2];
    for (var length = shortest; length <= longest; ++length)
      at[length] = this._firstIndex[length];

    for (var i = 0; i < lengths.Length; ++i)
      this._symbols[at[lengths[i]]++] = symbols[i];
  }

  /// <summary>Reads one codeword and returns what it stands for.</summary>
  internal int Read(DnxHdBitReader bits) {
    var code = 0;
    var length = 0;

    while (true) {
      code = (code << 1) | bits.Bit();
      ++length;

      if (length > this._longest)
        throw new InvalidDataException(
          $"A VC-3 codeword ran past {this._longest} bits, which is longer than any codeword in its table. The coding unit is damaged.");

      if (length < this._shortest)
        continue;

      var offset = code - this._firstCode[length];
      if (offset >= 0 && offset < this._count[length])
        return this._symbols[this._firstIndex[length] + offset];
    }
  }

  /// <summary>Builds a table from the parallel arrays of <see cref="DnxHdVlcTables"/>.</summary>
  internal static DnxHdVlcTable From(byte[] lengths, ushort[] symbols) {
    var widened = new int[symbols.Length];
    for (var i = 0; i < symbols.Length; ++i)
      widened[i] = symbols[i];

    return new(lengths, widened);
  }

  /// <summary>Builds a table whose symbols are single bytes.</summary>
  internal static DnxHdVlcTable From(byte[] lengths, byte[] symbols) {
    var widened = new int[symbols.Length];
    for (var i = 0; i < symbols.Length; ++i)
      widened[i] = symbols[i];

    return new(lengths, widened);
  }
}
