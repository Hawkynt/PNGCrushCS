namespace FileFormat.Codecs.Vp8;

/// <summary>
/// Every prediction mode a macroblock can carry, intra and inter, in one numbering.
/// </summary>
/// <remarks>
/// One numbering and not two, because VP8 needs it to be. The mode of the macroblock above decides
/// the subblock mode context of this one, and that neighbour may have been intra-coded in a frame
/// where this one is inter-coded; the census that picks the motion vector probabilities asks whether
/// a neighbour was <see cref="SPLIT_MV"/>, which is a question with an answer for an intra macroblock
/// too. RFC 6386 sets the inter modes to continue where the intra ones stop (16.2) precisely so that
/// one field can hold either, and the arithmetic in the mode trees depends on the values themselves.
/// </remarks>
internal static class Vp8Mode {

  // Whole-macroblock intra modes (RFC 6386, 8.2). The first four also serve as chroma modes.
  internal const int DC_PREDICTION = 0;
  internal const int VERTICAL_PREDICTION = 1;
  internal const int HORIZONTAL_PREDICTION = 2;
  internal const int TRUE_MOTION_PREDICTION = 3;

  /// <summary>Luma is predicted a 4x4 subblock at a time, each with its own mode.</summary>
  internal const int SUBBLOCK_PREDICTION = 4;

  internal const int INTRA_MODE_COUNT = 5;

  // The ten subblock modes of a B_PRED macroblock (RFC 6386, 11.2).
  internal const int B_DC_PREDICTION = 0;
  internal const int B_TRUE_MOTION_PREDICTION = 1;
  internal const int B_VERTICAL_PREDICTION = 2;
  internal const int B_HORIZONTAL_PREDICTION = 3;
  internal const int B_LEFT_DOWN_PREDICTION = 4;
  internal const int B_RIGHT_DOWN_PREDICTION = 5;
  internal const int B_VERTICAL_RIGHT_PREDICTION = 6;
  internal const int B_VERTICAL_LEFT_PREDICTION = 7;
  internal const int B_HORIZONTAL_DOWN_PREDICTION = 8;
  internal const int B_HORIZONTAL_UP_PREDICTION = 9;

  internal const int SUBBLOCK_MODE_COUNT = 10;

  // Whole-macroblock inter modes, continuing the intra numbering (RFC 6386, 16.2).
  internal const int NEAREST_MV = 5;
  internal const int NEAR_MV = 6;
  internal const int ZERO_MV = 7;
  internal const int NEW_MV = 8;
  internal const int SPLIT_MV = 9;

  // Subblock inter modes, continuing the subblock intra numbering (RFC 6386, 16.4).
  internal const int LEFT_4X4 = 10;
  internal const int ABOVE_4X4 = 11;
  internal const int ZERO_4X4 = 12;
  internal const int NEW_4X4 = 13;

  /// <summary>The subblock mode a whole-macroblock intra mode stands in for, when a neighbour asks (RFC 6386, 11.3).</summary>
  /// <remarks>
  /// A macroblock predicted as one 16x16 block still has to answer "what mode was your bottom row of
  /// subblocks" for the macroblock below it. The four full-block modes have obvious subblock
  /// counterparts and are read as those; only the numbering differs, which is the whole reason this
  /// mapping is not the identity.
  /// </remarks>
  internal static int AsSubblockMode(int wholeMacroblockMode)
    => wholeMacroblockMode switch {
      DC_PREDICTION => B_DC_PREDICTION,
      VERTICAL_PREDICTION => B_VERTICAL_PREDICTION,
      HORIZONTAL_PREDICTION => B_HORIZONTAL_PREDICTION,
      TRUE_MOTION_PREDICTION => B_TRUE_MOTION_PREDICTION,
      _ => B_DC_PREDICTION,
    };
}

/// <summary>Which picture a macroblock predicts from (RFC 6386, 16.2).</summary>
/// <remarks>
/// The numbers are the bitstream's own: the reference frame selector reads as 1, 2 or 3, and 0 is
/// left for "this frame", which is what an intra-coded macroblock predicts from. That lets the loop
/// filter's reference deltas and the motion vector sign biases be plain four-element arrays indexed
/// by this value, which is how RFC 6386 sections 9.4 and 9.7 describe them.
/// </remarks>
internal static class Vp8Reference {
  internal const int CURRENT = 0;
  internal const int LAST = 1;
  internal const int GOLDEN = 2;
  internal const int ALTERNATE = 3;
  internal const int COUNT = 4;
}

/// <summary>Which of the four token probability planes a residue block is read with (RFC 6386, 13.3).</summary>
internal static class Vp8CoefficientPlane {

  /// <summary>A luma block of a macroblock that has a Y2 block, so its own coefficient 0 is not coded.</summary>
  internal const int LUMA_AFTER_Y2 = 0;

  /// <summary>The Y2 block itself.</summary>
  internal const int Y2 = 1;

  /// <summary>A chroma block.</summary>
  internal const int CHROMA = 2;

  /// <summary>A luma block of a macroblock with no Y2 block, so coefficient 0 is coded here.</summary>
  internal const int LUMA_WITH_DC = 3;
}
