namespace FileFormat.Codecs.Vp9;

/// <summary>
/// The named constants of the VP9 Bitstream &amp; Decoding Process Specification, version 0.6.
/// </summary>
/// <remarks>
/// The symbols of section 3 together with the enumerations the semantics of section 7 give names to.
/// They are constants rather than enumerations because nearly every one of them is used as an array
/// index, and an <c>enum</c> that has to be cast at every use is a name that costs a cast.
/// <para/>
/// The numbering is the standard's and not a convenience. Block sizes ascend so that
/// <c>Max(BLOCK_16X16, size)</c> in the loop filter means what it says; transform sizes are the base
/// two logarithm of the transform width in units of four samples; and reference frames start at
/// <c>INTRA_FRAME</c> = 0 so that "is this block intra" is a comparison against zero.
/// </remarks>
internal static class Vp9Constants {

  // ============================================================================================
  // Section 3 — symbols
  // ============================================================================================

  internal const int REFS_PER_FRAME = 3;
  internal const int MV_FR_SIZE = 4;
  internal const int MVREF_NEIGHBOURS = 8;
  internal const int BLOCK_SIZE_GROUPS = 4;
  internal const int BLOCK_SIZES = 13;
  internal const int BLOCK_INVALID = 14;
  internal const int PARTITION_CONTEXTS = 16;
  internal const int MI_SIZE = 8;
  internal const int MIN_TILE_WIDTH_B64 = 4;
  internal const int MAX_TILE_WIDTH_B64 = 64;
  internal const int MAX_MV_REF_CANDIDATES = 2;
  internal const int NUM_REF_FRAMES = 8;
  internal const int MAX_REF_FRAMES = 4;
  internal const int IS_INTER_CONTEXTS = 4;
  internal const int COMP_MODE_CONTEXTS = 5;
  internal const int REF_CONTEXTS = 5;
  internal const int MAX_SEGMENTS = 8;
  internal const int SEG_LVL_ALT_Q = 0;
  internal const int SEG_LVL_ALT_L = 1;
  internal const int SEG_LVL_REF_FRAME = 2;
  internal const int SEG_LVL_SKIP = 3;
  internal const int SEG_LVL_MAX = 4;
  internal const int BLOCK_TYPES = 2;
  internal const int REF_TYPES = 2;
  internal const int COEF_BANDS = 6;
  internal const int PREV_COEF_CONTEXTS = 6;
  internal const int UNCONSTRAINED_NODES = 3;
  internal const int TX_SIZE_CONTEXTS = 2;
  internal const int SWITCHABLE_FILTERS = 3;
  internal const int INTERP_FILTER_CONTEXTS = 4;
  internal const int SKIP_CONTEXTS = 3;
  internal const int PARTITION_TYPES = 4;
  internal const int TX_SIZES = 4;
  internal const int TX_MODES = 5;
  internal const int MB_MODE_COUNT = 14;
  internal const int INTRA_MODES = 10;
  internal const int INTER_MODES = 4;
  internal const int INTER_MODE_CONTEXTS = 7;
  internal const int MV_JOINTS = 4;
  internal const int MV_CLASSES = 11;
  internal const int CLASS0_SIZE = 2;
  internal const int MV_OFFSET_BITS = 10;
  internal const int MAX_PROB = 255;
  internal const int MAX_MODE_LF_DELTAS = 2;
  internal const int COMPANDED_MVREF_THRESH = 8;
  internal const int MAX_LOOP_FILTER = 63;
  internal const int REF_SCALE_SHIFT = 14;
  internal const int SUBPEL_BITS = 4;
  internal const int SUBPEL_SHIFTS = 16;
  internal const int SUBPEL_MASK = 15;
  internal const int MV_BORDER = 128;
  internal const int INTERP_EXTEND = 4;
  internal const int BORDERINPIXELS = 160;
  internal const int MAX_UPDATE_FACTOR = 128;
  internal const int COUNT_SAT = 20;

  /// <summary>The seven cases <c>counter_to_context</c> maps a neighbour tally onto (section 3).</summary>
  internal const int BOTH_ZERO = 0;
  internal const int ZERO_PLUS_PREDICTED = 1;
  internal const int BOTH_PREDICTED = 2;
  internal const int NEW_PLUS_NON_INTRA = 3;
  internal const int BOTH_NEW = 4;
  internal const int INTRA_PLUS_NON_INTRA = 5;
  internal const int BOTH_INTRA = 6;
  internal const int INVALID_CASE = 9;

  // ============================================================================================
  // Block sizes (section 7.4.3)
  // ============================================================================================

  internal const int BLOCK_4X4 = 0;
  internal const int BLOCK_4X8 = 1;
  internal const int BLOCK_8X4 = 2;
  internal const int BLOCK_8X8 = 3;
  internal const int BLOCK_8X16 = 4;
  internal const int BLOCK_16X8 = 5;
  internal const int BLOCK_16X16 = 6;
  internal const int BLOCK_16X32 = 7;
  internal const int BLOCK_32X16 = 8;
  internal const int BLOCK_32X32 = 9;
  internal const int BLOCK_32X64 = 10;
  internal const int BLOCK_64X32 = 11;
  internal const int BLOCK_64X64 = 12;

  // ============================================================================================
  // Partitions (section 7.4.3)
  // ============================================================================================

  internal const int PARTITION_NONE = 0;
  internal const int PARTITION_HORZ = 1;
  internal const int PARTITION_VERT = 2;
  internal const int PARTITION_SPLIT = 3;

  // ============================================================================================
  // Transform sizes and modes (sections 7.4.8 and 7.3.1)
  // ============================================================================================

  internal const int TX_4X4 = 0;
  internal const int TX_8X8 = 1;
  internal const int TX_16X16 = 2;
  internal const int TX_32X32 = 3;

  internal const int ONLY_4X4 = 0;
  internal const int ALLOW_8X8 = 1;
  internal const int ALLOW_16X16 = 2;
  internal const int ALLOW_32X32 = 3;
  internal const int TX_MODE_SELECT = 4;

  /// <summary>Which one-dimensional transform each axis takes (section 3).</summary>
  internal const int DCT_DCT = 0;
  internal const int ADST_DCT = 1;
  internal const int DCT_ADST = 2;
  internal const int ADST_ADST = 3;

  // ============================================================================================
  // Prediction modes (sections 7.4.5 and 7.4.11)
  // ============================================================================================

  internal const int DC_PRED = 0;
  internal const int V_PRED = 1;
  internal const int H_PRED = 2;
  internal const int D45_PRED = 3;
  internal const int D135_PRED = 4;
  internal const int D117_PRED = 5;
  internal const int D153_PRED = 6;
  internal const int D207_PRED = 7;
  internal const int D63_PRED = 8;
  internal const int TM_PRED = 9;

  internal const int NEARESTMV = 10;
  internal const int NEARMV = 11;
  internal const int ZEROMV = 12;
  internal const int NEWMV = 13;

  // ============================================================================================
  // Reference frames and reference modes (sections 7.4.12 and 7.3.6)
  // ============================================================================================

  /// <summary>Not a frame at all: what <c>ref_frame[1]</c> holds when a block is not compound.</summary>
  internal const int NONE = -1;

  internal const int INTRA_FRAME = 0;
  internal const int LAST_FRAME = 1;
  internal const int GOLDEN_FRAME = 2;
  internal const int ALTREF_FRAME = 3;

  internal const int SINGLE_REFERENCE = 0;
  internal const int COMPOUND_REFERENCE = 1;
  internal const int REFERENCE_MODE_SELECT = 2;

  // ============================================================================================
  // Interpolation filters (section 7.2.7)
  // ============================================================================================

  internal const int EIGHTTAP = 0;
  internal const int EIGHTTAP_SMOOTH = 1;
  internal const int EIGHTTAP_SHARP = 2;
  internal const int BILINEAR = 3;
  internal const int SWITCHABLE = 4;

  // ============================================================================================
  // Motion vectors (sections 7.4.13 and 7.4.14)
  // ============================================================================================

  internal const int MV_JOINT_ZERO = 0;
  internal const int MV_JOINT_HNZVZ = 1;
  internal const int MV_JOINT_HZVNZ = 2;
  internal const int MV_JOINT_HNZVNZ = 3;

  internal const int MV_CLASS_0 = 0;

  // ============================================================================================
  // Coefficient tokens (section 7.4.16)
  // ============================================================================================

  internal const int ZERO_TOKEN = 0;
  internal const int ONE_TOKEN = 1;
  internal const int TWO_TOKEN = 2;
  internal const int THREE_TOKEN = 3;
  internal const int FOUR_TOKEN = 4;
  internal const int DCT_VAL_CATEGORY1 = 5;
  internal const int DCT_VAL_CATEGORY2 = 6;
  internal const int DCT_VAL_CATEGORY3 = 7;
  internal const int DCT_VAL_CATEGORY4 = 8;
  internal const int DCT_VAL_CATEGORY5 = 9;
  internal const int DCT_VAL_CATEGORY6 = 10;

  // ============================================================================================
  // Colour spaces (section 7.2.2)
  // ============================================================================================

  internal const int CS_UNKNOWN = 0;
  internal const int CS_BT_601 = 1;
  internal const int CS_RGB = 7;

  // ============================================================================================
  // Frame types (section 7.2)
  // ============================================================================================

  internal const int KEY_FRAME = 0;
  internal const int NON_KEY_FRAME = 1;

  /// <summary>The number of probability sets a stream can carry from one frame to another (section 7.2).</summary>
  internal const int FRAME_CONTEXTS = 4;

  /// <summary>
  /// The specification's <c>Clip3</c> (section 4.6), which is not <see cref="System.Math.Clamp"/>.
  /// </summary>
  /// <remarks>
  /// <c>Math.Clamp</c> throws when the bounds cross. The specification's clip does not: it tests the
  /// lower bound first, so a crossed pair answers the lower bound. That case arises for real — a
  /// 64x64 block whose lower half is off screen has a negative distance to the bottom edge — and it is
  /// a legal frame, not a malformed one.
  /// </remarks>
  internal static int Clip3(int low, int high, int value) => value < low ? low : value > high ? high : value;

  /// <summary>
  /// Where one coefficient context begins, in units of nodes, in the flattened coefficient
  /// probability and count tables.
  /// </summary>
  /// <remarks>
  /// The five dimensions of <c>coef_probs</c> down to but not including the node: transform size,
  /// whether the plane is chrominance, whether the block is inter coded, the band the scan position
  /// falls in, and the context the neighbouring coefficients give. Multiply by three for the
  /// probabilities and the token counts, and by two for the end-of-block counts.
  /// </remarks>
  internal static int CoefficientContext(int size, int plane, int reference, int band, int context)
    => ((((size * BLOCK_TYPES + plane) * REF_TYPES + reference) * COEF_BANDS + band) * PREV_COEF_CONTEXTS) + context;
}
