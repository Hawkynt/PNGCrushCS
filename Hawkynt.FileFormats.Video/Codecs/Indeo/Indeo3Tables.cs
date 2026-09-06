using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Indeo 3's constant tables: the twenty-four vector-quantisation sets a cell chooses one or two of,
/// and the requantisation table that keeps a reference cell inside the range the set it is about to be
/// coded against can reach.
/// </summary>
/// <remarks>
/// None of this is transmitted. A cell states a mode and a table index in one byte and nothing else,
/// so a decoder that does not already hold the same numbers reconstructs a different picture; they are
/// copied here exactly as the reference decoder holds them. See <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>
/// beside this file for where they come from.
/// <para/>
/// <b>The sets are written compressed, the way the format's own tables are.</b> A set is a list of
/// delta <i>pairs</i>, and most pairs stand for two or four table entries rather than one: <c>E2(a,b)</c>
/// is the pair and its negation, <c>E4(a,b)</c> is those two and the same pair swapped and negated
/// again. Writing the expansion out would be four times the text and would hide which numbers the
/// format actually chose, so the expansion is done once here instead.
/// <para/>
/// A pair is stored packed rather than as two numbers, because that is how it is applied: a dyad code
/// adds one 16-bit word to two neighbouring samples at once, and the arithmetic that carries between
/// the two lanes is part of the result rather than an accident of it. The 8x8 modes use the same pairs
/// with each delta doubled up into a 32-bit word, so that one addition covers four samples.
/// <para/>
/// Sets 1 and 2 hold 195 entries down to 77 as the quantiser coarsens; set 3 is the one used for
/// chrominance, and its last table stands in for the four indices above it — a stream may name index
/// 20 to 23 and every one of them is the same table.
/// </remarks>
internal static class Indeo3Tables {

  /// <summary>The number of vector-quantisation sets a cell may name.</summary>
  internal const int TABLE_COUNT = 24;

  /// <summary>The first table index whose quad codes address their two dyads the other way round.</summary>
  internal const int FIRST_SWAPPED_TABLE = 16;

  /// <summary>
  /// One compressed entry of a quantisation set: a delta pair and how many table entries it stands for.
  /// </summary>
  private readonly record struct Dyad(int Count, int A, int B);

  /// <summary>One entry: the pair itself.</summary>
  private static Dyad Pd(int a, int b) => new(1, a, b);

  /// <summary>Two entries: the pair and its negation.</summary>
  private static Dyad E2(int a, int b) => new(2, a, b);

  /// <summary>Four entries: the pair, its negation, the pair reversed, and that reversed pair negated.</summary>
  private static Dyad E4(int a, int b) => new(4, a, b);

  private static readonly Dyad[] _Set1Step1 = [
    Pd(   0,   0), E2(   2,   2), E4(  -1,   3), E2(   4,   4), E4(   1,   5),
    E2(  -4,   4), E4(  -2,   6), E4(   4,   9), E2(   9,   9), E4(   1,  10),
    E4(  -5,   8), E4(   9,  15), E4(  -3,  12), E4(   4,  16), E2(  16,  16),
    E4(   0,  18), E2( -12,  12), E4(  -9,  16), E4(  11,  27), E4(  19,  28),
    E4(  -6,  22), E4(   4,  29), E2(  30,  30), E4(  -2,  33), E4( -18,  23),
    E4( -15,  30), E4(  22,  46), E4(  13,  47), E4(  35,  49), E4( -11,  41),
    E4(   4,  51), E2(  54,  54), E2( -34,  34), E4( -29,  42), E4(  -6,  60),
    E4(  27,  76), E4(  43,  77), E4( -24,  55), E4(  14,  79), E4(  63,  83),
    E4( -20,  74), E4(   2,  88), E2(  93,  93), E4( -52,  61), E4(  52, 120),
    E4( -45,  75), E4(  75, 125), E4(  33, 122), E4( -13, 103), E4( -40,  96),
    E4( -34, 127), E2( -89,  89), E4( -78, 105), E2(  12,  12), E2(  23,  23),
    E2(  42,  42), E2(  73,  73),
  ];

  private static readonly Dyad[] _Set1Step2 = [
    Pd(   0,   0), E2(   3,   3), E4(  -1,   4), E2(   7,   7), E4(   2,   8),
    E4(  -2,   9), E2(  -6,   6), E4(   6,  13), E2(  13,  13), E4(   1,  14),
    E4(  -8,  12), E4(  14,  23), E4(  -5,  18), E4(   6,  24), E2(  24,  24),
    E4(  -1,  27), E2( -17,  17), E4( -13,  23), E4(  16,  40), E4(  28,  41),
    E4(  -9,  33), E4(   6,  43), E2(  46,  46), E4(  -4,  50), E4( -27,  34),
    E4( -22,  45), E4(  34,  69), E4(  19,  70), E4(  53,  73), E4( -17,  62),
    E4(   5,  77), E2(  82,  82), E2( -51,  51), E4( -43,  64), E4( -10,  90),
    E4(  41, 114), E4(  64, 116), E4( -37,  82), E4(  22, 119), E4(  95, 124),
    E4( -30, 111), E4( -78,  92), E4( -68, 113), E2(  18,  18), E2(  34,  34),
    E2(  63,  63), E2( 109, 109),
  ];

  private static readonly Dyad[] _Set1Step3 = [
    Pd(   0,   0), E2(   4,   4), E4(  -1,   5), E4(   3,  10), E2(   9,   9),
    E2(  -7,   7), E4(  -3,  12), E4(   8,  17), E2(  17,  17), E4(   1,  19),
    E4( -11,  16), E4(  -6,  23), E4(  18,  31), E4(   8,  32), E2(  33,  33),
    E4(  -1,  36), E2( -23,  23), E4( -17,  31), E4(  21,  54), E4(  37,  55),
    E4( -12,  44), E4(   8,  57), E2(  61,  61), E4(  -5,  66), E4( -36,  45),
    E4( -29,  60), E4(  45,  92), E4(  25,  93), E4(  71,  97), E4( -22,  83),
    E4(   7, 102), E2( 109, 109), E2( -68,  68), E4( -57,  85), E4( -13, 120),
    E4( -49, 110), E4(-104, 123), E2(  24,  24), E2(  46,  46), E2(  84,  84),
  ];

  private static readonly Dyad[] _Set1Step4 = [
    Pd(   0,   0), E2(   5,   5), E4(  -2,   7), E2(  11,  11), E4(   3,  13),
    E2(  -9,   9), E4(  -4,  15), E4(  11,  22), E2(  21,  21), E4(   2,  24),
    E4( -14,  20), E4(  23,  38), E4(  -8,  29), E4(  11,  39), E2(  41,  41),
    E4(  -1,  45), E2( -29,  29), E4( -22,  39), E4(  27,  67), E4(  47,  69),
    E4( -15,  56), E4(  11,  71), E2(  76,  76), E4(  -6,  83), E4( -45,  57),
    E4( -36,  75), E4(  56, 115), E4(  31, 117), E4(  88, 122), E4( -28, 104),
    E2( -85,  85), E4( -72, 106), E2(  30,  30), E2(  58,  58), E2( 105, 105),
  ];

  private static readonly Dyad[] _Set1Step5 = [
    Pd(   0,   0), E2(   6,   6), E4(  -2,   8), E2(  13,  13), E4(   4,  15),
    E2( -11,  11), E4(  -5,  18), E4(  13,  26), E2(  26,  26), E4(   2,  29),
    E4( -16,  24), E4(  28,  46), E4(  -9,  35), E4(  13,  47), E2(  49,  49),
    E4(  -1,  54), E2( -35,  35), E4( -26,  47), E4(  32,  81), E4(  56,  83),
    E4( -18,  67), E4(  13,  86), E2(  91,  91), E4(  -7,  99), E4( -54,  68),
    E4( -44,  90), E4( -33, 124), E2(-103, 103), E4( -86, 127), E2(  37,  37),
    E2(  69,  69),
  ];

  private static readonly Dyad[] _Set1Step6 = [
    Pd(   0,   0), E2(   7,   7), E4(  -3,  10), E2(  16,  16), E4(   5,  18),
    E2( -13,  13), E4(  -6,  21), E4(  15,  30), E2(  30,  30), E4(   2,  34),
    E4( -19,  28), E4(  32,  54), E4( -11,  41), E4(  15,  55), E2(  57,  57),
    E4(  -1,  63), E2( -40,  40), E4( -30,  55), E4(  37,  94), E4(  65,  96),
    E4( -21,  78), E4(  15, 100), E2( 106, 106), E4(  -8, 116), E4( -63,  79),
    E4( -51, 105), E2(-120, 120), E2(  43,  43), E2(  80,  80),
  ];

  private static readonly Dyad[] _Set1Step7 = [
    Pd(   0,   0), E2(   8,   8), E4(  -3,  11), E2(  18,  18), E4(   5,  20),
    E2( -15,  15), E4(  -7,  24), E4(  17,  35), E2(  34,  34), E4(   3,  38),
    E4( -22,  32), E4(  37,  61), E4( -13,  47), E4(  17,  63), E2(  65,  65),
    E4(  -1,  72), E2( -46,  46), E4( -35,  63), E4(  43, 107), E4(  75, 110),
    E4( -24,  89), E4(  17, 114), E2( 121, 121), E4( -72,  91), E4( -58, 120),
    E2(  49,  49), E2(  92,  92),
  ];

  private static readonly Dyad[] _Set1Step8 = [
    Pd(   0,   0), E2(   9,   9), E4(  -3,  12), E2(  20,  20), E4(   6,  23),
    E2( -17,  17), E4(  -7,  27), E4(  19,  39), E2(  39,  39), E4(   3,  43),
    E4( -24,  36), E4(  42,  69), E4( -14,  53), E4(  19,  71), E2(  73,  73),
    E4(  -2,  80), E2( -52,  52), E4( -39,  70), E4(  48, 121), E4(  84, 124),
    E4( -27, 100), E4( -81, 102), E2(  55,  55), E2( 104, 104),
  ];

  private static readonly Dyad[] _Set2Step1 = [
    Pd(   0,   0), E2(   2,   2), E4(   0,   2), E2(   4,   4), E4(   0,   4),
    E2(  -4,   4), E4(  -2,   6), E4(   4,   8), E2(   8,   8), E4(   0,  10),
    E4(  -4,   8), E4(   8,  14), E4(  -2,  12), E4(   4,  16), E2(  16,  16),
    E4(   0,  18), E2( -12,  12), E4(  -8,  16), E4(  10,  26), E4(  18,  28),
    E4(  -6,  22), E4(   4,  28), E2(  30,  30), E4(  -2,  32), E4( -18,  22),
    E4( -14,  30), E4(  22,  46), E4(  12,  46), E4(  34,  48), E4( -10,  40),
    E4(   4,  50), E2(  54,  54), E2( -34,  34), E4( -28,  42), E4(  -6,  60),
    E4(  26,  76), E4(  42,  76), E4( -24,  54), E4(  14,  78), E4(  62,  82),
    E4( -20,  74), E4(   2,  88), E2(  92,  92), E4( -52,  60), E4(  52, 118),
    E4( -44,  74), E4(  74, 118), E4(  32, 118), E4( -12, 102), E4( -40,  96),
    E4( -34, 118), E2( -88,  88), E4( -78, 104), E2(  12,  12), E2(  22,  22),
    E2(  42,  42), E2(  72,  72),
  ];

  private static readonly Dyad[] _Set2Step2 = [
    Pd(   0,   0), E2(   3,   3), E4(   0,   3), E2(   6,   6), E4(   3,   9),
    E4(  -3,   9), E2(  -6,   6), E4(   6,  12), E2(  12,  12), E4(   0,  15),
    E4(  -9,  12), E4(  15,  24), E4(  -6,  18), E4(   6,  24), E2(  24,  24),
    E4(   0,  27), E2( -18,  18), E4( -12,  24), E4(  15,  39), E4(  27,  42),
    E4(  -9,  33), E4(   6,  42), E2(  45,  45), E4(  -3,  51), E4( -27,  33),
    E4( -21,  45), E4(  33,  69), E4(  18,  69), E4(  54,  72), E4( -18,  63),
    E4(   6,  78), E2(  81,  81), E2( -51,  51), E4( -42,  63), E4(  -9,  90),
    E4(  42, 114), E4(  63, 117), E4( -36,  81), E4(  21, 120), E4(  96, 123),
    E4( -30, 111), E4( -78,  93), E4( -69, 114), E2(  18,  18), E2(  33,  33),
    E2(  63,  63), E2( 108, 108),
  ];

  private static readonly Dyad[] _Set2Step3 = [
    Pd(   0,   0), E2(   4,   4), E4(   0,   4), E4(   4,   8), E2(   8,   8),
    E2(  -8,   8), E4(  -4,  12), E4(   8,  16), E2(  16,  16), E4(   0,  20),
    E4( -12,  16), E4(  -4,  24), E4(  16,  32), E4(   8,  32), E2(  32,  32),
    E4(   0,  36), E2( -24,  24), E4( -16,  32), E4(  20,  52), E4(  36,  56),
    E4( -12,  44), E4(   8,  56), E2(  60,  60), E4(  -4,  64), E4( -36,  44),
    E4( -28,  60), E4(  44,  92), E4(  24,  92), E4(  72,  96), E4( -20,  84),
    E4(   8, 100), E2( 108, 108), E2( -68,  68), E4( -56,  84), E4( -12, 120),
    E4( -48, 108), E4(-104, 124), E2(  24,  24), E2(  44,  44), E2(  84,  84),
  ];

  private static readonly Dyad[] _Set2Step4 = [
    Pd(   0,   0), E2(   5,   5), E4(   0,   5), E2(  10,  10), E4(   5,  15),
    E2( -10,  10), E4(  -5,  15), E4(  10,  20), E2(  20,  20), E4(   0,  25),
    E4( -15,  20), E4(  25,  40), E4( -10,  30), E4(  10,  40), E2(  40,  40),
    E4(   0,  45), E2( -30,  30), E4( -20,  40), E4(  25,  65), E4(  45,  70),
    E4( -15,  55), E4(  10,  70), E2(  75,  75), E4(  -5,  85), E4( -45,  55),
    E4( -35,  75), E4(  55, 115), E4(  30, 115), E4(  90, 120), E4( -30, 105),
    E2( -85,  85), E4( -70, 105), E2(  30,  30), E2(  60,  60), E2( 105, 105),
  ];

  private static readonly Dyad[] _Set2Step5 = [
    Pd(   0,   0), E2(   6,   6), E4(   0,   6), E2(  12,  12), E4(   6,  12),
    E2( -12,  12), E4(  -6,  18), E4(  12,  24), E2(  24,  24), E4(   0,  30),
    E4( -18,  24), E4(  30,  48), E4(  -6,  36), E4(  12,  48), E2(  48,  48),
    E4(   0,  54), E2( -36,  36), E4( -24,  48), E4(  30,  78), E4(  54,  84),
    E4( -18,  66), E4(  12,  84), E2(  90,  90), E4(  -6,  96), E4( -54,  66),
    E4( -42,  90), E4( -30, 126), E2(-102, 102), E4( -84, 126), E2(  36,  36),
    E2(  66,  66),
  ];

  private static readonly Dyad[] _Set2Step6 = [
    Pd(   0,   0), E2(   7,   7), E4(   0,   7), E2(  14,  14), E4(   7,  21),
    E2( -14,  14), E4(  -7,  21), E4(  14,  28), E2(  28,  28), E4(   0,  35),
    E4( -21,  28), E4(  35,  56), E4( -14,  42), E4(  14,  56), E2(  56,  56),
    E4(   0,  63), E2( -42,  42), E4( -28,  56), E4(  35,  91), E4(  63,  98),
    E4( -21,  77), E4(  14,  98), E2( 105, 105), E4(  -7, 119), E4( -63,  77),
    E4( -49, 105), E2(-119, 119), E2(  42,  42), E2(  77,  77),
  ];

  private static readonly Dyad[] _Set2Step7 = [
    Pd(   0,   0), E2(   8,   8), E4(   0,   8), E2(  16,  16), E4(   8,  16),
    E2( -16,  16), E4(  -8,  24), E4(  16,  32), E2(  32,  32), E4(   0,  40),
    E4( -24,  32), E4(  40,  64), E4( -16,  48), E4(  16,  64), E2(  64,  64),
    E4(   0,  72), E2( -48,  48), E4( -32,  64), E4(  40, 104), E4(  72, 112),
    E4( -24,  88), E4(  16, 112), E2( 120, 120), E4( -72,  88), E4( -56, 120),
    E2(  48,  48), E2(  88,  88),
  ];

  private static readonly Dyad[] _Set2Step8 = [
    Pd(   0,   0), E2(   9,   9), E4(   0,   9), E2(  18,  18), E4(   9,  27),
    E2( -18,  18), E4(  -9,  27), E4(  18,  36), E2(  36,  36), E4(   0,  45),
    E4( -27,  36), E4(  45,  72), E4( -18,  54), E4(  18,  72), E2(  72,  72),
    E4(   0,  81), E2( -54,  54), E4( -36,  72), E4(  45, 117), E4(  81, 126),
    E4( -27,  99), E4( -81,  99), E2(  54,  54), E2( 108, 108),
  ];

  private static readonly Dyad[] _Set3Step1 = [
    Pd(   0,   0), E2(   2,   2), E4(   0,   3), E2(   6,   6), E4(   0,   7),
    E2(  -5,   5), E2(   5,  -5), E4(   6,  11), E4(   0,   8), E2(  11,  11),
    E4(   0,  12), E4(  12,  17), E2(  17,  17), E4(   6,  18), E4(  -8,  11),
    E4(   0,  15), E4(   0,  20), E4(  18,  25), E4(  11,  25), E2(  25,  25),
    E2( -14,  14), E2(  14, -14), E4(   0,  26), E4( -11,  18), E4(  -7,  22),
    E4(  26,  34), E4(  18,  34), E2(  34,  34), E4(  11,  35), E4(   0,  29),
    E4( -19,  22), E4( -15,  26), E4(   0,  37), E4(  27,  44), E4(  36,  44),
    E4(  18,  44), E4( -10,  33), E2(  45,  45),
  ];

  private static readonly Dyad[] _Set3Step2 = [
    Pd(   0,   0), E4(   0,   2), E2(   2,   2), E2(   6,   6), E4(   0,   6),
    E2(  -4,   4), E2(  10,  -6), E2(   0, -12), Pd(  -6, -12), E2(   6, -12),
    Pd(   6,  12), E2( -14,   0), E2(  12,  12), E2(   0, -18), E2(  14, -12),
    Pd( -18,  -6), E2(  18,  -6), Pd(  18,   6), Pd( -10, -18), E2(  10, -18),
    Pd(  10,  18), E2( -22,   0), E2(   0, -24), Pd( -22, -12), E2(  22, -12),
    Pd(  22,  12), Pd(  -8, -24), E2(   8, -24), Pd(   8,  24), Pd( -26,  -6),
    E2(  26,  -6), Pd(  26,   6), E2( -28,   0), E2(  20,  20), E2( -14, -26),
    E2( -30, -12), E2( -10, -32), E2( -18, -32), E2( -26, -26), E2( -34, -20),
    E2( -38, -12), E2( -32, -32), Pd(  32,  32), Pd( -22, -40), E2( -34, -34),
  ];

  private static readonly Dyad[] _Set3Step3 = [
    Pd(   0,   0), E4(   0,   2), E2(   4,   4), E2(  10,  10), E4(   0,  10),
    E2(  -6,   6), E2(  14,  -8), E2( -18,   0), E2(  10, -16), E2(   0, -24),
    Pd( -24,  -8), E2(  24,  -8), Pd(  24,   8), E2(  18,  18), E2(  20, -16),
    Pd( -14, -26), E2(  14, -26), Pd(  14,  26), E2( -30,   0), E2(   0, -34),
    Pd( -34,  -8), E2(  34,  -8), Pd(  34,   8), Pd( -30, -18), E2(  30, -18),
    Pd(  30,  18), Pd( -10, -34), E2(  10, -34), Pd(  10,  34), E2( -20, -34),
    E2( -40,   0), E2(  30,  30), E2( -40, -18), E2(   0, -44), E2( -16, -44),
    Pd( -36, -36), E2( -36, -36), E2( -26, -44), E2( -46, -26), E2( -52, -18),
    Pd( -20, -54), E2( -44, -44), Pd( -32, -54), Pd( -46, -46), E2( -46, -46),
  ];

  private static readonly Dyad[] _Set3Step4 = [
    Pd(   0,   0), E4(   0,   4), E2(   4,   4), E2(  12,  12), E4(   0,  12),
    E2(  -8,   8), E2(   8, -16), E2(   0, -24), Pd( -24,  -8), E2(  24,  -8),
    Pd(  24,   8), E2(  20, -16), E2( -28,   0), Pd( -16, -24), E2(  16, -24),
    Pd(  16,  24), E2(   0, -32), Pd( -28, -16), E2(  28, -16), Pd(  28,  16),
    Pd(  -8, -32), Pd(   8, -32), Pd( -32,  -8), E2(  32,  -8), Pd(  32,   8),
    Pd(  -8,  32), Pd(   8,  32), E2(  24,  24), E2(  24, -24), E2( -20, -32),
    E2( -40,   0), E2( -40, -16), Pd(   0, -44), Pd(   0, -44), E2( -44,   0),
    Pd(   0,  44), Pd(   0,  44), E2( -32, -32), E2( -16, -44), Pd( -24, -44),
    E2( -44, -24), Pd(  24,  44), E2( -48, -16), Pd( -36, -36), E2( -36, -36),
    Pd(  36,  36), Pd( -20, -52), E2(  40,  40), Pd( -32, -52),
  ];

  private static readonly Dyad[] _Set3Step5 = [
    Pd(   0,   0), E2(   2,   2), E2(   6,   6), E2(  12,  12), E2(  20,  20),
    E2(  32,  32), E2(  46,  46),
  ];

  /// <summary>
  /// One quantisation set as a decoder uses it: the same deltas packed for the 4-wide and the 8-wide
  /// block modes, how many of them a dyad code may name, and the divisor a quad code is split by.
  /// </summary>
  /// <param name="Deltas">Each entry a pair of deltas packed little-endian into one 16-bit word.</param>
  /// <param name="WideDeltas">Each entry the same pair with both deltas doubled up into 32 bits.</param>
  /// <param name="DyadCount">Codes below this name one entry directly; codes at or above it are quads.</param>
  /// <param name="QuadDivisor">A quad code divided by this gives one entry index and its remainder the other.</param>
  internal sealed record VqTable(short[] Deltas, int[] WideDeltas, int DyadCount, int QuadDivisor);

  /// <summary>The twenty-four sets, in the order a cell's table index addresses them.</summary>
  internal static readonly VqTable[] Tables = _BuildTables();

  /// <summary>
  /// The requantisation table, indexed by quantiser step and then by sample value.
  /// </summary>
  /// <remarks>
  /// Applied to a cell's reference pixels when the cell is coded against a coarser set than the one
  /// that produced them, so that adding a delta cannot leave the seven bits a sample is held in. The
  /// last few entries of several rows, and two entries in the middle of two others, are set by hand
  /// afterwards: those are what the original decoders do, not what the formula gives, and a decoder
  /// that computes them instead drifts from every file those values were encoded against.
  /// </remarks>
  internal static readonly byte[][] Requantise = _BuildRequantisationTable();

  private static VqTable[] _BuildTables() {
    // Sets 1 and 2 are the luminance sets, one per quantiser step; set 3 is the chrominance set, whose
    // coarsest table answers to the four indices above the ones it defines.
    var set1 = new[] { _Set1Step1, _Set1Step2, _Set1Step3, _Set1Step4, _Set1Step5, _Set1Step6, _Set1Step7, _Set1Step8 };
    var set2 = new[] { _Set2Step1, _Set2Step2, _Set2Step3, _Set2Step4, _Set2Step5, _Set2Step6, _Set2Step7, _Set2Step8 };
    var set3 = new[] { _Set3Step1, _Set3Step2, _Set3Step3, _Set3Step4, _Set3Step5 };

    // How many entries each set holds and what a quad code is divided by. Both are part of the format:
    // a table is addressed up to its own length even where the compressed form stops short of it, and
    // the entries past the end are pairs of zero.
    int[] lengths1 = [195, 159, 133, 115, 101, 93, 87, 77];
    int[] divisors1 = [7, 9, 10, 11, 12, 12, 12, 13];
    int[] lengths3 = [128, 79, 79, 79, 79];
    int[] divisors3 = [11, 13, 13, 13, 13];

    var tables = new VqTable[TABLE_COUNT];
    for (var i = 0; i < 8; ++i) {
      tables[i] = _Expand(set1[i], lengths1[i], divisors1[i]);
      tables[i + 8] = _Expand(set2[i], lengths1[i], divisors1[i]);
    }

    for (var i = 0; i < 5; ++i)
      tables[i + 16] = _Expand(set3[i], lengths3[i], divisors3[i]);

    // Indices 21 to 23 are the coarsest chrominance table over again.
    for (var i = 21; i < TABLE_COUNT; ++i)
      tables[i] = tables[20];

    return tables;
  }

  /// <summary>Expands one compressed set into the two packed forms a decoder adds with.</summary>
  private static VqTable _Expand(Dyad[] compressed, int length, int divisor) {
    var deltas = new short[length];
    var wide = new int[length];
    var at = 0;

    foreach (var entry in compressed) {
      _Pack(deltas, wide, ref at, entry.A, entry.B);
      if (entry.Count == 1)
        continue;

      _Pack(deltas, wide, ref at, -entry.A, -entry.B);
      if (entry.Count == 2)
        continue;

      _Pack(deltas, wide, ref at, entry.B, entry.A);
      _Pack(deltas, wide, ref at, -entry.B, -entry.A);
    }

    return new(deltas, wide, length, divisor);
  }

  /// <summary>
  /// Packs one delta pair into both forms.
  /// </summary>
  /// <remarks>
  /// Little-endian on purpose and not because of the machine this runs on: the packing is what makes
  /// one addition apply two deltas to two neighbouring samples, and which delta lands on which sample
  /// is decided by the byte order the samples themselves are stored in, which is the picture's.
  /// </remarks>
  private static void _Pack(short[] deltas, int[] wide, ref int at, int a, int b) {
    deltas[at] = (short)((b << 8) + a);
    wide[at] = (b << 24) + (b << 16) + (a << 8) + a;
    ++at;
  }

  private static byte[][] _BuildRequantisationTable() {
    ReadOnlySpan<int> offsets = [1, 1, 2, -3, -3, 3, 4, 4];
    ReadOnlySpan<int> deltas = [0, 1, 0, 4, 4, 1, 0, 1];

    var table = new byte[8][];
    for (var i = 0; i < 8; ++i) {
      var step = i + 2;
      var row = table[i] = new byte[128];
      for (var j = 0; j < 128; ++j)
        row[j] = (byte)((j + offsets[i]) / step * step + deltas[i]);
    }

    // The formula runs past 127 at the top of several rows, and a sample never may: each of these is
    // the largest value the row's own step can reach.
    table[0][127] = 126;
    table[1][119] = 118;
    table[1][120] = 118;
    table[2][126] = 124;
    table[2][127] = 124;
    table[6][124] = 120;
    table[6][125] = 120;
    table[6][126] = 120;
    table[6][127] = 120;

    // The two the formula gets right and Intel's own decoders do not. Matching them is the point.
    table[1][7] = 10;
    table[4][8] = 10;

    return table;
  }
}
