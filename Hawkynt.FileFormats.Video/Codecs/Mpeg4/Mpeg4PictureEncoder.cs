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

  private readonly Mpeg4Frame _source;
  private readonly Mpeg4Frame? _forwardReference;
  private readonly Mpeg4Frame? _backwardReference;
  private readonly Mpeg4Frame _target;
  private readonly Mpeg4AnchorMotion _motion;
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
    int roundingType) {
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
    this._writer.Write(0, 1);                              // vol_control_parameters
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

    for (var block = 0; block < 6; ++block) {
      this._PredictZero(prediction, this._forwardReference!, address, block, this._roundingType);
      var blockLevels = levels.Slice(block * 64, 64);
      this._QuantiseInterBlock(address, block, prediction, blockLevels);
      if (_HasAnyCoefficient(blockLevels))
        codedPattern |= 1 << (5 - block);
    }

    var chrominancePattern = codedPattern & 0x03;
    var luminancePattern = codedPattern >> 2;

    this._writer.Write(0, 1);                              // not_coded: keep anchors usable by B-VOPs
    this._WriteVlc(Mpeg4VlcTables.PredictedMacroblockType, _INTER_MACROBLOCK * 4 + chrominancePattern);
    this._WriteVlc(Mpeg4VlcTables.LuminancePattern, luminancePattern ^ 0xF);
    this._WriteZeroVector();                               // one vector for the whole macroblock

    for (var block = 0; block < 6; ++block) {
      this._PredictZero(prediction, this._forwardReference!, address, block, this._roundingType);
      var blockLevels = levels.Slice(block * 64, 64);
      if ((codedPattern & (1 << (5 - block))) != 0)
        this._WriteCoefficients(blockLevels, Mpeg4VlcTables.InterCoefficient, first: 0);

      this._ReconstructInter(address, block, prediction, blockLevels);
    }

    this._RecordZeroMotion(address);
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
    var type = this._BestBidirectionalType(address);
    Span<int> levels = stackalloc int[6 * 64];
    Span<int> prediction = stackalloc int[64];
    var codedPattern = 0;

    for (var block = 0; block < 6; ++block) {
      this._PredictBidirectional(prediction, address, block, type);
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

    switch (type) {
      case Mpeg4VlcTables.Forward:
        this._WriteZeroVector();
        break;

      case Mpeg4VlcTables.Backward:
        this._WriteZeroVector();
        break;

      case Mpeg4VlcTables.Interpolated:
        this._WriteZeroVector();
        this._WriteZeroVector();
        break;

      default:
        throw new InvalidOperationException($"B-VOP macroblock type {type} is not emitted by this encoder.");
    }

    for (var block = 0; block < 6; ++block) {
      this._PredictBidirectional(prediction, address, block, type);
      var blockLevels = levels.Slice(block * 64, 64);
      if ((codedPattern & (1 << (5 - block))) != 0)
        this._WriteCoefficients(blockLevels, Mpeg4VlcTables.InterCoefficient, first: 0);

      this._ReconstructInter(address, block, prediction, blockLevels);
    }
  }

  private int _BestBidirectionalType(int address) {
    Span<int> source = stackalloc int[64];
    Span<int> forward = stackalloc int[64];
    Span<int> backward = stackalloc int[64];
    Span<int> interpolated = stackalloc int[64];

    long forwardError = 0;
    long backwardError = 0;
    long interpolatedError = 0;

    for (var block = 0; block < 6; ++block) {
      this._ReadBlock(this._source, address, block, source);
      this._PredictZero(forward, this._forwardReference!, address, block, 0);
      this._PredictZero(backward, this._backwardReference!, address, block, 0);
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

  private void _PredictZero(Span<int> prediction, Mpeg4Frame reference, int address, int block, int rounding) {
    var (plane, stride, origin, width, height) = reference.PlaneOf(block);
    var (left, top) = this._BlockOrigin(address, block);
    var border = block < 4 ? Mpeg4Frame.Border : Mpeg4Frame.Border / 2;

    Mpeg4MotionCompensation.PredictHalfSample(
      prediction, plane, stride, origin, border, width, height, left, top, 0, 0, rounding);
  }

  private void _PredictBidirectional(Span<int> prediction, int address, int block, int type) {
    switch (type) {
      case Mpeg4VlcTables.Forward:
        this._PredictZero(prediction, this._forwardReference!, address, block, 0);
        return;

      case Mpeg4VlcTables.Backward:
        this._PredictZero(prediction, this._backwardReference!, address, block, 0);
        return;

      case Mpeg4VlcTables.Interpolated:
        Span<int> backward = stackalloc int[64];
        this._PredictZero(prediction, this._forwardReference!, address, block, 0);
        this._PredictZero(backward, this._backwardReference!, address, block, 0);
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
