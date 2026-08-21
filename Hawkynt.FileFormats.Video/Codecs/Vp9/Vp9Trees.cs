using System;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// The coding trees of VP9 (specification 9.3.1).
/// </summary>
/// <remarks>
/// A tree is pairs of signed bytes: a positive entry is the index of the pair one level deeper, and a
/// non-positive entry is the negated value of a leaf. Walking one costs a bool per level, read with
/// the probability belonging to the interior node the walk is standing on.
/// <para/>
/// A leaf of zero is a leaf: negating zero gives zero, so the walk stops on any entry that is not
/// strictly positive rather than on any entry that is negative. That is what lets <c>DC_PRED</c>,
/// <c>PARTITION_NONE</c> and <c>ZERO_TOKEN</c> — all of them zero — sit at the root of a tree.
/// </remarks>
internal static class Vp9Trees {

  /// <summary>How a superblock is split, when both a lower and a right half exist.</summary>
  internal static ReadOnlySpan<sbyte> Partition => [
    -Vp9Constants.PARTITION_NONE, 2,
      -Vp9Constants.PARTITION_HORZ, 4,
        -Vp9Constants.PARTITION_VERT, -Vp9Constants.PARTITION_SPLIT,
  ];

  /// <summary>The partition of a block that runs off the bottom of the picture: only its columns are real.</summary>
  internal static ReadOnlySpan<sbyte> ColumnsPartition => [
    -Vp9Constants.PARTITION_HORZ, -Vp9Constants.PARTITION_SPLIT,
  ];

  /// <summary>The partition of a block that runs off the right of the picture.</summary>
  internal static ReadOnlySpan<sbyte> RowsPartition => [
    -Vp9Constants.PARTITION_VERT, -Vp9Constants.PARTITION_SPLIT,
  ];

  /// <summary>The ten intra prediction modes, for luminance and for chrominance alike.</summary>
  internal static ReadOnlySpan<sbyte> IntraMode => [
    -Vp9Constants.DC_PRED, 2,
      -Vp9Constants.TM_PRED, 4,
        -Vp9Constants.V_PRED, 6,
          8, 12,
            -Vp9Constants.H_PRED, 10,
              -Vp9Constants.D135_PRED, -Vp9Constants.D117_PRED,
            -Vp9Constants.D45_PRED, 14,
              -Vp9Constants.D63_PRED, 16,
                -Vp9Constants.D153_PRED, -Vp9Constants.D207_PRED,
  ];

  /// <summary>Which of the eight segments a block belongs to.</summary>
  /// <remarks>
  /// A balanced tree of eight leaves, so every segment costs three bools whichever it is. The six
  /// interior nodes come first and the eight leaves after them.
  /// </remarks>
  internal static ReadOnlySpan<sbyte> Segment => [
    2, 4, 6, 8, 10, 12,
    0, -1, -2, -3, -4, -5, -6, -7,
  ];

  /// <summary>
  /// A single bool, dressed as a tree so that the elaborate context derivations of specification
  /// 9.3.2 can be described the same way for every syntax element that has one.
  /// </summary>
  internal static ReadOnlySpan<sbyte> Binary => [0, -1];

  internal static ReadOnlySpan<sbyte> TransformSize32 => [
    -Vp9Constants.TX_4X4, 2,
      -Vp9Constants.TX_8X8, 4,
        -Vp9Constants.TX_16X16, -Vp9Constants.TX_32X32,
  ];

  internal static ReadOnlySpan<sbyte> TransformSize16 => [
    -Vp9Constants.TX_4X4, 2,
      -Vp9Constants.TX_8X8, -Vp9Constants.TX_16X16,
  ];

  internal static ReadOnlySpan<sbyte> TransformSize8 => [
    -Vp9Constants.TX_4X4, -Vp9Constants.TX_8X8,
  ];

  /// <summary>
  /// Which motion vector an inter block uses, as an offset from <c>NEARESTMV</c>.
  /// </summary>
  /// <remarks>
  /// Zero sits at the root because a still block is the commonest thing in a film, and the values are
  /// offsets rather than the modes themselves because the probabilities are indexed by tree position.
  /// </remarks>
  internal static ReadOnlySpan<sbyte> InterMode => [
    -(Vp9Constants.ZEROMV - Vp9Constants.NEARESTMV), 2,
      -(Vp9Constants.NEARESTMV - Vp9Constants.NEARESTMV), 4,
        -(Vp9Constants.NEARMV - Vp9Constants.NEARESTMV), -(Vp9Constants.NEWMV - Vp9Constants.NEARESTMV),
  ];

  internal static ReadOnlySpan<sbyte> InterpolationFilter => [
    -Vp9Constants.EIGHTTAP, 2,
      -Vp9Constants.EIGHTTAP_SMOOTH, -Vp9Constants.EIGHTTAP_SHARP,
  ];

  internal static ReadOnlySpan<sbyte> MotionVectorJoint => [
    -Vp9Constants.MV_JOINT_ZERO, 2,
      -Vp9Constants.MV_JOINT_HNZVZ, 4,
        -Vp9Constants.MV_JOINT_HZVNZ, -Vp9Constants.MV_JOINT_HNZVNZ,
  ];

  /// <summary>The magnitude class of one component of a motion vector difference.</summary>
  internal static ReadOnlySpan<sbyte> MotionVectorClass => [
    0, 2,
    -1, 4,
    6, 8,
    -2, -3,
    10, 12,
    -4, -5,
    -6, 14,
    16, 18,
    -7, -8,
    -9, -10,
  ];

  /// <summary>The two fractional bits of a motion vector difference.</summary>
  internal static ReadOnlySpan<sbyte> MotionVectorFraction => [
    0, 2,
      -1, 4,
        -2, -3,
  ];

  /// <summary>The size range of one transform coefficient.</summary>
  internal static ReadOnlySpan<sbyte> Token => [
    -Vp9Constants.ZERO_TOKEN, 2,
      -Vp9Constants.ONE_TOKEN, 4,
        6, 10,
          -Vp9Constants.TWO_TOKEN, 8,
            -Vp9Constants.THREE_TOKEN, -Vp9Constants.FOUR_TOKEN,
          12, 14,
            -Vp9Constants.DCT_VAL_CATEGORY1, -Vp9Constants.DCT_VAL_CATEGORY2,
            16, 18,
              -Vp9Constants.DCT_VAL_CATEGORY3, -Vp9Constants.DCT_VAL_CATEGORY4,
              -Vp9Constants.DCT_VAL_CATEGORY5, -Vp9Constants.DCT_VAL_CATEGORY6,
  ];

  /// <summary>
  /// The part of <see cref="Token"/> that carries probabilities of its own, used when adapting them
  /// (specification 8.4.3).
  /// </summary>
  /// <remarks>
  /// Only three of the token tree's probabilities are transmitted; the rest are computed from the
  /// third by the Pareto table. This is the transmitted part, rooted at node two so that the first
  /// pair — which belongs to the end-of-block bool and not to the token — is skipped.
  /// </remarks>
  internal static ReadOnlySpan<sbyte> SmallToken => [
    0, 0,
    -Vp9Constants.ZERO_TOKEN, 4,
    -Vp9Constants.ONE_TOKEN, -Vp9Constants.TWO_TOKEN,
  ];
}
