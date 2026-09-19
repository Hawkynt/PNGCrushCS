using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>Canonical Annex-E VLC prepared for the encoder rather than the decoder.</summary>
/// <remarks>
/// <see cref="DnxHdVlcTable"/> derives the same canonical assignment while reading. Keeping the
/// writing lookup separate leaves the hot decoder representation alone and gives encoding a direct
/// symbol-to-codeword map.
/// </remarks>
internal sealed class DnxHdVlcEncoderTable {

  private readonly Dictionary<int, (uint Code, int Length)> _codes;

  private DnxHdVlcEncoderTable(ReadOnlySpan<byte> lengths, ReadOnlySpan<int> symbols) {
    if (lengths.Length != symbols.Length)
      throw new InvalidDataException("A VC-3 code table has a different number of lengths and symbols.");

    var longest = 0;
    foreach (var length in lengths)
      longest = Math.Max(longest, length);

    var counts = new int[longest + 1];
    foreach (var length in lengths)
      ++counts[length];

    var firstCode = new int[longest + 1];
    var nextCode = new int[longest + 1];
    var code = 0;
    for (var length = 1; length <= longest; ++length) {
      code = (code + counts[length - 1]) << 1;
      firstCode[length] = code;
      nextCode[length] = code;
    }

    this._codes = new(symbols.Length);
    for (var i = 0; i < symbols.Length; ++i) {
      var length = lengths[i];
      if (!this._codes.TryAdd(symbols[i], ((uint)nextCode[length]++, length)))
        throw new InvalidDataException($"A VC-3 code table defines symbol {symbols[i]} more than once.");
    }

    var usedAtLongest = (code + counts[longest]) << 1;
    if (usedAtLongest != 1 << (longest + 1))
      throw new InvalidDataException(
        "A VC-3 code table's lengths do not describe a complete code, so it has been transcribed wrongly.");
  }

  internal int LengthOf(int symbol)
    => this._codes.TryGetValue(symbol, out var code)
      ? code.Length
      : throw new InvalidDataException($"VC-3 has no codeword for symbol {symbol} in this table.");

  internal void Write(DnxHdBitWriter writer, int symbol) {
    if (!this._codes.TryGetValue(symbol, out var code))
      throw new InvalidDataException($"VC-3 has no codeword for symbol {symbol} in this table.");

    writer.WriteBits(code.Code, code.Length);
  }

  internal static DnxHdVlcEncoderTable From(byte[] lengths, ushort[] symbols) {
    var widened = new int[symbols.Length];
    for (var i = 0; i < symbols.Length; ++i)
      widened[i] = symbols[i];

    return new(lengths, widened);
  }

  internal static DnxHdVlcEncoderTable From(byte[] lengths, byte[] symbols) {
    var widened = new int[symbols.Length];
    for (var i = 0; i < symbols.Length; ++i)
      widened[i] = symbols[i];

    return new(lengths, widened);
  }
}
