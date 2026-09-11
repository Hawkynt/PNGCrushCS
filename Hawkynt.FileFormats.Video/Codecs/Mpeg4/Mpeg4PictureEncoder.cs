using System;
using System.IO;
using FileFormat.Codecs.MsMpeg4;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>Writes one rectangular MPEG-4 Part 2 I-, P- or B-VOP and reconstructs what a decoder will use.</summary>
internal sealed class Mpeg4PictureEncoder {

  /// <summary>INTER: predicted, one vector for the whole macroblock (Table 6-19).</summary>
  private const int _INTER_MACROBLOCK = 0;

  /// <summary>Macroblock type 3 is intra without a DQUANT field (Tables B-6 and B-7).</summary>
  private const int _INTRA_MACROBLOCK = 3;

  /// <summary>The largest positive or negative coefficient the third escape's signed twelve bits state.</summary>
  private const int _MAX_LEVEL = 2047;

  /// <summary>How far a motion search looks, in whole samples, around the predicted vector.</summary>
  /// <remarks>
  /// vop_fcode_forward is one here, so 7.6.3 gives a vector the range [-32, 31] half-samples, which
  /// is [-16, 15.5] whole ones. Fifteen keeps every candidate inside that: the range is not merely
  /// what a vector costs to state but what it means, because a vector outside it is wrapped by a
  /// whole range on the way back and returns as a different vector.
  /// </remarks>
  private const int _SEARCH_RANGE = 15;

  /// <summary>The half-sample range vop_fcode_forward 1 permits.</summary>
  private const int _VECTOR_LOW = -32;
  private const int _VECTOR_HIGH = 31;
  private const int _VECTOR_RANGE = 64;

  private readonly Mpeg4Frame _source;
  private readonly Mpeg4Frame? _forwardReference;
  private readonly Mpeg4Frame? _backwardReference;
  private readonly Mpeg4Frame _target;
  private readonly Mpeg4AnchorMotion _motion;

  /// <summary>
  /// The motion of the anchor that follows a B-VOP, which decides where that B-VOP carries bits.
  /// </summary>
  /// <remarks>
  /// A B macroblock whose co-located macroblock in the following anchor was not coded is not in the
  /// bitstream at all (6.3.7.2): it is reconstructed from the anchors without anything being said
  /// about it. That is a rule about where syntax elements are, not only about what they mean, so an
  /// encoder that writes such a macroblock anyway puts every macroblock after it in the wrong place.
  /// </remarks>
  private readonly Mpeg4AnchorMotion? _anchorMotion;

  /// <summary>
  /// A B-VOP's running vector predictors, one pair per direction.
  /// </summary>
  /// <remarks>
  /// 7.6.2 predicts a B-VOP's vector from the last vector of the same direction rather than from a
  /// median of neighbours, and only a macroblock that carries a vector of that direction moves it --
  /// a macroblock coded the other way round, or not carried at all, leaves the predictor where it
  /// was. A forward-only macroblock therefore advances the forward predictor and not the backward
  /// one, which is the part that a median-shaped implementation gets wrong.
  /// </remarks>
  private int _forwardPredictorX;
  private int _forwardPredictorY;
  private int _backwardPredictorX;
  private int _backwardPredictorY;
  private readonly int _codingType;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _quantiser;
  private readonly int _timeIncrementResolution;
  private readonly int _timeIncrement;
  private readonly int _moduloSeconds;
  private readonly int _timeIncrementBits;
  private readonly int _roundingType;
  private readonly MsMpeg4BitWriter _writer = new();
  private readonly Mpeg4IntraPrediction _prediction;

  internal Mpeg4PictureEncoder(
    Mpeg4Frame source,
    Mpeg4Frame? forwardReference,
    Mpeg4Frame? backwardReference,
    int codingType,
    int width,
    int height,
    int macroblockWidth,
    int macroblockHeight,
    int quantiser,
    int timeIncrementResolution,
    int timeIncrement,
    int moduloSeconds,
    int roundingType,
    Mpeg4AnchorMotion? anchorMotion = null) {
    this._source = source;
    this._forwardReference = forwardReference;
    this._backwardReference = backwardReference;
    this._codingType = codingType;
    this._width = width;
    this._height = height;
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;
    this._quantiser = quantiser;
    this._timeIncrementResolution = timeIncrementResolution;
    this._timeIncrement = timeIncrement;
    this._moduloSeconds = moduloSeconds;
    this._timeIncrementBits = _BitsFor(timeIncrementResolution);
    this._roundingType = roundingType;
    this._anchorMotion = anchorMotion;
    this._prediction = new(macroblockWidth, macroblockHeight);

    var count = checked(macroblockWidth * macroblockHeight);
    this._target = new(macroblockWidth, macroblockHeight);
    this._motion = new(count);

    if (codingType != Mpeg4VideoObjectPlane.IntraCoded && forwardReference == null)
      throw new ArgumentNullException(nameof(forwardReference), "A predicted MPEG-4 picture needs its preceding anchor.");

    if (codingType == Mpeg4VideoObjectPlane.BidirectionallyCoded && backwardReference == null)
      throw new ArgumentNullException(nameof(backwardReference), "A bidirectionally coded MPEG-4 picture needs its following anchor.");
  }

  /// <summary>The picture exactly as later motion compensation must see it.</summary>
  internal Mpeg4Frame Reconstructed => this._target;

  /// <summary>The motion state a later direct-mode B-VOP would need from this anchor.</summary>
  internal Mpeg4AnchorMotion Motion => this._motion;

  internal byte[] Encode() {
    this._WriteVideoObjectLayer();
    this._WriteVideoObjectPlane();

    var macroblocks = checked(this._macroblockWidth * this._macroblockHeight);
    for (var address = 0; address < macroblocks; ++address)
      switch (this._codingType) {
        case Mpeg4VideoObjectPlane.IntraCoded:
          this._WriteIntraMacroblock(address, predictedPicture: false);
          break;

        case Mpeg4VideoObjectPlane.PredictiveCoded:
          this._WritePredictedMacroblock(address);
          break;

        case Mpeg4VideoObjectPlane.BidirectionallyCoded:
          this._WriteBidirectionalMacroblock(address);
          break;

        default:
          throw new InvalidOperationException($"MPEG-4 picture type {this._codingType} is not encodable here.");
      }

    this._target.PadBorders();
    return this._writer.ToArray();
  }

  // ============================================================================================
  // Headers — ISO/IEC 14496-2, 6.2.3 and 6.2.5
  // ============================================================================================

  /// <summary>
  /// The fixed set of coding tools this encoder uses: Advanced Simple rectangular 8-bit 4:2:0,
  /// half-sample motion, H.263 quantisation, progressive, no sprites, resync markers, partitioning or
  /// scalability. Advanced Simple is named because B-VOPs are outside the Simple object type.
  /// </summary>
  private void _WriteVideoObjectLayer() {
    this._StartCode(Mpeg4StartCode.FirstVideoObjectLayer);
    this._writer.Write(0, 1);                              // random_accessible_vol
    this._writer.Write(17, 8);                             // Advanced Simple visual object type
    this._writer.Write(0, 1);                              // is_object_layer_identifier: version 1
    this._writer.Write(1, 4);                              // aspect_ratio_info: square pixels
    // vol_control_parameters carries low_delay, and low_delay is the difference between a stream that
    // reorders pictures and one that promises never to. Leaving the block out does not leave the
    // question open: a decoder that finds no low_delay takes the stream at its word as low-delay, and
    // then meets a B-VOP it was told could not exist. FFmpeg says so in as many words -- "low_delay
    // flag set incorrectly" -- and refuses the picture. Because this encoder writes B-VOPs, the block
    // is present and states the answer.
    this._writer.Write(1, 1);                              // vol_control_parameters
    this._writer.Write(1, 2);                              // chroma_format: 4:2:0
    this._writer.Write(0, 1);                              // low_delay: pictures are reordered
    this._writer.Write(0, 1);                              // vbv_parameters: none
    this._writer.Write(0, 2);                              // video_object_layer_shape: rectangular
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(this._timeIncrementResolution, 16);
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(0, 1);                              // fixed_vop_rate
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(this._width, 13);
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(this._height, 13);
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(0, 1);                              // interlaced
    this._writer.Write(1, 1);                              // obmc_disable
    this._writer.Write(0, 1);                              // sprite_enable, verid 1
    this._writer.Write(0, 1);                              // not_8_bit
    this._writer.Write(0, 1);                              // quant_type: H.263
    this._writer.Write(1, 1);                              // complexity_estimation_disable
    this._writer.Write(1, 1);                              // resync_marker_disable
    this._writer.Write(0, 1);                              // data_partitioned
    this._writer.Write(0, 1);                              // scalability
    this._NextStartCode();
  }

  private void _WriteVideoObjectPlane() {
    this._StartCode(Mpeg4StartCode.VideoObjectPlane);
    this._writer.Write(this._codingType, 2);

    for (var second = 0; second < this._moduloSeconds; ++second)
      this._writer.Write(1, 1);

    this._writer.Write(0, 1);                              // modulo_time_base terminator
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(this._timeIncrement, this._timeIncrementBits);
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(1, 1);                              // vop_coded

    if (this._codingType == Mpeg4VideoObjectPlane.PredictiveCoded)
      this._writer.Write(this._roundingType, 1);

    this._writer.Write(0, 3);                              // intra_dc_vlc_thr: always use DC VLC
    this._writer.Write(this._quantiser, 5);                // vop_quant

    if (this._codingType != Mpeg4VideoObjectPlane.IntraCoded)
      this._writer.Write(1, 3);                            // vop_fcode_forward

    if (this._codingType == Mpeg4VideoObjectPlane.BidirectionallyCoded)
      this._writer.Write(1, 3);                            // vop_fcode_backward
  }

  // ============================================================================================
  // I-VOP macroblocks
  // ============================================================================================

  private void _WriteIntraMacroblock(int address, bool predictedPicture) {
    Span<int> levels = stackalloc int[6 * 64];
    var codedPattern = 0;

    for (var block = 0; block < 6; ++block) {
      var blockLevels = levels.Slice(block * 64, 64);
      this._QuantiseIntraBlock(address, block, blockLevels);

      for (var scan = 1; scan < 64; ++scan)
        if (blockLevels[Mpeg4Quantisation.ZigZag[scan]] != 0) {
          codedPattern |= 1 << (5 - block);
          break;
        }
    }

    var chrominancePattern = codedPattern & 0x03;
    var luminancePattern = codedPattern >> 2;

    if (predictedPicture) {
      this._writer.Write(0, 1);                            // not_coded
      this._WriteVlc(Mpeg4VlcTables.PredictedMacroblockType, _INTRA_MACROBLOCK * 4 + chrominancePattern);
    } else {
      this._WriteVlc(Mpeg4VlcTables.IntraMacroblockType, _INTRA_MACROBLOCK * 4 + chrominancePattern);
    }

    this._writer.Write(0, 1);                              // ac_pred_flag
    this._WriteVlc(Mpeg4VlcTables.LuminancePattern, luminancePattern);

    for (var block = 0; block < 6; ++block) {
      var blockLevels = levels.Slice(block * 64, 64);
      this._WriteDc(address, block, blockLevels[0]);
      if ((codedPattern & (1 << (5 - block))) != 0)
        this._WriteCoefficients(blockLevels, Mpeg4VlcTables.IntraCoefficient, first: 1);

      this._ReconstructIntra(address, block, blockLevels);
    }

    this._RecordZeroMotion(address);
  }

  // ============================================================================================
  // P-VOP macroblocks
  // ============================================================================================

  /// <summary>
  /// Writes one zero-vector inter macroblock. This is real temporal prediction: unchanged areas cost
  /// no transform coefficients and changed areas carry only a residual. Motion search is an encoder
  /// optimisation, not a prerequisite for a predictive VOP, and keeping the vector at zero gives a
  /// deterministic baseline whose bitstream is still the normative P-VOP syntax.
  /// </summary>
  private void _WritePredictedMacroblock(int address) {
    Span<int> levels = stackalloc int[6 * 64];
    Span<int> prediction = stackalloc int[64];
    var codedPattern = 0;

    var (vectorX, vectorY) = this._SearchMotion(this._forwardReference!, address);

    for (var block = 0; block < 6; ++block) {
      this._PredictForward(prediction, address, block, vectorX, vectorY);
      var blockLevels = levels.Slice(block * 64, 64);
      this._QuantiseInterBlock(address, block, prediction, blockLevels);
      if (_HasAnyCoefficient(blockLevels))
        codedPattern |= 1 << (5 - block);
    }

    // not_coded: the macroblock is not in the bitstream at all and is the co-located one of the
    // reference with a zero vector. It is what makes a predicted picture cheap, and it is available
    // only when there is genuinely nothing to say -- no displacement and no residual. The
    // reconstruction still has to happen, or this encoder's anchor would differ from the decoder's.
    if (vectorX == 0 && vectorY == 0 && codedPattern == 0) {
      this._writer.Write(1, 1);
      this._RecordNotCoded(address);

      Span<int> empty = stackalloc int[64];
      for (var block = 0; block < 6; ++block) {
        this._PredictForward(prediction, address, block, 0, 0);
        this._ReconstructInter(address, block, prediction, empty);
      }

      return;
    }

    var chrominancePattern = codedPattern & 0x03;
    var luminancePattern = codedPattern >> 2;

    this._writer.Write(0, 1);                              // not_coded
    this._WriteVlc(Mpeg4VlcTables.PredictedMacroblockType, _INTER_MACROBLOCK * 4 + chrominancePattern);
    this._WriteVlc(Mpeg4VlcTables.LuminancePattern, luminancePattern ^ 0xF);

    // One vector for the whole macroblock, coded as the difference from 7.6.2's median of three
    // neighbours. The vector is recorded only after it is written, because the predictor is formed
    // from macroblocks before this one and this one is not yet among them.
    this._WriteVectorDifference(vectorX - this._PredictVector(this._motion.VectorX, address));
    this._WriteVectorDifference(vectorY - this._PredictVector(this._motion.VectorY, address));
    this._RecordMotion(address, vectorX, vectorY);

    for (var block = 0; block < 6; ++block) {
      this._PredictForward(prediction, address, block, vectorX, vectorY);
      var blockLevels = levels.Slice(block * 64, 64);
      if ((codedPattern & (1 << (5 - block))) != 0)
        this._WriteCoefficients(blockLevels, Mpeg4VlcTables.InterCoefficient, first: 0);

      this._ReconstructInter(address, block, prediction, blockLevels);
    }
  }

  /// <summary>
  /// Finds the whole-sample vector whose luminance prediction differs least from the source, in the
  /// half-sample units the syntax counts.
  /// </summary>
  /// <remarks>
  /// The search is whole-sample, so the vector it returns is always even and its luminance
  /// prediction is a plain copy. The zero vector is the incumbent and only a strictly better one
  /// displaces it: on flat or repeating content many vectors score identically, and a background
  /// macroblock that came out of here with a vector would spend bits saying that nothing moved.
  /// <para/>
  /// The reference carries a border wide enough for any vector the syntax can state, so a candidate
  /// never needs rejecting for reading past the edge -- 7.6.5's unrestricted vectors are what the
  /// border is for.
  /// </remarks>
  private (int X, int Y) _SearchMotion(Mpeg4Frame reference, int address) {
    var (plane, stride, origin, _, _) = reference.PlaneOf(0);
    var (sourcePlane, sourceStride, sourceOrigin, _, _) = this._source.PlaneOf(0);
    var left = address % this._macroblockWidth * 16;
    var top = address / this._macroblockWidth * 16;

    // The zero vector is the incumbent, scored before the loop rather than met somewhere inside it,
    // and only a strictly better candidate displaces it. That is not a tie-break detail: a
    // macroblock that did not move has to come out of here with a zero vector or not_coded cannot
    // leave it out, and on flat or repeating content many vectors score identically. Taking the
    // first equal-scoring candidate instead picks whichever corner the scan began at, nothing is
    // ever skipped, and a predicted picture ends up larger than the intra picture it replaces.
    var best = (X: 0, Y: 0);
    var bestCost = _Cost(sourcePlane, sourceStride, sourceOrigin, plane, stride, origin,
      left, top, 0, 0, int.MaxValue);

    for (var candidateY = -_SEARCH_RANGE; candidateY <= _SEARCH_RANGE; ++candidateY)
    for (var candidateX = -_SEARCH_RANGE; candidateX <= _SEARCH_RANGE; ++candidateX) {
      var cost = _Cost(sourcePlane, sourceStride, sourceOrigin, plane, stride, origin,
        left, top, candidateX, candidateY, bestCost);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (2 * candidateX, 2 * candidateY);
    }

    return best;
  }

  /// <summary>
  /// Absolute difference between a macroblock and the prediction one whole-sample vector offers,
  /// abandoned as soon as it cannot beat <paramref name="ceiling"/>.
  /// </summary>
  private static int _Cost(
    byte[] sourcePlane, int sourceStride, int sourceOrigin,
    byte[] plane, int stride, int origin,
    int left, int top, int vectorX, int vectorY, int ceiling) {
    var cost = 0;
    for (var y = 0; y < 16 && cost < ceiling; ++y) {
      var sourceRow = sourceOrigin + (top + y) * sourceStride + left;
      var referenceRow = origin + (top + y + vectorY) * stride + left + vectorX;
      for (var x = 0; x < 16; ++x)
        cost += Math.Abs(sourcePlane[sourceRow + x] - plane[referenceRow + x]);
    }

    return cost;
  }

  /// <summary>Writes one vector component's difference, folded into the range the f_code states.</summary>
  /// <remarks>
  /// Both the vector and the one it is predicted from lie inside the range while their difference
  /// need not, and 7.6.3 brings the sum back by adding or subtracting a whole range rather than by
  /// clamping. Folding here is how the far end of the range is reached at all.
  /// </remarks>
  private void _WriteVectorDifference(int difference) {
    if (difference < _VECTOR_LOW)
      difference += _VECTOR_RANGE;
    else if (difference > _VECTOR_HIGH)
      difference -= _VECTOR_RANGE;

    this._WriteVlc(Mpeg4VlcTables.MotionVectorDifference, difference);
  }

  /// <summary>The median of the three candidate predictors of Figure 7-8.</summary>
  /// <remarks>
  /// Every macroblock this encoder writes carries one vector, so the four per-block entries of a
  /// macroblock are equal and Figure 7-8's twelve cases collapse to the three neighbouring
  /// macroblocks. The validity rules are the decoder's: a candidate outside the picture is not
  /// there, and one that is missing counts as zero once the median is written out.
  /// </remarks>
  private int _PredictVector(short[] vectors, int address) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    var hasLeft = column > 0;
    var hasAbove = row > 0;
    var hasAboveRight = row > 0 && column + 1 < this._macroblockWidth;

    var left = hasLeft ? vectors[(address - 1) * 4 + 1] : 0;
    var above = hasAbove ? vectors[(address - this._macroblockWidth) * 4 + 2] : 0;
    var aboveRight = hasAboveRight ? vectors[(address - this._macroblockWidth + 1) * 4 + 2] : 0;

    var count = (hasLeft ? 1 : 0) + (hasAbove ? 1 : 0) + (hasAboveRight ? 1 : 0);
    if (count == 0)
      return 0;

    if (count == 1)
      return hasLeft ? left : hasAbove ? above : aboveRight;

    return _Median(left, above, aboveRight);
  }

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);

    if (b > c)
      b = c;

    return a > b ? a : b;
  }

  private void _RecordNotCoded(int address) {
    this._motion.IsNotCoded[address] = true;
    for (var block = 0; block < 4; ++block) {
      this._motion.VectorX[address * 4 + block] = 0;
      this._motion.VectorY[address * 4 + block] = 0;
    }
  }

  private void _RecordMotion(int address, int vectorX, int vectorY) {
    this._motion.IsNotCoded[address] = false;
    for (var block = 0; block < 4; ++block) {
      this._motion.VectorX[address * 4 + block] = (short)vectorX;
      this._motion.VectorY[address * 4 + block] = (short)vectorY;
    }
  }

  // ============================================================================================
  // B-VOP macroblocks
  // ============================================================================================

  /// <summary>
  /// Chooses forward, backward or interpolated zero-vector prediction per macroblock and codes the
  /// residual. The choice is made by sample SSE before quantisation, so B-VOPs genuinely use both
  /// anchors where their average is the better predictor without needing a motion-search heuristic.
  /// </summary>
  private void _WriteBidirectionalMacroblock(int address) {
    // 6.3.7.2: where the following anchor did not code this macroblock, the B-VOP does not carry it
    // either. Writing one anyway would not merely waste bits -- it would shift every macroblock
    // after it, because the decoder is not reading a macroblock here at all.
    if (this._anchorMotion != null && this._anchorMotion.IsNotCoded[address])
      return;

    var forward = this._SearchMotion(this._forwardReference!, address);
    var backward = this._SearchMotion(this._backwardReference!, address);
    var type = this._BestBidirectionalType(address, forward, backward);

    Span<int> levels = stackalloc int[6 * 64];
    Span<int> prediction = stackalloc int[64];
    var codedPattern = 0;

    for (var block = 0; block < 6; ++block) {
      this._PredictBidirectional(prediction, address, block, type, forward, backward);
      var blockLevels = levels.Slice(block * 64, 64);
      this._QuantiseInterBlock(address, block, prediction, blockLevels);
      if (_HasAnyCoefficient(blockLevels))
        codedPattern |= 1 << (5 - block);
    }

    this._WriteVlc(Mpeg4VlcTables.BidirectionalMode, codedPattern == 0 ? 1 : 2);
    this._WriteVlc(Mpeg4VlcTables.BidirectionalMacroblockType, type);

    if (codedPattern != 0) {
      this._writer.Write(codedPattern, 6);
      this._WriteVlc(Mpeg4VlcTables.BidirectionalQuantiserDifference, 0);
    }

    // Only the directions this macroblock actually uses are written, and only those move their
    // predictor. The order is forward then backward, which is the order they are read in.
    switch (type) {
      case Mpeg4VlcTables.Forward:
        this._WriteBidirectionalVector(forward, isForward: true);
        break;

      case Mpeg4VlcTables.Backward:
        this._WriteBidirectionalVector(backward, isForward: false);
        break;

      case Mpeg4VlcTables.Interpolated:
        this._WriteBidirectionalVector(forward, isForward: true);
        this._WriteBidirectionalVector(backward, isForward: false);
        break;

      default:
        throw new InvalidOperationException($"B-VOP macroblock type {type} is not emitted by this encoder.");
    }

    for (var block = 0; block < 6; ++block) {
      this._PredictBidirectional(prediction, address, block, type, forward, backward);
      var blockLevels = levels.Slice(block * 64, 64);
      if ((codedPattern & (1 << (5 - block))) != 0)
        this._WriteCoefficients(blockLevels, Mpeg4VlcTables.InterCoefficient, first: 0);

      this._ReconstructInter(address, block, prediction, blockLevels);
    }
  }

  /// <summary>Writes one B-VOP vector as the difference from its direction's running predictor.</summary>
  private void _WriteBidirectionalVector((int X, int Y) vector, bool isForward) {
    ref var predictorX = ref (isForward ? ref this._forwardPredictorX : ref this._backwardPredictorX);
    ref var predictorY = ref (isForward ? ref this._forwardPredictorY : ref this._backwardPredictorY);

    this._WriteVectorDifference(vector.X - predictorX);
    this._WriteVectorDifference(vector.Y - predictorY);
    predictorX = vector.X;
    predictorY = vector.Y;
  }

  private int _BestBidirectionalType(int address, (int X, int Y) forwardVector, (int X, int Y) backwardVector) {
    Span<int> source = stackalloc int[64];
    Span<int> forward = stackalloc int[64];
    Span<int> backward = stackalloc int[64];
    Span<int> interpolated = stackalloc int[64];

    long forwardError = 0;
    long backwardError = 0;
    long interpolatedError = 0;

    for (var block = 0; block < 6; ++block) {
      this._ReadBlock(this._source, address, block, source);
      this._PredictFrom(forward, this._forwardReference!, address, block, forwardVector.X, forwardVector.Y, 0);
      this._PredictFrom(backward, this._backwardReference!, address, block, backwardVector.X, backwardVector.Y, 0);
      forward.CopyTo(interpolated);
      Mpeg4MotionCompensation.Average(interpolated, backward);

      for (var i = 0; i < 64; ++i) {
        var df = source[i] - forward[i];
        var db = source[i] - backward[i];
        var di = source[i] - interpolated[i];
        forwardError += (long)df * df;
        backwardError += (long)db * db;
        interpolatedError += (long)di * di;
      }
    }

    if (interpolatedError <= forwardError && interpolatedError <= backwardError)
      return Mpeg4VlcTables.Interpolated;

    return forwardError <= backwardError ? Mpeg4VlcTables.Forward : Mpeg4VlcTables.Backward;
  }

  // ============================================================================================
  // Transform, quantisation and reconstruction
  // ============================================================================================

  private void _QuantiseIntraBlock(int address, int block, Span<int> levels) {
    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    this._ReadBlock(this._source, address, block, samples);
    MsMpeg4ForwardDct.Transform(samples, coefficients);

    var dcScaler = Mpeg4Quantisation.DcScaler(this._quantiser, block < 4);
    levels[0] = Math.Clamp(
      (int)Math.Round(coefficients[0] / dcScaler, MidpointRounding.AwayFromZero),
      -_MAX_LEVEL,
      _MAX_LEVEL);

    for (var index = 1; index < 64; ++index)
      levels[index] = _NearestH263Level(coefficients[index], this._quantiser);
  }

  private void _QuantiseInterBlock(int address, int block, ReadOnlySpan<int> prediction, Span<int> levels) {
    Span<int> residual = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    this._ReadBlock(this._source, address, block, residual);

    for (var i = 0; i < 64; ++i)
      residual[i] -= prediction[i];

    MsMpeg4ForwardDct.Transform(residual, coefficients);
    for (var index = 0; index < 64; ++index)
      levels[index] = _NearestH263Level(coefficients[index], this._quantiser);
  }

  /// <summary>
  /// Inverts the decoder's H.263 reconstruction by choosing the nearest level that reconstruction
  /// can actually produce. Level zero is special, so the candidate around the algebraic inverse is
  /// compared with zero and its immediate neighbours rather than rounded by a separate formula.
  /// </summary>
  private static int _NearestH263Level(double coefficient, int quantiser) {
    if (coefficient == 0)
      return 0;

    var sign = coefficient < 0 ? -1 : 1;
    var magnitude = Math.Abs(coefficient);
    var evenAdjustment = (quantiser & 1) == 0 ? 1 : 0;
    var estimate = Math.Clamp(
      (int)Math.Round(
        (magnitude - quantiser + evenAdjustment) / (2d * quantiser),
        MidpointRounding.AwayFromZero),
      0,
      _MAX_LEVEL);

    var first = Math.Max(0, estimate - 2);
    var last = Math.Min(_MAX_LEVEL, estimate + 2);
    var best = 0;
    var bestError = magnitude;

    for (var candidate = first; candidate <= last; ++candidate) {
      var reconstructed = Math.Abs(Mpeg4Quantisation.DequantiseH263(candidate, quantiser));
      var error = Math.Abs(magnitude - reconstructed);
      if (error >= bestError)
        continue;

      best = candidate;
      bestError = error;
    }

    return sign * best;
  }

  private void _ReconstructIntra(int address, int block, ReadOnlySpan<int> levels) {
    Span<int> samples = stackalloc int[64];
    samples[0] = Mpeg4Quantisation.Clamp(Mpeg4Quantisation.DcScaler(this._quantiser, block < 4) * levels[0]);
    for (var i = 1; i < 64; ++i)
      samples[i] = Mpeg4Quantisation.DequantiseH263(levels[i], this._quantiser);

    Mpeg4InverseDct.Transform(samples);
    this._Store(address, block, samples);
  }

  private void _ReconstructInter(
    int address, int block, ReadOnlySpan<int> prediction, ReadOnlySpan<int> levels) {
    Span<int> samples = stackalloc int[64];
    for (var i = 0; i < 64; ++i)
      samples[i] = Mpeg4Quantisation.DequantiseH263(levels[i], this._quantiser);

    Mpeg4InverseDct.Transform(samples);
    for (var i = 0; i < 64; ++i)
      samples[i] += prediction[i];

    this._Store(address, block, samples);
  }

  // ============================================================================================
  // Coefficients and vectors
  // ============================================================================================

  private void _WriteDc(int address, int block, int absoluteLevel) {
    var isLuminance = block < 4;
    var dcScaler = Mpeg4Quantisation.DcScaler(this._quantiser, isLuminance);

    // Apply the decoder's predictor once to a zero differential to obtain exactly the predicted level,
    // then once with the real differential. The second pass overwrites the temporary current-block
    // state with the final DC; its predictor only reads neighbouring blocks, never the current one.
    Span<int> predicted = stackalloc int[64];
    var fromAbove = this._prediction.PredictsFromAbove(address, block);
    this._prediction.Apply(address, block, predicted, this._quantiser, dcScaler, predictAc: false, fromAbove);
    var differential = absoluteLevel - predicted[0];
    predicted[0] = differential;
    this._prediction.Apply(address, block, predicted, this._quantiser, dcScaler, predictAc: false, fromAbove);

    var size = 0;
    while (differential >= 1 << size || differential < -((1 << size) - 1))
      ++size;

    if (size > 12)
      throw new InvalidDataException(
        $"The DC differential {differential} needs {size} bits, beyond MPEG-4 Part 2's DC-size tables.");

    this._WriteVlc(isLuminance ? Mpeg4VlcTables.LuminanceDcSize : Mpeg4VlcTables.ChrominanceDcSize, size);
    if (size == 0)
      return;

    var bits = differential > 0 ? differential : differential + (1 << size) - 1;
    this._writer.Write(bits, size);
    if (size > 8)
      this._writer.Write(1, 1);                            // marker_bit
  }

  /// <summary>
  /// Writes non-zero terms through escape type 3. It is longer than Annex B's common rows, but it can
  /// state every legal (last, run, level) triple directly and keeps this baseline writer independent
  /// of a second inverse index over the already validated decoder tables.
  /// </summary>
  private void _WriteCoefficients(ReadOnlySpan<int> levels, Mpeg4VlcTable table, int first) {
    var lastIndex = -1;
    for (var scan = first; scan < 64; ++scan)
      if (levels[Mpeg4Quantisation.ZigZag[scan]] != 0)
        lastIndex = scan;

    if (lastIndex < first)
      return;

    var previous = first - 1;
    for (var scan = first; scan <= lastIndex; ++scan) {
      var level = levels[Mpeg4Quantisation.ZigZag[scan]];
      if (level == 0)
        continue;

      var run = scan - previous - 1;
      previous = scan;

      this._WriteVlc(table, Mpeg4VlcTables.CoefficientEscape);
      this._writer.Write(3, 2);                            // escape type 3
      this._writer.Write(scan == lastIndex ? 1 : 0, 1);  // last
      this._writer.Write(run, 6);
      this._writer.Write(1, 1);                            // marker_bit
      this._writer.Write(level & 0xFFF, 12);
      this._writer.Write(1, 1);                            // marker_bit
    }
  }

  private void _WriteZeroVector() {
    this._WriteVlc(Mpeg4VlcTables.MotionVectorDifference, 0);
    this._WriteVlc(Mpeg4VlcTables.MotionVectorDifference, 0);
  }

  private void _RecordZeroMotion(int address) {
    this._motion.IsNotCoded[address] = false;
    for (var block = 0; block < 4; ++block) {
      this._motion.VectorX[address * 4 + block] = 0;
      this._motion.VectorY[address * 4 + block] = 0;
    }
  }

  private static bool _HasAnyCoefficient(ReadOnlySpan<int> levels) {
    for (var i = 0; i < 64; ++i)
      if (levels[i] != 0)
        return true;

    return false;
  }

  // ============================================================================================
  // Planes, prediction and bit output
  // ============================================================================================

  /// <summary>Predicts one block from the forward anchor at a stated vector.</summary>
  private void _PredictForward(Span<int> prediction, int address, int block, int vectorX, int vectorY)
    => this._PredictFrom(prediction, this._forwardReference!, address, block, vectorX, vectorY, this._roundingType);

  /// <summary>Predicts one block from a stated anchor at a stated vector.</summary>
  private void _PredictFrom(
    Span<int> prediction, Mpeg4Frame reference, int address, int block, int vectorX, int vectorY, int rounding) {
    var (plane, stride, origin, width, height) = reference.PlaneOf(block);
    var (left, top) = this._BlockOrigin(address, block);
    var border = block < 4 ? Mpeg4Frame.Border : Mpeg4Frame.Border / 2;

    // 7.6.2 derives the chrominance vector from the sum of the macroblock's four luminance vectors
    // through a rounding table, not by halving one of them. For a macroblock carrying a single
    // vector the sum is four times it, and the table is what decides where the quarter-sample
    // positions the sum can land are rounded to. Halving instead agrees with the table on only some
    // vectors, and the disagreement is a colour fringe on moving edges that no luminance comparison
    // sees -- so the derivation here is the decoder's own routine rather than a restatement of it.
    var blockVectorX = block < 4 ? vectorX : Mpeg4MotionCompensation.ToChroma(4 * vectorX);
    var blockVectorY = block < 4 ? vectorY : Mpeg4MotionCompensation.ToChroma(4 * vectorY);

    Mpeg4MotionCompensation.PredictHalfSample(
      prediction, plane, stride, origin, border, width, height, left, top,
      blockVectorX, blockVectorY, rounding);
  }

  private void _PredictZero(Span<int> prediction, Mpeg4Frame reference, int address, int block, int rounding) {
    var (plane, stride, origin, width, height) = reference.PlaneOf(block);
    var (left, top) = this._BlockOrigin(address, block);
    var border = block < 4 ? Mpeg4Frame.Border : Mpeg4Frame.Border / 2;

    Mpeg4MotionCompensation.PredictHalfSample(
      prediction, plane, stride, origin, border, width, height, left, top, 0, 0, rounding);
  }

  private void _PredictBidirectional(
    Span<int> prediction, int address, int block, int type,
    (int X, int Y) forwardVector, (int X, int Y) backwardVector) {
    switch (type) {
      case Mpeg4VlcTables.Forward:
        this._PredictFrom(
          prediction, this._forwardReference!, address, block, forwardVector.X, forwardVector.Y, 0);
        return;

      case Mpeg4VlcTables.Backward:
        this._PredictFrom(
          prediction, this._backwardReference!, address, block, backwardVector.X, backwardVector.Y, 0);
        return;

      case Mpeg4VlcTables.Interpolated:
        Span<int> backward = stackalloc int[64];
        this._PredictFrom(
          prediction, this._forwardReference!, address, block, forwardVector.X, forwardVector.Y, 0);
        this._PredictFrom(
          backward, this._backwardReference!, address, block, backwardVector.X, backwardVector.Y, 0);
        Mpeg4MotionCompensation.Average(prediction, backward);
        return;

      default:
        throw new InvalidOperationException($"B-VOP macroblock type {type} is not emitted by this encoder.");
    }
  }

  private void _ReadBlock(Mpeg4Frame frame, int address, int block, Span<int> samples) {
    var (plane, stride, origin, _, _) = frame.PlaneOf(block);
    var (left, top) = this._BlockOrigin(address, block);

    for (var y = 0; y < 8; ++y) {
      var source = origin + (top + y) * stride + left;
      for (var x = 0; x < 8; ++x)
        samples[y * 8 + x] = plane[source + x];
    }
  }

  private void _Store(int address, int block, ReadOnlySpan<int> samples) {
    var (plane, stride, origin, _, _) = this._target.PlaneOf(block);
    var (left, top) = this._BlockOrigin(address, block);

    for (var y = 0; y < 8; ++y) {
      var row = origin + (top + y) * stride + left;
      for (var x = 0; x < 8; ++x) {
        var value = samples[y * 8 + x];
        plane[row + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  private (int Left, int Top) _BlockOrigin(int address, int block) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    return block < 4
      ? (column * 16 + (block & 1) * 8, row * 16 + (block >> 1) * 8)
      : (column * 8, row * 8);
  }

  private void _WriteVlc(Mpeg4VlcTable table, int value) {
    foreach (var (code, entryValue) in table.Entries) {
      if (entryValue != value)
        continue;

      foreach (var bit in code)
        switch (bit) {
          case '0': this._writer.Write(0, 1); break;
          case '1': this._writer.Write(1, 1); break;
          case ' ': break;
          default: throw new InvalidDataException($"{table.Name} contains '{bit}', which is not a bit.");
        }

      return;
    }

    throw new InvalidDataException($"{table.Name} has no code for value {value}.");
  }

  private void _StartCode(byte code) {
    if ((this._writer.BitCount & 7) != 0)
      throw new InvalidOperationException("An MPEG-4 start code can only begin on a byte boundary.");

    this._writer.Write(0x000001, 24);
    this._writer.Write(code, 8);
  }

  /// <summary>Writes the stuffing bit and ones that align the following start code.</summary>
  private void _NextStartCode() {
    this._writer.Write(0, 1);
    while ((this._writer.BitCount & 7) != 0)
      this._writer.Write(1, 1);
  }

  private static int _BitsFor(int resolution) {
    var bits = 1;
    while ((1 << bits) < resolution)
      ++bits;

    return bits;
  }
}
