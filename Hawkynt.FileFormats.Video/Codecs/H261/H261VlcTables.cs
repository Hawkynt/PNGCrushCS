using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261;

/// <summary>
/// The variable-length code tables of ITU-T H.261, transcribed from the Recommendation.
/// </summary>
/// <remarks>
/// Tables 1 through 5 of ITU-T Recommendation H.261 (03/93), <i>Video codec for audiovisual services
/// at p x 64 kbit/s</i>, taken from the Recommendation itself and from nowhere else. <see
/// cref="H263VlcTable"/> is reused as the lookup structure — a prefix-code table read by peeking the
/// longest code and consuming what matched — because that mechanism has nothing codec-specific in it;
/// only the codes and the values they carry belong to H.261.
/// <para/>
/// Every table here was checked against H.263's own tables where the two might plausibly share one,
/// because H.263 explicitly inherited parts of H.261's coding: Table 3 (MVD) turns out to use exactly
/// the same bit patterns as H.263's Table 14 for every value the two ranges have in common (H.261's
/// vectors reach only ±15, H.263's ±15.5 in half-pixel units), which is a real and checked fact and not
/// an assumption — but the two are written out separately here rather than one reused as the other,
/// because H.261 vectors are whole-pixel and the range is narrower; forcing the wider table into
/// service would accept codes H.261 never defines. Tables 1, 2, 4 and 5 have no H.263 counterpart worth
/// comparing against: H.263's MCBPC/CBPY/TCOEF layer replaced the address-difference macroblock layer
/// and the two-table (first-coefficient / later-coefficient) coefficient coding of H.261 with a single
/// per-macroblock COD bit and a single coefficient table carrying its own end-of-block flag.
/// </remarks>
internal static class H261VlcTables {

  /// <summary>The value <see cref="MacroblockAddress"/> gives for the bit-stuffing codeword of 4.2.3.1.</summary>
  internal const int MbaStuffing = -1;

  /// <summary>The value <see cref="CoefficientNotFirst"/> gives for the end-of-block code of Table 5.</summary>
  internal const int CoefficientEob = -1;

  /// <summary>The value both coefficient tables give for the escape code of Table 5.</summary>
  internal const int CoefficientEscape = -2;

  // ============================================================================================
  // Table 1/H.261 — MBA
  // ============================================================================================

  /// <summary>
  /// Table 1 — macroblock address. The value is the absolute address, for the first transmitted
  /// macroblock of a group of blocks, or the difference from the last transmitted one otherwise; which
  /// of the two it means is not part of the table and is decided where it is read.
  /// </summary>
  /// <remarks>
  /// The Recommendation's own start-code entry — sixteen zero bits and a one, the same prefix a group
  /// of blocks or picture start code begins with — is left out of this table on purpose. Every code
  /// here is eleven bits or fewer, so a decoder that peeks sixteen bits before attempting a lookup can
  /// tell a start code from an macroblock address without the table's help, exactly the way the group
  /// of blocks layer decides whether a start code opens the next group.
  /// </remarks>
  internal static readonly H263VlcTable MacroblockAddress = new(
    "Table 1/H.261 (MBA)",
    ("1", 1),
    ("011", 2),
    ("010", 3),
    ("0011", 4),
    ("0010", 5),
    ("0001 1", 6),
    ("0001 0", 7),
    ("0000 111", 8),
    ("0000 110", 9),
    ("0000 1011", 10),
    ("0000 1010", 11),
    ("0000 1001", 12),
    ("0000 1000", 13),
    ("0000 0111", 14),
    ("0000 0110", 15),
    ("0000 0101 11", 16),
    ("0000 0101 10", 17),
    ("0000 0101 01", 18),
    ("0000 0101 00", 19),
    ("0000 0100 11", 20),
    ("0000 0100 10", 21),
    ("0000 0100 011", 22),
    ("0000 0100 010", 23),
    ("0000 0100 001", 24),
    ("0000 0100 000", 25),
    ("0000 0011 111", 26),
    ("0000 0011 110", 27),
    ("0000 0011 101", 28),
    ("0000 0011 100", 29),
    ("0000 0011 011", 30),
    ("0000 0011 010", 31),
    ("0000 0011 001", 32),
    ("0000 0011 000", 33),
    ("0000 0001 111", MbaStuffing));

  // ============================================================================================
  // Table 2/H.261 — MTYPE
  // ============================================================================================

  /// <summary>Table 2 — macroblock type. The value indexes <see cref="H261MacroblockTypes.All"/>.</summary>
  internal static readonly H263VlcTable MacroblockType = new(
    "Table 2/H.261 (MTYPE)",
    ("0001", 0),          // Intra
    ("0000 001", 1),      // Intra, MQUANT
    ("1", 2),              // Inter
    ("0000 1", 3),         // Inter, MQUANT
    ("0000 0000 1", 4),    // Inter+MC
    ("0000 0001", 5),      // Inter+MC, CBP+TCOEFF
    ("0000 0000 01", 6),   // Inter+MC, MQUANT, CBP+TCOEFF
    ("001", 7),            // Inter+MC+FIL
    ("01", 8),             // Inter+MC+FIL, CBP+TCOEFF
    ("0000 01", 9));       // Inter+MC+FIL, MQUANT, CBP+TCOEFF

  // ============================================================================================
  // Table 3/H.261 — MVD
  // ============================================================================================

  /// <summary>
  /// Table 3 — MVD. The value is the difference in whole pixels, which is the table's index less
  /// sixteen; the same code stands for a second difference thirty-two whole pixels away, and choosing
  /// between the two needs the predictor (4.2.3.4).
  /// </summary>
  internal static readonly H263VlcTable MotionVectorDifference = new(
    "Table 3/H.261 (MVD)",
    ("0000 0011 001", -16),
    ("0000 0011 011", -15),
    ("0000 0011 101", -14),
    ("0000 0011 111", -13),
    ("0000 0100 001", -12),
    ("0000 0100 011", -11),
    ("0000 0100 11", -10),
    ("0000 0101 01", -9),
    ("0000 0101 11", -8),
    ("0000 0111", -7),
    ("0000 1001", -6),
    ("0000 1011", -5),
    ("0000 111", -4),
    ("0001 1", -3),
    ("0011", -2),
    ("011", -1),
    ("1", 0),
    ("010", 1),
    ("0010", 2),
    ("0001 0", 3),
    ("0000 110", 4),
    ("0000 1010", 5),
    ("0000 1000", 6),
    ("0000 0110", 7),
    ("0000 0101 10", 8),
    ("0000 0101 00", 9),
    ("0000 0100 10", 10),
    ("0000 0100 010", 11),
    ("0000 0100 000", 12),
    ("0000 0011 110", 13),
    ("0000 0011 100", 14),
    ("0000 0011 010", 15));

  // ============================================================================================
  // Table 4/H.261 — CBP
  // ============================================================================================

  /// <summary>Table 4 — coded block pattern, 32*P1 + 16*P2 + 8*P3 + 4*P4 + 2*P5 + P6 (Figure 10).</summary>
  internal static readonly H263VlcTable CodedBlockPattern = new(
    "Table 4/H.261 (CBP)",
    ("111", 60),
    ("1101", 4),
    ("1100", 8),
    ("1011", 16),
    ("1010", 32),
    ("1001 1", 12),
    ("1001 0", 48),
    ("1000 1", 20),
    ("1000 0", 40),
    ("0111 1", 28),
    ("0111 0", 44),
    ("0110 1", 52),
    ("0110 0", 56),
    ("0101 1", 1),
    ("0101 0", 61),
    ("0100 1", 2),
    ("0100 0", 62),
    ("0011 11", 24),
    ("0011 10", 36),
    ("0011 01", 3),
    ("0011 00", 63),
    ("0010 111", 5),
    ("0010 110", 9),
    ("0010 101", 17),
    ("0010 100", 33),
    ("0010 011", 6),
    ("0010 010", 10),
    ("0010 001", 18),
    ("0010 000", 34),
    ("0001 1111", 7),
    ("0001 1110", 11),
    ("0001 1101", 19),
    ("0001 1100", 35),
    ("0001 1011", 13),
    ("0001 1010", 49),
    ("0001 1001", 21),
    ("0001 1000", 41),
    ("0001 0111", 14),
    ("0001 0110", 50),
    ("0001 0101", 22),
    ("0001 0100", 42),
    ("0001 0011", 15),
    ("0001 0010", 51),
    ("0001 0001", 23),
    ("0001 0000", 43),
    ("0000 1111", 25),
    ("0000 1110", 37),
    ("0000 1101", 26),
    ("0000 1100", 38),
    ("0000 1011", 29),
    ("0000 1010", 45),
    ("0000 1001", 53),
    ("0000 1000", 57),
    ("0000 0111", 30),
    ("0000 0110", 46),
    ("0000 0101", 54),
    ("0000 0100", 58),
    ("0000 0011 1", 31),
    ("0000 0011 0", 47),
    ("0000 0010 1", 55),
    ("0000 0010 0", 59),
    ("0000 0001 1", 27),
    ("0000 0001 0", 39));

  // ============================================================================================
  // Table 5/H.261 — TCOEFF
  // ============================================================================================
  //
  // The Recommendation gives one table with a footnote splitting run 0, level 1 into two codes: "1s"
  // when it is the first coefficient transmitted for the block and "11s" otherwise, because end of
  // block cannot be the first thing a coded block says (CBP and MTYPE already say the block carries at
  // least one coefficient) and its own code, "10", is freed up for that case alone. So this is really
  // two tables sharing every entry but that one and the end-of-block code, which is why there are two
  // fields below rather than one: CoefficientFirst never carries CoefficientEob and never needs the
  // sign-doubled "11s" spelling, CoefficientNotFirst carries both. Every coefficient of a block after
  // the first is read from CoefficientNotFirst, including a second one that happens to repeat run 0,
  // level 1 — the "first" in the footnote means position in the block, not value.

  /// <summary>The (Run, Level) of every TCOEFF symbol other than end-of-block and escape, by value.</summary>
  private static readonly (byte Run, byte Level)[] _Rows = [
    (0, 1),
    (0, 2), (0, 3), (0, 4), (0, 5), (0, 6), (0, 7), (0, 8), (0, 9), (0, 10),
    (0, 11), (0, 12), (0, 13), (0, 14), (0, 15),
    (1, 1), (1, 2), (1, 3), (1, 4), (1, 5), (1, 6), (1, 7),
    (2, 1), (2, 2), (2, 3), (2, 4), (2, 5),
    (3, 1), (3, 2), (3, 3), (3, 4),
    (4, 1), (4, 2), (4, 3),
    (5, 1), (5, 2), (5, 3),
    (6, 1), (6, 2),
    (7, 1), (7, 2),
    (8, 1), (8, 2),
    (9, 1), (9, 2),
    (10, 1), (10, 2),
    (11, 1), (12, 1), (13, 1), (14, 1), (15, 1), (16, 1), (17, 1), (18, 1), (19, 1), (20, 1),
    (21, 1), (22, 1), (23, 1), (24, 1), (25, 1), (26, 1),
  ];

  /// <summary>The run of the symbol at this value, shared by both coefficient tables.</summary>
  internal static int RunOf(int value) => _Rows[value].Run;

  /// <summary>The magnitude of the symbol at this value, shared by both coefficient tables.</summary>
  internal static int LevelOf(int value) => _Rows[value].Level;

  /// <summary>The value whose (Run, Level) is this pair, for building test streams from a table lookup
  /// rather than a second transcription of the codes.</summary>
  internal static int IndexOf(int run, int level) {
    for (var i = 0; i < _Rows.Length; ++i)
      if (_Rows[i].Run == run && _Rows[i].Level == level)
        return i;

    throw new System.ArgumentException($"Table 5/H.261 has no non-escaped code for run {run}, level {level}.");
  }

  /// <summary>
  /// The table used for the first coefficient of a coded block. Run 0, level 1 is "1" plus sign; end
  /// of block does not appear, because a coded block always carries at least one coefficient.
  /// </summary>
  internal static readonly H263VlcTable CoefficientFirst = new(
    "Table 5/H.261 (TCOEFF, first coefficient)",
    ("1", 0),
    ("0100", 1), ("0010 1", 2), ("0000 110", 3), ("0010 0110", 4), ("0010 0001", 5),
    ("0000 0010 10", 6), ("0000 0001 1101", 7), ("0000 0001 1000", 8), ("0000 0001 0011", 9),
    ("0000 0001 0000", 10), ("0000 0000 1101 0", 11), ("0000 0000 1100 1", 12),
    ("0000 0000 1100 0", 13), ("0000 0000 1011 1", 14),
    ("011", 15), ("0001 10", 16), ("0010 0101", 17), ("0000 0011 00", 18), ("0000 0001 1011", 19),
    ("0000 0000 1011 0", 20), ("0000 0000 1010 1", 21),
    ("0101", 22), ("0000 100", 23), ("0000 0010 11", 24), ("0000 0001 0100", 25),
    ("0000 0000 1010 0", 26),
    ("0011 1", 27), ("0010 0100", 28), ("0000 0001 1100", 29), ("0000 0000 1001 1", 30),
    ("0011 0", 31), ("0000 0011 11", 32), ("0000 0001 0010", 33),
    ("0001 11", 34), ("0000 0010 01", 35), ("0000 0000 1001 0", 36),
    ("0001 01", 37), ("0000 0001 1110", 38),
    ("0001 00", 39), ("0000 0001 0101", 40),
    ("0000 111", 41), ("0000 0001 0001", 42),
    ("0000 101", 43), ("0000 0000 1000 1", 44),
    ("0010 0111", 45), ("0000 0000 1000 0", 46),
    ("0010 0011", 47), ("0010 0010", 48), ("0010 0000", 49), ("0000 0011 10", 50),
    ("0000 0011 01", 51), ("0000 0010 00", 52), ("0000 0001 1111", 53), ("0000 0001 1010", 54),
    ("0000 0001 1001", 55), ("0000 0001 0111", 56), ("0000 0001 0110", 57),
    ("0000 0000 1111 1", 58), ("0000 0000 1111 0", 59), ("0000 0000 1110 1", 60),
    ("0000 0000 1110 0", 61), ("0000 0000 1101 1", 62),
    ("0000 01", CoefficientEscape));

  /// <summary>
  /// The table used for every coefficient after the first. Run 0, level 1 becomes "11" plus sign, which
  /// frees "10" for end of block.
  /// </summary>
  internal static readonly H263VlcTable CoefficientNotFirst = new(
    "Table 5/H.261 (TCOEFF, later coefficients)",
    ("10", CoefficientEob),
    ("11", 0),
    ("0100", 1), ("0010 1", 2), ("0000 110", 3), ("0010 0110", 4), ("0010 0001", 5),
    ("0000 0010 10", 6), ("0000 0001 1101", 7), ("0000 0001 1000", 8), ("0000 0001 0011", 9),
    ("0000 0001 0000", 10), ("0000 0000 1101 0", 11), ("0000 0000 1100 1", 12),
    ("0000 0000 1100 0", 13), ("0000 0000 1011 1", 14),
    ("011", 15), ("0001 10", 16), ("0010 0101", 17), ("0000 0011 00", 18), ("0000 0001 1011", 19),
    ("0000 0000 1011 0", 20), ("0000 0000 1010 1", 21),
    ("0101", 22), ("0000 100", 23), ("0000 0010 11", 24), ("0000 0001 0100", 25),
    ("0000 0000 1010 0", 26),
    ("0011 1", 27), ("0010 0100", 28), ("0000 0001 1100", 29), ("0000 0000 1001 1", 30),
    ("0011 0", 31), ("0000 0011 11", 32), ("0000 0001 0010", 33),
    ("0001 11", 34), ("0000 0010 01", 35), ("0000 0000 1001 0", 36),
    ("0001 01", 37), ("0000 0001 1110", 38),
    ("0001 00", 39), ("0000 0001 0101", 40),
    ("0000 111", 41), ("0000 0001 0001", 42),
    ("0000 101", 43), ("0000 0000 1000 1", 44),
    ("0010 0111", 45), ("0000 0000 1000 0", 46),
    ("0010 0011", 47), ("0010 0010", 48), ("0010 0000", 49), ("0000 0011 10", 50),
    ("0000 0011 01", 51), ("0000 0010 00", 52), ("0000 0001 1111", 53), ("0000 0001 1010", 54),
    ("0000 0001 1001", 55), ("0000 0001 0111", 56), ("0000 0001 0110", 57),
    ("0000 0000 1111 1", 58), ("0000 0000 1111 0", 59), ("0000 0000 1110 1", 60),
    ("0000 0000 1110 0", 61), ("0000 0000 1101 1", 62),
    ("0000 01", CoefficientEscape));
}
