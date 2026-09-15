using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Writes Indeo 2's one fixed Huffman code table in the bit order the codec stores it.
/// </summary>
/// <remarks>
/// Bytes are filled from their least-significant bit upwards, while the first bit written for a code
/// is that code's most-significant bit. That is the inverse of <see cref="Indeo2BitReader"/> rather
/// than a conventional little-endian integer write.
/// <para/>
/// The table carried by <see cref="Indeo2Tables.Codes"/> stores only symbols and lengths. Its codes
/// are canonical, so the reverse mapping is derived from those lengths once instead of duplicating a
/// second table that could drift away from the decoder's.
/// </remarks>
internal sealed class Indeo2BitWriter {

  private static readonly (ushort Code, byte Length)[] _ENCODING = _BuildEncoding();

  private readonly List<byte> _data = [];
  private int _bitCount;

  /// <summary>Writes the canonical Huffman code belonging to <paramref name="symbol"/>.</summary>
  internal void WriteSymbol(byte symbol) {
    var (code, length) = _ENCODING[symbol];
    if (length == 0)
      throw new ArgumentOutOfRangeException(nameof(symbol), symbol, "The value is not an Indeo 2 Huffman symbol.");

    for (var bit = length - 1; bit >= 0; --bit)
      this._WriteBit((code >> bit) & 1);
  }

  /// <summary>The number of bits the symbol occupies in the stream.</summary>
  internal static int CodeLength(byte symbol) {
    var length = _ENCODING[symbol].Length;
    if (length == 0)
      throw new ArgumentOutOfRangeException(nameof(symbol), symbol, "The value is not an Indeo 2 Huffman symbol.");

    return length;
  }

  internal byte[] ToArray() => this._data.ToArray();

  private void _WriteBit(int value) {
    if ((this._bitCount & 7) == 0)
      this._data.Add(0);

    if (value != 0) {
      var index = this._data.Count - 1;
      this._data[index] |= (byte)(1 << (this._bitCount & 7));
    }

    ++this._bitCount;
  }

  private static (ushort Code, byte Length)[] _BuildEncoding() {
    var encoding = new (ushort Code, byte Length)[256];
    var code = 0UL;

    foreach (var entry in Indeo2Tables.Codes) {
      var length = entry.Length;
      encoding[entry.Symbol] = ((ushort)(code >> (32 - length)), length);
      code += 1UL << (32 - length);
    }

    return encoding;
  }
}
