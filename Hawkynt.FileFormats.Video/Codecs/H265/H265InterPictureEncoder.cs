using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Codes one predicted picture — the slice data of a P slice, ITU-T H.265 clause 7.3.8.
/// </summary>
/// <remarks>
/// A predicted picture states, for each block, where in an already-decoded picture it came from and
/// how the samples differ from what was there. That is the whole of the saving over coding the
/// picture again: a scene that moves without changing costs a vector and nothing else.
/// <para/>
/// <b>Why this implements <see cref="IH265MotionContext"/>.</b> A motion vector is never sent whole —
/// the bitstream carries a difference against a predictor derived from the neighbouring blocks, or
/// an index into a list of candidates derived the same way. So the encoder cannot say what to write
/// until it knows what the decoder will predict, and the only safe way to know that is to run the
/// decoder's own derivation. <see cref="H265MotionPrediction"/> and
/// <see cref="H265MotionCompensation"/> therefore run here unchanged, over the picture this is
/// building.
/// <para/>
/// <b>The reconstruction is built as it goes</b>, and it has to be: the next picture predicts from
/// what a decoder will have, not from what the source looked like. Every block is predicted,
/// residual-coded, and then put back together from the prediction and the dequantised residual —
/// the same arithmetic the decoder will do. Anything else accumulates as drift over a group of
/// pictures, which looks like the picture slowly dissolving rather than like a bug.
/// <para/>
/// <b>What it does not do.</b> One prediction unit per coding unit, one reference, integer-sample
/// motion, and no rate-distortion search over coding unit sizes. Each of those costs compression
/// and none of them costs correctness: the syntax is ordinary Main-profile HEVC and any decoder
/// reads it.
/// </remarks>
internal sealed class H265InterPictureEncoder : IH265MotionContext {

  /// <summary>The smallest block the motion field is stored at, as a base-two logarithm.</summary>
  private const int _LOG2_MIN_BLOCK = 2;

  private const int _MIN_BLOCK = 1 << _LOG2_MIN_BLOCK;

  /// <summary>How far the search looks for a block, in whole samples, in each direction.</summary>
  private const int _SEARCH_RANGE = 16;

  /// <summary>Motion vectors are in quarter samples — clause 8.5.3.2.</summary>
  private const int _QUARTER_SAMPLE = 4;

  private readonly H265SequenceParameterSet _sps;
  private readonly H265PictureParameterSet _pps;
  private readonly H265SliceHeader _header;
  private readonly H265Picture _reference;
  private readonly H265Picture _source;
  private readonly H265Picture _picture;
  private readonly int _qp;
  private readonly int _log2CtbSize;
  private readonly int _log2CuSize;
  private readonly int _blocksAcross;
  private readonly int _blocksDown;
  private readonly int _minBlocksPerCtbSide;

  private readonly bool[] _skipped;
  private readonly byte[] _codingTreeDepth;

  private int _cuX;
  private int _cuY;
  private int _cuLog2Size;

  /// <param name="source">The picture to code, already converted to the coded sample format.</param>
  /// <param name="reference">The reconstruction the decoder will predict from.</param>
  /// <param name="qp">The luma quantiser this slice codes at.</param>
  /// <param name="log2CuSize">
  /// The coding unit size this writer uses, as a base-two logarithm. Smaller units follow motion
  /// more closely and cost more syntax; this writer uses one size everywhere rather than choosing.
  /// </param>
  internal H265InterPictureEncoder(
    H265SequenceParameterSet sps,
    H265PictureParameterSet pps,
    H265SliceHeader header,
    H265Picture source,
    H265Picture reference,
    int qp,
    int log2CuSize = 4) {
    this._sps = sps;
    this._pps = pps;
    this._header = header;
    this._source = source;
    this._reference = reference;
    this._qp = qp;
    this._log2CtbSize = sps.CtbLog2SizeY;
    this._log2CuSize = Math.Clamp(log2CuSize, sps.MinCbLog2SizeY, sps.CtbLog2SizeY);

    this._picture = new(sps.Width, sps.Height, _LOG2_MIN_BLOCK) {
      PictureOrderCount = source.PictureOrderCount,
      Marking = H265ReferenceMarking.ShortTerm,
    };

    this._blocksAcross = (sps.Width + _MIN_BLOCK - 1) >> _LOG2_MIN_BLOCK;
    this._blocksDown = (sps.Height + _MIN_BLOCK - 1) >> _LOG2_MIN_BLOCK;
    this._minBlocksPerCtbSide = 1 << (this._log2CtbSize - _LOG2_MIN_BLOCK);

    this._skipped = new bool[this._blocksAcross * this._blocksDown];
    this._codingTreeDepth = new byte[this._blocksAcross * this._blocksDown];
  }

  /// <summary>The reconstruction, which is what the next picture predicts from.</summary>
  internal H265Picture Reconstruction => this._picture;

  // ── the motion context the shared derivation reads ──────────────────────────

  public H265SliceHeader Header => this._header;

  public H265SequenceParameterSet Sps => this._sps;

  public H265PictureParameterSet Pps => this._pps;

  public H265Picture Picture => this._picture;

  public IReadOnlyList<H265Picture> ReferenceList(int list) => list == 0 ? [this._reference] : [];

  public H265Picture? CollocatedPicture => null;

  public int BlockIndexAt(int x, int y) => (y >> _LOG2_MIN_BLOCK) * this._blocksAcross + (x >> _LOG2_MIN_BLOCK);

  public H265MotionInfo MotionAt(int index) {
    var motion = this._picture.Motion;
    return new() {
      PredictL0 = motion.PredictionFlagL0[index],
      PredictL1 = motion.PredictionFlagL1[index],
      RefIdxL0 = motion.RefIdxL0[index],
      RefIdxL1 = motion.RefIdxL1[index],
      MvL0X = motion.MvL0X[index],
      MvL0Y = motion.MvL0Y[index],
      MvL1X = motion.MvL1X[index],
      MvL1Y = motion.MvL1Y[index],
    };
  }

  public bool IsIntraAt(int index) => this._picture.IsIntraBlock[index];

  /// <summary>
  /// Whether a position may be read from while coding the block at another — clause 6.4.1.
  /// </summary>
  /// <remarks>
  /// One slice and one tile, so the only question left is whether the position has been coded yet,
  /// which the z-scan order answers.
  /// </remarks>
  public bool IsAvailableAt(int x0, int y0, int x, int y) {
    if (x < 0 || y < 0 || x >= this._sps.Width || y >= this._sps.Height)
      return false;

    return this._ZScanAddress(x, y) < this._ZScanAddress(x0, y0);
  }

  public int CodingBlockX => this._cuX;

  public int CodingBlockY => this._cuY;

  public int CodingBlockSize => 1 << this._cuLog2Size;

  public H265PartitionMode CodingBlockPartitionMode => H265PartitionMode.Square;

  // ── coding ──────────────────────────────────────────────────────────────────

  /// <summary>Codes the whole picture and returns the arithmetic-coded slice data.</summary>
  internal byte[] Encode() {
    var contexts = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(contexts, this._header.SliceType == H265SliceType.P ? 1 : 0, this._header.SliceQpY);

    var cabac = new H265CabacEncoder(contexts);

    var across = (this._sps.Width + (1 << this._log2CtbSize) - 1) >> this._log2CtbSize;
    var down = (this._sps.Height + (1 << this._log2CtbSize) - 1) >> this._log2CtbSize;
    var total = across * down;

    for (var index = 0; index < total; ++index) {
      var x = (index % across) << this._log2CtbSize;
      var y = (index / across) << this._log2CtbSize;
      this._EncodeCodingQuadtree(cabac, x, y, this._log2CtbSize, 0);
      cabac.EncodeTerminate(index + 1 == total ? 1 : 0);
    }

    return cabac.Finish();
  }

  private void _EncodeCodingQuadtree(H265CabacEncoder cabac, int x0, int y0, int log2CbSize, int depth) {
    var size = 1 << log2CbSize;
    var fits = x0 + size <= this._sps.Width && y0 + size <= this._sps.Height;
    var split = log2CbSize > this._log2CuSize;

    if (log2CbSize > this._sps.MinCbLog2SizeY && fits)
      cabac.EncodeBin(
        H265CabacContexts.SPLIT_CU_FLAG + this._SplitContext(x0, y0, depth), split || !fits ? 1 : 0);

    // A coding block that hangs over the edge of the picture is split until what is left fits, and
    // the flag saying so is inferred rather than written.
    if (!fits)
      split = log2CbSize > this._sps.MinCbLog2SizeY;

    if (!split) {
      this._EncodeCodingUnit(cabac, x0, y0, log2CbSize, depth);
      return;
    }

    var half = size >> 1;
    foreach (var (dx, dy) in new[] { (0, 0), (half, 0), (0, half), (half, half) }) {
      var x = x0 + dx;
      var y = y0 + dy;
      if (x < this._sps.Width && y < this._sps.Height)
        this._EncodeCodingQuadtree(cabac, x, y, log2CbSize - 1, depth + 1);
    }
  }

  private int _SplitContext(int x0, int y0, int depth) {
    var context = 0;
    if (this.IsAvailableAt(x0, y0, x0 - 1, y0) && this._codingTreeDepth[this.BlockIndexAt(x0 - 1, y0)] > depth)
      ++context;
    if (this.IsAvailableAt(x0, y0, x0, y0 - 1) && this._codingTreeDepth[this.BlockIndexAt(x0, y0 - 1)] > depth)
      ++context;

    return context;
  }

  private int _SkipContext(int x0, int y0) {
    var context = 0;
    if (this.IsAvailableAt(x0, y0, x0 - 1, y0) && this._skipped[this.BlockIndexAt(x0 - 1, y0)])
      ++context;
    if (this.IsAvailableAt(x0, y0, x0, y0 - 1) && this._skipped[this.BlockIndexAt(x0, y0 - 1)])
      ++context;

    return context;
  }

  private void _EncodeCodingUnit(H265CabacEncoder cabac, int x0, int y0, int log2CbSize, int depth) {
    var size = 1 << log2CbSize;

    this._cuX = x0;
    this._cuY = y0;
    this._cuLog2Size = log2CbSize;
    this._Fill(this._codingTreeDepth, x0, y0, size, (byte)depth);

    var chosen = this._Search(x0, y0, size);

    // The merge candidate costs one bin where an explicit vector costs several, so it is taken
    // whenever it names the same motion the search settled on.
    var merged = H265MotionPrediction.DeriveMerge(this, x0, y0, size, size, 0, 0);
    var mergeMatches = merged.PredictL0 && !merged.PredictL1
                       && merged.RefIdxL0 == 0 && merged.MvL0X == chosen.X && merged.MvL0Y == chosen.Y;

    var motion = H265MotionInfo.None;
    motion.Set(0, true, 0, chosen.X, chosen.Y);

    this._Predict(x0, y0, size, motion);
    var levels = this._Quantise(x0, y0, log2CbSize, out var anyLuma, out var anyCb, out var anyCr);
    var anyResidual = anyLuma || anyCb || anyCr;

    // Skipping is exactly this: the merge candidate, and nothing else at all.
    var skip = mergeMatches && !anyResidual;
    cabac.EncodeBin(H265CabacContexts.CU_SKIP_FLAG + this._SkipContext(x0, y0), skip ? 1 : 0);
    this._Fill(this._skipped, x0, y0, size, skip);

    if (skip) {
      this._Store(x0, y0, size, motion);
      this._Reconstruct(x0, y0, log2CbSize, levels, false, false, false);
      return;
    }

    cabac.EncodeBin(H265CabacContexts.PRED_MODE_FLAG, 0); // inter
    cabac.EncodeBin(H265CabacContexts.PART_MODE, 1);      // PART_2Nx2N

    if (mergeMatches) {
      cabac.EncodeBin(H265CabacContexts.MERGE_FLAG, 1);
      this._WriteMergeIndex(cabac, 0);
    } else {
      cabac.EncodeBin(H265CabacContexts.MERGE_FLAG, 0);
      this._WriteExplicitMotion(cabac, x0, y0, size, chosen);
    }

    this._Store(x0, y0, size, motion);

    // rqt_root_cbf is only written where the decoder would not infer it, which is everywhere except
    // a whole-unit merge — and there it is inferred as present, so a merged unit always carries a
    // transform tree even when everything in it is zero.
    if (!mergeMatches) {
      cabac.EncodeBin(H265CabacContexts.RQT_ROOT_CBF, anyResidual ? 1 : 0);
      if (!anyResidual) {
        this._Reconstruct(x0, y0, log2CbSize, levels, false, false, false);
        return;
      }
    }

    this._EncodeTransformTree(cabac, x0, y0, log2CbSize, levels, anyLuma, anyCb, anyCr);
    this._Reconstruct(x0, y0, log2CbSize, levels, anyLuma, anyCb, anyCr);
  }

  private void _WriteMergeIndex(H265CabacEncoder cabac, int index) {
    if (this._header.MaxNumMergeCand <= 1)
      return;

    cabac.EncodeBin(H265CabacContexts.MERGE_IDX, index > 0 ? 1 : 0);
    if (index == 0)
      return;

    for (var i = 1; i < index; ++i)
      cabac.EncodeBypass(1);

    if (index < this._header.MaxNumMergeCand - 1)
      cabac.EncodeBypass(0);
  }

  /// <summary>Writes a vector as the difference from whichever of the two predictors is closer.</summary>
  private void _WriteExplicitMotion(H265CabacEncoder cabac, int x0, int y0, int size, (int X, int Y) chosen) {
    var best = 0;
    var bestCost = int.MaxValue;
    var bestDeltaX = 0;
    var bestDeltaY = 0;

    for (var flag = 0; flag < 2; ++flag) {
      var predictor = H265MotionPrediction.DerivePredictor(this, x0, y0, size, size, 0, 0, 0, flag);
      var deltaX = chosen.X - predictor.X;
      var deltaY = chosen.Y - predictor.Y;
      var cost = Math.Abs(deltaX) + Math.Abs(deltaY);
      if (cost >= bestCost)
        continue;

      best = flag;
      bestCost = cost;
      bestDeltaX = deltaX;
      bestDeltaY = deltaY;
    }

    _WriteMotionVectorDifference(cabac, bestDeltaX, bestDeltaY);
    cabac.EncodeBin(H265CabacContexts.MVP_FLAG, best);
  }

  /// <summary>
  /// The motion vector difference — clause 7.3.8.9.
  /// </summary>
  /// <remarks>
  /// Two context-coded flags say whether each component is non-zero and whether it exceeds one; the
  /// rest is an exponential-Golomb tail and a sign, all bypassed. Both components state their
  /// greater-than-zero flags before either states anything else, which is what lets the two contexts
  /// see a run of like decisions.
  /// </remarks>
  private static void _WriteMotionVectorDifference(H265CabacEncoder cabac, int deltaX, int deltaY) {
    var absoluteX = Math.Abs(deltaX);
    var absoluteY = Math.Abs(deltaY);

    cabac.EncodeBin(H265CabacContexts.ABS_MVD_GREATER0_FLAG, absoluteX > 0 ? 1 : 0);
    cabac.EncodeBin(H265CabacContexts.ABS_MVD_GREATER0_FLAG, absoluteY > 0 ? 1 : 0);

    if (absoluteX > 0)
      cabac.EncodeBin(H265CabacContexts.ABS_MVD_GREATER1_FLAG, absoluteX > 1 ? 1 : 0);
    if (absoluteY > 0)
      cabac.EncodeBin(H265CabacContexts.ABS_MVD_GREATER1_FLAG, absoluteY > 1 ? 1 : 0);

    _WriteMotionVectorComponent(cabac, deltaX);
    _WriteMotionVectorComponent(cabac, deltaY);
  }

  private static void _WriteMotionVectorComponent(H265CabacEncoder cabac, int delta) {
    var magnitude = Math.Abs(delta);
    if (magnitude == 0)
      return;

    if (magnitude > 1)
      _WriteExponentialGolomb(cabac, magnitude - 2);

    cabac.EncodeBypass(delta < 0 ? 1 : 0);
  }

  /// <summary>An order-one exponential-Golomb code, every bin bypassed — clause 9.3.3.3.</summary>
  private static void _WriteExponentialGolomb(H265CabacEncoder cabac, int value) {
    var order = 1;
    var remaining = value;

    while (remaining >= 1 << order) {
      cabac.EncodeBypass(1);
      remaining -= 1 << order;
      ++order;
    }

    cabac.EncodeBypass(0);
    cabac.EncodeBypassBits(remaining, order);
  }

  private void _EncodeTransformTree(
    H265CabacEncoder cabac, int x0, int y0, int log2CbSize, Levels levels, bool anyLuma, bool anyCb, bool anyCr) {
    // One transform block per coding unit: the sequence allows no inter transform hierarchy, and a
    // coding unit this size is within the largest transform block, so no split flag is written and
    // the decoder infers no split.
    cabac.EncodeBin(H265CabacContexts.CBF_CHROMA, anyCb ? 1 : 0);
    cabac.EncodeBin(H265CabacContexts.CBF_CHROMA, anyCr ? 1 : 0);

    // Where neither chrominance block carries anything the luminance flag is inferred to one, which
    // is consistent because a transform tree is only written at all when something is coded.
    if (anyCb || anyCr)
      cabac.EncodeBin(H265CabacContexts.CBF_LUMA + 1, anyLuma ? 1 : 0);

    if (anyLuma)
      H265ResidualWriter.Encode(cabac, levels.Luma, log2CbSize, 0, -1, this._sps.ChromaArrayType);

    if (anyCb)
      H265ResidualWriter.Encode(cabac, levels.Cb, log2CbSize - 1, 1, -1, this._sps.ChromaArrayType);

    if (anyCr)
      H265ResidualWriter.Encode(cabac, levels.Cr, log2CbSize - 1, 2, -1, this._sps.ChromaArrayType);
  }

  // ── prediction, residual and reconstruction ─────────────────────────────────

  /// <summary>
  /// Searches the reference for the block that costs the fewest bits to correct.
  /// </summary>
  /// <remarks>
  /// The zero vector is the incumbent rather than the first candidate scanned. A block whose content
  /// did not move has to come out with the zero vector and not with whichever equally good
  /// displacement the scan happened to reach first — the zero vector is nearly always what the
  /// predictor already says, so it is the one that costs nothing to state.
  /// </remarks>
  private (int X, int Y) _Search(int x0, int y0, int size) {
    var width = Math.Min(size, this._sps.Width - x0);
    var height = Math.Min(size, this._sps.Height - y0);

    var best = (X: 0, Y: 0);
    var bestCost = this._MatchCost(x0, y0, width, height, 0, 0, int.MaxValue);

    for (var dy = -_SEARCH_RANGE; dy <= _SEARCH_RANGE; ++dy)
      for (var dx = -_SEARCH_RANGE; dx <= _SEARCH_RANGE; ++dx) {
        if (dx == 0 && dy == 0)
          continue;

        var cost = this._MatchCost(x0, y0, width, height, dx, dy, bestCost);
        if (cost >= bestCost)
          continue;

        bestCost = cost;
        best = (dx * _QUARTER_SAMPLE, dy * _QUARTER_SAMPLE);
      }

    return best;
  }

  private int _MatchCost(int x0, int y0, int width, int height, int dx, int dy, int ceiling) {
    var cost = 0;
    for (var y = 0; y < height; ++y) {
      var sourceRow = (y0 + y) * this._sps.Width;
      var referenceRow = Math.Clamp(y0 + y + dy, 0, this._sps.Height - 1) * this._sps.Width;

      for (var x = 0; x < width; ++x) {
        var referenceX = Math.Clamp(x0 + x + dx, 0, this._sps.Width - 1);
        cost += Math.Abs(this._source.Luma[sourceRow + x0 + x] - this._reference.Luma[referenceRow + referenceX]);
      }

      if (cost >= ceiling)
        return int.MaxValue;
    }

    return cost;
  }

  private void _Predict(int x0, int y0, int size, in H265MotionInfo motion)
    => H265MotionCompensation.Predict(this, x0, y0, size, size, motion);

  private readonly record struct Levels(int[] Luma, int[] Cb, int[] Cr);

  /// <summary>Transforms and quantises what the prediction did not account for.</summary>
  private Levels _Quantise(int x0, int y0, int log2CbSize, out bool anyLuma, out bool anyCb, out bool anyCr) {
    var levels = new Levels(
      this._Residual(this._source.Luma, this._picture.Luma, this._sps.Width, x0, y0, log2CbSize),
      this._Residual(this._source.Cb, this._picture.Cb, this._picture.ChromaWidth, x0 >> 1, y0 >> 1, log2CbSize - 1),
      this._Residual(this._source.Cr, this._picture.Cr, this._picture.ChromaWidth, x0 >> 1, y0 >> 1, log2CbSize - 1));

    H265ForwardTransform.Forward(levels.Luma, log2CbSize, sine: false, this._sps.BitDepthLuma);
    H265ForwardTransform.Quantise(levels.Luma, log2CbSize, this._qp, this._sps.BitDepthLuma, intra: false);

    var chromaQp = H265Dequantiser.ChromaQp(Math.Clamp(this._qp, 0, 51));
    foreach (var plane in new[] { levels.Cb, levels.Cr }) {
      H265ForwardTransform.Forward(plane, log2CbSize - 1, sine: false, this._sps.BitDepthChroma);
      H265ForwardTransform.Quantise(plane, log2CbSize - 1, chromaQp, this._sps.BitDepthChroma, intra: false);
    }

    anyLuma = _Any(levels.Luma);
    anyCb = _Any(levels.Cb);
    anyCr = _Any(levels.Cr);
    return levels;
  }

  private int[] _Residual(ushort[] source, ushort[] predicted, int stride, int x0, int y0, int log2Size) {
    var size = 1 << log2Size;
    var block = new int[size * size];

    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        var at = (y0 + y) * stride + x0 + x;
        block[(y << log2Size) + x] = source[at] - predicted[at];
      }

    return block;
  }

  /// <summary>Puts the block back together the way the decoder will: prediction plus residual.</summary>
  private void _Reconstruct(
    int x0, int y0, int log2CbSize, Levels levels, bool anyLuma, bool anyCb, bool anyCr) {
    if (anyLuma)
      this._AddResidual(
        this._picture.Luma, this._sps.Width, x0, y0, log2CbSize, levels.Luma, this._qp, this._sps.BitDepthLuma, 0);

    var chromaQp = H265Dequantiser.ChromaQp(Math.Clamp(this._qp, 0, 51));
    if (anyCb)
      this._AddResidual(
        this._picture.Cb, this._picture.ChromaWidth, x0 >> 1, y0 >> 1, log2CbSize - 1, levels.Cb,
        chromaQp, this._sps.BitDepthChroma, 1);

    if (anyCr)
      this._AddResidual(
        this._picture.Cr, this._picture.ChromaWidth, x0 >> 1, y0 >> 1, log2CbSize - 1, levels.Cr,
        chromaQp, this._sps.BitDepthChroma, 2);
  }

  private void _AddResidual(
    ushort[] plane, int stride, int x0, int y0, int log2Size, int[] levels, int qp, int bitDepth, int matrixId) {
    var block = (int[])levels.Clone();
    H265Dequantiser.Scale(block, log2Size, qp, bitDepth, this._pps.ScalingList, matrixId);
    H265Transform.Inverse(block, log2Size, sine: false, bitDepth);

    var size = 1 << log2Size;
    var maximum = (1 << bitDepth) - 1;
    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        var at = (y0 + y) * stride + x0 + x;
        plane[at] = (ushort)Math.Clamp(plane[at] + block[(y << log2Size) + x], 0, maximum);
      }
  }

  private void _Store(int x0, int y0, int size, in H265MotionInfo motion) {
    var field = this._picture.Motion;
    for (var y = y0; y < Math.Min(y0 + size, this._sps.Height); y += _MIN_BLOCK)
      for (var x = x0; x < Math.Min(x0 + size, this._sps.Width); x += _MIN_BLOCK) {
        var index = this.BlockIndexAt(x, y);
        field.PredictionFlagL0[index] = motion.PredictL0;
        field.PredictionFlagL1[index] = motion.PredictL1;
        field.RefIdxL0[index] = motion.RefIdxL0;
        field.RefIdxL1[index] = motion.RefIdxL1;
        field.MvL0X[index] = motion.MvL0X;
        field.MvL0Y[index] = motion.MvL0Y;
        field.MvL1X[index] = motion.MvL1X;
        field.MvL1Y[index] = motion.MvL1Y;
        this._picture.IsIntraBlock[index] = false;
      }
  }

  private void _Fill<T>(T[] target, int x0, int y0, int size, T value) {
    for (var y = y0; y < Math.Min(y0 + size, this._sps.Height); y += _MIN_BLOCK)
      for (var x = x0; x < Math.Min(x0 + size, this._sps.Width); x += _MIN_BLOCK)
        target[this.BlockIndexAt(x, y)] = value;
  }

  private long _ZScanAddress(int x, int y) {
    var across = (this._sps.Width + (1 << this._log2CtbSize) - 1) >> this._log2CtbSize;
    var ctb = (long)((y >> this._log2CtbSize) * across + (x >> this._log2CtbSize));

    var withinX = (x >> _LOG2_MIN_BLOCK) & (this._minBlocksPerCtbSide - 1);
    var withinY = (y >> _LOG2_MIN_BLOCK) & (this._minBlocksPerCtbSide - 1);

    var morton = 0L;
    for (var bit = 0; (1 << bit) < this._minBlocksPerCtbSide; ++bit)
      morton |= (long)((withinX >> bit) & 1) << (bit << 1)
                | (long)((withinY >> bit) & 1) << ((bit << 1) + 1);

    return ctb * this._minBlocksPerCtbSide * this._minBlocksPerCtbSide + morton;
  }

  private static bool _Any(int[] block) {
    foreach (var value in block)
      if (value != 0)
        return true;

    return false;
  }
}
