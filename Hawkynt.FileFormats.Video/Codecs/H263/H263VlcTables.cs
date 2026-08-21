namespace FileFormat.Codecs.H263;

/// <summary>
/// The variable-length code tables of ITU-T H.263, transcribed from the Recommendation.
/// </summary>
/// <remarks>
/// Tables 7, 8, 12, 14 and 16 of ITU-T Rec. H.263 (01/2005), <i>Video coding for low bit rate
/// communication</i>, taken from the Recommendation itself and from nowhere else. Each was checked
/// three ways after transcription: the bit count the Recommendation prints beside every code against
/// the length of the code as written here, the prefix property (which
/// <see cref="H263VlcTable"/> enforces at construction), and for Table 14 the symmetry the table has
/// by construction — the code for a vector and the code for its negation differ in their last bit
/// and in nothing else, for all thirty-one pairs.
/// <para/>
/// The sign bit that Table 16 prints as a trailing <c>s</c> on every coefficient code is not part of
/// the codes here; it is read separately, after the code. Dropping it keeps the lookup at four
/// thousand cells instead of eight and cannot introduce an ambiguity: if one code without its sign
/// bit were a prefix of another, then that code with one of its two sign bits would be a prefix of
/// the other with its sign bit, which the Recommendation's table is not.
/// </remarks>
internal static class H263VlcTables {

  /// <summary>The value both MCBPC tables give for the stuffing code, which carries no macroblock.</summary>
  internal const int McbpcStuffing = -1;

  /// <summary>The value <see cref="Coefficient"/> gives for the escape code of H.263 5.4.2.</summary>
  internal const int CoefficientEscape = 102;

  /// <summary>The macroblock type an MCBPC value states.</summary>
  internal static int TypeOf(int mcbpc) => mcbpc >> 2;

  /// <summary>The two-bit chrominance coded block pattern an MCBPC value states.</summary>
  internal static int ChromaPatternOf(int mcbpc) => mcbpc & 3;

  /// <summary>
  /// Table 7 — MCBPC for I-pictures. The value is the macroblock type times four plus CBPC.
  /// </summary>
  /// <remarks>
  /// Only types 3 (INTRA) and 4 (INTRA+Q) appear, because in a picture that is entirely intra coded
  /// the other four types have nothing to mean.
  /// </remarks>
  internal static readonly H263VlcTable IntraMacroblockType = new(
    "Table 7/H.263 (MCBPC for I-pictures)",
    ("1", 3 * 4 + 0),
    ("001", 3 * 4 + 1),
    ("010", 3 * 4 + 2),
    ("011", 3 * 4 + 3),
    ("0001", 4 * 4 + 0),
    ("0000 01", 4 * 4 + 1),
    ("0000 10", 4 * 4 + 2),
    ("0000 11", 4 * 4 + 3),
    ("0000 0000 1", McbpcStuffing));

  /// <summary>
  /// Table 8 — MCBPC for P-pictures. The value is the macroblock type times four plus CBPC.
  /// </summary>
  /// <remarks>
  /// Types 2 (INTER4V) and 5 (INTER4V+Q) are in the table and are refused by the macroblock layer:
  /// four vectors per macroblock is the Advanced Prediction mode of Annex F, which this decoder does
  /// not implement. They are transcribed rather than left out so that such a stream is refused by
  /// name at the macroblock that uses them, instead of failing as an unrecognised code somewhere
  /// after it.
  /// </remarks>
  internal static readonly H263VlcTable PredictedMacroblockType = new(
    "Table 8/H.263 (MCBPC for P-pictures)",
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
    ("0000 0000 1", McbpcStuffing),
    ("0000 0000 010", 5 * 4 + 0),
    ("0000 0000 0110 0", 5 * 4 + 1),
    ("0000 0000 0111 0", 5 * 4 + 2),
    ("0000 0000 0111 1", 5 * 4 + 3));

  /// <summary>
  /// Table 12 — CBPY. The value is the pattern an intra macroblock means by the code; an inter
  /// macroblock means its complement.
  /// </summary>
  /// <remarks>
  /// The leftmost bit of the pattern is block one of Figure 5, which is the macroblock's top-left
  /// luminance quadrant. Reading the value as an inter pattern without complementing it produces a
  /// picture in which exactly the blocks that were coded are the ones left as pure prediction, which
  /// is not obviously wrong to look at.
  /// </remarks>
  internal static readonly H263VlcTable LuminancePattern = new(
    "Table 12/H.263 (CBPY)",
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
  /// Table 14 — MVD. The value is the difference in half-pixel units, which is the table's index less
  /// thirty-two.
  /// </summary>
  /// <remarks>
  /// Every code stands for a pair of differences thirty-two whole pixels apart, of which only one
  /// puts the reconstructed vector inside the permitted range of -16 to 15.5. The value here is the
  /// first of the pair; choosing between them is done where the vector is reconstructed, because it
  /// needs the predictor.
  /// </remarks>
  internal static readonly H263VlcTable MotionVectorDifference = new(
    "Table 14/H.263 (MVD)",
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
    ("0000 0000 0011 0", 31));

  /// <summary>
  /// Table 16 — TCOEF, without the trailing sign bit. The value is the row's index; the escape code
  /// takes <see cref="CoefficientEscape"/>.
  /// </summary>
  internal static readonly H263VlcTable Coefficient = new(
    "Table 16/H.263 (TCOEF)",
    ("10", 0),
    ("1111", 1),
    ("0101 01", 2),
    ("0010 111", 3),
    ("0001 1111", 4),
    ("0001 0010 1", 5),
    ("0001 0010 0", 6),
    ("0000 1000 01", 7),
    ("0000 1000 00", 8),
    ("0000 0000 111", 9),
    ("0000 0000 110", 10),
    ("0000 0100 000", 11),
    ("110", 12),
    ("0101 00", 13),
    ("0001 1110", 14),
    ("0000 0011 11", 15),
    ("0000 0100 001", 16),
    ("0000 0101 0000", 17),
    ("1110", 18),
    ("0001 1101", 19),
    ("0000 0011 10", 20),
    ("0000 0101 0001", 21),
    ("0110 1", 22),
    ("0001 0001 1", 23),
    ("0000 0011 01", 24),
    ("0110 0", 25),
    ("0001 0001 0", 26),
    ("0000 0101 0010", 27),
    ("0101 1", 28),
    ("0000 0011 00", 29),
    ("0000 0101 0011", 30),
    ("0100 11", 31),
    ("0000 0010 11", 32),
    ("0000 0101 0100", 33),
    ("0100 10", 34),
    ("0000 0010 10", 35),
    ("0100 01", 36),
    ("0000 0010 01", 37),
    ("0100 00", 38),
    ("0000 0010 00", 39),
    ("0010 110", 40),
    ("0000 0101 0101", 41),
    ("0010 101", 42),
    ("0010 100", 43),
    ("0001 1100", 44),
    ("0001 1011", 45),
    ("0001 0000 1", 46),
    ("0001 0000 0", 47),
    ("0000 1111 1", 48),
    ("0000 1111 0", 49),
    ("0000 1110 1", 50),
    ("0000 1110 0", 51),
    ("0000 1101 1", 52),
    ("0000 1101 0", 53),
    ("0000 0100 010", 54),
    ("0000 0100 011", 55),
    ("0000 0101 0110", 56),
    ("0000 0101 0111", 57),
    ("0111", 58),
    ("0000 1100 1", 59),
    ("0000 0000 101", 60),
    ("0011 11", 61),
    ("0000 0000 100", 62),
    ("0011 10", 63),
    ("0011 01", 64),
    ("0011 00", 65),
    ("0010 011", 66),
    ("0010 010", 67),
    ("0010 001", 68),
    ("0010 000", 69),
    ("0001 1010", 70),
    ("0001 1001", 71),
    ("0001 1000", 72),
    ("0001 0111", 73),
    ("0001 0110", 74),
    ("0001 0101", 75),
    ("0001 0100", 76),
    ("0001 0011", 77),
    ("0000 1100 0", 78),
    ("0000 1011 1", 79),
    ("0000 1011 0", 80),
    ("0000 1010 1", 81),
    ("0000 1010 0", 82),
    ("0000 1001 1", 83),
    ("0000 1001 0", 84),
    ("0000 1000 1", 85),
    ("0000 0001 11", 86),
    ("0000 0001 10", 87),
    ("0000 0001 01", 88),
    ("0000 0001 00", 89),
    ("0000 0100 100", 90),
    ("0000 0100 101", 91),
    ("0000 0100 110", 92),
    ("0000 0100 111", 93),
    ("0000 0101 1000", 94),
    ("0000 0101 1001", 95),
    ("0000 0101 1010", 96),
    ("0000 0101 1011", 97),
    ("0000 0101 1100", 98),
    ("0000 0101 1101", 99),
    ("0000 0101 1110", 100),
    ("0000 0101 1111", 101),
    ("0000 011", CoefficientEscape));

  /// <summary>
  /// The (LAST, RUN, LEVEL) triples of Table 16, in the table's own order.
  /// </summary>
  /// <remarks>
  /// Written out as one list and split into three arrays rather than as three lists, because the
  /// three are read together for every coefficient of every block and a slip that shifted one of them
  /// by a row against the others would be a picture that decodes and is wrong.
  /// </remarks>
  private static readonly (byte Last, byte Run, byte Level)[] _CoefficientRows = [
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

  /// <summary>Whether the row is the last coefficient of its block, indexed by <see cref="Coefficient"/>'s value.</summary>
  internal static readonly bool[] CoefficientIsLast = _BuildIsLast();

  /// <summary>How many zeroes precede the coefficient, indexed by <see cref="Coefficient"/>'s value.</summary>
  internal static readonly byte[] CoefficientRun = _BuildRun();

  /// <summary>The magnitude of the coefficient, indexed by <see cref="Coefficient"/>'s value.</summary>
  internal static readonly byte[] CoefficientLevel = _BuildLevel();

  private static bool[] _BuildIsLast() {
    var result = new bool[_CoefficientRows.Length];
    for (var i = 0; i < result.Length; ++i)
      result[i] = _CoefficientRows[i].Last != 0;

    return result;
  }

  private static byte[] _BuildRun() {
    var result = new byte[_CoefficientRows.Length];
    for (var i = 0; i < result.Length; ++i)
      result[i] = _CoefficientRows[i].Run;

    return result;
  }

  private static byte[] _BuildLevel() {
    var result = new byte[_CoefficientRows.Length];
    for (var i = 0; i < result.Length; ++i)
      result[i] = _CoefficientRows[i].Level;

    return result;
  }
}
