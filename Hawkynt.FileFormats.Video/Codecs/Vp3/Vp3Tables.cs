using System.Text;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// The constant tables of VP3: the ones the Theora specification prints in Appendix B as VP3's
/// hard-coded equivalents of its setup header, and the ones both formats share.
/// </summary>
/// <remarks>
/// Theora carries the loop filter limits, the quantisation scales and base matrices, and the DCT
/// token codebooks in a setup header. VP3 has no setup header — its container states the frame size
/// and nothing else — so all of it is fixed, and Appendix B of the Theora specification is where the
/// fixed values are written down. Everything in this file is from there or from the body of the
/// specification, and from nowhere else.
/// </remarks>
internal static class Vp3Tables {

  /// <summary>
  /// The loop filter limit for each quantisation index, Theora Appendix B.2.
  /// </summary>
  /// <remarks>
  /// Indexed by the frame's quantisation index, not by anything per-block: one limit drives the
  /// deblocking of a whole frame. The last sixteen are zero, so above a quantisation index of 47 —
  /// which is to say at the finest quantisers — the filter does nothing at all, because there is no
  /// blocking to remove.
  /// </remarks>
  internal static readonly int[] LoopFilterLimits = [
    30, 25, 20, 20, 15, 15, 14, 14,
    13, 13, 12, 12, 11, 11, 10, 10,
    9, 9, 8, 8, 7, 7, 7, 7,
    6, 6, 6, 6, 5, 5, 5, 5,
    4, 4, 4, 4, 3, 3, 3, 3,
    2, 2, 2, 2, 2, 2, 2, 2,
    0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0,
  ];

  /// <summary>The AC scale for each quantisation index, Theora Appendix B.3.</summary>
  internal static readonly int[] AcScale = [
    500, 450, 400, 370, 340, 310, 285, 265,
    245, 225, 210, 195, 185, 180, 170, 160,
    150, 145, 135, 130, 125, 115, 110, 107,
    100, 96, 93, 89, 85, 82, 75, 74,
    70, 68, 64, 60, 57, 56, 52, 50,
    49, 45, 44, 43, 40, 38, 37, 35,
    33, 32, 30, 29, 28, 25, 24, 22,
    21, 19, 18, 17, 15, 13, 12, 10,
  ];

  /// <summary>The DC scale for each quantisation index, Theora Appendix B.3.</summary>
  internal static readonly int[] DcScale = [
    220, 200, 190, 180, 170, 170, 160, 160,
    150, 150, 140, 140, 130, 130, 120, 120,
    110, 110, 100, 100, 90, 90, 90, 80,
    80, 80, 70, 70, 70, 60, 60, 60,
    60, 50, 50, 50, 50, 40, 40, 40,
    40, 40, 30, 30, 30, 30, 30, 30,
    30, 20, 20, 20, 20, 20, 20, 20,
    20, 10, 10, 10, 10, 10, 10, 10,
  ];

  /// <summary>
  /// The three base matrices of Theora Appendix B.3, in natural (row-major) coefficient order.
  /// </summary>
  /// <remarks>
  /// The first is the luma matrix for intra blocks, the second the chroma matrix for intra blocks,
  /// and the third serves every plane of every inter block. Theora allows a stream to interpolate
  /// between as many as three hundred and eighty-four of these across the range of quantisation
  /// indices; VP3 has one per quantisation type and plane and it does not vary, which is why
  /// <see cref="Vp3Quantisation"/> has no interpolation in it.
  /// </remarks>
  internal static readonly int[][] BaseMatrices = [
    [
      16, 11, 10, 16, 24, 40, 51, 61,
      12, 12, 14, 19, 26, 58, 60, 55,
      14, 13, 16, 24, 40, 57, 69, 56,
      14, 17, 22, 29, 51, 87, 80, 62,
      18, 22, 37, 58, 68, 109, 103, 77,
      24, 35, 55, 64, 81, 104, 113, 92,
      49, 64, 78, 87, 103, 121, 120, 101,
      72, 92, 95, 98, 112, 100, 103, 99,
    ],
    [
      17, 18, 24, 47, 99, 99, 99, 99,
      18, 21, 26, 66, 99, 99, 99, 99,
      24, 26, 56, 99, 99, 99, 99, 99,
      47, 66, 99, 99, 99, 99, 99, 99,
      99, 99, 99, 99, 99, 99, 99, 99,
      99, 99, 99, 99, 99, 99, 99, 99,
      99, 99, 99, 99, 99, 99, 99, 99,
      99, 99, 99, 99, 99, 99, 99, 99,
    ],
    [
      16, 16, 16, 20, 24, 28, 32, 40,
      16, 16, 20, 24, 28, 32, 40, 48,
      16, 20, 24, 28, 32, 40, 48, 64,
      20, 24, 28, 32, 40, 48, 64, 64,
      24, 28, 32, 40, 48, 64, 64, 64,
      28, 32, 40, 48, 64, 64, 64, 96,
      32, 40, 48, 64, 64, 64, 96, 128,
      40, 48, 64, 64, 64, 96, 128, 128,
    ],
  ];

  /// <summary>
  /// Which base matrix each quantisation type and colour plane uses, from QRBMIS in Appendix B.3.
  /// </summary>
  /// <remarks>Indexed by quantisation type — 0 intra, 1 inter — and then by colour plane.</remarks>
  internal static readonly int[][] BaseMatrixOf = [[0, 1, 1], [2, 2, 2]];

  /// <summary>
  /// The zig-zag index of each coefficient in natural order, Figure 2.8.
  /// </summary>
  /// <remarks>
  /// Coefficients arrive from the bitstream in zig-zag order and are dequantised into natural order,
  /// so this maps the natural position a coefficient belongs at onto the position it was read at.
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
  /// The sixteen-bit sines and cosines the inverse DCT scales by, Table 7.65.
  /// </summary>
  /// <remarks>
  /// Indexed so that <c>Cosines[i]</c> is the approximation of both cos(i&#960;/16) and
  /// sin((8&#8722;i)&#960;/16); the entry for zero is never used, because C0 is one and nothing in the
  /// transform multiplies by it.
  /// </remarks>
  internal static readonly int[] Cosines = [0, 64277, 60547, 54491, 46341, 36410, 25080, 12785];

  /// <summary>The reference frame each macro block coding mode predicts from, Table 7.46.</summary>
  /// <remarks>Zero means none — the intra predictor — one the previous frame, two the golden frame.</remarks>
  internal static readonly int[] ReferenceOfMode = [1, 0, 1, 1, 1, 2, 2, 1];

  /// <summary>The mode each Huffman code names under coding schemes one to six, Table 7.19.</summary>
  /// <remarks>
  /// The first row is unused: scheme zero states its own assignment in the frame, and scheme seven
  /// codes the mode number directly in three bits rather than with a Huffman code.
  /// </remarks>
  internal static readonly int[][] ModeAlphabets = [
    [],
    [3, 4, 2, 0, 1, 5, 6, 7],
    [3, 4, 0, 2, 1, 5, 6, 7],
    [3, 2, 4, 0, 1, 5, 6, 7],
    [3, 2, 0, 4, 1, 5, 6, 7],
    [0, 3, 4, 2, 1, 5, 6, 7],
    [0, 5, 3, 4, 2, 1, 6, 7],
  ];

  /// <summary>The first run length each long-run Huffman code covers, Table 7.7.</summary>
  internal static readonly int[] LongRunStarts = [1, 2, 4, 6, 10, 18, 34];

  /// <summary>How many bits follow each long-run Huffman code, Table 7.7.</summary>
  internal static readonly int[] LongRunExtraBits = [0, 1, 1, 2, 3, 4, 12];

  /// <summary>The first run length each short-run Huffman code covers, Table 7.11.</summary>
  internal static readonly int[] ShortRunStarts = [1, 3, 5, 7, 11, 15];

  /// <summary>How many bits follow each short-run Huffman code, Table 7.11.</summary>
  internal static readonly int[] ShortRunExtraBits = [1, 1, 1, 2, 2, 4];

  /// <summary>
  /// The longest run the long-run coding can state, past which VP3 and Theora part company.
  /// </summary>
  /// <remarks>
  /// Theora reads a fresh bit value after a run this long so that longer runs of one value can be
  /// stated; VP3 does not, and simply toggles as it does after any other run. That caps a VP3 run at
  /// this length, which for the frame sizes VP3 was used at is never reached — a 1920&#215;1080 frame
  /// in 4:2:0 has fewer superblocks than this — so the difference costs nothing here.
  /// </remarks>
  internal const int LONG_RUN_LIMIT = 4129;

  /// <summary>The Huffman codes for long run lengths, Table 7.7.</summary>
  internal static readonly Vp3VlcTable LongRunLengths = new(
    "Table 7.7 (long run lengths)",
    ("0", 0), ("10", 1), ("110", 2), ("1110", 3), ("11110", 4), ("111110", 5), ("111111", 6));

  /// <summary>The Huffman codes for short run lengths, Table 7.11.</summary>
  internal static readonly Vp3VlcTable ShortRunLengths = new(
    "Table 7.11 (short run lengths)",
    ("0", 0), ("10", 1), ("110", 2), ("1110", 3), ("11110", 4), ("11111", 5));

  /// <summary>The Huffman codes for macro block coding modes, Table 7.19.</summary>
  /// <remarks>
  /// The value is the index of the code, not a mode: which mode a code names depends on the scheme
  /// the frame chose, which is what <see cref="ModeAlphabets"/> holds.
  /// </remarks>
  internal static readonly Vp3VlcTable ModeIndices = new(
    "Table 7.19 (macro block coding modes)",
    ("0", 0), ("10", 1), ("110", 2), ("1110", 3), ("11110", 4), ("111110", 5), ("1111110", 6),
    ("1111111", 7));

  /// <summary>
  /// The Huffman codes for motion vector components, Table 7.23.
  /// </summary>
  /// <remarks>
  /// The table is built rather than written out because it is built rather than chosen: past the four
  /// shortest codes it is a prefix, then the magnitude less its group's first value, then a sign bit,
  /// for all fifty-six of the remaining codes. Writing sixty-three lines out by hand would only add
  /// somewhere for a typing slip to hide, and the symmetry — a value and its negation differing in
  /// their last bit and nothing else — is the property the printed table is checked by anyway.
  /// </remarks>
  internal static readonly Vp3VlcTable MotionVectorComponents = _BuildMotionVectorTable();

  private static Vp3VlcTable _BuildMotionVectorTable() {
    var entries = new (string, int)[63];
    var at = 0;
    entries[at++] = ("000", 0);
    entries[at++] = ("001", 1);
    entries[at++] = ("010", -1);
    entries[at++] = ("0110", 2);
    entries[at++] = ("0111", -2);
    entries[at++] = ("1000", 3);
    entries[at++] = ("1001", -3);

    // Magnitudes 4…7 under b101, 8…15 under b110 and 16…31 under b111, each as the offset from the
    // group's first magnitude in as many bits as the group needs, then the sign.
    _AddMotionVectorGroup(entries, ref at, "101", 4, 2);
    _AddMotionVectorGroup(entries, ref at, "110", 8, 3);
    _AddMotionVectorGroup(entries, ref at, "111", 16, 4);

    return new("Table 7.23 (motion vector components)", entries);
  }

  private static void _AddMotionVectorGroup(
    (string, int)[] entries, ref int at, string prefix, int first, int bits) {
    for (var magnitude = first; magnitude < first << 1; ++magnitude) {
      var code = new StringBuilder(prefix);
      for (var bit = bits - 1; bit >= 0; --bit)
        code.Append((char)('0' + ((magnitude - first) >> bit & 1)));

      entries[at++] = (code + "0", magnitude);
      entries[at++] = (code + "1", -magnitude);
    }
  }

  /// <summary>
  /// The weights and divisor for each set of usable DC predictors, Table 7.47.
  /// </summary>
  /// <remarks>
  /// Indexed by the four availability flags read as a number — left, then below-left, then below,
  /// then below-right, the lowest bit being the left one. The first four entries of each row are the
  /// weights and the fifth is what the weighted sum is divided by. Index zero never happens: with no
  /// neighbour at all the predictor is the last DC value seen for the same reference frame, and the
  /// weighted sum is not computed at all.
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
}
