using System;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.Asv1;

/// <summary>
/// ASV1's two variable-length code tables (asv1.txt clause 5.1) and the scan that turns a coefficient
/// group's serial number into a position in the block.
/// </summary>
internal static class Asv1VlcTables {

  /// <summary>The value <see cref="CodedCoefficientPattern"/> answers with for its End Of Block code.</summary>
  internal const int EndOfBlock = -1;

  /// <summary>The value <see cref="Level"/> answers with for its eight-bit escape.</summary>
  private const int _LevelEscape = short.MinValue;

  /// <summary>
  /// Clause 5.1.2: which of a coefficient group's four positions carry a coded coefficient, sixteen
  /// patterns plus the code that says no further group in this block carries one at all.
  /// </summary>
  internal static readonly H263VlcTable CodedCoefficientPattern = new(
    "ASV1 Coded coefficient pattern",
    ("00001", 14), ("00010", 13), ("00011", 12), ("00100", 11), ("00101", 10),
    ("00110", 9), ("00111", 8), ("01000", 7), ("01001", 6), ("01010", 5),
    ("01011", 4), ("01100", 3), ("01101", 2), ("01110", 1),
    ("01111", EndOfBlock),
    ("10", 0),
    ("11", 15));

  /// <summary>
  /// Clause 5.1.1: a coded coefficient's level, small values by code and everything else by an eight-bit
  /// two's-complement escape.
  /// </summary>
  internal static readonly H263VlcTable Level = new(
    "ASV1 Level",
    ("0011", -3), ("011", -2), ("11", -1),
    ("000", _LevelEscape),
    ("10", 1), ("010", 2), ("0010", 3));

  /// <summary>
  /// Reads one coefficient's level, following <see cref="Level"/>'s escape into an eight-bit
  /// two's-complement value when the short code names it.
  /// </summary>
  internal static int ReadLevel(ref H263BitReader reader) {
    var coded = Level.Read(ref reader);
    if (coded != _LevelEscape)
      return coded;

    var raw = reader.ReadBits(8);
    return raw >= 128 ? raw - 256 : raw;
  }

  /// <summary>
  /// Clause 3.3 and 3.4: the raster position a coefficient group's serial number (0 to 15) and its
  /// position within the group (0 to 3) together name, in the order the pattern bits of
  /// <see cref="CodedCoefficientPattern"/> address them.
  /// </summary>
  /// <remarks>
  /// The document draws the sixteen groups as a 4x4 diagram and each group's own four coefficients as a
  /// 2x2 one, both read left to right and top to bottom on the page. Measured against real files, a
  /// group's own row and column run the other way from that reading — the diagrams give (row, column)
  /// where a page reads (column, row) — and the same swap applies one level down, to which of a group's
  /// four positions each pattern bit addresses: a `00001111...`-style ordering.
  /// <para/>
  /// Coefficient groups ten to fifteen are never reached by a real ASV1 block — clause 3.3 states their
  /// four positions "cannot be coded (they must be 0)" — so the last twenty-four entries of this table
  /// exist only to keep it a plain lookup rather than a partial one, and decoding a stream that names
  /// one of those groups is refused before this table is consulted at all.
  /// </remarks>
  internal static readonly int[] ScanPosition = _BuildScanPosition();

  private static int[] _BuildScanPosition() {
    // Group index (0..15) to its own (row, column) among the block's 4x4 grid of groups, clause 3.3.
    ReadOnlySpan<(int Row, int Column)> groupOrigin = [
      (0, 0), (1, 0), (0, 1), (1, 1), (0, 2), (2, 0), (0, 3), (1, 2),
      (2, 1), (3, 0), (1, 3), (2, 2), (3, 1), (2, 3), (3, 2), (3, 3),
    ];

    var positions = new int[64];
    for (var group = 0; group < 16; ++group) {
      var (groupRow, groupColumn) = groupOrigin[group];
      for (var bit = 0; bit < 4; ++bit) {
        var withinRow = bit & 1;
        var withinColumn = (bit >> 1) & 1;
        var x = groupColumn * 2 + withinColumn;
        var y = groupRow * 2 + withinRow;
        positions[group * 4 + bit] = y * 8 + x;
      }
    }

    return positions;
  }
}
