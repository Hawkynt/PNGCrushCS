using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// Reads the per-macroblock prediction records — segment, skip flag, modes and motion vectors — out
/// of the first partition (RFC 6386, 10, 11, 16 and 17).
/// </summary>
/// <remarks>
/// The whole frame's records are read in one pass, before any residue is. They live in the first
/// partition and the residue lives in the others, so the two are independent streams and reading
/// each straight through is simpler than interleaving them by macroblock row — and it lets the
/// reconstruction stage look at any macroblock's modes, which the loop filter needs anyway.
/// </remarks>
internal static class Vp8ModeReader {

  internal static void ReadFrame(
    ref Vp8BoolDecoder reader,
    Vp8MacroblockGrid grid,
    Vp8Segmentation segmentation,
    Vp8Entropy entropy,
    bool isKeyFrame,
    bool skipEnabled,
    int skipProbability,
    int intraProbability,
    int lastProbability,
    int goldenProbability,
    ReadOnlySpan<int> signBias) {
    var mapIndex = 0;

    for (var row = 0; row < grid.Rows; ++row) {
      // The bounds a motion vector predictor is held inside are one macroblock outside the picture,
      // in the eighth-pixel units the vectors themselves use (RFC 6386, 16.3).
      var toTop = -((row + 1) << 7);
      var toBottom = (grid.Rows - row) << 7;

      for (var column = 0; column < grid.Columns; ++column, ++mapIndex) {
        var index = grid.IndexOf(row, column);

        if (segmentation.UpdateMap)
          segmentation[mapIndex] = _ReadSegment(ref reader, segmentation.TreeProbabilities);

        grid.Segment[index] = (byte)segmentation[mapIndex];
        grid.Skipped[index] = skipEnabled && reader.ReadBool(skipProbability) != 0;

        if (isKeyFrame) {
          _ReadKeyFrameModes(ref reader, grid, index);
          continue;
        }

        if (reader.ReadBool(intraProbability) == 0) {
          _ReadInterFrameIntraModes(ref reader, grid, index, entropy);
          continue;
        }

        _ReadInterModes(
          ref reader, grid, index, entropy, lastProbability, goldenProbability, signBias,
          -((column + 1) << 7), (grid.Columns - column) << 7, toTop, toBottom);
      }
    }
  }

  /// <summary>Which segment this macroblock is in, read with the three probabilities of the frame header.</summary>
  private static byte _ReadSegment(ref Vp8BoolDecoder reader, byte[] probabilities)
    => (byte)reader.ReadTree(Vp8Trees.Segment, probabilities, 0);

  // ============================================================================================
  // Intra-coded macroblocks
  // ============================================================================================

  /// <summary>
  /// The modes of a macroblock in a key frame, whose subblock modes are read with a context
  /// (RFC 6386, 11).
  /// </summary>
  private static void _ReadKeyFrameModes(ref Vp8BoolDecoder reader, Vp8MacroblockGrid grid, int index) {
    var lumaMode = reader.ReadTree(
      Vp8Trees.KeyFrameLumaMode, Vp8Trees.KeyFrameLumaModeProbabilities, 0);

    if (lumaMode == Vp8Mode.SUBBLOCK_PREDICTION) {
      var modes = grid.SubblockModes(index);
      for (var subblock = 0; subblock < 16; ++subblock) {
        var above = grid.SubblockModeAbove(index, subblock);
        var left = grid.SubblockModeLeft(index, subblock);
        modes[subblock] = (byte)reader.ReadTree(
          Vp8Trees.SubblockMode,
          Vp8Tables.KeyFrameSubblockModeProbabilities,
          (above * Vp8Mode.SUBBLOCK_MODE_COUNT + left) * (Vp8Mode.SUBBLOCK_MODE_COUNT - 1));
      }
    }

    grid.LumaMode[index] = (byte)lumaMode;
    grid.ChromaMode[index] = (byte)reader.ReadTree(
      Vp8Trees.ChromaMode, Vp8Trees.KeyFrameChromaModeProbabilities, 0);
    grid.MotionVector[index] = Vp8MotionVector.Zero;
    grid.ReferenceFrame[index] = Vp8Reference.CURRENT;
  }

  /// <summary>
  /// The modes of an intra-coded macroblock in an interframe: the same fields under different trees
  /// and with no context on the subblock modes (RFC 6386, 16.1).
  /// </summary>
  private static void _ReadInterFrameIntraModes(
    ref Vp8BoolDecoder reader, Vp8MacroblockGrid grid, int index, Vp8Entropy entropy) {
    var lumaMode = reader.ReadTree(Vp8Trees.LumaMode, entropy.LumaModeProbabilities, 0);

    if (lumaMode == Vp8Mode.SUBBLOCK_PREDICTION) {
      var modes = grid.SubblockModes(index);
      for (var subblock = 0; subblock < 16; ++subblock)
        modes[subblock] = (byte)reader.ReadTree(
          Vp8Trees.SubblockMode, Vp8Trees.SubblockModeProbabilities, 0);
    }

    grid.LumaMode[index] = (byte)lumaMode;
    grid.ChromaMode[index] = (byte)reader.ReadTree(Vp8Trees.ChromaMode, entropy.ChromaModeProbabilities, 0);
    grid.MotionVector[index] = Vp8MotionVector.Zero;
    grid.ReferenceFrame[index] = Vp8Reference.CURRENT;
  }

  // ============================================================================================
  // Inter-coded macroblocks
  // ============================================================================================

  private static void _ReadInterModes(
    ref Vp8BoolDecoder reader,
    Vp8MacroblockGrid grid,
    int index,
    Vp8Entropy entropy,
    int lastProbability,
    int goldenProbability,
    ReadOnlySpan<int> signBias,
    int toLeft,
    int toRight,
    int toTop,
    int toBottom) {
    var reference = reader.ReadBool(lastProbability) != 0
      ? Vp8Reference.GOLDEN + reader.ReadBool(goldenProbability)
      : Vp8Reference.LAST;
    grid.ReferenceFrame[index] = (byte)reference;

    Span<Vp8MotionVector> candidates = stackalloc Vp8MotionVector[4];
    Span<int> census = stackalloc int[4];
    _SurveyNearbyMotionVectors(grid, index, reference, signBias, candidates, census);

    Span<byte> probabilities = stackalloc byte[4];
    var contexts = Vp8Trees.MotionVectorReferenceProbabilities;
    for (var node = 0; node < 4; ++node)
      probabilities[node] = contexts[census[node] * 4 + node];

    var mode = reader.ReadTree(Vp8Trees.MotionVectorReference, probabilities, 0);
    grid.LumaMode[index] = (byte)mode;
    grid.ChromaMode[index] = (byte)mode;

    switch (mode) {
      case Vp8Mode.NEAREST_MV:
        grid.MotionVector[index] = candidates[1].Clamped(toLeft, toRight, toTop, toBottom);
        return;

      case Vp8Mode.NEAR_MV:
        grid.MotionVector[index] = candidates[2].Clamped(toLeft, toRight, toTop, toBottom);
        return;

      case Vp8Mode.ZERO_MV:
        grid.MotionVector[index] = Vp8MotionVector.Zero;
        return;

      case Vp8Mode.NEW_MV:
        // The best predictor is clamped, and the sum of it and the offset read here is not. RFC 6386
        // section 18.1 says otherwise; the reference decoder and every other implementation leave
        // the sum alone and clamp the read positions instead, which is what this decoder does — see
        // Vp8InterPrediction.
        grid.MotionVector[index] = _ReadMotionVector(ref reader, entropy.MotionVectorProbabilities)
          .Plus(candidates[0].Clamped(toLeft, toRight, toTop, toBottom));
        return;

      case Vp8Mode.SPLIT_MV:
        _ReadSplitMotionVectors(
          ref reader, grid, index, entropy, candidates[0].Clamped(toLeft, toRight, toTop, toBottom));
        grid.MotionVector[index] = grid.SubblockMotionVectors(index)[15];
        return;

      default:
        return;
    }
  }

  /// <summary>
  /// Surveys the motion vectors of the macroblocks above, to the left, and above and to the left,
  /// producing the three predictors and the census that picks the mode probabilities (RFC 6386, 16.3).
  /// </summary>
  /// <remarks>
  /// The census is a weighted vote: the two immediate neighbours count double the diagonal one, and
  /// vectors that agree pool their weight. What comes out is a sorted-by-popularity list — the most
  /// popular non-zero vector is "nearest", the next is "near", and the winner overall is the base
  /// that an explicitly coded vector is an offset from.
  /// <para/>
  /// Two details of the reference implementation survive here because the bitstream is written
  /// against them and not against the description. A candidate is compared only against the most
  /// recently added one rather than against all of them, so three neighbours holding the vectors A,
  /// B, A produce three entries and not two. And the count of split-coded neighbours is written into
  /// the same slot that, until the line before, held the number of entries added — which is how the
  /// merge of the third entry into "nearest" knows there were three distinct vectors to begin with.
  /// </remarks>
  private static void _SurveyNearbyMotionVectors(
    Vp8MacroblockGrid grid,
    int index,
    int reference,
    ReadOnlySpan<int> signBias,
    Span<Vp8MotionVector> candidates,
    Span<int> census) {
    candidates.Clear();
    census.Clear();

    var above = grid.Above(index);
    var left = grid.Left(index);
    var aboveLeft = grid.AboveLeft(index);

    var found = 0;

    if (grid.ReferenceFrame[above] != Vp8Reference.CURRENT) {
      if (!grid.MotionVector[above].IsZero) {
        candidates[++found] = _Biased(grid.MotionVector[above], grid.ReferenceFrame[above], reference, signBias);
      }

      census[found] += 2;
    }

    if (grid.ReferenceFrame[left] != Vp8Reference.CURRENT) {
      if (!grid.MotionVector[left].IsZero) {
        var candidate = _Biased(grid.MotionVector[left], grid.ReferenceFrame[left], reference, signBias);
        if (candidate != candidates[found])
          candidates[++found] = candidate;

        census[found] += 2;
      } else
        census[0] += 2;
    }

    if (grid.ReferenceFrame[aboveLeft] != Vp8Reference.CURRENT) {
      if (!grid.MotionVector[aboveLeft].IsZero) {
        var candidate = _Biased(grid.MotionVector[aboveLeft], grid.ReferenceFrame[aboveLeft], reference, signBias);
        if (candidate != candidates[found])
          candidates[++found] = candidate;

        census[found] += 1;
      } else
        census[0] += 1;
    }

    // A third distinct vector that turns out to equal the first lends its weight to it. The test is
    // on the fourth census slot, which is non-zero only when a third entry was added.
    if (census[3] != 0 && candidates[found] == candidates[1])
      census[1] += 1;

    census[3] = ((grid.LumaMode[above] == Vp8Mode.SPLIT_MV ? 1 : 0)
                 + (grid.LumaMode[left] == Vp8Mode.SPLIT_MV ? 1 : 0)) * 2
                + (grid.LumaMode[aboveLeft] == Vp8Mode.SPLIT_MV ? 1 : 0);

    if (census[2] > census[1]) {
      (census[1], census[2]) = (census[2], census[1]);
      (candidates[1], candidates[2]) = (candidates[2], candidates[1]);
    }

    if (census[1] >= census[0])
      candidates[0] = candidates[1];
  }

  /// <summary>
  /// Turns a neighbour's vector round when it and this macroblock disagree about which way time runs
  /// (RFC 6386, 9.7 and 16.3).
  /// </summary>
  /// <remarks>
  /// The alternate reference frame is often a frame from the future, so a vector that points at it
  /// points the opposite way from one that points at the previous frame. The sign bias flags say
  /// which references are which; where a neighbour's reference and this one's disagree, the
  /// neighbour's vector is negated before it is any use as a prediction.
  /// </remarks>
  private static Vp8MotionVector _Biased(
    Vp8MotionVector motionVector, int neighbourReference, int reference, ReadOnlySpan<int> signBias)
    => signBias[neighbourReference] != signBias[reference] ? motionVector.Negated() : motionVector;

  /// <summary>
  /// Reads the partitioning of a SPLITMV macroblock and one motion vector per subset (RFC 6386, 16.4).
  /// </summary>
  private static void _ReadSplitMotionVectors(
    ref Vp8BoolDecoder reader,
    Vp8MacroblockGrid grid,
    int index,
    Vp8Entropy entropy,
    Vp8MotionVector best) {
    var partitioning = reader.ReadTree(
      Vp8Trees.SplitPartition, Vp8Trees.SplitPartitionProbabilities, 0);
    var membership = Vp8Split.Membership.Slice(partitioning * 16, 16);
    var subsets = Vp8Split.SubsetCount[partitioning];
    var motionVectors = grid.SubblockMotionVectors(index);

    for (var subset = 0; subset < subsets; ++subset) {
      var first = 0;
      while (membership[first] != subset)
        ++first;

      var left = grid.SubblockMotionVectorLeft(index, first);
      var above = grid.SubblockMotionVectorAbove(index, first);

      var mode = reader.ReadTree(
        Vp8Trees.SubblockMotionVectorReference,
        Vp8Trees.SubblockMotionVectorProbabilities,
        _SubblockContext(left, above) * 3);

      var motionVector = mode switch {
        Vp8Mode.LEFT_4X4 => left,
        Vp8Mode.ABOVE_4X4 => above,
        Vp8Mode.ZERO_4X4 => Vp8MotionVector.Zero,
        _ => _ReadMotionVector(ref reader, entropy.MotionVectorProbabilities).Plus(best),
      };

      for (var subblock = first; subblock < 16; ++subblock)
        if (membership[subblock] == subset)
          motionVectors[subblock] = motionVector;
    }
  }

  /// <summary>Which of the five subblock mode contexts the two neighbouring vectors put us in (RFC 6386, 16.4).</summary>
  private static int _SubblockContext(Vp8MotionVector left, Vp8MotionVector above) {
    var same = left == above;
    if (same)
      return left.IsZero ? 4 : 3;

    return above.IsZero ? 2 : left.IsZero ? 1 : 0;
  }

  // ============================================================================================
  // Motion vector components
  // ============================================================================================

  /// <summary>Reads a vector, row component first (RFC 6386, 17.1).</summary>
  private static Vp8MotionVector _ReadMotionVector(ref Vp8BoolDecoder reader, byte[] probabilities) {
    var row = _ReadComponent(ref reader, probabilities, 0);
    var column = _ReadComponent(ref reader, probabilities, Vp8Trees.MV_PROBABILITY_COUNT);
    return new(row, column);
  }

  /// <summary>
  /// Reads one component of a motion vector and doubles it into eighth-pixel units (RFC 6386, 17.1).
  /// </summary>
  /// <remarks>
  /// Small magnitudes are tree-coded; large ones are written a bit at a time, low three bits first
  /// and then bits nine down to four, with bit three left out whenever the bits already read make it
  /// certain. That certainty is the point: a value coded the long way is at least eight, so a value
  /// whose upper bits are all zero must have bit three set and saying so would be a wasted bool.
  /// </remarks>
  private static int _ReadComponent(ref Vp8BoolDecoder reader, byte[] probabilities, int offset) {
    int magnitude;

    if (reader.ReadBool(probabilities[offset + Vp8Trees.MV_IS_SHORT]) != 0) {
      magnitude = 0;
      for (var bit = 0; bit < 3; ++bit)
        magnitude += reader.ReadBool(probabilities[offset + Vp8Trees.MV_LONG_BITS + bit]) << bit;

      for (var bit = 9; bit > 3; --bit)
        magnitude += reader.ReadBool(probabilities[offset + Vp8Trees.MV_LONG_BITS + bit]) << bit;

      if ((magnitude & 0xFFF0) == 0 || reader.ReadBool(probabilities[offset + Vp8Trees.MV_LONG_BITS + 3]) != 0)
        magnitude += 8;
    } else
      magnitude = reader.ReadTree(
        Vp8Trees.SmallMotionVector, probabilities, offset + Vp8Trees.MV_SHORT_TREE);

    if (magnitude != 0 && reader.ReadBool(probabilities[offset + Vp8Trees.MV_SIGN]) != 0)
      magnitude = -magnitude;

    return magnitude * 2;
  }
}
