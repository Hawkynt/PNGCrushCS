using System;
using System.IO;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Reads Indeo 2's Huffman codes out of a frame, least-significant-bit first.
/// </summary>
/// <remarks>
/// Bit order is the codec's and not a choice: a byte is consumed from its low bit upwards, bytes in
/// the order they are stored, and the first bit read is the leading bit of the code. Reading it the
/// other way round produces codes that are valid table entries almost every time, so the mistake shows
/// as a picture rather than as a failure.
/// <para/>
/// Past the end of the frame the reader yields zero bits rather than refusing. That is deliberate and
/// matches what the plane decoders need: they check how much is left before each run, and a code that
/// straddles the last byte is a code the encoder wrote, not a fault.
/// </remarks>
internal ref struct Indeo2BitReader {

  private readonly ReadOnlySpan<byte> _data;
  private readonly int _bitCount;
  private int _position;

  internal Indeo2BitReader(ReadOnlySpan<byte> data) {
    this._data = data;
    this._bitCount = data.Length * 8;
    this._position = 0;
  }

  /// <summary>How many bits of the frame have not been read yet, negative once the end is past.</summary>
  internal readonly int BitsLeft => this._bitCount - this._position;

  /// <summary>Reads one Huffman code and returns the symbol it stands for.</summary>
  /// <exception cref="InvalidDataException">The next bits spell no code the table defines.</exception>
  internal byte ReadSymbol() {
    var (symbol, length) = Indeo2CodeTable.Lookup(this._Peek());
    if (length == 0)
      throw new InvalidDataException(
        $"An Indeo 2 plane holds a bit pattern at bit {this._position} that is not one of the codec's 143 codes.");

    this._position += length;
    return symbol;
  }

  /// <summary>
  /// The next fourteen bits, the first one read as the most significant.
  /// </summary>
  /// <remarks>
  /// Fourteen because that is the longest code; the bits past the code's own end land in the low bits
  /// of the index, where the lookup ignores them.
  /// </remarks>
  private readonly int _Peek() {
    var value = 0;

    for (var i = 0; i < Indeo2CodeTable.MAX_CODE_LENGTH; ++i) {
      var at = this._position + i;
      var bit = at < this._bitCount ? (this._data[at >> 3] >> (at & 7)) & 1 : 0;
      value = (value << 1) | bit;
    }

    return value;
  }
}
