namespace FileFormat.Codecs.Indeo;

/// <summary>
/// The one Huffman table Indeo 2 codes every plane of every frame with, expanded into a flat lookup.
/// </summary>
/// <remarks>
/// <see cref="Indeo2Tables.Codes"/> gives a symbol and a code length per entry and no codes at all,
/// because the codes are the canonical ones: entries are taken in the order written, the shortest
/// first, and each is the previous code plus one at its own length. That is the whole construction —
/// nothing about it is stored in a file, and an entry moved in that list is a different table.
/// <para/>
/// The lookup is indexed by the next fourteen bits of the stream, fourteen being the longest code, so
/// one array read finds any code and its length in one step. Every index whose leading bits spell a
/// shorter code is filled with that code, which is what makes the single read work: the bits past the
/// end of the code are whatever follows in the stream and must not change the answer.
/// <para/>
/// <b>The stream is read least-significant-bit first</b>, and the first bit read is the top bit of the
/// code. Those two together are the reason the index is assembled the way <see cref="Indeo2BitReader"/>
/// assembles it rather than simply being the next fourteen bits as a little-endian number.
/// </remarks>
internal static class Indeo2CodeTable {

  /// <summary>The longest code the table defines, and so the width of the lookup.</summary>
  internal const int MAX_CODE_LENGTH = 14;

  private static readonly (byte Symbol, byte Length)[] _LOOKUP = _Build();

  /// <summary>
  /// The symbol the next bits spell and how many of them it took, or a length of zero where those bits
  /// spell no code at all.
  /// </summary>
  internal static (byte Symbol, byte Length) Lookup(int index) => _LOOKUP[index];

  private static (byte Symbol, byte Length)[] _Build() {
    var lookup = new (byte Symbol, byte Length)[1 << MAX_CODE_LENGTH];

    // The canonical code is carried left-aligned in thirty-two bits, so advancing to the next code of
    // the same length is one addition whatever that length is, and a length that grows shifts nothing.
    var code = 0UL;

    foreach (var entry in Indeo2Tables.Codes) {
      var length = entry.Length;
      var value = (int)(code >> (32 - length));
      var first = value << (MAX_CODE_LENGTH - length);
      var count = 1 << (MAX_CODE_LENGTH - length);

      for (var i = 0; i < count; ++i)
        lookup[first + i] = (entry.Symbol, length);

      code += 1UL << (32 - length);
    }

    return lookup;
  }
}
