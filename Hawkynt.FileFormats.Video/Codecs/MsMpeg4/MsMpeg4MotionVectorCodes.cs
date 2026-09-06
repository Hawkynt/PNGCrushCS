using System;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// One of version 3's motion vector tables turned round, so that a vector can be written as well as
/// read.
/// </summary>
/// <remarks>
/// The table is stated as eleven hundred lengths and eleven hundred vectors, with the codewords
/// implied by assigning them in order — so writing a vector means knowing which codeword the
/// assignment gave its entry, which is what this holds. Both halves are biased by thirty-two, so the
/// index is a pair of six-bit numbers and the whole table is four thousand and ninety-six entries of
/// which most stand for nothing: a vector the table has no entry for is written with the escape
/// codeword and then twelve plain bits.
/// <para/>
/// The escape is the entry whose vector reads as (-32, -32), which is why that one vector cannot be
/// written at all — a corner of the range no encoder reaches, and the price of spending no codeword on
/// saying "escape".
/// </remarks>
internal sealed class MsMpeg4MotionVectorCodes {

  /// <summary>How many values each half of a vector takes, once it is biased.</summary>
  private const int _RANGE = 64;

  /// <summary>The codeword of each biased pair, or minus one where the table has none.</summary>
  private readonly int[] _code = new int[_RANGE * _RANGE];

  private readonly int[] _length = new int[_RANGE * _RANGE];

  internal MsMpeg4MotionVectorCodes(int[] lengths, int[] symbols) {
    ArgumentNullException.ThrowIfNull(lengths);
    ArgumentNullException.ThrowIfNull(symbols);

    Array.Fill(this._length, 0);

    var consumed = 0L;
    for (var i = 0; i < lengths.Length; ++i) {
      var length = lengths[i];
      if (length <= 0)
        continue;

      var code = (int)(uint)(consumed >> (32 - length));
      consumed += 1L << (32 - length);

      var symbol = symbols[i];
      var index = ((symbol >> 8) << 6) | (symbol & 0xFF);
      if (symbol == 0) {
        this.EscapeCode = code;
        this.EscapeLength = length;
        continue;
      }

      this._code[index] = code;
      this._length[index] = length;
    }
  }

  /// <summary>The codeword that says the vector follows in twelve plain bits.</summary>
  internal int EscapeCode { get; }

  /// <summary>How many bits that codeword occupies.</summary>
  internal int EscapeLength { get; }

  /// <summary>
  /// Writes one vector difference, both halves already biased into nought to sixty-three.
  /// </summary>
  internal void Write(MsMpeg4BitWriter writer, int x, int y) {
    ArgumentNullException.ThrowIfNull(writer);

    var index = (x << 6) | y;
    var length = this._length[index];
    if (length > 0) {
      writer.Write(this._code[index], length);
      return;
    }

    writer.Write(this.EscapeCode, this.EscapeLength);
    writer.Write(x, 6);
    writer.Write(y, 6);
  }
}
