using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Moves the probability tables towards the frequencies the frame turned out to have
/// (specification 8.4).
/// </summary>
/// <remarks>
/// This runs after the frame is decoded and changes nothing about it. What it changes is the table
/// the <em>next</em> frame starts from, on the reasoning that consecutive frames of a film look alike,
/// so what this frame cost is a good guess at what the next one will.
/// <para/>
/// The move is deliberately partial. Each probability ends up somewhere between where it was and what
/// the counts say, weighted by how many observations there were: a context seen twice barely moves,
/// one seen twenty times or more moves by the full update factor. That is what stops a table being
/// thrown across the room by a handful of blocks.
/// <para/>
/// It is also the part of a VP9 decoder that fails invisibly. Every arithmetic here is applied by
/// both ends without a word of it in the bitstream, so a mistake produces a frame that decodes
/// perfectly and a successor that is noise.
/// </remarks>
internal static class Vp9Adaptation {

  /// <summary>
  /// Merges one probability with the counts of the boolean it belongs to (specification 8.4.1).
  /// </summary>
  private static byte _MergeProbability(byte previous, int zeroCount, int oneCount, int countSaturation, int maxUpdateFactor) {
    // Sixty-four bits for the ratio because a coefficient context of a large frame is seen millions
    // of times, and multiplying that count by 256 is what the estimate is made of.
    long total = zeroCount + oneCount;
    var probability = total == 0
      ? 128
      : (int)Math.Clamp((zeroCount * 256L + (total >> 1)) / total, 1, 255);

    var count = (int)Math.Min(total, countSaturation);
    var factor = maxUpdateFactor * count / countSaturation;

    return (byte)((previous * (256 - factor) + probability * factor + 128) >> 8);
  }

  /// <summary>
  /// Walks a coding tree, merging every interior node's probability with the counts of the leaves
  /// below it, and answers how many observations that subtree saw (specification 8.4.2).
  /// </summary>
  private static int _MergeProbabilities(
    ReadOnlySpan<sbyte> tree, int node, Span<byte> probabilities, ReadOnlySpan<int> counts,
    int countSaturation, int maxUpdateFactor) {
    var left = tree[node];
    var leftCount = left <= 0
      ? counts[-left]
      : _MergeProbabilities(tree, left, probabilities, counts, countSaturation, maxUpdateFactor);

    var right = tree[node + 1];
    var rightCount = right <= 0
      ? counts[-right]
      : _MergeProbabilities(tree, right, probabilities, counts, countSaturation, maxUpdateFactor);

    probabilities[node >> 1] =
      _MergeProbability(probabilities[node >> 1], leftCount, rightCount, countSaturation, maxUpdateFactor);

    return leftCount + rightCount;
  }

  private static void _AdaptTree(ReadOnlySpan<sbyte> tree, Span<byte> probabilities, ReadOnlySpan<int> counts)
    => _MergeProbabilities(tree, 0, probabilities, counts, COUNT_SAT, MAX_UPDATE_FACTOR);

  private static byte _AdaptBool(byte probability, ReadOnlySpan<int> counts)
    => _MergeProbability(probability, counts[0], counts[1], COUNT_SAT, MAX_UPDATE_FACTOR);

  // ============================================================================================
  // Coefficients (specification 8.4.3)
  // ============================================================================================

  /// <summary>
  /// Adapts the coefficient probabilities.
  /// </summary>
  /// <param name="updateFactor">
  /// 128 when the previous frame was a key frame and 112 otherwise. The frame after a key frame is
  /// allowed to move further because the tables it inherited were the format's defaults rather than
  /// anything measured.
  /// </param>
  internal static void AdaptCoefficients(Vp9Probabilities probabilities, Vp9Counts counts, int updateFactor) {
    // A tighter saturation than the rest of the tables use: coefficient contexts are seen thousands
    // of times in a frame, so twenty-four observations already say as much as a hundred would.
    const int COEFFICIENT_COUNT_SATURATION = 24;

    var probabilitySpan = probabilities.Coefficient.AsSpan();
    var tokenCounts = counts.Token.AsSpan();
    var moreCounts = counts.MoreCoefficients.AsSpan();

    for (var size = 0; size < TX_SIZES; ++size)
    for (var plane = 0; plane < BLOCK_TYPES; ++plane)
    for (var reference = 0; reference < REF_TYPES; ++reference)
    for (var band = 0; band < COEF_BANDS; ++band) {
      var contexts = band == 0 ? 3 : PREV_COEF_CONTEXTS;
      for (var context = 0; context < contexts; ++context) {
        var at = CoefficientContext(size, plane, reference, band, context);
        var probability = probabilitySpan.Slice(at * UNCONSTRAINED_NODES, UNCONSTRAINED_NODES);

        _MergeProbabilities(
          Vp9Trees.SmallToken, 2, probability, tokenCounts.Slice(at * UNCONSTRAINED_NODES, UNCONSTRAINED_NODES),
          COEFFICIENT_COUNT_SATURATION, updateFactor);
        _MergeProbabilities(
          Vp9Trees.Binary, 0, probability, moreCounts.Slice(at * 2, 2),
          COEFFICIENT_COUNT_SATURATION, updateFactor);
      }
    }
  }

  // ============================================================================================
  // Everything else (specification 8.4.4)
  // ============================================================================================

  internal static void AdaptNonCoefficients(
    Vp9Probabilities probabilities, Vp9Counts counts, bool filterIsSwitchable, bool transformSizeIsSelected,
    bool allowHighPrecisionMotionVectors) {
    for (var i = 0; i < IS_INTER_CONTEXTS; ++i)
      probabilities.IsInter[i] = _AdaptBool(probabilities.IsInter[i], counts.IsInter.AsSpan(i * 2, 2));

    for (var i = 0; i < COMP_MODE_CONTEXTS; ++i)
      probabilities.CompoundMode[i] = _AdaptBool(probabilities.CompoundMode[i], counts.CompoundMode.AsSpan(i * 2, 2));

    for (var i = 0; i < REF_CONTEXTS; ++i)
      probabilities.CompoundReference[i] =
        _AdaptBool(probabilities.CompoundReference[i], counts.CompoundReference.AsSpan(i * 2, 2));

    for (var i = 0; i < REF_CONTEXTS; ++i)
    for (var j = 0; j < 2; ++j)
      probabilities.SingleReference[i * 2 + j] =
        _AdaptBool(probabilities.SingleReference[i * 2 + j], counts.SingleReference.AsSpan((i * 2 + j) * 2, 2));

    for (var i = 0; i < INTER_MODE_CONTEXTS; ++i)
      _AdaptTree(
        Vp9Trees.InterMode, probabilities.InterMode.AsSpan(i * (INTER_MODES - 1), INTER_MODES - 1),
        counts.InterMode.AsSpan(i * INTER_MODES, INTER_MODES));

    for (var i = 0; i < BLOCK_SIZE_GROUPS; ++i)
      _AdaptTree(
        Vp9Trees.IntraMode, probabilities.YMode.AsSpan(i * (INTRA_MODES - 1), INTRA_MODES - 1),
        counts.IntraMode.AsSpan(i * INTRA_MODES, INTRA_MODES));

    for (var i = 0; i < INTRA_MODES; ++i)
      _AdaptTree(
        Vp9Trees.IntraMode, probabilities.UvMode.AsSpan(i * (INTRA_MODES - 1), INTRA_MODES - 1),
        counts.UvMode.AsSpan(i * INTRA_MODES, INTRA_MODES));

    for (var i = 0; i < PARTITION_CONTEXTS; ++i)
      _AdaptTree(
        Vp9Trees.Partition, probabilities.Partition.AsSpan(i * (PARTITION_TYPES - 1), PARTITION_TYPES - 1),
        counts.Partition.AsSpan(i * PARTITION_TYPES, PARTITION_TYPES));

    for (var i = 0; i < SKIP_CONTEXTS; ++i)
      probabilities.Skip[i] = _AdaptBool(probabilities.Skip[i], counts.Skip.AsSpan(i * 2, 2));

    if (filterIsSwitchable)
      for (var i = 0; i < INTERP_FILTER_CONTEXTS; ++i)
        _AdaptTree(
          Vp9Trees.InterpolationFilter,
          probabilities.InterpolationFilter.AsSpan(i * (SWITCHABLE_FILTERS - 1), SWITCHABLE_FILTERS - 1),
          counts.InterpolationFilter.AsSpan(i * SWITCHABLE_FILTERS, SWITCHABLE_FILTERS));

    if (transformSizeIsSelected)
      for (var i = 0; i < TX_SIZE_CONTEXTS; ++i) {
        _AdaptTree(
          Vp9Trees.TransformSize8, _TransformSizeProbabilities(probabilities, TX_8X8, i),
          counts.TransformSize.AsSpan((TX_8X8 * TX_SIZE_CONTEXTS + i) * TX_SIZES, 2));
        _AdaptTree(
          Vp9Trees.TransformSize16, _TransformSizeProbabilities(probabilities, TX_16X16, i),
          counts.TransformSize.AsSpan((TX_16X16 * TX_SIZE_CONTEXTS + i) * TX_SIZES, 3));
        _AdaptTree(
          Vp9Trees.TransformSize32, _TransformSizeProbabilities(probabilities, TX_32X32, i),
          counts.TransformSize.AsSpan((TX_32X32 * TX_SIZE_CONTEXTS + i) * TX_SIZES, 4));
      }

    _AdaptTree(Vp9Trees.MotionVectorJoint, probabilities.MotionVectorJoint, counts.MotionVectorJoint);

    for (var i = 0; i < 2; ++i) {
      probabilities.MotionVectorSign[i] =
        _AdaptBool(probabilities.MotionVectorSign[i], counts.MotionVectorSign.AsSpan(i * 2, 2));

      _AdaptTree(
        Vp9Trees.MotionVectorClass,
        probabilities.MotionVectorClass.AsSpan(i * (MV_CLASSES - 1), MV_CLASSES - 1),
        counts.MotionVectorClass.AsSpan(i * MV_CLASSES, MV_CLASSES));

      probabilities.MotionVectorClass0Bit[i] =
        _AdaptBool(probabilities.MotionVectorClass0Bit[i], counts.MotionVectorClass0Bit.AsSpan(i * CLASS0_SIZE, 2));

      for (var j = 0; j < MV_OFFSET_BITS; ++j)
        probabilities.MotionVectorBits[i * MV_OFFSET_BITS + j] = _AdaptBool(
          probabilities.MotionVectorBits[i * MV_OFFSET_BITS + j],
          counts.MotionVectorBits.AsSpan((i * MV_OFFSET_BITS + j) * 2, 2));

      for (var j = 0; j < CLASS0_SIZE; ++j)
        _AdaptTree(
          Vp9Trees.MotionVectorFraction,
          probabilities.MotionVectorClass0Fraction.AsSpan((i * CLASS0_SIZE + j) * (MV_FR_SIZE - 1), MV_FR_SIZE - 1),
          counts.MotionVectorClass0Fraction.AsSpan((i * CLASS0_SIZE + j) * MV_FR_SIZE, MV_FR_SIZE));

      _AdaptTree(
        Vp9Trees.MotionVectorFraction,
        probabilities.MotionVectorFraction.AsSpan(i * (MV_FR_SIZE - 1), MV_FR_SIZE - 1),
        counts.MotionVectorFraction.AsSpan(i * MV_FR_SIZE, MV_FR_SIZE));

      if (!allowHighPrecisionMotionVectors)
        continue;

      probabilities.MotionVectorClass0HighPrecision[i] = _AdaptBool(
        probabilities.MotionVectorClass0HighPrecision[i], counts.MotionVectorClass0HighPrecision.AsSpan(i * 2, 2));
      probabilities.MotionVectorHighPrecision[i] =
        _AdaptBool(probabilities.MotionVectorHighPrecision[i], counts.MotionVectorHighPrecision.AsSpan(i * 2, 2));
    }
  }

  private static Span<byte> _TransformSizeProbabilities(Vp9Probabilities probabilities, int maximum, int context)
    => probabilities.TransformSize.AsSpan((maximum * TX_SIZE_CONTEXTS + context) * (TX_SIZES - 1), TX_SIZES - 1);
}
