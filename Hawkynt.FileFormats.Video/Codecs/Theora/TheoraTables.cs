namespace FileFormat.Codecs.Theora;

/// <summary>The eight ways a macro block may be coded — Theora specification Table 7.18.</summary>
internal enum TheoraCodingMode {

  /// <summary>The co-located block of the previous frame, unmoved. The default.</summary>
  InterNoMotion = 0,

  /// <summary>Not predicted from anything; the only mode an intra frame uses.</summary>
  Intra = 1,

  /// <summary>The previous frame, offset by a motion vector coded here.</summary>
  InterMotion = 2,

  /// <summary>The previous frame, offset by the last motion vector used.</summary>
  InterMotionLast = 3,

  /// <summary>The previous frame, offset by the one before that.</summary>
  InterMotionLast2 = 4,

  /// <summary>The golden frame, unmoved.</summary>
  InterGoldenNoMotion = 5,

  /// <summary>The golden frame, offset by a motion vector coded here.</summary>
  InterGoldenMotion = 6,

  /// <summary>The previous frame, with a motion vector of its own for each luma block.</summary>
  InterMotionFour = 7,
}

/// <summary>The fixed tables the frame layer decodes against.</summary>
/// <remarks>
/// Every one of these is written out in the Theora specification as a table of Huffman codes, and
/// every one of them turns out to be a unary prefix followed by a fixed number of extra bits — so
/// they are decoded by counting leading ones rather than by walking a tree. The section and table
/// numbers beside each say where the values came from.
/// </remarks>
internal static class TheoraTables {

  /// <summary>
  /// The zig-zag index of each coefficient in natural order — Figure 2.8.
  /// </summary>
  /// <remarks>
  /// Natural order is row-major from lowest frequency to highest; zig-zag order is the diagonal
  /// traversal the tokens are coded in. The row and column here are frequency numbers and not pixel
  /// positions, so this table is unaffected by Theora's bottom-up pixel coordinates.
  /// </remarks>
  internal static readonly int[] ZigZag = [
    0, 1, 5, 6, 14, 15, 27, 28,
    2, 4, 7, 13, 16, 26, 29, 42,
    3, 8, 12, 17, 25, 30, 41, 43,
    9, 11, 18, 24, 31, 40, 44, 53,
    10, 19, 23, 32, 39, 45, 52, 54,
    20, 22, 33, 38, 46, 51, 55, 60,
    21, 34, 37, 47, 50, 56, 59, 61,
    35, 36, 48, 49, 57, 58, 62, 63,
  ];

  /// <summary>
  /// Which coding mode each Huffman code stands for, in schemes 1 through 6 — Table 7.19.
  /// </summary>
  /// <remarks>
  /// Six fixed permutations of the eight modes, indexed by the code's position in the unary
  /// alphabet. An encoder counts how often each mode occurs and picks whichever permutation puts
  /// the common ones on the short codes; scheme 0 spells a permutation out in the frame header and
  /// scheme 7 abandons the Huffman code and writes three bits a macro block.
  /// </remarks>
  internal static readonly byte[][] ModeSchemes = [
    [3, 4, 2, 0, 1, 5, 6, 7],
    [3, 4, 0, 2, 1, 5, 6, 7],
    [3, 2, 4, 0, 1, 5, 6, 7],
    [3, 2, 0, 4, 1, 5, 6, 7],
    [0, 3, 4, 2, 1, 5, 6, 7],
    [0, 5, 3, 4, 2, 1, 6, 7],
  ];

  /// <summary>Where each long-run Huffman code's run lengths begin — Table 7.7.</summary>
  internal static readonly int[] LongRunStart = [1, 2, 4, 6, 10, 18, 34];

  /// <summary>How many extra bits each long-run Huffman code carries — Table 7.7.</summary>
  internal static readonly int[] LongRunBits = [0, 1, 1, 2, 3, 4, 12];

  /// <summary>The longest run the long-run code can state, after which a fresh bit value is read.</summary>
  internal const int LONG_RUN_MAXIMUM = 4129;

  /// <summary>Where each short-run Huffman code's run lengths begin — Table 7.11.</summary>
  internal static readonly int[] ShortRunStart = [1, 3, 5, 7, 11, 15];

  /// <summary>How many extra bits each short-run Huffman code carries — Table 7.11.</summary>
  internal static readonly int[] ShortRunBits = [1, 1, 1, 2, 2, 4];

  /// <summary>
  /// Which reference frame each coding mode predicts from — Table 7.46.
  /// </summary>
  /// <remarks>
  /// Zero means none, one the previous frame, two the golden frame. Used twice over: to pick the
  /// plane a predictor is copied out of, and — more subtly — to decide which neighbours a block's DC
  /// coefficient may be predicted from, since only blocks predicting from the same reference frame
  /// count. The two are treated as different even when the golden frame and the previous frame are
  /// in fact the same picture.
  /// </remarks>
  internal static readonly int[] ReferenceFrameOf = [1, 0, 1, 1, 1, 2, 2, 1];

  /// <summary>
  /// The weights and divisor for each combination of available DC predictors — Table 7.47.
  /// </summary>
  /// <remarks>
  /// Indexed by the four availability flags packed as
  /// <c>left | (lowerLeft &lt;&lt; 1) | (lower &lt;&lt; 2) | (lowerRight &lt;&lt; 3)</c>, five
  /// entries a row: the four weights and then the divisor. The odd-looking 75/53 over 128 and
  /// 29/−26/29 over 32 are the specification's, and the negative weight is not a misprint — the
  /// three-neighbour predictor extrapolates a gradient rather than averaging.
  /// </remarks>
  internal static readonly int[][] DcPredictorWeights = [
    [0, 0, 0, 0, 1],
    [1, 0, 0, 0, 1],
    [0, 1, 0, 0, 1],
    [1, 0, 0, 0, 1],
    [0, 0, 1, 0, 1],
    [1, 0, 1, 0, 2],
    [0, 0, 1, 0, 1],
    [29, -26, 29, 0, 32],
    [0, 0, 0, 1, 1],
    [75, 0, 0, 53, 128],
    [0, 1, 0, 1, 2],
    [75, 0, 0, 53, 128],
    [0, 0, 1, 0, 1],
    [75, 0, 0, 53, 128],
    [0, 3, 10, 3, 16],
    [29, -26, 29, 0, 32],
  ];

  /// <summary>
  /// The 16-bit approximations of the cosines and sines the inverse transform is built from —
  /// Table 7.65.
  /// </summary>
  /// <remarks>
  /// Indexed one through seven: entry <c>i</c> is the integer nearest to
  /// <c>cos(i * pi / 16) * 65536</c>, which is also <c>sin((8 - i) * pi / 16) * 65536</c>. Written
  /// out rather than computed because the transform is normative to the bit and a decoder that
  /// rounded one of these differently would drift through the whole prediction loop.
  /// </remarks>
  internal static readonly int[] Cosine = [0, 64277, 60547, 54491, 46341, 36410, 25080, 12785];

  /// <summary>Which of the five Huffman table groups a token index belongs to — Table 7.42.</summary>
  /// <remarks>
  /// The DC coefficient has a group to itself and the 63 AC coefficients are split into four bands.
  /// The bands are uneven on purpose: the low frequencies carry most of the energy and vary most in
  /// their statistics, so they get finer-grained codebooks.
  /// </remarks>
  internal static int HuffmanGroupOf(int tokenIndex) => tokenIndex switch {
    0 => 0,
    <= 5 => 1,
    <= 14 => 2,
    <= 27 => 3,
    _ => 4,
  };
}
