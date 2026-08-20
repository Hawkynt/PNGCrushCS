namespace FileFormat.Codecs.Mpeg1;

/// <summary>
/// Every variable-length code table of ISO/IEC 11172-2 Annex B, transcribed from the standard.
/// </summary>
/// <remarks>
/// Each table below names the table of the standard it came from, because that is the only way to
/// check one: there is no way to tell a correct entry from a wrong one by looking at the code, and a
/// reader who wants to verify a line needs to know which page to open. Table B.14 is transcribed
/// grouped by run rather than sorted by code length, which is how the standard prints it and how a
/// missing level shows up as a gap in a sequence.
/// </remarks>
internal static class Mpeg1VlcTables {

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
  internal static readonly Mpeg1VlcTable MacroblockAddressIncrement = new("Table B.1 (macroblock_address_increment)",
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
  internal static readonly Mpeg1VlcTable IntraMacroblockType = new("Table B.2 (macroblock_type, I pictures)",
    ("1", TypeIntra),
    ("01", TypeIntra | TypeQuant));

  /// <summary>Table B.3 — macroblock_type for P pictures.</summary>
  internal static readonly Mpeg1VlcTable PredictedMacroblockType = new("Table B.3 (macroblock_type, P pictures)",
    ("1", TypeMotionForward | TypePattern),
    ("01", TypePattern),
    ("001", TypeMotionForward),
    ("0001 1", TypeIntra),
    ("0001 0", TypeQuant | TypeMotionForward | TypePattern),
    ("0000 1", TypeQuant | TypePattern),
    ("0000 01", TypeQuant | TypeIntra));

  /// <summary>Table B.4 — macroblock_type for B pictures.</summary>
  internal static readonly Mpeg1VlcTable BidirectionalMacroblockType = new("Table B.4 (macroblock_type, B pictures)",
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
  internal static readonly Mpeg1VlcTable CodedBlockPattern = new("Table B.9 (coded_block_pattern)",
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
  // Table B.10 — motion_code
  // ============================================================================================

  /// <summary>
  /// Table B.10, used for all four of the horizontal and vertical, forward and backward motion codes.
  /// </summary>
  internal static readonly Mpeg1VlcTable MotionCode = new("Table B.10 (motion_code)",
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

  /// <summary>Table B.12 — dct_dc_size_luminance. The value is how many bits the differential is.</summary>
  internal static readonly Mpeg1VlcTable LuminanceDcSize = new("Table B.12 (dct_dc_size_luminance)",
    ("100", 0),
    ("00", 1),
    ("01", 2),
    ("101", 3),
    ("110", 4),
    ("1110", 5),
    ("1111 0", 6),
    ("1111 10", 7),
    ("1111 110", 8));

  /// <summary>Table B.13 — dct_dc_size_chrominance.</summary>
  internal static readonly Mpeg1VlcTable ChrominanceDcSize = new("Table B.13 (dct_dc_size_chrominance)",
    ("00", 0),
    ("01", 1),
    ("10", 2),
    ("110", 3),
    ("1110", 4),
    ("1111 0", 5),
    ("1111 10", 6),
    ("1111 110", 7),
    ("1111 1110", 8));

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
  /// <see cref="Mpeg1BlockDecoder"/> — rather than a second copy of a hundred and eleven codes.
  /// <para/>
  /// The sign bit the standard shows as a trailing <c>s</c> is not part of the code here. It is read
  /// separately after the run and level are known, which is what makes the longest code sixteen bits
  /// instead of seventeen and the lookup half the size.
  /// </remarks>
  internal static readonly Mpeg1VlcTable Coefficient = new("Table B.14 (dct_coeff_next)",
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
}
