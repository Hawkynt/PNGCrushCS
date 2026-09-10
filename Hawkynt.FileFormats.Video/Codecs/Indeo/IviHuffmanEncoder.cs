using System;
using System.IO;

namespace FileFormat.Codecs.Indeo;

/// <summary>Turns an Indeo Huffman descriptor into the code words a writer needs.</summary>
/// <remarks>
/// This is the writing mirror of <see cref="IviHuffmanTable.FromDescriptor"/>. The descriptor itself
/// is the format data; the code words are generated from it so the decoder and encoder cannot carry
/// two hand-maintained copies of the same book.
/// </remarks>
internal sealed class IviHuffmanEncoder {

  private readonly Code[] _codes = new Code[256];
  private readonly bool[] _defined = new bool[256];

  private IviHuffmanEncoder() { }

  internal static IviHuffmanEncoder FromDescriptor(ReadOnlySpan<byte> rowWidths) {
    var encoder = new IviHuffmanEncoder();
    var symbol = 0;

    for (var row = 0; row < rowWidths.Length && symbol < 256; ++row) {
      var width = rowWidths[row];
      var isLastRow = row == rowWidths.Length - 1;
      var terminator = isLastRow ? 0 : 1;
      var length = row + width + terminator;

      if (length > IviHuffmanTable.MaximumCodeLength)
        throw new InvalidDataException(
          $"This Indeo Huffman descriptor would need a code of {length} bits, beyond the format's "
          + $"{IviHuffmanTable.MaximumCodeLength}-bit limit.");

      var prefix = ((1 << row) - 1) << (width + terminator);
      for (var index = 0; index < 1 << width && symbol < 256; ++index, ++symbol) {
        var conventional = prefix | index;
        var reversed = 0u;
        for (var bit = 0; bit < length; ++bit)
          reversed |= (uint)((conventional >> (length - 1 - bit)) & 1) << bit;

        encoder._codes[symbol] = new(reversed, length == 0 ? 1 : length);
        encoder._defined[symbol] = true;
      }
    }

    if (symbol == 0)
      throw new InvalidDataException("This Indeo Huffman descriptor defines no symbols.");

    return encoder;
  }

  internal void Write(IviBitWriter writer, int symbol) {
    if ((uint)symbol >= 256 || !this._defined[symbol])
      throw new InvalidDataException($"The selected Indeo Huffman book does not define symbol {symbol}.");

    var code = this._codes[symbol];
    writer.Write(code.Bits, code.Length);
  }

  private readonly record struct Code(uint Bits, int Length);
}
