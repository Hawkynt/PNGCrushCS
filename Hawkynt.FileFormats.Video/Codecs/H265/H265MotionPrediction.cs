using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Where a prediction block's motion comes from when it is not stated outright — ITU-T H.265,
/// clause 8.5.3.2.
/// </summary>
/// <remarks>
/// Two ways, and HEVC leans on the first far more heavily than H.264 did. <b>Merge</b> takes a
/// neighbour's motion whole — reference index, both vectors, everything — and costs only an index
/// into a list of candidates the decoder builds the same way the encoder did. <b>Advanced motion
/// vector prediction</b> states a reference and a difference from one of two predicted vectors.
/// Between them they are why HEVC spends so much less on motion than its predecessor: most blocks of
/// most pictures move exactly as the block beside them does, and merge says so in three bins.
/// <para/>
/// <b>Both lists are built, never searched.</b> The encoder and the decoder construct the identical
/// list from the identical neighbours in the identical order, and the bitstream carries only an
/// index into it. So every rule below — which neighbour is consulted first, when one is dropped for
/// duplicating another, how many candidates a partition of a given shape is allowed — is normative in
/// the strongest sense: a decoder that ordered the list differently would read the right index and
/// take the wrong motion, and would produce a picture.
/// <para/>
/// <b>Vectors are scaled by the distance they were measured over.</b> A neighbour that points at a
/// picture two frames back says something about a block that points at a picture one frame back —
/// half as much, if the motion is steady — and the scaling is what makes a candidate from a different
/// reference worth having at all. The arithmetic is fixed-point and exact, and the same scaling
/// serves the temporal candidate, which borrows motion from another picture entirely.
/// </remarks>
internal static class H265MotionPrediction {

  /// <summary>
  /// Table 8-6: which two candidates each combined bi-predictive candidate is made from.
  /// </summary>
  /// <remarks>
  /// A B slice can build new candidates out of the halves of the ones it has: the first list's motion
  /// from one candidate and the second list's from another. The order pairs the most promising
  /// combinations first, and the loop stops as soon as the list is full.
  /// </remarks>
  private static readonly byte[] _CombinedL0 = [0, 1, 0, 2, 1, 2, 0, 3, 1, 3, 2, 3];

  private static readonly byte[] _CombinedL1 = [1, 0, 2, 0, 2, 1, 3, 0, 3, 1, 3, 2];

  /// <summary>
  /// Builds the merge candidate list and takes one entry — clause 8.5.3.2.2.
  /// </summary>
  internal static H265MotionInfo DeriveMerge(
    H265FrameDecoder frame, int xPb, int yPb, int nPbW, int nPbH, int partIdx, int mergeIndex) {
    ArgumentNullException.ThrowIfNull(frame);

    var xCb = frame.CodingBlockX;
    var yCb = frame.CodingBlockY;
    var nCbS = frame.CodingBlockSize;
    var partitionMode = frame.CodingBlockPartitionMode;

    // An 8x8 coding block whose merge candidates are shared across a parallel merge region derives
    // one candidate list for the whole block rather than one per prediction block. That is what makes
    // the region estimable in parallel: the second prediction block's list may not depend on the
    // first's motion, so it is not allowed to have one of its own.
    if (frame.Pps.Log2ParallelMergeLevel > 2 && nCbS == 8) {
      xPb = xCb;
      yPb = yCb;
      nPbW = nCbS;
      nPbH = nCbS;
      partIdx = 0;
      partitionMode = H265PartitionMode.Square;
    }

    var candidates = new List<H265MotionInfo>(6);
    _AddSpatialCandidates(frame, candidates, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, partitionMode);

    var maximum = frame.Header.MaxNumMergeCand;
    if (candidates.Count < maximum && _TemporalCandidate(frame, xPb, yPb, nPbW, nPbH, out var temporal))
      candidates.Add(temporal);

    var original = candidates.Count;

    if (frame.Header.SliceType == H265SliceType.B)
      _AddCombinedCandidates(frame, candidates, original, maximum);

    _AddZeroCandidates(frame, candidates, maximum);

    if (mergeIndex >= candidates.Count)
      throw new System.IO.InvalidDataException(
        $"An H.265 prediction block names merge candidate {mergeIndex}, but only {candidates.Count} were built. "
        + "The entropy decoder is out of step with the bitstream.");

    var chosen = candidates[mergeIndex];

    // A prediction block of eight samples' area may not use two references. A merge candidate that
    // does is cut down to the first list rather than refused, because the encoder chose the index
    // knowing this rule would apply.
    if (chosen.PredictL0 && chosen.PredictL1 && nPbW + nPbH == 12) {
      chosen.PredictL1 = false;
      chosen.RefIdxL1 = -1;
      chosen.MvL1X = 0;
      chosen.MvL1Y = 0;
    }

    return chosen;
  }

  /// <summary>
  /// The five neighbours a merge candidate may come from — clause 8.5.3.2.3.
  /// </summary>
  /// <remarks>
  /// Left, above, above-right, below-left and above-left, in that order, and each is dropped when it
  /// repeats one already taken. The two exclusions that are not about repetition are the interesting
  /// ones: the second prediction block of a horizontally split coding block may not take the block
  /// above it and the second of a vertically split one may not take the block to its left, because
  /// that neighbour is the <em>other half of the same coding block</em> — merging with it would make
  /// the split meaningless and would code the same motion twice.
  /// <para/>
  /// <b>A candidate dropped for duplicating an earlier one is still compared against.</b> The
  /// standard keeps two variables per neighbour — whether it exists at all, and whether it became a
  /// candidate — and the comparisons that remove duplicates use the first. So a neighbour whose own
  /// candidacy was cancelled for repeating an earlier one still cancels a later one that repeats it,
  /// which is what keeps a value that was already rejected from re-entering the list under another
  /// name and shifting every index after it.
  /// <para/>
  /// <b>A candidate ruled out for being the other half of the same coding block is not.</b> That
  /// exclusion is a different thing from a duplicate: the neighbour is not a candidate and its value
  /// was never in the list, so a later candidate that happens to match it has not been seen before
  /// and belongs. The two cases are told apart below by which of the two flags each comparison reads,
  /// and getting them the same way round costs a picture with the right shapes in it and the wrong
  /// motion.
  /// </remarks>
  private static void _AddSpatialCandidates(
    H265FrameDecoder frame, List<H265MotionInfo> candidates, int xCb, int yCb, int nCbS,
    int xPb, int yPb, int nPbW, int nPbH, int partIdx, H265PartitionMode partitionMode) {
    var splitVertically = partitionMode is H265PartitionMode.VerticalHalves
      or H265PartitionMode.VerticalQuarterLeft or H265PartitionMode.VerticalQuarterRight;
    var splitHorizontally = partitionMode is H265PartitionMode.HorizontalHalves
      or H265PartitionMode.HorizontalQuarterTop or H265PartitionMode.HorizontalQuarterBottom;

    bool Exists(int xNb, int yNb, out H265MotionInfo motion) {
      motion = H265MotionInfo.None;
      if (!_IsPredictionBlockAvailable(frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, xNb, yNb))
        return false;

      motion = frame.MotionAt(frame.BlockIndexAt(xNb, yNb));
      return true;
    }

    // A neighbour inside the same parallel merge region is not a candidate, so that every prediction
    // block of the region can build its list without waiting for the others to be decoded.
    bool OutsideMergeRegion(int xNb, int yNb) {
      var level = frame.Pps.Log2ParallelMergeLevel;
      return xPb >> level != xNb >> level || yPb >> level != yNb >> level;
    }

    // Whether each neighbour is there, and separately whether it may be a candidate. The second
    // prediction block of a divided coding block may not take the other half — the left neighbour of
    // a vertically divided block, the one above a horizontally divided one — because merging with it
    // would code the same motion twice and make the division pointless.
    var neighbouringLeft = Exists(xPb - 1, yPb + nPbH - 1, out var left);
    var existsLeft = neighbouringLeft && !(splitVertically && partIdx == 1);
    var existsAbove = Exists(xPb + nPbW - 1, yPb - 1, out var above) && !(splitHorizontally && partIdx == 1);
    var existsAboveRight = Exists(xPb + nPbW, yPb - 1, out var aboveRight);
    var existsBelowLeft = Exists(xPb - 1, yPb + nPbH, out var belowLeft);
    var existsAboveLeft = Exists(xPb - 1, yPb - 1, out var aboveLeft);

    var takeLeft = existsLeft && OutsideMergeRegion(xPb - 1, yPb + nPbH - 1);

    var takeAbove = existsAbove
                    && !(neighbouringLeft && above.SameAs(left))
                    && OutsideMergeRegion(xPb + nPbW - 1, yPb - 1);

    var takeAboveRight = existsAboveRight
                         && !(existsAbove && aboveRight.SameAs(above))
                         && OutsideMergeRegion(xPb + nPbW, yPb - 1);

    var takeBelowLeft = existsBelowLeft
                        && !(neighbouringLeft && belowLeft.SameAs(left))
                        && OutsideMergeRegion(xPb - 1, yPb + nPbH);

    // The corner is only consulted when one of the other four failed to become a candidate: with all
    // four present the list is already as long as the spatial part is allowed to be.
    var takeAboveLeft = existsAboveLeft
                        && !(takeLeft && takeAbove && takeAboveRight && takeBelowLeft)
                        && !(neighbouringLeft && aboveLeft.SameAs(left))
                        && !(existsAbove && aboveLeft.SameAs(above))
                        && OutsideMergeRegion(xPb - 1, yPb - 1);

    if (takeLeft)
      candidates.Add(left);

    if (takeAbove)
      candidates.Add(above);

    if (takeAboveRight)
      candidates.Add(aboveRight);

    if (takeBelowLeft)
      candidates.Add(belowLeft);

    if (takeAboveLeft)
      candidates.Add(aboveLeft);
  }

  /// <summary>
  /// Whether a neighbouring prediction block may be consulted — clause 6.4.2.
  /// </summary>
  /// <remarks>
  /// The z-scan availability of clause 6.4.1, plus two things it does not cover. An intra coded
  /// neighbour has no motion to lend. And the second prediction block of a quartered coding block may
  /// not consult the block below-left of it, which belongs to the same coding block and has not been
  /// decoded — the z-scan alone would allow it, because the two are at the same depth.
  /// </remarks>
  private static bool _IsPredictionBlockAvailable(
    H265FrameDecoder frame, int xCb, int yCb, int nCbS, int xPb, int yPb, int nPbW, int nPbH,
    int partIdx, int xNb, int yNb) {
    if (nPbW << 1 == nCbS && nPbH << 1 == nCbS && partIdx == 1
        && yCb + nPbH <= yNb && xCb + nPbW > xNb)
      return false;

    if (!frame.IsAvailableAt(xPb, yPb, xNb, yNb))
      return false;

    return !frame.IsIntraAt(frame.BlockIndexAt(xNb, yNb));
  }

  /// <summary>
  /// The candidate that borrows motion from another picture — clauses 8.5.3.2.8 and 8.5.3.2.9.
  /// </summary>
  /// <remarks>
  /// Where the spatial candidates say "this block moves like the one beside it", this one says "this
  /// block moves as whatever was here in the last picture did", which is the better guess for an
  /// object crossing a static background. The motion is read from a sixteen-sample grid rather than
  /// from the finest one, because that is the resolution a decoder is required to keep for a picture
  /// it is no longer decoding.
  /// </remarks>
  private static bool _TemporalCandidate(
    H265FrameDecoder frame, int xPb, int yPb, int nPbW, int nPbH, out H265MotionInfo motion) {
    motion = H265MotionInfo.None;

    if (!frame.Header.TemporalMvpEnabled)
      return false;

    var lists = frame.Header.SliceType == H265SliceType.B ? 2 : 1;
    var found = false;

    for (var list = 0; list < lists; ++list) {
      if (!_CollocatedVector(frame, xPb, yPb, nPbW, nPbH, list, 0, out var x, out var y))
        continue;

      motion.Set(list, true, 0, x, y);
      found = true;
    }

    return found;
  }

  /// <summary>Reads and scales one vector out of the collocated picture — clause 8.5.3.2.9.</summary>
  private static bool _CollocatedVector(
    H265FrameDecoder frame, int xPb, int yPb, int nPbW, int nPbH, int list, int refIdx,
    out int mvX, out int mvY) {
    mvX = 0;
    mvY = 0;

    var collocated = frame.CollocatedPicture;
    if (collocated == null)
      return false;

    // The block below and to the right of this one, which is where an object that has moved on will
    // be. It is only consulted when it lies inside the same coding tree block row — reaching into the
    // row below would mean keeping motion for a part of the picture a parallel decoder has not
    // reached.
    var xBottomRight = xPb + nPbW;
    var yBottomRight = yPb + nPbH;

    if (yPb >> frame.Sps.CtbLog2SizeY == yBottomRight >> frame.Sps.CtbLog2SizeY
        && yBottomRight < frame.Sps.Height
        && xBottomRight < frame.Sps.Width
        && _ReadCollocated(frame, collocated, (xBottomRight >> 4) << 4, (yBottomRight >> 4) << 4,
          list, refIdx, out mvX, out mvY))
      return true;

    return _ReadCollocated(frame, collocated,
      ((xPb + (nPbW >> 1)) >> 4) << 4, ((yPb + (nPbH >> 1)) >> 4) << 4, list, refIdx, out mvX, out mvY);
  }

  private static bool _ReadCollocated(
    H265FrameDecoder frame, H265Picture collocated, int x, int y, int list, int refIdx,
    out int mvX, out int mvY) {
    mvX = 0;
    mvY = 0;

    if (x >= frame.Sps.Width || y >= frame.Sps.Height)
      return false;

    var index = (y >> collocated.MinBlockLog2Size) * collocated.BlocksAcross + (x >> collocated.MinBlockLog2Size);
    if (collocated.IsIntraBlock[index])
      return false;

    var motion = collocated.Motion;
    int sourceList;

    if (!motion.PredictionFlagL0[index])
      sourceList = 1;
    else if (!motion.PredictionFlagL1[index])
      sourceList = 0;
    else
      // The collocated block used both lists, so one has to be chosen. When every reference this
      // picture uses lies in the past the two are interchangeable and the list being derived is
      // taken; otherwise the choice is fixed by the slice header, so that both directions of a
      // bidirectional picture borrow from the same side.
      sourceList = _AllReferencesArePast(frame) ? list : frame.Header.CollocatedFromL0 ? 1 : 0;

    if (!motion.PredictionFlag(sourceList, index))
      return false;

    var sourceRefIdx = motion.RefIdx(sourceList, index);
    var sourcePocs = collocated.ReferencePictureOrderCounts[sourceList];
    var sourceLongTerm = collocated.ReferenceIsLongTerm[sourceList];
    if (sourceRefIdx < 0 || sourceRefIdx >= sourcePocs.Length)
      return false;

    var targets = frame.ReferenceList(list);
    if (refIdx >= targets.Count)
      return false;

    var target = targets[refIdx];
    var targetIsLongTerm = target.Marking == H265ReferenceMarking.LongTerm;

    // A vector measured to a long-term reference cannot be scaled onto a short-term one or the other
    // way about: the distance to a long-term picture is arbitrary, so the ratio the scaling depends
    // on means nothing.
    if (targetIsLongTerm != sourceLongTerm[sourceRefIdx])
      return false;

    var collocatedDistance = collocated.PictureOrderCount - sourcePocs[sourceRefIdx];
    var currentDistance = frame.Picture.PictureOrderCount - target.PictureOrderCount;

    var x0 = motion.MvX(sourceList, index);
    var y0 = motion.MvY(sourceList, index);

    if (targetIsLongTerm || collocatedDistance == currentDistance) {
      mvX = x0;
      mvY = y0;
      return true;
    }

    mvX = Scale(x0, currentDistance, collocatedDistance);
    mvY = Scale(y0, currentDistance, collocatedDistance);
    return true;
  }

  /// <summary>Whether every picture this slice may refer to comes before it — the low-delay case.</summary>
  private static bool _AllReferencesArePast(H265FrameDecoder frame) {
    var current = frame.Picture.PictureOrderCount;

    for (var list = 0; list < 2; ++list)
      foreach (var picture in frame.ReferenceList(list))
        if (picture.PictureOrderCount > current)
          return false;

    return true;
  }

  /// <summary>
  /// Scales a vector from the distance it was measured over onto the distance it is wanted for —
  /// clause 8.5.3.2.8.
  /// </summary>
  /// <remarks>
  /// A reciprocal in fourteen fractional bits, then a multiply and a rounded shift, with both
  /// distances clipped to a byte first. Every step of it is fixed by the standard, including the
  /// rounding away from zero at the end: two decoders that rounded a scaled vector differently would
  /// fetch their prediction from one sample over.
  /// </remarks>
  internal static int Scale(int value, int wanted, int measured) {
    var td = Math.Clamp(measured, -128, 127);
    var tb = Math.Clamp(wanted, -128, 127);

    if (td == 0)
      return value;

    var reciprocal = (16384 + (Math.Abs(td) >> 1)) / td;
    var factor = Math.Clamp((tb * reciprocal + 32) >> 6, -4096, 4095);

    var scaled = factor * value;
    var magnitude = (Math.Abs(scaled) + 127) >> 8;
    return Math.Clamp(scaled < 0 ? -magnitude : magnitude, -32768, 32767);
  }

  /// <summary>
  /// The candidates made by pairing one candidate's first list with another's second —
  /// clause 8.5.3.2.4.
  /// </summary>
  private static void _AddCombinedCandidates(
    H265FrameDecoder frame, List<H265MotionInfo> candidates, int original, int maximum) {
    if (original <= 1 || candidates.Count >= maximum)
      return;

    var listL0 = frame.ReferenceList(0);
    var listL1 = frame.ReferenceList(1);

    for (var i = 0; i < original * (original - 1) && candidates.Count < maximum; ++i) {
      if (i >= _CombinedL0.Length)
        return;

      var first = candidates[_CombinedL0[i]];
      var second = candidates[_CombinedL1[i]];

      if (!first.PredictL0 || !second.PredictL1)
        continue;

      // A combination that predicts the same samples twice from the same picture is worth nothing,
      // so it is only kept when the two halves differ in their picture or in their vector.
      var samePicture = first.RefIdxL0 < listL0.Count && second.RefIdxL1 < listL1.Count
                        && listL0[first.RefIdxL0].PictureOrderCount
                        == listL1[second.RefIdxL1].PictureOrderCount;

      if (samePicture && first.MvL0X == second.MvL1X && first.MvL0Y == second.MvL1Y)
        continue;

      candidates.Add(new() {
        PredictL0 = true,
        PredictL1 = true,
        RefIdxL0 = first.RefIdxL0,
        MvL0X = first.MvL0X,
        MvL0Y = first.MvL0Y,
        RefIdxL1 = second.RefIdxL1,
        MvL1X = second.MvL1X,
        MvL1Y = second.MvL1Y,
      });
    }
  }

  /// <summary>Fills the rest of the list with motionless candidates — clause 8.5.3.2.5.</summary>
  private static void _AddZeroCandidates(H265FrameDecoder frame, List<H265MotionInfo> candidates, int maximum) {
    var bidirectional = frame.Header.SliceType == H265SliceType.B;
    var references = bidirectional
      ? Math.Min(frame.Header.NumRefIdxL0Active, frame.Header.NumRefIdxL1Active)
      : frame.Header.NumRefIdxL0Active;

    var zeroIndex = 0;
    while (candidates.Count < maximum) {
      var refIdx = zeroIndex < references ? zeroIndex : 0;

      candidates.Add(new() {
        PredictL0 = true,
        RefIdxL0 = (sbyte)refIdx,
        PredictL1 = bidirectional,
        RefIdxL1 = (sbyte)(bidirectional ? refIdx : -1),
      });

      ++zeroIndex;
    }
  }

  // ================================================================================================
  // Advanced motion vector prediction — clauses 8.5.3.2.6 to 8.5.3.2.8
  // ================================================================================================

  /// <summary>Builds the two-entry predictor list and takes one — clause 8.5.3.2.6.</summary>
  internal static (short X, short Y) DerivePredictor(
    H265FrameDecoder frame, int xPb, int yPb, int nPbW, int nPbH, int partIdx, int list, int refIdx,
    int predictorFlag) {
    ArgumentNullException.ThrowIfNull(frame);

    var xCb = frame.CodingBlockX;
    var yCb = frame.CodingBlockY;
    var nCbS = frame.CodingBlockSize;

    var haveLeft = _DeriveLeftPredictor(
      frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, list, refIdx,
      out var leftX, out var leftY, out var anyLeftNeighbour);

    var haveAbove = _DeriveAbovePredictor(
      frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, list, refIdx, anyLeftNeighbour,
      ref haveLeft, ref leftX, ref leftY, out var aboveX, out var aboveY);

    var predictors = new List<(int X, int Y)>(3);
    if (haveLeft)
      predictors.Add((leftX, leftY));

    if (haveAbove && !(haveLeft && leftX == aboveX && leftY == aboveY))
      predictors.Add((aboveX, aboveY));

    // The temporal predictor is only worth its cost when the two spatial ones agree — where they
    // differ, the list is already full of genuinely different guesses.
    if (predictors.Count < 2
        && _CollocatedVector(frame, xPb, yPb, nPbW, nPbH, list, refIdx, out var colX, out var colY))
      predictors.Add((colX, colY));

    while (predictors.Count < 2)
      predictors.Add((0, 0));

    var chosen = predictors[predictorFlag];
    return ((short)chosen.X, (short)chosen.Y);
  }

  /// <summary>The predictor from the left, from the two neighbours below it — clause 8.5.3.2.7.</summary>
  private static bool _DeriveLeftPredictor(
    H265FrameDecoder frame, int xCb, int yCb, int nCbS, int xPb, int yPb, int nPbW, int nPbH,
    int partIdx, int list, int refIdx, out int mvX, out int mvY, out bool anyNeighbour) {
    mvX = 0;
    mvY = 0;

    Span<int> xs = [xPb - 1, xPb - 1];
    Span<int> ys = [yPb + nPbH, yPb + nPbH - 1];

    anyNeighbour = false;
    for (var k = 0; k < 2; ++k)
      anyNeighbour |= _IsPredictionBlockAvailable(
        frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, xs[k], ys[k]);

    // First pass: a neighbour that already points at the picture this block wants, whose vector
    // needs no scaling at all.
    for (var k = 0; k < 2; ++k)
      if (_TryExactPredictor(frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, xs[k], ys[k],
            list, refIdx, out mvX, out mvY))
        return true;

    // Second pass: any neighbour, with its vector scaled onto this block's reference distance.
    for (var k = 0; k < 2; ++k)
      if (_TryScaledPredictor(frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, xs[k], ys[k],
            list, refIdx, out mvX, out mvY))
        return true;

    return false;
  }

  /// <summary>The predictor from above, from the three neighbours across its top — clause 8.5.3.2.7.</summary>
  private static bool _DeriveAbovePredictor(
    H265FrameDecoder frame, int xCb, int yCb, int nCbS, int xPb, int yPb, int nPbW, int nPbH,
    int partIdx, int list, int refIdx, bool anyLeftNeighbour,
    ref bool haveLeft, ref int leftX, ref int leftY, out int mvX, out int mvY) {
    mvX = 0;
    mvY = 0;

    Span<int> xs = [xPb + nPbW, xPb + nPbW - 1, xPb - 1];
    Span<int> ys = [yPb - 1, yPb - 1, yPb - 1];

    var found = false;
    for (var k = 0; k < 3 && !found; ++k)
      found = _TryExactPredictor(frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, xs[k], ys[k],
        list, refIdx, out mvX, out mvY);

    if (anyLeftNeighbour)
      return found;

    // With nothing at all to the left, the block above stands in for it as well — and then the
    // search runs again with scaling permitted, which is the only place the standard allows a scaled
    // vector from above.
    if (found) {
      haveLeft = true;
      leftX = mvX;
      leftY = mvY;
    }

    found = false;
    for (var k = 0; k < 3 && !found; ++k)
      found = _TryScaledPredictor(frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, xs[k], ys[k],
        list, refIdx, out mvX, out mvY);

    return found;
  }

  /// <summary>A neighbour whose vector already points at the wanted picture.</summary>
  private static bool _TryExactPredictor(
    H265FrameDecoder frame, int xCb, int yCb, int nCbS, int xPb, int yPb, int nPbW, int nPbH,
    int partIdx, int xNb, int yNb, int list, int refIdx, out int mvX, out int mvY) {
    mvX = 0;
    mvY = 0;

    if (!_IsPredictionBlockAvailable(frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, xNb, yNb))
      return false;

    var neighbour = frame.MotionAt(frame.BlockIndexAt(xNb, yNb));
    var wanted = _ReferencePoc(frame, list, refIdx);
    if (wanted == null)
      return false;

    for (var pass = 0; pass < 2; ++pass) {
      var from = pass == 0 ? list : 1 - list;
      if (!neighbour.Predicts(from))
        continue;

      var poc = _ReferencePoc(frame, from, neighbour.RefIdx(from));
      if (poc != wanted.Value)
        continue;

      mvX = neighbour.MvX(from);
      mvY = neighbour.MvY(from);
      return true;
    }

    return false;
  }

  /// <summary>A neighbour whose vector has to be scaled onto the wanted picture's distance.</summary>
  private static bool _TryScaledPredictor(
    H265FrameDecoder frame, int xCb, int yCb, int nCbS, int xPb, int yPb, int nPbW, int nPbH,
    int partIdx, int xNb, int yNb, int list, int refIdx, out int mvX, out int mvY) {
    mvX = 0;
    mvY = 0;

    if (!_IsPredictionBlockAvailable(frame, xCb, yCb, nCbS, xPb, yPb, nPbW, nPbH, partIdx, xNb, yNb))
      return false;

    var neighbour = frame.MotionAt(frame.BlockIndexAt(xNb, yNb));
    var targets = frame.ReferenceList(list);
    if (refIdx >= targets.Count)
      return false;

    var target = targets[refIdx];
    var targetIsLongTerm = target.Marking == H265ReferenceMarking.LongTerm;

    for (var pass = 0; pass < 2; ++pass) {
      var from = pass == 0 ? list : 1 - list;
      if (!neighbour.Predicts(from))
        continue;

      var sourceList = frame.ReferenceList(from);
      var sourceIndex = neighbour.RefIdx(from);
      if (sourceIndex < 0 || sourceIndex >= sourceList.Count)
        continue;

      var source = sourceList[sourceIndex];
      if ((source.Marking == H265ReferenceMarking.LongTerm) != targetIsLongTerm)
        continue;

      mvX = neighbour.MvX(from);
      mvY = neighbour.MvY(from);

      if (targetIsLongTerm)
        return true;

      var current = frame.Picture.PictureOrderCount;
      mvX = Scale(mvX, current - target.PictureOrderCount, current - source.PictureOrderCount);
      mvY = Scale(mvY, current - target.PictureOrderCount, current - source.PictureOrderCount);
      return true;
    }

    return false;
  }

  private static int? _ReferencePoc(H265FrameDecoder frame, int list, int index) {
    var pictures = frame.ReferenceList(list);
    return index >= 0 && index < pictures.Count ? pictures[index].PictureOrderCount : null;
  }
}
