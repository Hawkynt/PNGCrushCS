using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// The mode info of one block: which segment it belongs to, whether it carries a residue, what
/// transform it uses, how it is predicted and from where (specification 6.4.5 to 6.4.20).
/// </summary>
internal sealed partial class Vp9FrameDecoder {

  /// <summary>Two motion vectors per reference list, the working values of <c>Mv</c>.</summary>
  private readonly int[] _motionVector = new int[2 * 2];

  private readonly int[] _nearestMotionVector = new int[2 * 2];
  private readonly int[] _nearMotionVector = new int[2 * 2];
  private readonly int[] _bestMotionVector = new int[2 * 2];
  private bool _useHighPrecision;

  private void _ReadModeInfo() {
    if (this._header.FrameIsIntra)
      this._ReadIntraFrameModeInfo();
    else
      this._ReadInterFrameModeInfo();
  }

  // ============================================================================================
  // A block of an intra frame (specification 6.4.6)
  // ============================================================================================

  private void _ReadIntraFrameModeInfo() {
    this._ReadIntraSegmentId();
    this._ReadSkip();
    this._ReadTransformSize(true);

    this._referenceFrame[0] = INTRA_FRAME;
    this._referenceFrame[1] = NONE;
    this._isInter = false;

    if (this._miSize >= BLOCK_8X8) {
      var mode = this._ReadKeyFrameIntraMode(0, 0);
      this._yMode = mode;
      for (var block = 0; block < 4; ++block)
        this._subModes[block] = (byte)mode;
    } else {
      var wide = Vp9Tables.Blocks4x4Wide[this._miSize];
      var high = Vp9Tables.Blocks4x4High[this._miSize];
      var mode = DC_PRED;

      for (var idy = 0; idy < 2; idy += high)
      for (var idx = 0; idx < 2; idx += wide) {
        mode = this._ReadKeyFrameIntraMode(idy, idx);
        for (var y = 0; y < high; ++y)
        for (var x = 0; x < wide; ++x)
          this._subModes[(idy + y) * 2 + idx + x] = (byte)mode;
      }

      this._yMode = mode;
    }

    this._uvMode = this._reader.ReadTree(
      Vp9Trees.IntraMode, Vp9DefaultProbabilities.KeyFrameUvMode.Slice(this._yMode * (INTRA_MODES - 1), INTRA_MODES - 1));
  }

  /// <summary>
  /// Reads one intra mode of an intra frame, conditioned on the modes above and to the left
  /// (specification 9.3.2).
  /// </summary>
  private int _ReadKeyFrameIntraMode(int idy, int idx) {
    int above;
    int left;

    if (this._miSize >= BLOCK_8X8) {
      above = this._availableAbove ? this._grid.SubModes[this._grid.IndexOf(this._miRow - 1, this._miCol) * 4 + 2] : DC_PRED;
      left = this._availableLeft ? this._grid.SubModes[this._grid.IndexOf(this._miRow, this._miCol - 1) * 4 + 1] : DC_PRED;
    } else {
      above = idy != 0
        ? this._subModes[idx]
        : this._availableAbove
          ? this._grid.SubModes[this._grid.IndexOf(this._miRow - 1, this._miCol) * 4 + 2 + idx]
          : DC_PRED;

      left = idx != 0
        ? this._subModes[idy * 2]
        : this._availableLeft
          ? this._grid.SubModes[this._grid.IndexOf(this._miRow, this._miCol - 1) * 4 + 1 + idy * 2]
          : DC_PRED;
    }

    return this._reader.ReadTree(
      Vp9Trees.IntraMode,
      Vp9DefaultProbabilities.KeyFrameYMode.Slice((above * INTRA_MODES + left) * (INTRA_MODES - 1), INTRA_MODES - 1));
  }

  // ============================================================================================
  // A block of an inter frame (specification 6.4.11)
  // ============================================================================================

  private void _ReadInterFrameModeInfo() {
    var left = this._availableLeft ? this._grid.IndexOf(this._miRow, this._miCol - 1) : -1;
    var above = this._availableAbove ? this._grid.IndexOf(this._miRow - 1, this._miCol) : -1;

    this._leftReferenceFrame[0] = left >= 0 ? this._grid.ReferenceFrames[left * 2] : INTRA_FRAME;
    this._aboveReferenceFrame[0] = above >= 0 ? this._grid.ReferenceFrames[above * 2] : INTRA_FRAME;
    this._leftReferenceFrame[1] = left >= 0 ? this._grid.ReferenceFrames[left * 2 + 1] : NONE;
    this._aboveReferenceFrame[1] = above >= 0 ? this._grid.ReferenceFrames[above * 2 + 1] : NONE;

    this._leftIntra = this._leftReferenceFrame[0] <= INTRA_FRAME;
    this._aboveIntra = this._aboveReferenceFrame[0] <= INTRA_FRAME;
    this._leftSingle = this._leftReferenceFrame[1] <= NONE;
    this._aboveSingle = this._aboveReferenceFrame[1] <= NONE;

    this._ReadInterSegmentId();
    this._ReadSkip();
    this._ReadIsInter();
    this._ReadTransformSize(!this._skip || !this._isInter);

    if (this._isInter)
      this._ReadInterBlockModeInfo();
    else
      this._ReadIntraBlockModeInfo();
  }

  private void _ReadIntraBlockModeInfo() {
    this._referenceFrame[0] = INTRA_FRAME;
    this._referenceFrame[1] = NONE;

    if (this._miSize >= BLOCK_8X8) {
      var context = Vp9Tables.SizeGroup[this._miSize];
      var mode = this._reader.ReadTree(
        Vp9Trees.IntraMode, this._probabilities.YMode.AsSpan(context * (INTRA_MODES - 1), INTRA_MODES - 1));
      ++this._counts.IntraMode[context * INTRA_MODES + mode];

      this._yMode = mode;
      for (var block = 0; block < 4; ++block)
        this._subModes[block] = (byte)mode;
    } else {
      var wide = Vp9Tables.Blocks4x4Wide[this._miSize];
      var high = Vp9Tables.Blocks4x4High[this._miSize];
      var mode = DC_PRED;

      for (var idy = 0; idy < 2; idy += high)
      for (var idx = 0; idx < 2; idx += wide) {
        // A sub-block always uses the smallest size group, whatever size the block itself is.
        mode = this._reader.ReadTree(Vp9Trees.IntraMode, this._probabilities.YMode.AsSpan(0, INTRA_MODES - 1));
        ++this._counts.IntraMode[mode];

        for (var y = 0; y < high; ++y)
        for (var x = 0; x < wide; ++x)
          this._subModes[(idy + y) * 2 + idx + x] = (byte)mode;
      }

      this._yMode = mode;
    }

    this._uvMode = this._reader.ReadTree(
      Vp9Trees.IntraMode, this._probabilities.UvMode.AsSpan(this._yMode * (INTRA_MODES - 1), INTRA_MODES - 1));
    ++this._counts.UvMode[this._yMode * INTRA_MODES + this._uvMode];
  }

  private void _ReadInterBlockModeInfo() {
    this._ReadReferenceFrames();

    for (var list = 0; list < 2; ++list) {
      if (this._referenceFrame[list] <= INTRA_FRAME)
        continue;

      this._FindMotionVectorReferences(this._referenceFrame[list], -1);
      this._FindBestReferenceMotionVectors(list);
    }

    var isCompound = this._referenceFrame[1] > INTRA_FRAME;

    if (this._header.IsFeatureActive(this._segmentId, SEG_LVL_SKIP))
      this._yMode = ZEROMV;
    else if (this._miSize >= BLOCK_8X8)
      this._yMode = NEARESTMV + this._ReadInterMode();

    this._interpolationFilter = this._header.InterpolationFilter == SWITCHABLE
      ? this._ReadInterpolationFilter()
      : this._header.InterpolationFilter;

    if (this._miSize >= BLOCK_8X8) {
      this._AssignMotionVector(isCompound);
      for (var list = 0; list < 1 + (isCompound ? 1 : 0); ++list)
      for (var block = 0; block < 4; ++block) {
        this._blockMotionVectors[(list * 4 + block) * 2] = (short)this._motionVector[list * 2];
        this._blockMotionVectors[(list * 4 + block) * 2 + 1] = (short)this._motionVector[list * 2 + 1];
      }

      return;
    }

    var wide = Vp9Tables.Blocks4x4Wide[this._miSize];
    var high = Vp9Tables.Blocks4x4High[this._miSize];

    for (var idy = 0; idy < 2; idy += high)
    for (var idx = 0; idx < 2; idx += wide) {
      this._yMode = NEARESTMV + this._ReadInterMode();

      if (this._yMode is NEARESTMV or NEARMV)
        for (var list = 0; list < 1 + (isCompound ? 1 : 0); ++list)
          this._AppendSub8x8MotionVectors(idy * 2 + idx, list);

      this._AssignMotionVector(isCompound);

      for (var y = 0; y < high; ++y)
      for (var x = 0; x < wide; ++x) {
        var block = (idy + y) * 2 + idx + x;
        for (var list = 0; list < 1 + (isCompound ? 1 : 0); ++list) {
          this._blockMotionVectors[(list * 4 + block) * 2] = (short)this._motionVector[list * 2];
          this._blockMotionVectors[(list * 4 + block) * 2 + 1] = (short)this._motionVector[list * 2 + 1];
        }
      }
    }
  }

  // ============================================================================================
  // Reference frames (specification 6.4.17)
  // ============================================================================================

  private void _ReadReferenceFrames() {
    if (this._header.IsFeatureActive(this._segmentId, SEG_LVL_REF_FRAME)) {
      this._referenceFrame[0] = (sbyte)this._header.Feature(this._segmentId, SEG_LVL_REF_FRAME);
      this._referenceFrame[1] = NONE;
      return;
    }

    var compoundMode = this._header.ReferenceMode == REFERENCE_MODE_SELECT
      ? this._ReadCompoundMode()
      : this._header.ReferenceMode;

    if (compoundMode == COMPOUND_REFERENCE) {
      var fixedIndex = this._header.ReferenceFrameSignBias[this._header.CompoundFixedReference];
      var variable = this._ReadCompoundReference();
      this._referenceFrame[fixedIndex] = (sbyte)this._header.CompoundFixedReference;
      this._referenceFrame[fixedIndex == 0 ? 1 : 0] = (sbyte)this._header.CompoundVariableReference[variable];
      return;
    }

    this._referenceFrame[0] = this._ReadSingleReferenceFirst() != 0
      ? this._ReadSingleReferenceSecond() != 0 ? (sbyte)ALTREF_FRAME : (sbyte)GOLDEN_FRAME
      : (sbyte)LAST_FRAME;
    this._referenceFrame[1] = NONE;
  }

  // ============================================================================================
  // Segment identity (specification 6.4.7, 6.4.12 and 6.4.14)
  // ============================================================================================

  private void _ReadIntraSegmentId()
    => this._segmentId = this._header.SegmentationEnabled && this._header.SegmentationUpdateMap
      ? this._reader.ReadTree(Vp9Trees.Segment, this._header.SegmentationTreeProbabilities)
      : 0;

  private void _ReadInterSegmentId() {
    if (!this._header.SegmentationEnabled) {
      this._segmentId = 0;
      return;
    }

    var predicted = this._PredictedSegmentId();

    if (!this._header.SegmentationUpdateMap) {
      this._segmentId = predicted;
      return;
    }

    if (!this._header.SegmentationTemporalUpdate) {
      this._segmentId = this._reader.ReadTree(Vp9Trees.Segment, this._header.SegmentationTreeProbabilities);
      return;
    }

    var context = this._leftSegmentPrediction[this._miRow] + this._aboveSegmentPrediction[this._miCol];
    var usePrediction = this._reader.ReadBool(this._header.SegmentationPredictionProbabilities[context]);

    this._segmentId = usePrediction != 0
      ? predicted
      : this._reader.ReadTree(Vp9Trees.Segment, this._header.SegmentationTreeProbabilities);

    for (var i = 0; i < Vp9Tables.Blocks8x8Wide[this._miSize]; ++i)
      this._aboveSegmentPrediction[this._miCol + i] = (byte)usePrediction;

    for (var i = 0; i < Vp9Tables.Blocks8x8High[this._miSize]; ++i)
      this._leftSegmentPrediction[this._miRow + i] = (byte)usePrediction;
  }

  /// <summary>
  /// The smallest segment the block's on-screen area held a frame ago (specification 6.4.14).
  /// </summary>
  /// <remarks>
  /// The smallest and not, say, the commonest, because the segment features are adjustments and the
  /// lowest numbered segment is the one a frame that segments by activity puts its quietest blocks in.
  /// </remarks>
  private int _PredictedSegmentId() {
    var wide = Math.Min(this._header.MiCols - this._miCol, Vp9Tables.Blocks8x8Wide[this._miSize]);
    var high = Math.Min(this._header.MiRows - this._miRow, Vp9Tables.Blocks8x8High[this._miSize]);

    var segment = 7;
    for (var y = 0; y < high; ++y)
    for (var x = 0; x < wide; ++x)
      segment = Math.Min(segment, this._grid.PreviousSegmentIds[this._grid.IndexOf(this._miRow + y, this._miCol + x)]);

    return segment;
  }

  // ============================================================================================
  // Skip, inter and transform size (specification 6.4.8, 6.4.10 and 6.4.13)
  // ============================================================================================

  private void _ReadSkip() {
    if (this._header.IsFeatureActive(this._segmentId, SEG_LVL_SKIP)) {
      this._skip = true;
      return;
    }

    var context = this._SkipContext();
    var skip = this._reader.ReadBool(this._probabilities.Skip[context]);
    ++this._counts.Skip[context * 2 + skip];
    this._skip = skip != 0;
  }

  private void _ReadIsInter() {
    if (this._header.IsFeatureActive(this._segmentId, SEG_LVL_REF_FRAME)) {
      this._isInter = this._header.Feature(this._segmentId, SEG_LVL_REF_FRAME) != INTRA_FRAME;
      return;
    }

    var context = this._IsInterContext();
    var isInter = this._reader.ReadBool(this._probabilities.IsInter[context]);
    ++this._counts.IsInter[context * 2 + isInter];
    this._isInter = isInter != 0;
  }

  private void _ReadTransformSize(bool allowSelect) {
    var maximum = Vp9Tables.MaxTransformSize[this._miSize];

    if (!allowSelect || this._header.TransformMode != TX_MODE_SELECT || this._miSize < BLOCK_8X8) {
      this._transformSize = Math.Min(maximum, Vp9Tables.BiggestTransformSize[this._header.TransformMode]);
      return;
    }

    var context = this._TransformSizeContext(maximum);
    var probabilities = this._probabilities.TransformSize.AsSpan(
      (maximum * TX_SIZE_CONTEXTS + context) * (TX_SIZES - 1), TX_SIZES - 1);

    var tree = maximum switch {
      TX_32X32 => Vp9Trees.TransformSize32,
      TX_16X16 => Vp9Trees.TransformSize16,
      _ => Vp9Trees.TransformSize8,
    };

    this._transformSize = this._reader.ReadTree(tree, probabilities);
    ++this._counts.TransformSize[(maximum * TX_SIZE_CONTEXTS + context) * TX_SIZES + this._transformSize];
  }

  // ============================================================================================
  // Motion vectors (specification 6.4.18 to 6.4.20)
  // ============================================================================================

  private void _AssignMotionVector(bool isCompound) {
    this._motionVector[2] = 0;
    this._motionVector[3] = 0;

    for (var list = 0; list < 1 + (isCompound ? 1 : 0); ++list)
      switch (this._yMode) {
        case NEWMV:
          this._ReadMotionVector(list);
          break;
        case NEARESTMV:
          this._motionVector[list * 2] = this._nearestMotionVector[list * 2];
          this._motionVector[list * 2 + 1] = this._nearestMotionVector[list * 2 + 1];
          break;
        case NEARMV:
          this._motionVector[list * 2] = this._nearMotionVector[list * 2];
          this._motionVector[list * 2 + 1] = this._nearMotionVector[list * 2 + 1];
          break;
        default:
          this._motionVector[list * 2] = 0;
          this._motionVector[list * 2 + 1] = 0;
          break;
      }
  }

  /// <summary>
  /// Reads a motion vector as a difference from the best of the candidates (specification 6.4.19).
  /// </summary>
  /// <remarks>
  /// Eighth-sample accuracy is only available when the predicted vector is small. A large predicted
  /// vector means fast motion, where an eighth of a sample is below what the picture can show, so the
  /// format spends the bit elsewhere — which is why the precision of the difference depends on the
  /// candidate rather than on the frame alone.
  /// </remarks>
  private void _ReadMotionVector(int list) {
    this._useHighPrecision =
      this._header.AllowHighPrecisionMotionVectors
      && _UsesHighPrecision(this._bestMotionVector[list * 2], this._bestMotionVector[list * 2 + 1]);

    var joint = this._reader.ReadTree(Vp9Trees.MotionVectorJoint, this._probabilities.MotionVectorJoint);
    ++this._counts.MotionVectorJoint[joint];

    var row = joint is MV_JOINT_HZVNZ or MV_JOINT_HNZVNZ ? this._ReadMotionVectorComponent(0) : 0;
    var column = joint is MV_JOINT_HNZVZ or MV_JOINT_HNZVNZ ? this._ReadMotionVectorComponent(1) : 0;

    this._motionVector[list * 2] = this._bestMotionVector[list * 2] + row;
    this._motionVector[list * 2 + 1] = this._bestMotionVector[list * 2 + 1] + column;
  }

  private static bool _UsesHighPrecision(int row, int column)
    => (Math.Abs(row) >> 3) < COMPANDED_MVREF_THRESH && (Math.Abs(column) >> 3) < COMPANDED_MVREF_THRESH;

  private int _ReadMotionVectorComponent(int component) {
    var sign = this._reader.ReadBool(this._probabilities.MotionVectorSign[component]);
    ++this._counts.MotionVectorSign[component * 2 + sign];

    var magnitudeClass = this._reader.ReadTree(
      Vp9Trees.MotionVectorClass,
      this._probabilities.MotionVectorClass.AsSpan(component * (MV_CLASSES - 1), MV_CLASSES - 1));
    ++this._counts.MotionVectorClass[component * MV_CLASSES + magnitudeClass];

    int magnitude;
    if (magnitudeClass == MV_CLASS_0) {
      var bit = this._reader.ReadBool(this._probabilities.MotionVectorClass0Bit[component]);
      ++this._counts.MotionVectorClass0Bit[component * CLASS0_SIZE + bit];

      var fraction = this._reader.ReadTree(
        Vp9Trees.MotionVectorFraction,
        this._probabilities.MotionVectorClass0Fraction.AsSpan(
          (component * CLASS0_SIZE + bit) * (MV_FR_SIZE - 1), MV_FR_SIZE - 1));
      ++this._counts.MotionVectorClass0Fraction[(component * CLASS0_SIZE + bit) * MV_FR_SIZE + fraction];

      var high = this._ReadHighPrecisionBit(this._probabilities.MotionVectorClass0HighPrecision[component]);
      ++this._counts.MotionVectorClass0HighPrecision[component * 2 + high];

      magnitude = ((bit << 3) | (fraction << 1) | high) + 1;
    } else {
      var bits = 0;
      for (var i = 0; i < magnitudeClass; ++i) {
        var bit = this._reader.ReadBool(this._probabilities.MotionVectorBits[component * MV_OFFSET_BITS + i]);
        ++this._counts.MotionVectorBits[(component * MV_OFFSET_BITS + i) * 2 + bit];
        bits |= bit << i;
      }

      var fraction = this._reader.ReadTree(
        Vp9Trees.MotionVectorFraction,
        this._probabilities.MotionVectorFraction.AsSpan(component * (MV_FR_SIZE - 1), MV_FR_SIZE - 1));
      ++this._counts.MotionVectorFraction[component * MV_FR_SIZE + fraction];

      var high = this._ReadHighPrecisionBit(this._probabilities.MotionVectorHighPrecision[component]);
      ++this._counts.MotionVectorHighPrecision[component * 2 + high];

      magnitude = (CLASS0_SIZE << (magnitudeClass + 2)) + (((bits << 3) | (fraction << 1) | high) + 1);
    }

    return sign != 0 ? -magnitude : magnitude;
  }

  /// <summary>
  /// Reads the eighth-sample bit, or answers the one it is defined to be when the stream does not
  /// carry it (specification 9.3.1).
  /// </summary>
  /// <remarks>
  /// One when it is not coded, and still counted as a one. That is not an oversight in the counting:
  /// the probability of this bool is adapted only when the frame allows eighth-sample vectors at all,
  /// so the counts of the frames that do not are never read.
  /// </remarks>
  private int _ReadHighPrecisionBit(byte probability) {
    return this._useHighPrecision ? this._reader.ReadBool(probability) : 1;
  }
}
