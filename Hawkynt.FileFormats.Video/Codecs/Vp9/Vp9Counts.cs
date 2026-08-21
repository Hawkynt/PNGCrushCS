using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// How many times each value of each syntax element was decoded, in each context
/// (specification 8.3 and 9.3.4).
/// </summary>
/// <remarks>
/// This is the input to the backward probability adaptation of specification 8.4, which is the second
/// of the two ways VP9 keeps its probabilities close to the truth: rather than spending bits on
/// telling the decoder what the frequencies were, the decoder counts them for itself and both ends
/// move their tables the same way.
/// <para/>
/// That makes every one of these counters part of the decode and not diagnostics. A counter that is
/// missed, or incremented in the wrong context, changes the probabilities the *next* frame is read
/// with — so the frame it was missed in decodes perfectly and the one after it becomes noise. There
/// is no partial credit here.
/// </remarks>
internal sealed class Vp9Counts {

  internal readonly int[] IntraMode = new int[BLOCK_SIZE_GROUPS * INTRA_MODES];
  internal readonly int[] UvMode = new int[INTRA_MODES * INTRA_MODES];
  internal readonly int[] Partition = new int[PARTITION_CONTEXTS * PARTITION_TYPES];
  internal readonly int[] InterpolationFilter = new int[INTERP_FILTER_CONTEXTS * SWITCHABLE_FILTERS];
  internal readonly int[] InterMode = new int[INTER_MODE_CONTEXTS * INTER_MODES];
  internal readonly int[] TransformSize = new int[TX_SIZES * TX_SIZE_CONTEXTS * TX_SIZES];
  internal readonly int[] IsInter = new int[IS_INTER_CONTEXTS * 2];
  internal readonly int[] CompoundMode = new int[COMP_MODE_CONTEXTS * 2];
  internal readonly int[] SingleReference = new int[REF_CONTEXTS * 2 * 2];
  internal readonly int[] CompoundReference = new int[REF_CONTEXTS * 2];
  internal readonly int[] Skip = new int[SKIP_CONTEXTS * 2];
  internal readonly int[] MotionVectorJoint = new int[MV_JOINTS];
  internal readonly int[] MotionVectorSign = new int[2 * 2];
  internal readonly int[] MotionVectorClass = new int[2 * MV_CLASSES];
  internal readonly int[] MotionVectorClass0Bit = new int[2 * CLASS0_SIZE];
  internal readonly int[] MotionVectorClass0Fraction = new int[2 * CLASS0_SIZE * MV_FR_SIZE];
  internal readonly int[] MotionVectorClass0HighPrecision = new int[2 * 2];
  internal readonly int[] MotionVectorBits = new int[2 * MV_OFFSET_BITS * 2];
  internal readonly int[] MotionVectorFraction = new int[2 * MV_FR_SIZE];
  internal readonly int[] MotionVectorHighPrecision = new int[2 * 2];

  /// <summary>
  /// Tokens, indexed <c>[(((((size * 2 + plane) * 2 + reference) * 6 + band) * 6 + ctx) * 3) + Min(2, token)]</c>.
  /// </summary>
  internal readonly int[] Token =
    new int[TX_SIZES * BLOCK_TYPES * REF_TYPES * COEF_BANDS * PREV_COEF_CONTEXTS * UNCONSTRAINED_NODES];

  /// <summary>
  /// The end-of-block bool, indexed the same way with two entries per context rather than three.
  /// </summary>
  internal readonly int[] MoreCoefficients =
    new int[TX_SIZES * BLOCK_TYPES * REF_TYPES * COEF_BANDS * PREV_COEF_CONTEXTS * 2];

  /// <summary>Sets every counter to zero, which is <c>clear_counts</c>.</summary>
  internal void Clear() {
    Array.Clear(this.IntraMode);
    Array.Clear(this.UvMode);
    Array.Clear(this.Partition);
    Array.Clear(this.InterpolationFilter);
    Array.Clear(this.InterMode);
    Array.Clear(this.TransformSize);
    Array.Clear(this.IsInter);
    Array.Clear(this.CompoundMode);
    Array.Clear(this.SingleReference);
    Array.Clear(this.CompoundReference);
    Array.Clear(this.Skip);
    Array.Clear(this.MotionVectorJoint);
    Array.Clear(this.MotionVectorSign);
    Array.Clear(this.MotionVectorClass);
    Array.Clear(this.MotionVectorClass0Bit);
    Array.Clear(this.MotionVectorClass0Fraction);
    Array.Clear(this.MotionVectorClass0HighPrecision);
    Array.Clear(this.MotionVectorBits);
    Array.Clear(this.MotionVectorFraction);
    Array.Clear(this.MotionVectorHighPrecision);
    Array.Clear(this.Token);
    Array.Clear(this.MoreCoefficients);
  }
}
