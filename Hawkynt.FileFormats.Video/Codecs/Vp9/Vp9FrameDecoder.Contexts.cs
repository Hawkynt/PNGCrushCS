using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Which probability each syntax element is read with (specification 9.3.2).
/// </summary>
/// <remarks>
/// Every syntax element in VP9 is coded with a probability chosen from what the neighbouring blocks
/// turned out to be. A block beside one that was skipped is more likely to be skipped; a block beside
/// two that predicted from the last frame is more likely to do the same. The derivations below are
/// the format's whole opinion on what "similar" means, and they are dense in proportion to how much
/// the choice is worth: the reference frame ones enumerate every combination of what the two
/// neighbours could have been, because that is where the bits are.
/// <para/>
/// They are written out here in the shape the specification states them in, branch for branch. A
/// tidier formulation would be shorter and would be very hard to check against the standard, and a
/// context that is wrong by one is not an error — it is a different probability, a different bool, and
/// a picture that is merely not the right one.
/// </remarks>
internal sealed partial class Vp9FrameDecoder {

  // ============================================================================================
  // Partition (specification 6.4.3 and 9.3.2)
  // ============================================================================================

  /// <summary>
  /// Reads how a block is split.
  /// </summary>
  /// <remarks>
  /// A block whose lower or right half lies off screen cannot use every partition: there is nothing
  /// to code for the missing half, so the choice narrows to the one split that keeps the visible half
  /// whole and the one that divides it further. When both halves are off screen there is no choice at
  /// all and no bits are spent — but the outcome is still counted, because the adaptation has to see
  /// the same numbers in both the encoder and the decoder.
  /// </remarks>
  private int _ReadPartition(int row, int column, int size, int blocks, bool hasRows, bool hasColumns) {
    var context = this._PartitionContext(row, column, size, blocks);
    var probabilities = this._header.FrameIsIntra
      ? Vp9DefaultProbabilities.KeyFramePartition.Slice(context * (PARTITION_TYPES - 1), PARTITION_TYPES - 1)
      : this._probabilities.Partition.AsSpan(context * (PARTITION_TYPES - 1), PARTITION_TYPES - 1);

    int partition;
    if (hasRows && hasColumns)
      partition = this._reader.ReadTree(Vp9Trees.Partition, probabilities);
    else if (hasColumns)
      partition = this._reader.ReadBool(probabilities[1]) != 0 ? PARTITION_SPLIT : PARTITION_HORZ;
    else if (hasRows)
      partition = this._reader.ReadBool(probabilities[2]) != 0 ? PARTITION_SPLIT : PARTITION_VERT;
    else
      partition = PARTITION_SPLIT;

    ++this._counts.Partition[context * PARTITION_TYPES + partition];
    return partition;
  }

  /// <summary>How far the neighbouring superblocks were split (specification 9.3.2, <c>partition</c>).</summary>
  private int _PartitionContext(int row, int column, int size, int blocks) {
    var above = 0;
    var left = 0;
    var sizeLog2 = Vp9Tables.ModeInfoWidthLog2[size];
    var offset = Vp9Tables.ModeInfoWidthLog2[BLOCK_64X64] - sizeLog2;

    for (var i = 0; i < blocks; ++i) {
      above |= this._abovePartition[column + i];
      left |= this._leftPartition[row + i];
    }

    var aboveSplit = (above & (1 << offset)) > 0 ? 1 : 0;
    var leftSplit = (left & (1 << offset)) > 0 ? 1 : 0;
    return sizeLog2 * 4 + leftSplit * 2 + aboveSplit;
  }

  // ============================================================================================
  // Block flags
  // ============================================================================================

  /// <summary>How many of the two neighbours carried no coefficients (specification 9.3.2, <c>skip</c>).</summary>
  private int _SkipContext() {
    var context = 0;
    if (this._availableAbove)
      context += this._grid.Skips[this._grid.IndexOf(this._miRow - 1, this._miCol)] ? 1 : 0;

    if (this._availableLeft)
      context += this._grid.Skips[this._grid.IndexOf(this._miRow, this._miCol - 1)] ? 1 : 0;

    return context;
  }

  /// <summary>How many of the two neighbours were intra coded (specification 9.3.2, <c>is_inter</c>).</summary>
  private int _IsInterContext() {
    if (this._availableAbove && this._availableLeft)
      return this._leftIntra && this._aboveIntra ? 3 : this._leftIntra || this._aboveIntra ? 1 : 0;

    if (this._availableAbove || this._availableLeft)
      return 2 * ((this._availableAbove ? this._aboveIntra : this._leftIntra) ? 1 : 0);

    return 0;
  }

  /// <summary>Whether the neighbours used transforms larger than this block's maximum (specification 9.3.2, <c>tx_size</c>).</summary>
  private int _TransformSizeContext(int maximum) {
    var above = maximum;
    var left = maximum;

    if (this._availableAbove) {
      var index = this._grid.IndexOf(this._miRow - 1, this._miCol);
      if (!this._grid.Skips[index])
        above = this._grid.TransformSizes[index];
    }

    if (this._availableLeft) {
      var index = this._grid.IndexOf(this._miRow, this._miCol - 1);
      if (!this._grid.Skips[index])
        left = this._grid.TransformSizes[index];
    }

    if (!this._availableLeft)
      left = above;

    if (!this._availableAbove)
      above = left;

    return above + left > maximum ? 1 : 0;
  }

  /// <summary>Which motion vector the block takes, read with the neighbour tally the search produced.</summary>
  private int _ReadInterMode() {
    var context = this._modeContext[this._referenceFrame[0]];
    var mode = this._reader.ReadTree(
      Vp9Trees.InterMode, this._probabilities.InterMode.AsSpan(context * (INTER_MODES - 1), INTER_MODES - 1));
    ++this._counts.InterMode[context * INTER_MODES + mode];
    return mode;
  }

  /// <summary>Which interpolation filter the block uses, read with the filters its neighbours chose (specification 9.3.2).</summary>
  private int _ReadInterpolationFilter() {
    var left = this._availableLeft && this._leftReferenceFrame[0] > INTRA_FRAME
      ? this._grid.InterpolationFilters[this._grid.IndexOf(this._miRow, this._miCol - 1)]
      : 3;
    var above = this._availableAbove && this._aboveReferenceFrame[0] > INTRA_FRAME
      ? this._grid.InterpolationFilters[this._grid.IndexOf(this._miRow - 1, this._miCol)]
      : 3;

    var context = left == above ? left
      : left == 3 && above != 3 ? above
      : left != 3 && above == 3 ? left
      : 3;

    var filter = this._reader.ReadTree(
      Vp9Trees.InterpolationFilter,
      this._probabilities.InterpolationFilter.AsSpan(context * (SWITCHABLE_FILTERS - 1), SWITCHABLE_FILTERS - 1));
    ++this._counts.InterpolationFilter[context * SWITCHABLE_FILTERS + filter];
    return filter;
  }

  // ============================================================================================
  // Which reference frame (specification 9.3.2)
  // ============================================================================================

  private int _ReadCompoundMode() {
    var context = this._CompoundModeContext();
    var mode = this._reader.ReadBool(this._probabilities.CompoundMode[context]);
    ++this._counts.CompoundMode[context * 2 + mode];
    return mode;
  }

  /// <summary>Whether the block uses compound prediction (specification 9.3.2, <c>comp_mode</c>).</summary>
  private int _CompoundModeContext() {
    var fixedReference = this._header.CompoundFixedReference;

    if (this._availableAbove && this._availableLeft) {
      if (this._aboveSingle && this._leftSingle)
        return (this._aboveReferenceFrame[0] == fixedReference ? 1 : 0)
               ^ (this._leftReferenceFrame[0] == fixedReference ? 1 : 0);

      if (this._aboveSingle)
        return 2 + (this._aboveReferenceFrame[0] == fixedReference || this._aboveIntra ? 1 : 0);

      if (this._leftSingle)
        return 2 + (this._leftReferenceFrame[0] == fixedReference || this._leftIntra ? 1 : 0);

      return 4;
    }

    if (this._availableAbove)
      return this._aboveSingle ? this._aboveReferenceFrame[0] == fixedReference ? 1 : 0 : 3;

    if (this._availableLeft)
      return this._leftSingle ? this._leftReferenceFrame[0] == fixedReference ? 1 : 0 : 3;

    return 1;
  }

  private int _ReadCompoundReference() {
    var context = this._CompoundReferenceContext();
    var reference = this._reader.ReadBool(this._probabilities.CompoundReference[context]);
    ++this._counts.CompoundReference[context * 2 + reference];
    return reference;
  }

  /// <summary>
  /// Which of the two variable references a compound block uses (specification 9.3.2,
  /// <c>comp_ref</c>).
  /// </summary>
  /// <remarks>
  /// The longest derivation in the format, and the one that most repays being written out branch
  /// for branch: it enumerates every combination of what the two neighbours could have been —
  /// intra or inter, single or compound, agreeing or not — because compound prediction is where a
  /// well-chosen probability is worth the most bits.
  /// </remarks>
  private int _CompoundReferenceContext() {
    var fixedIndex = this._header.ReferenceFrameSignBias[this._header.CompoundFixedReference];
    var variableIndex = fixedIndex == 0 ? 1 : 0;
    var fixedReference = this._header.CompoundFixedReference;
    var firstVariable = this._header.CompoundVariableReference[0];
    var secondVariable = this._header.CompoundVariableReference[1];

    if (this._availableAbove && this._availableLeft) {
      if (this._aboveIntra && this._leftIntra)
        return 2;

      if (this._leftIntra)
        return 1 + 2 * ((this._aboveSingle
          ? this._aboveReferenceFrame[0]
          : this._aboveReferenceFrame[variableIndex]) != secondVariable ? 1 : 0);

      if (this._aboveIntra)
        return 1 + 2 * ((this._leftSingle
          ? this._leftReferenceFrame[0]
          : this._leftReferenceFrame[variableIndex]) != secondVariable ? 1 : 0);

      var aboveVariable = this._aboveSingle ? this._aboveReferenceFrame[0] : this._aboveReferenceFrame[variableIndex];
      var leftVariable = this._leftSingle ? this._leftReferenceFrame[0] : this._leftReferenceFrame[variableIndex];

      if (aboveVariable == leftVariable && secondVariable == aboveVariable)
        return 0;

      if (this._leftSingle && this._aboveSingle) {
        if ((aboveVariable == fixedReference && leftVariable == firstVariable)
            || (leftVariable == fixedReference && aboveVariable == firstVariable))
          return 4;

        return aboveVariable == leftVariable ? 3 : 1;
      }

      if (this._leftSingle || this._aboveSingle) {
        var compound = this._leftSingle ? aboveVariable : leftVariable;
        var single = this._aboveSingle ? aboveVariable : leftVariable;

        if (compound == secondVariable && single != secondVariable)
          return 1;

        return single == secondVariable && compound != secondVariable ? 2 : 4;
      }

      return aboveVariable == leftVariable ? 4 : 2;
    }

    if (this._availableAbove) {
      if (this._aboveIntra)
        return 2;

      return this._aboveSingle
        ? 3 * (this._aboveReferenceFrame[0] != secondVariable ? 1 : 0)
        : 4 * (this._aboveReferenceFrame[variableIndex] != secondVariable ? 1 : 0);
    }

    if (this._availableLeft) {
      if (this._leftIntra)
        return 2;

      return this._leftSingle
        ? 3 * (this._leftReferenceFrame[0] != secondVariable ? 1 : 0)
        : 4 * (this._leftReferenceFrame[variableIndex] != secondVariable ? 1 : 0);
    }

    return 2;
  }

  private int _ReadSingleReferenceFirst() {
    var context = this._SingleReferenceFirstContext();
    var value = this._reader.ReadBool(this._probabilities.SingleReference[context * 2]);
    ++this._counts.SingleReference[(context * 2) * 2 + value];
    return value;
  }

  /// <summary>Whether a single-reference block predicts from the last frame (specification 9.3.2, <c>single_ref_p1</c>).</summary>
  private int _SingleReferenceFirstContext() {
    if (this._availableAbove && this._availableLeft) {
      if (this._aboveIntra && this._leftIntra)
        return 2;

      if (this._leftIntra)
        return this._aboveSingle
          ? 4 * (this._aboveReferenceFrame[0] == LAST_FRAME ? 1 : 0)
          : 1 + (this._aboveReferenceFrame[0] == LAST_FRAME || this._aboveReferenceFrame[1] == LAST_FRAME ? 1 : 0);

      if (this._aboveIntra)
        return this._leftSingle
          ? 4 * (this._leftReferenceFrame[0] == LAST_FRAME ? 1 : 0)
          : 1 + (this._leftReferenceFrame[0] == LAST_FRAME || this._leftReferenceFrame[1] == LAST_FRAME ? 1 : 0);

      if (this._aboveSingle && this._leftSingle)
        return 2 * (this._aboveReferenceFrame[0] == LAST_FRAME ? 1 : 0)
               + 2 * (this._leftReferenceFrame[0] == LAST_FRAME ? 1 : 0);

      if (!this._aboveSingle && !this._leftSingle)
        return 1 + (this._aboveReferenceFrame[0] == LAST_FRAME
                    || this._aboveReferenceFrame[1] == LAST_FRAME
                    || this._leftReferenceFrame[0] == LAST_FRAME
                    || this._leftReferenceFrame[1] == LAST_FRAME ? 1 : 0);

      var single = this._aboveSingle ? this._aboveReferenceFrame[0] : this._leftReferenceFrame[0];
      var compoundFirst = this._aboveSingle ? this._leftReferenceFrame[0] : this._aboveReferenceFrame[0];
      var compoundSecond = this._aboveSingle ? this._leftReferenceFrame[1] : this._aboveReferenceFrame[1];
      var compoundHasLast = compoundFirst == LAST_FRAME || compoundSecond == LAST_FRAME ? 1 : 0;

      return single == LAST_FRAME ? 3 + compoundHasLast : compoundHasLast;
    }

    if (this._availableAbove) {
      if (this._aboveIntra)
        return 2;

      return this._aboveSingle
        ? 4 * (this._aboveReferenceFrame[0] == LAST_FRAME ? 1 : 0)
        : 1 + (this._aboveReferenceFrame[0] == LAST_FRAME || this._aboveReferenceFrame[1] == LAST_FRAME ? 1 : 0);
    }

    if (this._availableLeft) {
      if (this._leftIntra)
        return 2;

      return this._leftSingle
        ? 4 * (this._leftReferenceFrame[0] == LAST_FRAME ? 1 : 0)
        : 1 + (this._leftReferenceFrame[0] == LAST_FRAME || this._leftReferenceFrame[1] == LAST_FRAME ? 1 : 0);
    }

    return 2;
  }

  private int _ReadSingleReferenceSecond() {
    var context = this._SingleReferenceSecondContext();
    var value = this._reader.ReadBool(this._probabilities.SingleReference[context * 2 + 1]);
    ++this._counts.SingleReference[(context * 2 + 1) * 2 + value];
    return value;
  }

  /// <summary>Which of the two long-term references it predicts from instead (specification 9.3.2, <c>single_ref_p2</c>).</summary>
  private int _SingleReferenceSecondContext() {
    if (this._availableAbove && this._availableLeft) {
      if (this._aboveIntra && this._leftIntra)
        return 2;

      if (this._leftIntra) {
        if (!this._aboveSingle)
          return 1 + 2 * (this._aboveReferenceFrame[0] == GOLDEN_FRAME
                          || this._aboveReferenceFrame[1] == GOLDEN_FRAME ? 1 : 0);

        return this._aboveReferenceFrame[0] == LAST_FRAME
          ? 3
          : 4 * (this._aboveReferenceFrame[0] == GOLDEN_FRAME ? 1 : 0);
      }

      if (this._aboveIntra) {
        if (!this._leftSingle)
          return 1 + 2 * (this._leftReferenceFrame[0] == GOLDEN_FRAME
                          || this._leftReferenceFrame[1] == GOLDEN_FRAME ? 1 : 0);

        return this._leftReferenceFrame[0] == LAST_FRAME
          ? 3
          : 4 * (this._leftReferenceFrame[0] == GOLDEN_FRAME ? 1 : 0);
      }

      if (this._aboveSingle && this._leftSingle) {
        if (this._aboveReferenceFrame[0] == LAST_FRAME && this._leftReferenceFrame[0] == LAST_FRAME)
          return 3;

        if (this._aboveReferenceFrame[0] == LAST_FRAME)
          return 4 * (this._leftReferenceFrame[0] == GOLDEN_FRAME ? 1 : 0);

        if (this._leftReferenceFrame[0] == LAST_FRAME)
          return 4 * (this._aboveReferenceFrame[0] == GOLDEN_FRAME ? 1 : 0);

        return 2 * (this._aboveReferenceFrame[0] == GOLDEN_FRAME ? 1 : 0)
               + 2 * (this._leftReferenceFrame[0] == GOLDEN_FRAME ? 1 : 0);
      }

      if (!this._aboveSingle && !this._leftSingle) {
        if (this._aboveReferenceFrame[0] == this._leftReferenceFrame[0]
            && this._aboveReferenceFrame[1] == this._leftReferenceFrame[1])
          return 3 * (this._aboveReferenceFrame[0] == GOLDEN_FRAME
                      || this._aboveReferenceFrame[1] == GOLDEN_FRAME ? 1 : 0);

        return 2;
      }

      var single = this._aboveSingle ? this._aboveReferenceFrame[0] : this._leftReferenceFrame[0];
      var compoundFirst = this._aboveSingle ? this._leftReferenceFrame[0] : this._aboveReferenceFrame[0];
      var compoundSecond = this._aboveSingle ? this._leftReferenceFrame[1] : this._aboveReferenceFrame[1];
      var compoundHasGolden = compoundFirst == GOLDEN_FRAME || compoundSecond == GOLDEN_FRAME ? 1 : 0;

      if (single == GOLDEN_FRAME)
        return 3 + compoundHasGolden;

      return single == ALTREF_FRAME ? compoundHasGolden : 1 + 2 * compoundHasGolden;
    }

    if (this._availableAbove) {
      if (this._aboveIntra || (this._aboveReferenceFrame[0] == LAST_FRAME && this._aboveSingle))
        return 2;

      return this._aboveSingle
        ? 4 * (this._aboveReferenceFrame[0] == GOLDEN_FRAME ? 1 : 0)
        : 3 * (this._aboveReferenceFrame[0] == GOLDEN_FRAME || this._aboveReferenceFrame[1] == GOLDEN_FRAME ? 1 : 0);
    }

    if (this._availableLeft) {
      if (this._leftIntra || (this._leftReferenceFrame[0] == LAST_FRAME && this._leftSingle))
        return 2;

      return this._leftSingle
        ? 4 * (this._leftReferenceFrame[0] == GOLDEN_FRAME ? 1 : 0)
        : 3 * (this._leftReferenceFrame[0] == GOLDEN_FRAME || this._leftReferenceFrame[1] == GOLDEN_FRAME ? 1 : 0);
    }

    return 2;
  }
}
