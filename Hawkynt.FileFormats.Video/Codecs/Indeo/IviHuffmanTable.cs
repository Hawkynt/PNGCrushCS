using System;
using System.IO;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// One of Indeo's Huffman codebooks, built from the four-bit-per-row descriptor that stands in for it
/// in the bitstream.
/// </summary>
/// <remarks>
/// Indeo never transmits a codebook as code lengths. It transmits a <b>descriptor</b>: a row count
/// and one nibble per row. Row <i>i</i> holds 2^<i>x</i> codes whose prefix is <i>i</i> ones followed
/// by a zero — the last row drops the zero — and whose remaining <i>x</i> bits are the code's
/// position within the row, where <i>x</i> is that row's nibble. Eight such descriptors for
/// macroblock signals and eight for block signals are built into the format, and a stream may also
/// spell one out for itself; either way the codebook is generated rather than stored, which is what
/// keeps the tables small enough for a 1990s decoder.
/// <para/>
/// A codebook made this way is often <b>incomplete</b>: the rows need not fill the code space, and a
/// descriptor asking for more than 256 codes is truncated to 256 because the run-value maps have only
/// 256 entries. A bit pattern that no row covers is therefore possible, and it means the read has
/// gone out of step with the stream — there is no such thing as a "spare" code here — so it throws
/// rather than resolving to symbol zero.
/// </remarks>
internal sealed class IviHuffmanTable {

  /// <summary>The longest code Indeo allows, and the width of the lookup.</summary>
  /// <remarks>
  /// A descriptor that would generate a longer code is refused, in the built-in tables as in a custom
  /// one, so the whole codebook fits a single flat lookup with no second level.
  /// </remarks>
  internal const int MaximumCodeLength = 13;

  private const int _LOOKUP_SIZE = 1 << MaximumCodeLength;

  private readonly short[] _symbols = new short[_LOOKUP_SIZE];
  private readonly byte[] _lengths = new byte[_LOOKUP_SIZE];

  private IviHuffmanTable() { }

  /// <summary>
  /// Builds the codebook a descriptor stands for.
  /// </summary>
  /// <param name="rowWidths">One nibble per row, as the descriptor gives them.</param>
  internal static IviHuffmanTable FromDescriptor(ReadOnlySpan<byte> rowWidths) {
    var table = new IviHuffmanTable();
    var count = 0;

    for (var row = 0; row < rowWidths.Length && count < 256; ++row) {
      var width = rowWidths[row];
      var isLastRow = row == rowWidths.Length - 1;
      var terminator = isLastRow ? 0 : 1;
      var length = row + width + terminator;

      if (length > MaximumCodeLength)
        throw new InvalidDataException(
          $"This Indeo stream describes a Huffman codebook whose row {row} would need codes of {length} bits. "
          + $"Indeo codes are at most {MaximumCodeLength} bits, so the descriptor is not one a codebook can be "
          + "built from.");

      var prefix = ((1 << row) - 1) << (width + terminator);

      for (var index = 0; index < 1 << width && count < 256; ++index)
        table._Add(prefix | index, length == 0 ? 1 : length, count++);
    }

    if (count == 0)
      throw new InvalidDataException(
        "This Indeo stream describes a Huffman codebook with no rows, which stands for no codes at all.");

    return table;
  }

  /// <summary>Reads one symbol.</summary>
  internal int Read(IviBitReader reader) {
    var window = (int)reader.Peek(MaximumCodeLength);
    var length = this._lengths[window];

    if (length == 0)
      throw new InvalidDataException(
        $"This Indeo band holds a code at bit {reader.Position} that its Huffman codebook does not define. "
        + "The codebooks Indeo generates from a descriptor need not cover every bit pattern, so a pattern "
        + "outside the book means the block data has been read out of step and not that a spare code was used.");

    reader.Skip(length);
    return this._symbols[window];
  }

  /// <summary>
  /// Enters one code, filling every lookup slot whose low bits are that code.
  /// </summary>
  /// <remarks>
  /// The lookup is indexed by the next thirteen bits <b>as the reader delivers them</b>, first bit
  /// lowest, so a code occupies the slots whose low <c>length</c> bits are the code written backwards.
  /// The bits above it are whatever follows the code and are not part of it, which is what the fill
  /// covers.
  /// </remarks>
  private void _Add(int code, int length, int symbol) {
    var reversed = 0;
    for (var bit = 0; bit < length; ++bit)
      reversed |= ((code >> (length - 1 - bit)) & 1) << bit;

    for (var slot = reversed; slot < _LOOKUP_SIZE; slot += 1 << length) {
      this._symbols[slot] = (short)symbol;
      this._lengths[slot] = (byte)length;
    }
  }
}
