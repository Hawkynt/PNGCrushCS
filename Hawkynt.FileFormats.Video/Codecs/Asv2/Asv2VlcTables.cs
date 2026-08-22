using System;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.Asv2;

/// <summary>
/// ASV2's three variable-length code tables (asv1.txt clause 5.2) and the scan that turns a
/// coefficient group's serial number into a position in the block.
/// </summary>
/// <remarks>
/// ASV1 reads a coefficient group's pattern bit for one of its four positions least significant bit
/// first; ASV2 reads the same four-position pattern most significant bit first instead. Neither order
/// is stated in words anywhere in the document — both were settled the same way, against a real encoded
/// picture, where only one order reconstructs it.
/// </remarks>
internal static class Asv2VlcTables {

  /// <summary>
  /// Clause 5.2.1: coefficient group zero's own pattern, which never carries the block's DC position
  /// (position zero of a full sixteen-value pattern) and so is coded with a table of eight rather than
  /// the sixteen <see cref="CodedCoefficientPattern"/> has for every group after it.
  /// </summary>
  internal static readonly H263VlcTable FirstCoefficientPattern = new(
    "ASV2 First Coded coefficient pattern",
    ("00", 0b0111), ("01", 0b0000), ("100", 0b0110), ("101", 0b0100),
    ("1100", 0b0011), ("1101", 0b0001), ("1110", 0b0101), ("1111", 0b0010));

  /// <summary>Clause 5.2.2: the pattern for every coefficient group after the first.</summary>
  internal static readonly H263VlcTable CodedCoefficientPattern = new(
    "ASV2 Coded coefficient pattern",
    ("00", 0b0000), ("010", 0b0100), ("011", 0b1000), ("1000", 0b1010),
    ("1001", 0b1100), ("1010", 0b0010), ("1011", 0b1101), ("1100", 0b1111),
    ("1101", 0b1110), ("111000", 0b0111), ("111001", 0b0101), ("111010", 0b0011),
    ("111011", 0b0001), ("111100", 0b0110), ("111101", 0b1001), ("11111", 0b1011));

  /// <summary>The value <see cref="Level"/> answers with for its eight-bit escape.</summary>
  private const int _LevelEscape = short.MinValue;

  /// <summary>
  /// Clause 5.2.3: a coded coefficient's level. The document prints magnitudes one to seven and the
  /// boundary magnitude thirty-one in full and leaves every value between unstated behind an ellipsis;
  /// what it does print is a nested code — <c>k</c> zero bits, a one, then <c>k</c> further bits and a
  /// sign — one range of magnitudes longer each time <c>k</c> increases, doubling in size and adding two
  /// bits to the code, which is what a magnitude's own bits being read least significant first and the
  /// document's own boundary values (<c>0000111110</c> for +31, <c>0000111111</c> for -31) together pin
  /// down as <c>magnitude = 2^k + reverse(offset, k bits)</c> for every printed value at once. Applying
  /// that formula to the unstated magnitudes eight to thirty was checked against real encoded pictures —
  /// see <c>README.md</c> — rather than shipped on the strength of the formula alone.
  /// </summary>
  internal static readonly H263VlcTable Level = new(
    "ASV2 Level",
    // magnitude 1 (k = 0)
    ("10", 1), ("11", -1),
    // magnitude 2..3 (k = 1)
    ("0100", 2), ("0101", -2), ("0110", 3), ("0111", -3),
    // magnitude 4..7 (k = 2)
    ("001000", 4), ("001001", -4), ("001010", 6), ("001011", -6),
    ("001100", 5), ("001101", -5), ("001110", 7), ("001111", -7),
    // magnitude 8..15 (k = 3), unstated in the document
    ("00010000", 8), ("00010001", -8), ("00010010", 12), ("00010011", -12),
    ("00010100", 10), ("00010101", -10), ("00010110", 14), ("00010111", -14),
    ("00011000", 9), ("00011001", -9), ("00011010", 13), ("00011011", -13),
    ("00011100", 11), ("00011101", -11), ("00011110", 15), ("00011111", -15),
    // magnitude 16..31 (k = 4), unstated in the document except the boundary at 31
    ("0000100000", 16), ("0000100001", -16), ("0000100010", 24), ("0000100011", -24),
    ("0000100100", 20), ("0000100101", -20), ("0000100110", 28), ("0000100111", -28),
    ("0000101000", 18), ("0000101001", -18), ("0000101010", 26), ("0000101011", -26),
    ("0000101100", 22), ("0000101101", -22), ("0000101110", 30), ("0000101111", -30),
    ("0000110000", 17), ("0000110001", -17), ("0000110010", 25), ("0000110011", -25),
    ("0000110100", 21), ("0000110101", -21), ("0000110110", 29), ("0000110111", -29),
    ("0000111000", 19), ("0000111001", -19), ("0000111010", 27), ("0000111011", -27),
    ("0000111100", 23), ("0000111101", -23), ("0000111110", 31), ("0000111111", -31),
    // escape, "00000" then an eight-bit two's-complement value
    ("00000", _LevelEscape));

  /// <summary>
  /// Reads one coefficient's level, following <see cref="Level"/>'s escape into an eight-bit
  /// two's-complement value when the short code names it.
  /// </summary>
  internal static int ReadLevel(ref H263BitReader reader) {
    var coded = Level.Read(ref reader);
    if (coded != _LevelEscape)
      return coded;

    var raw = Asv2Bitstream.ReadReversedBits(ref reader, 8);
    return raw >= 128 ? raw - 256 : raw;
  }

  /// <summary>
  /// The sixteen-entry coefficient-group scan clauses 3.3 and 3.4 draw as two diagrams — sixteen groups
  /// across a block, and four positions within one group — read the other way from how the page prints
  /// them, row and column swapped at both levels at once, exactly as ASV1's own reading of the same two
  /// diagrams needed. ASV2 reaches every one of the sixteen groups the diagram names, where ASV1's own
  /// coding can only ever reach the first ten of them.
  /// </summary>
  internal static readonly int[] ScanPosition = _BuildScanPosition();

  private static int[] _BuildScanPosition() {
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
