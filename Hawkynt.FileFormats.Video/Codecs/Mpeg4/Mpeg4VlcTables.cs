using System.IO;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// The variable-length code tables of ISO/IEC 14496-2 Annex B, transcribed from the standard.
/// </summary>
/// <remarks>
/// Tables B-6, B-7, B-8, B-12, B-13, B-14, B-16 and B-17 of ISO/IEC 14496-2, <i>Coding of
/// audio-visual objects — Part 2: Visual</i>, with the escape bounds of B-19 to B-22 beside them.
/// Each was checked three ways after transcription: the bit count the standard prints beside every
/// code against the length of the code as written here, the prefix property (which
/// <see cref="Mpeg4VlcTable"/> enforces at construction), and the coefficient tables against the
/// escape bounds — every table's largest level for a given run equals the LMAX the standard prints
/// for it, and its largest run for a given level equals the RMAX, which is an independent statement
/// of the same hundred and two rows.
/// <para/>
/// Four of the tables are the ones ITU-T H.263 prints, unchanged: B-6 is H.263's Table 7, B-7 is its
/// Table 8 without the two macroblock types that need H.263's own Annex F, B-8 is its Table 12, and
/// B-17 — the inter coefficient table — is its Table 16 in full. B-12 is <i>almost</i> H.263's Table
/// 14 and the difference matters: it carries one code the older table does not, and it drops the
/// older table's habit of giving every code two meanings. Reusing an H.263 vector decoder here would
/// work until the first vector that needed the extra code.
/// <para/>
/// The sign bit that the coefficient tables print as a trailing <c>s</c> is not part of the codes
/// here; it is read separately, after the code. Dropping it keeps each lookup at four thousand cells
/// instead of eight and cannot introduce an ambiguity: if one code without its sign bit were a prefix
/// of another, then that code with one of its two sign bits would be a prefix of the other with its
/// sign bit, which the standard's tables are not.
/// </remarks>
internal static class Mpeg4VlcTables {

  /// <summary>The value both MCBPC tables give for the stuffing code, which carries no macroblock.</summary>
  internal const int McbpcStuffing = -1;

  /// <summary>The value the coefficient tables give for the escape code of clause 7.4.1.3.</summary>
  internal const int CoefficientEscape = 102;

  /// <summary>The macroblock type an MCBPC value states.</summary>
  internal static int TypeOf(int mcbpc) => mcbpc >> 2;

  /// <summary>The two-bit chrominance coded block pattern an MCBPC value states.</summary>
  internal static int ChromaPatternOf(int mcbpc) => mcbpc & 3;

  // ============================================================================================
  // Macroblock layer
  // ============================================================================================

  /// <summary>Table B-6 — MCBPC for I-VOPs. The value is the macroblock type times four plus CBPC.</summary>
  internal static readonly Mpeg4VlcTable IntraMacroblockType = new(
    "Table B-6 (MCBPC for I-VOPs)",
    ("1", 3 * 4 + 0),
    ("001", 3 * 4 + 1),
    ("010", 3 * 4 + 2),
    ("011", 3 * 4 + 3),
    ("0001", 4 * 4 + 0),
    ("0000 01", 4 * 4 + 1),
    ("0000 10", 4 * 4 + 2),
    ("0000 11", 4 * 4 + 3),
    ("0000 0000 1", McbpcStuffing));

  /// <summary>Table B-7 — MCBPC for P-VOPs.</summary>
  internal static readonly Mpeg4VlcTable PredictedMacroblockType = new(
    "Table B-7 (MCBPC for P-VOPs)",
    ("1", 0 * 4 + 0),
    ("0011", 0 * 4 + 1),
    ("0010", 0 * 4 + 2),
    ("0001 01", 0 * 4 + 3),
    ("011", 1 * 4 + 0),
    ("0000 111", 1 * 4 + 1),
    ("0000 110", 1 * 4 + 2),
    ("0000 0010 1", 1 * 4 + 3),
    ("010", 2 * 4 + 0),
    ("0000 101", 2 * 4 + 1),
    ("0000 100", 2 * 4 + 2),
    ("0000 0101", 2 * 4 + 3),
    ("0001 1", 3 * 4 + 0),
    ("0000 0100", 3 * 4 + 1),
    ("0000 0011", 3 * 4 + 2),
    ("0000 011", 3 * 4 + 3),
    ("0001 00", 4 * 4 + 0),
    ("0000 0010 0", 4 * 4 + 1),
    ("0000 0001 1", 4 * 4 + 2),
    ("0000 0001 0", 4 * 4 + 3),
    ("0000 0000 1", McbpcStuffing));

  /// <summary>
  /// Table B-8 — CBPY. The value is the pattern an intra macroblock means by the code; an inter
  /// macroblock means its complement.
  /// </summary>
  internal static readonly Mpeg4VlcTable LuminancePattern = new(
    "Table B-8 (CBPY)",
    ("0011", 0),
    ("0010 1", 1),
    ("0010 0", 2),
    ("1001", 3),
    ("0001 1", 4),
    ("0111", 5),
    ("0000 10", 6),
    ("1011", 7),
    ("0001 0", 8),
    ("0000 11", 9),
    ("0101", 10),
    ("1010", 11),
    ("0100", 12),
    ("1000", 13),
    ("0110", 14),
    ("11", 15));

  /// <summary>
  /// Table B-3 — MODB, which says what a bidirectionally coded macroblock carries.
  /// </summary>
  /// <remarks>
  /// The value is the number of things that follow: nought for a macroblock that states neither its
  /// type nor a coded block pattern — which is the direct mode with a zero delta vector — one for a
  /// type alone, and two for a type and a pattern.
  /// </remarks>
  internal static readonly Mpeg4VlcTable BidirectionalMode = new(
    "Table B-3 (modb)",
    ("1", 0),
    ("01", 1),
    ("00", 2));

  /// <summary>
  /// Table B-4 — the macroblock type of a bidirectionally coded macroblock.
  /// </summary>
  internal static readonly Mpeg4VlcTable BidirectionalMacroblockType = new(
    "Table B-4 (mb_type for B-VOPs)",
    ("1", Direct),
    ("01", Interpolated),
    ("001", Backward),
    ("0001", Forward));

  /// <summary>
  /// Table 6-28 — DBQUANT, the change in quantiser a bidirectionally coded macroblock may carry.
  /// </summary>
  /// <remarks>
  /// Three values and not four, unlike the DQUANT of a predicted macroblock: a bidirectionally coded
  /// picture may leave the quantiser alone, which a predicted one has no code for.
  /// </remarks>
  internal static readonly Mpeg4VlcTable BidirectionalQuantiserDifference = new(
    "Table 6-28 (dbquant)",
    ("0", 0),
    ("10", -2),
    ("11", 2));

  /// <summary>Direct: no vectors of its own, only a delta from the following anchor's.</summary>
  internal const int Direct = 0;

  /// <summary>Interpolated: predicted from both references and averaged.</summary>
  internal const int Interpolated = 1;

  /// <summary>Backward: predicted from the following anchor alone.</summary>
  internal const int Backward = 2;

  /// <summary>Forward: predicted from the preceding anchor alone.</summary>
  internal const int Forward = 3;

  // ============================================================================================
  // Motion vectors
  // ============================================================================================

  /// <summary>
  /// Table B-12 — the motion vector difference, in half-sample units.
  /// </summary>
  /// <remarks>
  /// The standard prints the values in whole and half samples — <c>-16</c>, <c>-15.5</c>, … — and
  /// then says in as many words that the value of <c>mv_data</c> is twice what the column shows. So
  /// the values here are those doubled numbers, which makes them whole and makes the arithmetic that
  /// follows integer.
  /// <para/>
  /// The last code, for a difference of sixteen whole samples, is the one H.263's otherwise identical
  /// table does not have. The standard notes that it shall not be used when the picture's motion code
  /// is one, which is the only case where it could not be reached by the wraparound instead.
  /// </remarks>
  internal static readonly Mpeg4VlcTable MotionVectorDifference = new(
    "Table B-12 (motion vector difference)",
    ("0000 0000 0010 1", -32),
    ("0000 0000 0011 1", -31),
    ("0000 0000 0101", -30),
    ("0000 0000 0111", -29),
    ("0000 0000 1001", -28),
    ("0000 0000 1011", -27),
    ("0000 0000 1101", -26),
    ("0000 0000 1111", -25),
    ("0000 0001 001", -24),
    ("0000 0001 011", -23),
    ("0000 0001 101", -22),
    ("0000 0001 111", -21),
    ("0000 0010 001", -20),
    ("0000 0010 011", -19),
    ("0000 0010 101", -18),
    ("0000 0010 111", -17),
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
    ("0000 0011 000", 16),
    ("0000 0010 110", 17),
    ("0000 0010 100", 18),
    ("0000 0010 010", 19),
    ("0000 0010 000", 20),
    ("0000 0001 110", 21),
    ("0000 0001 100", 22),
    ("0000 0001 010", 23),
    ("0000 0001 000", 24),
    ("0000 0000 1110", 25),
    ("0000 0000 1100", 26),
    ("0000 0000 1010", 27),
    ("0000 0000 1000", 28),
    ("0000 0000 0110", 29),
    ("0000 0000 0100", 30),
    ("0000 0000 0011 0", 31),
    ("0000 0000 0010 0", 32));

  /// <summary>
  /// Reads one component of a motion vector difference and reconstructs it (ISO/IEC 14496-2, 7.6.3).
  /// </summary>
  /// <remarks>
  /// The code names a coarse difference and the residual bits after it name where inside that
  /// difference's step the real one is, which is how one table of sixty-five codes covers a range
  /// that doubles with every step of the motion code. A residual is present only when the motion code
  /// is more than one and the coarse difference is not zero — a zero needs no refining, and reading
  /// residual bits for it puts the decoder a few bits into the next syntax element.
  /// </remarks>
  internal static int ReadMotionVectorDifference(ref Mpeg4BitReader reader, int fCode) {
    var data = MotionVectorDifference.Read(ref reader);
    if (fCode == 1 || data == 0)
      return data;

    var scale = 1 << (fCode - 1);
    var residual = reader.ReadBits(fCode - 1);
    var magnitude = ((data < 0 ? -data : data) - 1) * scale + residual + 1;
    return data < 0 ? -magnitude : magnitude;
  }

  // ============================================================================================
  // Block layer
  // ============================================================================================

  /// <summary>Table B-13 — the size of an intra block's luminance DC differential.</summary>
  internal static readonly Mpeg4VlcTable LuminanceDcSize = new(
    "Table B-13 (dct_dc_size_luminance)",
    ("011", 0),
    ("11", 1),
    ("10", 2),
    ("010", 3),
    ("001", 4),
    ("0001", 5),
    ("0000 1", 6),
    ("0000 01", 7),
    ("0000 001", 8),
    ("0000 0001", 9),
    ("0000 0000 1", 10),
    ("0000 0000 01", 11),
    ("0000 0000 001", 12));

  /// <summary>Table B-14 — the same for chrominance.</summary>
  internal static readonly Mpeg4VlcTable ChrominanceDcSize = new(
    "Table B-14 (dct_dc_size_chrominance)",
    ("11", 0),
    ("10", 1),
    ("01", 2),
    ("001", 3),
    ("0001", 4),
    ("0000 1", 5),
    ("0000 01", 6),
    ("0000 001", 7),
    ("0000 0001", 8),
    ("0000 0000 1", 9),
    ("0000 0000 01", 10),
    ("0000 0000 001", 11),
    ("0000 0000 0001", 12));

  /// <summary>Table B-16 — the coefficients of an intra block.</summary>
  internal static readonly Mpeg4VlcTable IntraCoefficient = new(
    "Table B-16 (intra TCOEF)",
    ("10", 0), ("110", 1), ("1111", 2), ("0110 1", 3), ("0110 0", 4),
    ("0101 01", 5), ("0100 11", 6), ("0100 10", 7), ("0010 111", 8), ("0001 1111", 9),
    ("0001 1110", 10), ("0001 1101", 11), ("0001 0010 1", 12), ("0001 0010 0", 13), ("0001 0001 1", 14),
    ("0001 0000 1", 15), ("0000 1000 01", 16), ("0000 1000 00", 17), ("0000 0011 11", 18), ("0000 0011 10", 19),
    ("0000 0000 111", 20), ("0000 0000 110", 21), ("0000 0100 000", 22), ("0000 0100 001", 23),
    ("0000 0101 0000", 24), ("0000 0101 0001", 25), ("0000 0101 0010", 26),
    ("1110", 27), ("0101 00", 28), ("0010 110", 29), ("0001 1100", 30), ("0001 0000 0", 31),
    ("0000 1111 1", 32), ("0000 0011 01", 33), ("0000 0100 010", 34), ("0000 0101 0011", 35),
    ("0000 0101 0101", 36),
    ("0101 1", 37), ("0010 101", 38), ("0000 1111 0", 39), ("0000 0011 00", 40), ("0000 0101 0110", 41),
    ("0100 01", 42), ("0001 1011", 43), ("0000 1110 1", 44), ("0000 0010 11", 45),
    ("0100 00", 46), ("0001 0001 0", 47), ("0000 0010 10", 48),
    ("0011 01", 49), ("0000 1110 0", 50), ("0000 0010 00", 51),
    ("0010 010", 52), ("0000 1101 1", 53), ("0000 0101 0100", 54),
    ("0010 100", 55), ("0000 1101 0", 56), ("0000 0101 0111", 57),
    ("0001 1001", 58), ("0000 0010 01", 59),
    ("0001 1000", 60), ("0000 0100 011", 61),
    ("0001 0111", 62), ("0000 1100 1", 63), ("0000 1100 0", 64), ("0000 0001 11", 65),
    ("0000 0101 1000", 66),
    ("0111", 67), ("0011 00", 68), ("0001 0110", 69), ("0000 1011 1", 70), ("0000 0001 10", 71),
    ("0000 0000 101", 72), ("0000 0000 100", 73), ("0000 0101 1001", 74),
    ("0011 11", 75), ("0000 1011 0", 76), ("0000 0001 01", 77),
    ("0011 10", 78), ("0000 0001 00", 79),
    ("0010 001", 80), ("0000 0100 100", 81),
    ("0010 000", 82), ("0000 0100 101", 83),
    ("0010 011", 84), ("0000 0101 1010", 85),
    ("0001 0101", 86), ("0000 0101 1011", 87),
    ("0001 0100", 88), ("0001 0011", 89), ("0001 1010", 90), ("0000 1010 1", 91), ("0000 1010 0", 92),
    ("0000 1001 1", 93), ("0000 1001 0", 94), ("0000 1000 1", 95), ("0000 0100 110", 96),
    ("0000 0100 111", 97), ("0000 0101 1100", 98), ("0000 0101 1101", 99), ("0000 0101 1110", 100),
    ("0000 0101 1111", 101),
    ("0000 011", CoefficientEscape));

  /// <summary>Table B-17 — the coefficients of an inter block.</summary>
  internal static readonly Mpeg4VlcTable InterCoefficient = new(
    "Table B-17 (inter TCOEF)",
    ("10", 0), ("1111", 1), ("0101 01", 2), ("0010 111", 3), ("0001 1111", 4),
    ("0001 0010 1", 5), ("0001 0010 0", 6), ("0000 1000 01", 7), ("0000 1000 00", 8),
    ("0000 0000 111", 9), ("0000 0000 110", 10), ("0000 0100 000", 11),
    ("110", 12), ("0101 00", 13), ("0001 1110", 14), ("0000 0011 11", 15), ("0000 0100 001", 16),
    ("0000 0101 0000", 17),
    ("1110", 18), ("0001 1101", 19), ("0000 0011 10", 20), ("0000 0101 0001", 21),
    ("0110 1", 22), ("0001 0001 1", 23), ("0000 0011 01", 24),
    ("0110 0", 25), ("0001 0001 0", 26), ("0000 0101 0010", 27),
    ("0101 1", 28), ("0000 0011 00", 29), ("0000 0101 0011", 30),
    ("0100 11", 31), ("0000 0010 11", 32), ("0000 0101 0100", 33),
    ("0100 10", 34), ("0000 0010 10", 35),
    ("0100 01", 36), ("0000 0010 01", 37),
    ("0100 00", 38), ("0000 0010 00", 39),
    ("0010 110", 40), ("0000 0101 0101", 41),
    ("0010 101", 42), ("0010 100", 43), ("0001 1100", 44), ("0001 1011", 45),
    ("0001 0000 1", 46), ("0001 0000 0", 47), ("0000 1111 1", 48), ("0000 1111 0", 49),
    ("0000 1110 1", 50), ("0000 1110 0", 51), ("0000 1101 1", 52), ("0000 1101 0", 53),
    ("0000 0100 010", 54), ("0000 0100 011", 55), ("0000 0101 0110", 56), ("0000 0101 0111", 57),
    ("0111", 58), ("0000 1100 1", 59), ("0000 0000 101", 60),
    ("0011 11", 61), ("0000 0000 100", 62),
    ("0011 10", 63), ("0011 01", 64), ("0011 00", 65), ("0010 011", 66), ("0010 010", 67),
    ("0010 001", 68), ("0010 000", 69), ("0001 1010", 70), ("0001 1001", 71), ("0001 1000", 72),
    ("0001 0111", 73), ("0001 0110", 74), ("0001 0101", 75), ("0001 0100", 76), ("0001 0011", 77),
    ("0000 1100 0", 78), ("0000 1011 1", 79), ("0000 1011 0", 80), ("0000 1010 1", 81),
    ("0000 1010 0", 82), ("0000 1001 1", 83), ("0000 1001 0", 84), ("0000 1000 1", 85),
    ("0000 0001 11", 86), ("0000 0001 10", 87), ("0000 0001 01", 88), ("0000 0001 00", 89),
    ("0000 0100 100", 90), ("0000 0100 101", 91), ("0000 0100 110", 92), ("0000 0100 111", 93),
    ("0000 0101 1000", 94), ("0000 0101 1001", 95), ("0000 0101 1010", 96), ("0000 0101 1011", 97),
    ("0000 0101 1100", 98), ("0000 0101 1101", 99), ("0000 0101 1110", 100), ("0000 0101 1111", 101),
    ("0000 011", CoefficientEscape));

  /// <summary>
  /// The (LAST, RUN, LEVEL) triples of Table B-16, in the table's own order.
  /// </summary>
  /// <remarks>
  /// Written out as one list and split into three arrays rather than as three lists, because the
  /// three are read together for every coefficient of every block and a slip that shifted one of them
  /// by a row against the others would be a picture that decodes and is wrong.
  /// </remarks>
  private static readonly (byte Last, byte Run, byte Level)[] _IntraRows = [
    (0, 0, 1), (0, 0, 2), (0, 0, 3), (0, 0, 4), (0, 0, 5), (0, 0, 6), (0, 0, 7), (0, 0, 8), (0, 0, 9),
    (0, 0, 10), (0, 0, 11), (0, 0, 12), (0, 0, 13), (0, 0, 14), (0, 0, 15), (0, 0, 16), (0, 0, 17),
    (0, 0, 18), (0, 0, 19), (0, 0, 20), (0, 0, 21), (0, 0, 22), (0, 0, 23), (0, 0, 24), (0, 0, 25),
    (0, 0, 26), (0, 0, 27),
    (0, 1, 1), (0, 1, 2), (0, 1, 3), (0, 1, 4), (0, 1, 5), (0, 1, 6), (0, 1, 7), (0, 1, 8), (0, 1, 9),
    (0, 1, 10),
    (0, 2, 1), (0, 2, 2), (0, 2, 3), (0, 2, 4), (0, 2, 5),
    (0, 3, 1), (0, 3, 2), (0, 3, 3), (0, 3, 4),
    (0, 4, 1), (0, 4, 2), (0, 4, 3),
    (0, 5, 1), (0, 5, 2), (0, 5, 3),
    (0, 6, 1), (0, 6, 2), (0, 6, 3),
    (0, 7, 1), (0, 7, 2), (0, 7, 3),
    (0, 8, 1), (0, 8, 2),
    (0, 9, 1), (0, 9, 2),
    (0, 10, 1), (0, 11, 1), (0, 12, 1), (0, 13, 1), (0, 14, 1),
    (1, 0, 1), (1, 0, 2), (1, 0, 3), (1, 0, 4), (1, 0, 5), (1, 0, 6), (1, 0, 7), (1, 0, 8),
    (1, 1, 1), (1, 1, 2), (1, 1, 3),
    (1, 2, 1), (1, 2, 2),
    (1, 3, 1), (1, 3, 2),
    (1, 4, 1), (1, 4, 2),
    (1, 5, 1), (1, 5, 2),
    (1, 6, 1), (1, 6, 2),
    (1, 7, 1), (1, 8, 1), (1, 9, 1), (1, 10, 1), (1, 11, 1), (1, 12, 1), (1, 13, 1), (1, 14, 1),
    (1, 15, 1), (1, 16, 1), (1, 17, 1), (1, 18, 1), (1, 19, 1), (1, 20, 1),
  ];

  /// <summary>The (LAST, RUN, LEVEL) triples of Table B-17, in the table's own order.</summary>
  private static readonly (byte Last, byte Run, byte Level)[] _InterRows = [
    (0, 0, 1), (0, 0, 2), (0, 0, 3), (0, 0, 4), (0, 0, 5), (0, 0, 6),
    (0, 0, 7), (0, 0, 8), (0, 0, 9), (0, 0, 10), (0, 0, 11), (0, 0, 12),
    (0, 1, 1), (0, 1, 2), (0, 1, 3), (0, 1, 4), (0, 1, 5), (0, 1, 6),
    (0, 2, 1), (0, 2, 2), (0, 2, 3), (0, 2, 4),
    (0, 3, 1), (0, 3, 2), (0, 3, 3),
    (0, 4, 1), (0, 4, 2), (0, 4, 3),
    (0, 5, 1), (0, 5, 2), (0, 5, 3),
    (0, 6, 1), (0, 6, 2), (0, 6, 3),
    (0, 7, 1), (0, 7, 2),
    (0, 8, 1), (0, 8, 2),
    (0, 9, 1), (0, 9, 2),
    (0, 10, 1), (0, 10, 2),
    (0, 11, 1), (0, 12, 1), (0, 13, 1), (0, 14, 1), (0, 15, 1), (0, 16, 1),
    (0, 17, 1), (0, 18, 1), (0, 19, 1), (0, 20, 1), (0, 21, 1), (0, 22, 1),
    (0, 23, 1), (0, 24, 1), (0, 25, 1), (0, 26, 1),
    (1, 0, 1), (1, 0, 2), (1, 0, 3),
    (1, 1, 1), (1, 1, 2),
    (1, 2, 1), (1, 3, 1), (1, 4, 1), (1, 5, 1), (1, 6, 1), (1, 7, 1),
    (1, 8, 1), (1, 9, 1), (1, 10, 1), (1, 11, 1), (1, 12, 1), (1, 13, 1),
    (1, 14, 1), (1, 15, 1), (1, 16, 1), (1, 17, 1), (1, 18, 1), (1, 19, 1),
    (1, 20, 1), (1, 21, 1), (1, 22, 1), (1, 23, 1), (1, 24, 1), (1, 25, 1),
    (1, 26, 1), (1, 27, 1), (1, 28, 1), (1, 29, 1), (1, 30, 1), (1, 31, 1),
    (1, 32, 1), (1, 33, 1), (1, 34, 1), (1, 35, 1), (1, 36, 1), (1, 37, 1),
    (1, 38, 1), (1, 39, 1), (1, 40, 1),
  ];

  /// <summary>Whether each row of Table B-16 is the last coefficient of its block.</summary>
  internal static readonly bool[] IntraIsLast = _BuildIsLast(_IntraRows);

  /// <summary>How many zeroes precede the coefficient, for each row of Table B-16.</summary>
  internal static readonly byte[] IntraRun = _BuildRun(_IntraRows);

  /// <summary>The magnitude of the coefficient, for each row of Table B-16.</summary>
  internal static readonly byte[] IntraLevel = _BuildLevel(_IntraRows);

  /// <summary>Whether each row of Table B-17 is the last coefficient of its block.</summary>
  internal static readonly bool[] InterIsLast = _BuildIsLast(_InterRows);

  /// <summary>How many zeroes precede the coefficient, for each row of Table B-17.</summary>
  internal static readonly byte[] InterRun = _BuildRun(_InterRows);

  /// <summary>The magnitude of the coefficient, for each row of Table B-17.</summary>
  internal static readonly byte[] InterLevel = _BuildLevel(_InterRows);

  // ============================================================================================
  // The bounds the escape forms are stated against — Tables B-19 to B-22
  // ============================================================================================

  /// <summary>
  /// The largest level the table carries for a run (ISO/IEC 14496-2, Tables B-19 and B-20).
  /// </summary>
  /// <remarks>
  /// Derived from the tables above rather than transcribed a second time. The standard prints these
  /// as tables of their own, and a decoder that transcribed both would have two statements of the
  /// same hundred and two rows that could disagree; deriving one from the other means they cannot.
  /// The tests check the derivation against the numbers the standard prints.
  /// </remarks>
  internal static int LargestLevel(bool intra, bool last, int run) {
    var rows = intra ? _IntraRows : _InterRows;
    var largest = 0;
    foreach (var (rowLast, rowRun, rowLevel) in rows)
      if (rowLast != 0 == last && rowRun == run && rowLevel > largest)
        largest = rowLevel;

    return largest;
  }

  /// <summary>The largest run the table carries for a level (ISO/IEC 14496-2, Tables B-21 and B-22).</summary>
  internal static int LargestRun(bool intra, bool last, int level) {
    var rows = intra ? _IntraRows : _InterRows;
    var largest = -1;
    foreach (var (rowLast, rowRun, rowLevel) in rows)
      if (rowLast != 0 == last && rowLevel == level && rowRun > largest)
        largest = rowRun;

    return largest;
  }

  private static bool[] _BuildIsLast((byte Last, byte Run, byte Level)[] rows) {
    var result = new bool[rows.Length];
    for (var i = 0; i < result.Length; ++i)
      result[i] = rows[i].Last != 0;

    return result;
  }

  private static byte[] _BuildRun((byte Last, byte Run, byte Level)[] rows) {
    var result = new byte[rows.Length];
    for (var i = 0; i < result.Length; ++i)
      result[i] = rows[i].Run;

    return result;
  }

  private static byte[] _BuildLevel((byte Last, byte Run, byte Level)[] rows) {
    var result = new byte[rows.Length];
    for (var i = 0; i < result.Length; ++i)
      result[i] = rows[i].Level;

    return result;
  }

  /// <summary>Refuses a coefficient table row index the escape forms cannot use.</summary>
  internal static void RefuseNestedEscape(int code) {
    if (code == CoefficientEscape)
      throw new InvalidDataException(
        "An escaped coefficient code in the MPEG-4 block layer is followed by another escape code. ISO/IEC 14496-2 "
        + "7.4.1.3 has the first two escape forms carry an ordinary coefficient code, not another escape.");
  }
}
