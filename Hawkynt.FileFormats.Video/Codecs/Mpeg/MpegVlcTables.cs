namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// Every variable-length code table of ISO/IEC 11172-2 Annex B and ISO/IEC 13818-2 Annex B,
/// transcribed from the standards.
/// </summary>
/// <remarks>
/// Each table below names the table of the standard it came from, because that is the only way to
/// check one: there is no way to tell a correct entry from a wrong one by looking at the code, and a
/// reader who wants to verify a line needs to know which page to open. Table B.14 is transcribed
/// grouped by run rather than sorted by code length, which is how the standard prints it and how a
/// missing level shows up as a gap in a sequence.
/// <para/>
/// Most of the tables are shared, and that is not an economy — the two standards print the same
/// codes. Tables B.1, B.2, B.3, B.4, B.10 and B.14 are identical in 11172-2 and 13818-2; B.9 gains
/// one entry, B.12 and B.13 gain three each, and B.15 exists only in 13818-2. Where a table is
/// shared it is written once, so that a correction has one place to be made; where the two differ
/// by whole columns — the DC sizes, the coefficients — both are written out in full rather than one
/// being expressed as a patch on the other, because a patch is not something a reader can check
/// against a page. B.9 is the exception, and deliberately: 13818-2 prints it as MPEG-1's table plus
/// one row carrying a note about when that row may be used, so it is built here the same way.
/// </remarks>
internal static class MpegVlcTables {

  // ============================================================================================
  // Table B.1 — macroblock_address_increment
  // ============================================================================================

  /// <summary>The value <see cref="MacroblockAddressIncrement"/> returns for macroblock_stuffing.</summary>
  internal const int Stuffing = -1;

  /// <summary>The value <see cref="MacroblockAddressIncrement"/> returns for macroblock_escape.</summary>
  internal const int Escape = -2;

  /// <summary>
  /// Table B.1. The value is the increment; a macroblock_escape adds 33 and is read again, and
  /// macroblock_stuffing is discarded.
  /// </summary>
  internal static readonly MpegVlcTable MacroblockAddressIncrement = new("Table B.1 (macroblock_address_increment)",
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
    ("0000 0001 111", Stuffing),
    ("0000 0001 000", Escape));

  // ============================================================================================
  // Tables B.2, B.3 and B.4 — macroblock_type, one table per picture type
  // ============================================================================================

  /// <summary>macroblock_quant: a quantiser_scale of the macroblock's own follows.</summary>
  internal const int TypeQuant = 1;

  /// <summary>macroblock_motion_forward.</summary>
  internal const int TypeMotionForward = 2;

  /// <summary>macroblock_motion_backward.</summary>
  internal const int TypeMotionBackward = 4;

  /// <summary>macroblock_pattern: a coded_block_pattern follows.</summary>
  internal const int TypePattern = 8;

  /// <summary>macroblock_intra.</summary>
  internal const int TypeIntra = 16;

  /// <summary>Table B.2 — macroblock_type for I pictures.</summary>
  internal static readonly MpegVlcTable IntraMacroblockType = new("Table B.2 (macroblock_type, I pictures)",
    ("1", TypeIntra),
    ("01", TypeIntra | TypeQuant));

  /// <summary>Table B.3 — macroblock_type for P pictures.</summary>
  internal static readonly MpegVlcTable PredictedMacroblockType = new("Table B.3 (macroblock_type, P pictures)",
    ("1", TypeMotionForward | TypePattern),
    ("01", TypePattern),
    ("001", TypeMotionForward),
    ("0001 1", TypeIntra),
    ("0001 0", TypeQuant | TypeMotionForward | TypePattern),
    ("0000 1", TypeQuant | TypePattern),
    ("0000 01", TypeQuant | TypeIntra));

  /// <summary>Table B.4 — macroblock_type for B pictures.</summary>
  internal static readonly MpegVlcTable BidirectionalMacroblockType = new("Table B.4 (macroblock_type, B pictures)",
    ("10", TypeMotionForward | TypeMotionBackward),
    ("11", TypeMotionForward | TypeMotionBackward | TypePattern),
    ("010", TypeMotionBackward),
    ("011", TypeMotionBackward | TypePattern),
    ("0010", TypeMotionForward),
    ("0011", TypeMotionForward | TypePattern),
    ("0001 1", TypeIntra),
    ("0001 0", TypeQuant | TypeMotionForward | TypeMotionBackward | TypePattern),
    ("0000 11", TypeQuant | TypeMotionForward | TypePattern),
    ("0000 10", TypeQuant | TypeMotionBackward | TypePattern),
    ("0000 01", TypeQuant | TypeIntra));

  // ============================================================================================
  // Table B.9 — coded_block_pattern
  // ============================================================================================

  /// <summary>
  /// Table B.9. The value is the pattern: bit 5 is the first luminance block and bit 0 the second
  /// chrominance one, so a block <c>i</c> is coded when <c>pattern &amp; (1 &lt;&lt; (5 - i))</c>.
  /// </summary>
  /// <remarks>
  /// Zero is absent, and correctly so: a macroblock with no coded block has no macroblock_pattern to
  /// read a pattern out of, so there is nothing for the code to mean. The two codes beginning
  /// <c>0000 0000 0</c> are likewise undefined here and are refused.
  /// </remarks>
  internal static readonly MpegVlcTable CodedBlockPattern = new("Table B.9 (coded_block_pattern)",
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

  /// <summary>
  /// 13818-2 Table B.9, including the one code MPEG-1 does not have: a pattern of zero.
  /// </summary>
  /// <remarks>
  /// The standard attaches a note to that row saying it shall not be used with 4:2:0, and the reason
  /// is worth stating because it explains why the row exists at all. In 4:2:2 and 4:4:4 a macroblock
  /// carries chrominance blocks beyond the six this pattern covers, and those are coded by
  /// <c>coded_block_pattern_1</c> and <c>coded_block_pattern_2</c> instead — so a macroblock may
  /// have every one of its first six blocks empty and still be worth coding. In 4:2:0 there are no
  /// further blocks, an all-zero pattern would say the macroblock coded nothing, and a macroblock
  /// that codes nothing has no <c>macroblock_pattern</c> and so never reaches this table. That is
  /// why the two formats get two tables here rather than one permissive one.
  /// </remarks>
  internal static readonly MpegVlcTable CodedBlockPatternWithZero = _With(
    CodedBlockPattern, "13818-2 Table B.9 (coded_block_pattern, 4:2:2 and 4:4:4)", ("0000 0000 1", 0));

  /// <summary>The same table with one code added, which is how 13818-2 prints its Table B.9.</summary>
  private static MpegVlcTable _With(MpegVlcTable table, string name, params (string Code, int Value)[] extra) {
    var entries = new (string Code, int Value)[table.Entries.Count + extra.Length];
    for (var i = 0; i < table.Entries.Count; ++i)
      entries[i] = table.Entries[i];

    extra.CopyTo(entries, table.Entries.Count);
    return new(name, entries);
  }

  // ============================================================================================
  // Table B.10 — motion_code
  // ============================================================================================

  /// <summary>
  /// Table B.10, used for all four of the horizontal and vertical, forward and backward motion codes.
  /// </summary>
  internal static readonly MpegVlcTable MotionCode = new("Table B.10 (motion_code)",
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
    ("0000 0011 010", 15),
    ("0000 0011 000", 16));

  // ============================================================================================
  // Tables B.12 and B.13 — dct_dc_size
  // ============================================================================================

  /// <summary>11172-2 Table B.12 — dct_dc_size_luminance. The value is how many bits the differential is.</summary>
  internal static readonly MpegVlcTable Mpeg1LuminanceDcSize = new("11172-2 Table B.12 (dct_dc_size_luminance)",
    ("100", 0),
    ("00", 1),
    ("01", 2),
    ("101", 3),
    ("110", 4),
    ("1110", 5),
    ("1111 0", 6),
    ("1111 10", 7),
    ("1111 110", 8));

  /// <summary>11172-2 Table B.13 — dct_dc_size_chrominance.</summary>
  internal static readonly MpegVlcTable Mpeg1ChrominanceDcSize = new("11172-2 Table B.13 (dct_dc_size_chrominance)",
    ("00", 0),
    ("01", 1),
    ("10", 2),
    ("110", 3),
    ("1110", 4),
    ("1111 0", 5),
    ("1111 10", 6),
    ("1111 110", 7),
    ("1111 1110", 8));

  /// <summary>
  /// 13818-2 Table B.12 — dct_dc_size_luminance, which reaches three sizes further than MPEG-1's.
  /// </summary>
  /// <remarks>
  /// The extra sizes are what <c>intra_dc_precision</c> buys: an eight-bit DC differential needs no
  /// more than size 8, and the nine, ten and eleven-bit precisions 13818-2 adds need one more size
  /// each. Note that the luminance table's longest code is nine bits and the chrominance table's is
  /// ten — the two are not symmetric, and assuming they were is a slip that only shows up on a
  /// stream coding its DC to eleven bits.
  /// </remarks>
  internal static readonly MpegVlcTable Mpeg2LuminanceDcSize = new("13818-2 Table B.12 (dct_dc_size_luminance)",
    ("100", 0),
    ("00", 1),
    ("01", 2),
    ("101", 3),
    ("110", 4),
    ("1110", 5),
    ("1111 0", 6),
    ("1111 10", 7),
    ("1111 110", 8),
    ("1111 1110", 9),
    ("1111 1111 0", 10),
    ("1111 1111 1", 11));

  /// <summary>13818-2 Table B.13 — dct_dc_size_chrominance.</summary>
  internal static readonly MpegVlcTable Mpeg2ChrominanceDcSize = new("13818-2 Table B.13 (dct_dc_size_chrominance)",
    ("00", 0),
    ("01", 1),
    ("10", 2),
    ("110", 3),
    ("1110", 4),
    ("1111 0", 5),
    ("1111 10", 6),
    ("1111 110", 7),
    ("1111 1110", 8),
    ("1111 1111 0", 9),
    ("1111 1111 10", 10),
    ("1111 1111 11", 11));

  // ============================================================================================
  // Table B.14 — dct_coeff_first and dct_coeff_next
  // ============================================================================================

  /// <summary>The value <see cref="Coefficient"/> returns for End of Block.</summary>
  internal const int EndOfBlock = -1;

  /// <summary>The value <see cref="Coefficient"/> returns for the escape code.</summary>
  internal const int CoefficientEscape = -2;

  /// <summary>The run of a value <see cref="Coefficient"/> returned.</summary>
  internal static int RunOf(int packed) => packed >> 8;

  /// <summary>The level of a value <see cref="Coefficient"/> returned, always positive.</summary>
  internal static int LevelOf(int packed) => packed & 0xFF;

  /// <summary>
  /// Table B.14, the run-level codes, in the <c>dct_coeff_next</c> spelling.
  /// </summary>
  /// <remarks>
  /// One table serves both spellings the standard prints. They differ in exactly one place: as the
  /// first coefficient of a block a leading <c>1</c> is the whole code and means a level of one,
  /// while as a later coefficient the same position holds <c>10</c> for End of Block and <c>11</c> for
  /// a level of one. Every other code in the table begins with a zero, so reading the first
  /// coefficient is this table with that one bit handled ahead of it — see
  /// <see cref="MpegBlockDecoder"/> — rather than a second copy of a hundred and eleven codes.
  /// <para/>
  /// The sign bit the standard shows as a trailing <c>s</c> is not part of the code here. It is read
  /// separately after the run and level are known, which is what makes the longest code sixteen bits
  /// instead of seventeen and the lookup half the size.
  /// </remarks>
  internal static readonly MpegVlcTable Coefficient = new("Table B.14 (dct_coeff_next)",
    ("10", EndOfBlock),
    ("0000 01", CoefficientEscape),

    // run 0, levels 1 to 40
    ("11", (0 << 8) | 1),
    ("0100", (0 << 8) | 2),
    ("0010 1", (0 << 8) | 3),
    ("0000 110", (0 << 8) | 4),
    ("0010 0110", (0 << 8) | 5),
    ("0010 0001", (0 << 8) | 6),
    ("0000 0010 10", (0 << 8) | 7),
    ("0000 0001 1101", (0 << 8) | 8),
    ("0000 0001 1000", (0 << 8) | 9),
    ("0000 0001 0011", (0 << 8) | 10),
    ("0000 0001 0000", (0 << 8) | 11),
    ("0000 0000 1101 0", (0 << 8) | 12),
    ("0000 0000 1100 1", (0 << 8) | 13),
    ("0000 0000 1100 0", (0 << 8) | 14),
    ("0000 0000 1011 1", (0 << 8) | 15),
    ("0000 0000 0111 11", (0 << 8) | 16),
    ("0000 0000 0111 10", (0 << 8) | 17),
    ("0000 0000 0111 01", (0 << 8) | 18),
    ("0000 0000 0111 00", (0 << 8) | 19),
    ("0000 0000 0110 11", (0 << 8) | 20),
    ("0000 0000 0110 10", (0 << 8) | 21),
    ("0000 0000 0110 01", (0 << 8) | 22),
    ("0000 0000 0110 00", (0 << 8) | 23),
    ("0000 0000 0101 11", (0 << 8) | 24),
    ("0000 0000 0101 10", (0 << 8) | 25),
    ("0000 0000 0101 01", (0 << 8) | 26),
    ("0000 0000 0101 00", (0 << 8) | 27),
    ("0000 0000 0100 11", (0 << 8) | 28),
    ("0000 0000 0100 10", (0 << 8) | 29),
    ("0000 0000 0100 01", (0 << 8) | 30),
    ("0000 0000 0100 00", (0 << 8) | 31),
    ("0000 0000 0011 000", (0 << 8) | 32),
    ("0000 0000 0010 111", (0 << 8) | 33),
    ("0000 0000 0010 110", (0 << 8) | 34),
    ("0000 0000 0010 101", (0 << 8) | 35),
    ("0000 0000 0010 100", (0 << 8) | 36),
    ("0000 0000 0010 011", (0 << 8) | 37),
    ("0000 0000 0010 010", (0 << 8) | 38),
    ("0000 0000 0010 001", (0 << 8) | 39),
    ("0000 0000 0010 000", (0 << 8) | 40),

    // run 1, levels 1 to 18
    ("011", (1 << 8) | 1),
    ("0001 10", (1 << 8) | 2),
    ("0010 0101", (1 << 8) | 3),
    ("0000 0011 00", (1 << 8) | 4),
    ("0000 0001 1011", (1 << 8) | 5),
    ("0000 0000 1011 0", (1 << 8) | 6),
    ("0000 0000 1010 1", (1 << 8) | 7),
    ("0000 0000 0011 111", (1 << 8) | 8),
    ("0000 0000 0011 110", (1 << 8) | 9),
    ("0000 0000 0011 101", (1 << 8) | 10),
    ("0000 0000 0011 100", (1 << 8) | 11),
    ("0000 0000 0011 011", (1 << 8) | 12),
    ("0000 0000 0011 010", (1 << 8) | 13),
    ("0000 0000 0011 001", (1 << 8) | 14),
    ("0000 0000 0001 0011", (1 << 8) | 15),
    ("0000 0000 0001 0010", (1 << 8) | 16),
    ("0000 0000 0001 0001", (1 << 8) | 17),
    ("0000 0000 0001 0000", (1 << 8) | 18),

    // run 2
    ("0101", (2 << 8) | 1),
    ("0000 100", (2 << 8) | 2),
    ("0000 0010 11", (2 << 8) | 3),
    ("0000 0001 0100", (2 << 8) | 4),
    ("0000 0000 1010 0", (2 << 8) | 5),

    // run 3
    ("0011 1", (3 << 8) | 1),
    ("0010 0100", (3 << 8) | 2),
    ("0000 0001 1100", (3 << 8) | 3),
    ("0000 0000 1001 1", (3 << 8) | 4),

    // run 4
    ("0011 0", (4 << 8) | 1),
    ("0000 0011 11", (4 << 8) | 2),
    ("0000 0001 0010", (4 << 8) | 3),

    // run 5
    ("0001 11", (5 << 8) | 1),
    ("0000 0010 01", (5 << 8) | 2),
    ("0000 0000 1001 0", (5 << 8) | 3),

    // run 6
    ("0001 01", (6 << 8) | 1),
    ("0000 0001 1110", (6 << 8) | 2),
    ("0000 0000 0001 0100", (6 << 8) | 3),

    // run 7
    ("0001 00", (7 << 8) | 1),
    ("0000 0001 0101", (7 << 8) | 2),

    // run 8
    ("0000 111", (8 << 8) | 1),
    ("0000 0001 0001", (8 << 8) | 2),

    // run 9
    ("0000 101", (9 << 8) | 1),
    ("0000 0000 1000 1", (9 << 8) | 2),

    // run 10
    ("0010 0111", (10 << 8) | 1),
    ("0000 0000 1000 0", (10 << 8) | 2),

    // run 11
    ("0010 0011", (11 << 8) | 1),
    ("0000 0000 0001 1010", (11 << 8) | 2),

    // run 12
    ("0010 0010", (12 << 8) | 1),
    ("0000 0000 0001 1001", (12 << 8) | 2),

    // run 13
    ("0010 0000", (13 << 8) | 1),
    ("0000 0000 0001 1000", (13 << 8) | 2),

    // run 14
    ("0000 0011 10", (14 << 8) | 1),
    ("0000 0000 0001 0111", (14 << 8) | 2),

    // run 15
    ("0000 0011 01", (15 << 8) | 1),
    ("0000 0000 0001 0110", (15 << 8) | 2),

    // run 16
    ("0000 0010 00", (16 << 8) | 1),
    ("0000 0000 0001 0101", (16 << 8) | 2),

    // runs 17 to 31, level 1 only
    ("0000 0001 1111", (17 << 8) | 1),
    ("0000 0001 1010", (18 << 8) | 1),
    ("0000 0001 1001", (19 << 8) | 1),
    ("0000 0001 0111", (20 << 8) | 1),
    ("0000 0001 0110", (21 << 8) | 1),
    ("0000 0000 1111 1", (22 << 8) | 1),
    ("0000 0000 1111 0", (23 << 8) | 1),
    ("0000 0000 1110 1", (24 << 8) | 1),
    ("0000 0000 1110 0", (25 << 8) | 1),
    ("0000 0000 1101 1", (26 << 8) | 1),
    ("0000 0000 0001 1111", (27 << 8) | 1),
    ("0000 0000 0001 1110", (28 << 8) | 1),
    ("0000 0000 0001 1101", (29 << 8) | 1),
    ("0000 0000 0001 1100", (30 << 8) | 1),
    ("0000 0000 0001 1011", (31 << 8) | 1));

  // ============================================================================================
  // 13818-2 Table B.15 — dct_coefficients_1
  // ============================================================================================

  /// <summary>
  /// 13818-2 Table B.15, the second coefficient table, used for intra blocks when
  /// <c>intra_vlc_format</c> is one (13818-2, Table 7-3).
  /// </summary>
  /// <remarks>
  /// The same hundred and eleven run-level pairs as Table B.14, coded differently. B.14 has to serve
  /// intra and non-intra blocks both, and those have very different coefficient statistics; B.15 is
  /// what B.14 would have been had it only ever had to code intra blocks, so the short codes go to
  /// the long runs of low-frequency energy an intra block actually produces. An encoder picks per
  /// picture and says which in the picture coding extension.
  /// <para/>
  /// Two differences in shape from B.14, both of which matter to the code that reads it. End of
  /// Block is <c>0110</c> and not <c>10</c>; and there is no <c>dct_coeff_first</c> spelling,
  /// because this table is only ever used for intra blocks, whose first coefficient is the DC and is
  /// coded separately — so <c>10</c> is unconditionally a run of nought and a level of one. Reading
  /// this table with B.14's first-coefficient rule would take the top bit of the first code as a
  /// level and desynchronise the block.
  /// <para/>
  /// Transcribed in the order 13818-2 prints it, which is by code length, and with the trailing sign
  /// bit the standard shows removed — it is read after the run and level here, as it is for B.14.
  /// The run and level sets are identical to B.14's, which is a check worth making after any edit:
  /// runs nought to sixteen carry levels one to forty, eighteen, five, four, three, three, three and
  /// then two apiece, and runs seventeen to thirty-one carry a level of one.
  /// </remarks>
  internal static readonly MpegVlcTable IntraCoefficient = new("13818-2 Table B.15 (dct_coefficients_1)",
    ("0110", EndOfBlock),
    ("0000 01", CoefficientEscape),

    ("10", (0 << 8) | 1),
    ("010", (1 << 8) | 1),
    ("110", (0 << 8) | 2),
    ("0010 1", (2 << 8) | 1),
    ("0111", (0 << 8) | 3),
    ("0011 1", (3 << 8) | 1),
    ("0001 10", (4 << 8) | 1),
    ("0011 0", (1 << 8) | 2),
    ("0001 11", (5 << 8) | 1),
    ("0000 110", (6 << 8) | 1),
    ("0000 100", (7 << 8) | 1),
    ("1110 0", (0 << 8) | 4),
    ("0000 111", (2 << 8) | 2),
    ("0000 101", (8 << 8) | 1),
    ("1111 000", (9 << 8) | 1),

    ("1110 1", (0 << 8) | 5),
    ("0001 01", (0 << 8) | 6),
    ("1111 001", (1 << 8) | 3),
    ("0010 0110", (3 << 8) | 2),
    ("1111 010", (10 << 8) | 1),
    ("0010 0001", (11 << 8) | 1),
    ("0010 0101", (12 << 8) | 1),
    ("0010 0100", (13 << 8) | 1),
    ("0001 00", (0 << 8) | 7),
    ("0010 0111", (1 << 8) | 4),
    ("1111 1100", (2 << 8) | 3),
    ("1111 1101", (4 << 8) | 2),
    ("0000 0010 0", (5 << 8) | 2),
    ("0000 0010 1", (14 << 8) | 1),
    ("0000 0011 1", (15 << 8) | 1),
    ("0000 0011 01", (16 << 8) | 1),

    ("1111 011", (0 << 8) | 8),
    ("1111 100", (0 << 8) | 9),
    ("0010 0011", (0 << 8) | 10),
    ("0010 0010", (0 << 8) | 11),
    ("0010 0000", (1 << 8) | 5),
    ("0000 0011 00", (2 << 8) | 4),
    ("0000 0001 1100", (3 << 8) | 3),
    ("0000 0001 0010", (4 << 8) | 3),
    ("0000 0001 1110", (6 << 8) | 2),
    ("0000 0001 0101", (7 << 8) | 2),
    ("0000 0001 0001", (8 << 8) | 2),
    ("0000 0001 1111", (17 << 8) | 1),
    ("0000 0001 1010", (18 << 8) | 1),
    ("0000 0001 1001", (19 << 8) | 1),
    ("0000 0001 0111", (20 << 8) | 1),
    ("0000 0001 0110", (21 << 8) | 1),

    ("1111 1010", (0 << 8) | 12),
    ("1111 1011", (0 << 8) | 13),
    ("1111 1110", (0 << 8) | 14),
    ("1111 1111", (0 << 8) | 15),

    ("0000 0000 1011 0", (1 << 8) | 6),
    ("0000 0000 1010 1", (1 << 8) | 7),
    ("0000 0000 1010 0", (2 << 8) | 5),
    ("0000 0000 1001 1", (3 << 8) | 4),
    ("0000 0000 1001 0", (5 << 8) | 3),
    ("0000 0000 1000 1", (9 << 8) | 2),
    ("0000 0000 1000 0", (10 << 8) | 2),
    ("0000 0000 1111 1", (22 << 8) | 1),
    ("0000 0000 1111 0", (23 << 8) | 1),
    ("0000 0000 1110 1", (24 << 8) | 1),
    ("0000 0000 1110 0", (25 << 8) | 1),
    ("0000 0000 1101 1", (26 << 8) | 1),

    ("0000 0000 0111 11", (0 << 8) | 16),
    ("0000 0000 0111 10", (0 << 8) | 17),
    ("0000 0000 0111 01", (0 << 8) | 18),
    ("0000 0000 0111 00", (0 << 8) | 19),
    ("0000 0000 0110 11", (0 << 8) | 20),
    ("0000 0000 0110 10", (0 << 8) | 21),
    ("0000 0000 0110 01", (0 << 8) | 22),
    ("0000 0000 0110 00", (0 << 8) | 23),
    ("0000 0000 0101 11", (0 << 8) | 24),
    ("0000 0000 0101 10", (0 << 8) | 25),
    ("0000 0000 0101 01", (0 << 8) | 26),
    ("0000 0000 0101 00", (0 << 8) | 27),
    ("0000 0000 0100 11", (0 << 8) | 28),
    ("0000 0000 0100 10", (0 << 8) | 29),
    ("0000 0000 0100 01", (0 << 8) | 30),
    ("0000 0000 0100 00", (0 << 8) | 31),

    ("0000 0000 0011 000", (0 << 8) | 32),
    ("0000 0000 0010 111", (0 << 8) | 33),
    ("0000 0000 0010 110", (0 << 8) | 34),
    ("0000 0000 0010 101", (0 << 8) | 35),
    ("0000 0000 0010 100", (0 << 8) | 36),
    ("0000 0000 0010 011", (0 << 8) | 37),
    ("0000 0000 0010 010", (0 << 8) | 38),
    ("0000 0000 0010 001", (0 << 8) | 39),
    ("0000 0000 0010 000", (0 << 8) | 40),

    ("0000 0000 0011 111", (1 << 8) | 8),
    ("0000 0000 0011 110", (1 << 8) | 9),
    ("0000 0000 0011 101", (1 << 8) | 10),
    ("0000 0000 0011 100", (1 << 8) | 11),
    ("0000 0000 0011 011", (1 << 8) | 12),
    ("0000 0000 0011 010", (1 << 8) | 13),
    ("0000 0000 0011 001", (1 << 8) | 14),

    ("0000 0000 0001 0011", (1 << 8) | 15),
    ("0000 0000 0001 0010", (1 << 8) | 16),
    ("0000 0000 0001 0001", (1 << 8) | 17),
    ("0000 0000 0001 0000", (1 << 8) | 18),
    ("0000 0000 0001 0100", (6 << 8) | 3),
    ("0000 0000 0001 1010", (11 << 8) | 2),
    ("0000 0000 0001 1001", (12 << 8) | 2),
    ("0000 0000 0001 1000", (13 << 8) | 2),
    ("0000 0000 0001 0111", (14 << 8) | 2),
    ("0000 0000 0001 0110", (15 << 8) | 2),
    ("0000 0000 0001 0101", (16 << 8) | 2),
    ("0000 0000 0001 1111", (27 << 8) | 1),
    ("0000 0000 0001 1110", (28 << 8) | 1),
    ("0000 0000 0001 1101", (29 << 8) | 1),
    ("0000 0000 0001 1100", (30 << 8) | 1),
    ("0000 0000 0001 1011", (31 << 8) | 1));
}
