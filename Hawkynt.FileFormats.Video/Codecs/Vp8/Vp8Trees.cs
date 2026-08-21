using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// The coding trees of VP8 and the small constant probability arrays that go with them.
/// </summary>
/// <remarks>
/// A tree is laid out as RFC 6386 section 8.1 describes: pairs of signed bytes, where a positive
/// entry is the index of the pair one level deeper and a non-positive entry is the negated value of a
/// leaf. Reading a value is then a walk of one bool per level, each read with the probability
/// belonging to the interior node the walk is standing on — see
/// <see cref="Vp8BoolDecoder.ReadTree"/>.
/// <para/>
/// A leaf of zero is a leaf and not a branch: negating zero gives zero back, so the walk has to stop
/// on any entry that is not strictly positive rather than on any entry that is negative. That is why
/// <c>DC_PRED</c>, whose value is zero, can sit at the root of two of these trees at all.
/// </remarks>
internal static class Vp8Trees {

  internal const int TOKEN_COUNT = 12;

  // ============================================================================================
  // Prediction modes
  // ============================================================================================

  /// <summary>Luma mode of an intra macroblock in an interframe (RFC 6386, 16.1).</summary>
  internal static ReadOnlySpan<sbyte> LumaMode => [
    -Vp8Mode.DC_PREDICTION, 2,
      4, 6,
        -Vp8Mode.VERTICAL_PREDICTION, -Vp8Mode.HORIZONTAL_PREDICTION,
        -Vp8Mode.TRUE_MOTION_PREDICTION, -Vp8Mode.SUBBLOCK_PREDICTION,
  ];

  /// <summary>Luma mode of a macroblock in a key frame, which is the same alphabet under a different tree (RFC 6386, 11.2).</summary>
  internal static ReadOnlySpan<sbyte> KeyFrameLumaMode => [
    -Vp8Mode.SUBBLOCK_PREDICTION, 2,
      4, 6,
        -Vp8Mode.DC_PREDICTION, -Vp8Mode.VERTICAL_PREDICTION,
        -Vp8Mode.HORIZONTAL_PREDICTION, -Vp8Mode.TRUE_MOTION_PREDICTION,
  ];

  /// <summary>Chroma mode, which is the first four luma modes and nothing else (RFC 6386, 11.4).</summary>
  internal static ReadOnlySpan<sbyte> ChromaMode => [
    -Vp8Mode.DC_PREDICTION, 2,
      -Vp8Mode.VERTICAL_PREDICTION, 4,
        -Vp8Mode.HORIZONTAL_PREDICTION, -Vp8Mode.TRUE_MOTION_PREDICTION,
  ];

  /// <summary>The ten modes of a 4x4 luma subblock (RFC 6386, 11.2).</summary>
  internal static ReadOnlySpan<sbyte> SubblockMode => [
    -Vp8Mode.B_DC_PREDICTION, 2,
      -Vp8Mode.B_TRUE_MOTION_PREDICTION, 4,
        -Vp8Mode.B_VERTICAL_PREDICTION, 6,
          8, 12,
            -Vp8Mode.B_HORIZONTAL_PREDICTION, 10,
              -Vp8Mode.B_RIGHT_DOWN_PREDICTION, -Vp8Mode.B_VERTICAL_RIGHT_PREDICTION,
            -Vp8Mode.B_LEFT_DOWN_PREDICTION, 14,
              -Vp8Mode.B_VERTICAL_LEFT_PREDICTION, 16,
                -Vp8Mode.B_HORIZONTAL_DOWN_PREDICTION, -Vp8Mode.B_HORIZONTAL_UP_PREDICTION,
  ];

  /// <summary>Which of the four segments a macroblock belongs to (RFC 6386, 10).</summary>
  internal static ReadOnlySpan<sbyte> Segment => [2, 4, 0, -1, -2, -3];

  internal static ReadOnlySpan<byte> KeyFrameLumaModeProbabilities => [145, 156, 163, 128];
  internal static ReadOnlySpan<byte> KeyFrameChromaModeProbabilities => [142, 114, 183];

  /// <summary>The interframe luma mode probabilities a key frame resets to (RFC 6386, 16.1).</summary>
  internal static ReadOnlySpan<byte> DefaultLumaModeProbabilities => [112, 86, 140, 37];

  /// <summary>The interframe chroma mode probabilities a key frame resets to (RFC 6386, 16.1).</summary>
  internal static ReadOnlySpan<byte> DefaultChromaModeProbabilities => [162, 101, 204];

  /// <summary>
  /// Subblock mode probabilities in an interframe, which are constant and contextless (RFC 6386, 16.1).
  /// </summary>
  /// <remarks>
  /// The one place where a key frame is not simply an interframe with fixed probabilities: a key
  /// frame picks its probabilities from <see cref="Vp8Tables.KeyFrameSubblockModeProbabilities"/> by
  /// the modes above and to the left, and an interframe uses this one array for every subblock.
  /// </remarks>
  internal static ReadOnlySpan<byte> SubblockModeProbabilities => [120, 90, 79, 133, 87, 85, 80, 111, 151];

  // ============================================================================================
  // Inter modes and motion vectors
  // ============================================================================================

  /// <summary>How a macroblock's motion vector is arrived at (RFC 6386, 16.2).</summary>
  internal static ReadOnlySpan<sbyte> MotionVectorReference => [
    -Vp8Mode.ZERO_MV, 2,
      -Vp8Mode.NEAREST_MV, 4,
        -Vp8Mode.NEAR_MV, 6,
          -Vp8Mode.NEW_MV, -Vp8Mode.SPLIT_MV,
  ];

  /// <summary>How the sixteen subblocks of a SPLITMV macroblock are grouped (RFC 6386, 16.4).</summary>
  internal static ReadOnlySpan<sbyte> SplitPartition => [
    -Vp8Split.SIXTEENTHS, 2,
      -Vp8Split.QUARTERS, 4,
        -Vp8Split.TOP_BOTTOM, -Vp8Split.LEFT_RIGHT,
  ];

  internal static ReadOnlySpan<byte> SplitPartitionProbabilities => [110, 111, 150];

  /// <summary>How one subset of a SPLITMV macroblock gets its motion vector (RFC 6386, 16.4).</summary>
  internal static ReadOnlySpan<sbyte> SubblockMotionVectorReference => [
    -Vp8Mode.LEFT_4X4, 2,
      -Vp8Mode.ABOVE_4X4, 4,
        -Vp8Mode.ZERO_4X4, -Vp8Mode.NEW_4X4,
  ];

  /// <summary>
  /// Subblock motion vector mode probabilities, by how the left and above neighbours relate (RFC 6386, 16.4).
  /// </summary>
  /// <remarks>
  /// Five contexts of three probabilities each, in the order: neither neighbour special, left is
  /// zero, above is zero, the two are equal, the two are equal and zero.
  /// </remarks>
  internal static ReadOnlySpan<byte> SubblockMotionVectorProbabilities => [
    147, 136, 18,
    106, 145, 1,
    179, 121, 1,
    223, 1, 34,
    208, 1, 1,
  ];

  /// <summary>
  /// The probabilities for the mode tree, chosen by the census of nearby motion vectors (RFC 6386, 16.3).
  /// </summary>
  /// <remarks>
  /// Six rows of four. The four columns are the four interior nodes of
  /// <see cref="MotionVectorReference"/>, and each is indexed by its own census count — the weight
  /// found for zero, nearest, near, and the extent to which the neighbours used SPLITMV — which is
  /// why this is a table of six by four rather than a probability per context.
  /// </remarks>
  internal static ReadOnlySpan<byte> MotionVectorReferenceProbabilities => [
    7, 1, 1, 143,
    14, 18, 14, 107,
    135, 64, 57, 68,
    60, 56, 128, 65,
    159, 134, 128, 34,
    234, 188, 128, 28,
  ];

  /// <summary>The eight small magnitudes a motion vector component can take without spelling out its bits (RFC 6386, 17.1).</summary>
  internal static ReadOnlySpan<sbyte> SmallMotionVector => [
    2, 8,
      4, 6,
        0, -1,
        -2, -3,
      10, 12,
        -4, -5,
        -6, -7,
  ];

  /// <summary>Where each of the nineteen probabilities of one motion vector component sits (RFC 6386, 17.1).</summary>
  internal const int MV_IS_SHORT = 0;
  internal const int MV_SIGN = 1;
  internal const int MV_SHORT_TREE = 2;
  internal const int MV_LONG_BITS = MV_SHORT_TREE + 7;
  internal const int MV_PROBABILITY_COUNT = MV_LONG_BITS + 10;

  /// <summary>The motion vector probabilities a key frame resets to, row component then column (RFC 6386, 17.2).</summary>
  internal static ReadOnlySpan<byte> DefaultMotionVectorProbabilities => [
    162, 128, 225, 146, 172, 147, 214, 39, 156,
    128, 129, 132, 75, 145, 178, 206, 239, 254, 254,
    164, 128, 204, 170, 119, 235, 140, 230, 228,
    128, 130, 130, 74, 148, 180, 203, 236, 254, 254,
  ];

  /// <summary>The chance that a frame header restates one motion vector probability (RFC 6386, 17.2).</summary>
  internal static ReadOnlySpan<byte> MotionVectorUpdateProbabilities => [
    237, 246, 253, 253, 254, 254, 254, 254, 254,
    254, 254, 254, 254, 254, 250, 250, 252, 254, 254,
    231, 243, 245, 253, 254, 254, 254, 254, 254,
    254, 254, 254, 254, 254, 251, 251, 254, 254, 254,
  ];

  // ============================================================================================
  // Residue tokens
  // ============================================================================================

  /// <summary>The twelve tokens one coefficient position is coded as (RFC 6386, 13.2).</summary>
  /// <remarks>
  /// End-of-block is the root's zero branch, which is why a token that follows a literal zero is read
  /// by starting the walk at node 2 instead: end-of-block cannot follow a zero, so the branch that
  /// would decide it is not written at all.
  /// </remarks>
  internal static ReadOnlySpan<sbyte> Token => [
    -Vp8Token.END_OF_BLOCK, 2,
      -Vp8Token.ZERO, 4,
        -Vp8Token.ONE, 6,
          8, 12,
            -Vp8Token.TWO, 10,
              -Vp8Token.THREE, -Vp8Token.FOUR,
            14, 16,
              -Vp8Token.CATEGORY_1, -Vp8Token.CATEGORY_2,
            18, 20,
              -Vp8Token.CATEGORY_3, -Vp8Token.CATEGORY_4,
              -Vp8Token.CATEGORY_5, -Vp8Token.CATEGORY_6,
  ];

  /// <summary>Where in <see cref="Token"/> to start when the position before this one held a literal zero.</summary>
  internal const int TOKEN_TREE_WITHOUT_END_OF_BLOCK = 2;

  /// <summary>Which probability band each of the sixteen scan positions is read with (RFC 6386, 13.3).</summary>
  internal static ReadOnlySpan<byte> CoefficientBands => [0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7];

  /// <summary>Scan position to position within the 4x4 block, the zig-zag of RFC 6386 section 13.</summary>
  internal static ReadOnlySpan<byte> ZigZag => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];

  /// <summary>The smallest value each of the six range tokens stands for (RFC 6386, 13.2).</summary>
  internal static ReadOnlySpan<short> CategoryBase => [5, 7, 11, 19, 35, 67];

  /// <summary>How many extra bits each of the six range tokens is followed by (RFC 6386, 13.2).</summary>
  internal static ReadOnlySpan<byte> CategoryBits => [1, 2, 3, 4, 5, 11];

  /// <summary>Where each category's extra-bit probabilities begin in <see cref="CategoryProbabilities"/>.</summary>
  internal static ReadOnlySpan<byte> CategoryProbabilityOffset => [0, 1, 3, 6, 10, 15];

  /// <summary>
  /// The probabilities for the extra bits of the six range tokens, highest-order bit first (RFC 6386, 13.2).
  /// </summary>
  internal static ReadOnlySpan<byte> CategoryProbabilities => [
    159,
    165, 145,
    173, 148, 140,
    176, 155, 140, 135,
    180, 157, 141, 134, 130,
    254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129,
  ];

  /// <summary>Which of the nine left-hand coefficient contexts each of the 25 blocks reads and writes.</summary>
  /// <remarks>
  /// Four luma rows, two each for U and V, and one for Y2 — the neighbour context is per plane, so a
  /// chroma block asks about chroma and never about the luma block that covers the same pixels.
  /// </remarks>
  internal static ReadOnlySpan<byte> LeftContextIndex => [
    0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3,
    4, 4, 5, 5, 6, 6, 7, 7, 8,
  ];

  /// <summary>Which of the nine above coefficient contexts each of the 25 blocks reads and writes.</summary>
  internal static ReadOnlySpan<byte> AboveContextIndex => [
    0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3,
    4, 5, 4, 5, 6, 7, 6, 7, 8,
  ];
}

/// <summary>The twelve residue tokens (RFC 6386, 13.2).</summary>
internal static class Vp8Token {
  internal const int ZERO = 0;
  internal const int ONE = 1;
  internal const int TWO = 2;
  internal const int THREE = 3;
  internal const int FOUR = 4;
  internal const int CATEGORY_1 = 5;
  internal const int CATEGORY_2 = 6;
  internal const int CATEGORY_3 = 7;
  internal const int CATEGORY_4 = 8;
  internal const int CATEGORY_5 = 9;
  internal const int CATEGORY_6 = 10;
  internal const int END_OF_BLOCK = 11;
}

/// <summary>The four ways a SPLITMV macroblock divides its sixteen subblocks (RFC 6386, 16.4).</summary>
internal static class Vp8Split {
  internal const int TOP_BOTTOM = 0;
  internal const int LEFT_RIGHT = 1;
  internal const int QUARTERS = 2;
  internal const int SIXTEENTHS = 3;

  /// <summary>Which subset each of the sixteen subblocks belongs to, one row per partitioning.</summary>
  internal static ReadOnlySpan<byte> Membership => [
    0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1,
    0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
    0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 3, 3, 2, 2, 3, 3,
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
  ];

  /// <summary>How many subsets each partitioning has.</summary>
  internal static ReadOnlySpan<byte> SubsetCount => [2, 2, 4, 16];
}
