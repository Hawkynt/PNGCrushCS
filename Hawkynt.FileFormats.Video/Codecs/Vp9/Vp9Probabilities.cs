using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// One complete set of the probabilities a frame is read with.
/// </summary>
/// <remarks>
/// A stream keeps five of these: the working set the current frame is being decoded with, and four
/// saved sets a frame header can name. The saved sets are what makes VP9's probability coding cheap —
/// a frame says which of the four to start from, sends only the differences it wants on top, and may
/// then have the result written back over the one it started from once the frame has been decoded and
/// the true frequencies are known.
/// <para/>
/// The split between <see cref="LoadFrom"/> and <see cref="LoadTransformSizeAndSkipFrom"/> is not
/// tidiness. Specification 6.1.2 restores the transform size and skip tables at a different point in
/// the refresh than it restores everything else, and the two points differ in whether the frame's own
/// forward updates survive. Merging them would silently discard the transform mode a frame header
/// asked for.
/// <para/>
/// Every table is flat, indexed by the strides written on it, so that the coefficient table — read
/// once per coefficient — costs one bounds check.
/// </remarks>
internal sealed class Vp9Probabilities {

  /// <summary>Transform size, indexed <c>[(maxTransformSize * 2 + ctx) * 3 + node]</c>.</summary>
  /// <remarks>
  /// Kept as a full square even though the row for a maximum of 4x4 is never read and the rows for
  /// 8x8 and 16x16 use fewer than three nodes. The holes cost twelve bytes and save the reader a
  /// second lookup to find where a row starts.
  /// </remarks>
  internal readonly byte[] TransformSize = new byte[TX_SIZES * TX_SIZE_CONTEXTS * (TX_SIZES - 1)];

  /// <summary>
  /// The three transmitted probabilities of each coefficient context, indexed
  /// <c>[(((((size * 2 + plane) * 2 + reference) * 6 + band) * 6 + ctx) * 3) + node]</c>.
  /// </summary>
  internal readonly byte[] Coefficient =
    new byte[TX_SIZES * BLOCK_TYPES * REF_TYPES * COEF_BANDS * PREV_COEF_CONTEXTS * UNCONSTRAINED_NODES];

  internal readonly byte[] Skip = new byte[SKIP_CONTEXTS];
  internal readonly byte[] InterMode = new byte[INTER_MODE_CONTEXTS * (INTER_MODES - 1)];
  internal readonly byte[] InterpolationFilter = new byte[INTERP_FILTER_CONTEXTS * (SWITCHABLE_FILTERS - 1)];
  internal readonly byte[] IsInter = new byte[IS_INTER_CONTEXTS];
  internal readonly byte[] CompoundMode = new byte[COMP_MODE_CONTEXTS];
  internal readonly byte[] SingleReference = new byte[REF_CONTEXTS * 2];
  internal readonly byte[] CompoundReference = new byte[REF_CONTEXTS];
  internal readonly byte[] YMode = new byte[BLOCK_SIZE_GROUPS * (INTRA_MODES - 1)];
  internal readonly byte[] UvMode = new byte[INTRA_MODES * (INTRA_MODES - 1)];
  internal readonly byte[] Partition = new byte[PARTITION_CONTEXTS * (PARTITION_TYPES - 1)];
  internal readonly byte[] MotionVectorJoint = new byte[MV_JOINTS - 1];
  internal readonly byte[] MotionVectorSign = new byte[2];
  internal readonly byte[] MotionVectorClass = new byte[2 * (MV_CLASSES - 1)];
  internal readonly byte[] MotionVectorClass0Bit = new byte[2];
  internal readonly byte[] MotionVectorBits = new byte[2 * MV_OFFSET_BITS];
  internal readonly byte[] MotionVectorClass0Fraction = new byte[2 * CLASS0_SIZE * (MV_FR_SIZE - 1)];
  internal readonly byte[] MotionVectorFraction = new byte[2 * (MV_FR_SIZE - 1)];
  internal readonly byte[] MotionVectorClass0HighPrecision = new byte[2];
  internal readonly byte[] MotionVectorHighPrecision = new byte[2];

  /// <summary>Puts every table back to the values of specification 10.5.</summary>
  internal void Reset() {
    Vp9DefaultProbabilities.TransformSize.CopyTo(this.TransformSize);
    Vp9DefaultProbabilities.Coefficient.CopyTo(this.Coefficient);
    Vp9DefaultProbabilities.Skip.CopyTo(this.Skip);
    Vp9DefaultProbabilities.InterMode.CopyTo(this.InterMode);
    Vp9DefaultProbabilities.InterpolationFilter.CopyTo(this.InterpolationFilter);
    Vp9DefaultProbabilities.IsInter.CopyTo(this.IsInter);
    Vp9DefaultProbabilities.CompoundMode.CopyTo(this.CompoundMode);
    Vp9DefaultProbabilities.SingleReference.CopyTo(this.SingleReference);
    Vp9DefaultProbabilities.CompoundReference.CopyTo(this.CompoundReference);
    Vp9DefaultProbabilities.YMode.CopyTo(this.YMode);
    Vp9DefaultProbabilities.UvMode.CopyTo(this.UvMode);
    Vp9DefaultProbabilities.Partition.CopyTo(this.Partition);
    Vp9DefaultProbabilities.MotionVectorJoint.CopyTo(this.MotionVectorJoint);
    Vp9DefaultProbabilities.MotionVectorSign.CopyTo(this.MotionVectorSign);
    Vp9DefaultProbabilities.MotionVectorClass.CopyTo(this.MotionVectorClass);
    Vp9DefaultProbabilities.MotionVectorClass0Bit.CopyTo(this.MotionVectorClass0Bit);
    Vp9DefaultProbabilities.MotionVectorBits.CopyTo(this.MotionVectorBits);
    Vp9DefaultProbabilities.MotionVectorClass0Fraction.CopyTo(this.MotionVectorClass0Fraction);
    Vp9DefaultProbabilities.MotionVectorFraction.CopyTo(this.MotionVectorFraction);
    Vp9DefaultProbabilities.MotionVectorClass0HighPrecision.CopyTo(this.MotionVectorClass0HighPrecision);
    Vp9DefaultProbabilities.MotionVectorHighPrecision.CopyTo(this.MotionVectorHighPrecision);
  }

  /// <summary>Copies every table into <paramref name="target"/>, which is <c>save_probs</c>.</summary>
  internal void SaveTo(Vp9Probabilities target) {
    this.LoadIntoAllBut(target);
    this.TransformSize.CopyTo(target.TransformSize, 0);
    this.Skip.CopyTo(target.Skip, 0);
  }

  /// <summary>Takes every table except transform size and skip from <paramref name="source"/>, which is <c>load_probs</c>.</summary>
  internal void LoadFrom(Vp9Probabilities source) => source.LoadIntoAllBut(this);

  /// <summary>Takes the transform size and skip tables from <paramref name="source"/>, which is <c>load_probs2</c>.</summary>
  internal void LoadTransformSizeAndSkipFrom(Vp9Probabilities source) {
    source.TransformSize.CopyTo(this.TransformSize, 0);
    source.Skip.CopyTo(this.Skip, 0);
  }

  private void LoadIntoAllBut(Vp9Probabilities target) {
    this.Coefficient.CopyTo(target.Coefficient, 0);
    this.InterMode.CopyTo(target.InterMode, 0);
    this.InterpolationFilter.CopyTo(target.InterpolationFilter, 0);
    this.IsInter.CopyTo(target.IsInter, 0);
    this.CompoundMode.CopyTo(target.CompoundMode, 0);
    this.SingleReference.CopyTo(target.SingleReference, 0);
    this.CompoundReference.CopyTo(target.CompoundReference, 0);
    this.YMode.CopyTo(target.YMode, 0);
    this.UvMode.CopyTo(target.UvMode, 0);
    this.Partition.CopyTo(target.Partition, 0);
    this.MotionVectorJoint.CopyTo(target.MotionVectorJoint, 0);
    this.MotionVectorSign.CopyTo(target.MotionVectorSign, 0);
    this.MotionVectorClass.CopyTo(target.MotionVectorClass, 0);
    this.MotionVectorClass0Bit.CopyTo(target.MotionVectorClass0Bit, 0);
    this.MotionVectorBits.CopyTo(target.MotionVectorBits, 0);
    this.MotionVectorClass0Fraction.CopyTo(target.MotionVectorClass0Fraction, 0);
    this.MotionVectorFraction.CopyTo(target.MotionVectorFraction, 0);
    this.MotionVectorClass0HighPrecision.CopyTo(target.MotionVectorClass0HighPrecision, 0);
    this.MotionVectorHighPrecision.CopyTo(target.MotionVectorHighPrecision, 0);
  }
}
