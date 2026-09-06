using System;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// Codes one picture of Microsoft's MPEG-4 and reconstructs it as the decoder will.
/// </summary>
/// <remarks>
/// Every decision here is the encoder's own — none of the three formats says how to choose a motion
/// vector, a quantised level or a coded block pattern, only how to write one down — but two of them
/// are not free, and getting either wrong produces a stream that decodes into something the encoder
/// never saw:
/// <list type="bullet">
/// <item><b>Everything is coded against the reconstruction, never against the source.</b> A predicted
/// picture is predicted from what the decoder is holding, which is the previous picture as this
/// encoder reconstructed it and not the picture that was handed in. Coding against the source lets the
/// quantisation error of every picture accumulate silently into the next.</item>
/// <item><b>The prediction machinery is the decoder's, run forwards.</b> The DC predictor, its
/// gradient test, the coded block pattern predictor of version 3 and the motion vector median are all
/// the same code the decoder uses, because a predictor that disagreed by one would not be a worse
/// picture but a different one.</item>
/// </list>
/// What this deliberately does not do is choose: the alternating current prediction is always off, the
/// run-level tables version 3 could choose between are always the middle pair, and the quantiser is
/// whatever the caller asked for. Each of those is a rate-distortion decision with no effect on
/// whether the result decodes, and leaving them fixed keeps the encoder's output the same bytes every
/// time it is run on the same input.
/// </remarks>
internal sealed class MsMpeg4PictureEncoder {

  /// <summary>The largest magnitude a quantised level may take, which the third escape's eight bits fix.</summary>
  private const int _MAX_LEVEL = 127;

  /// <summary>The largest magnitude the run-level tables are indexed over.</summary>
  /// <remarks>
  /// Sixty-four, which is well past the largest any of the six tables actually states — but it is the
  /// bound the tables' derived limits are sized to, and a magnitude past it has no entry at any run
  /// and so cannot use the escape form that shortens a run.
  /// </remarks>
  private const int _LARGEST_TABLE_LEVEL = 64;

  /// <summary>
  /// How far the first pass of a motion search steps, in half-samples; each pass halves it.
  /// </summary>
  /// <remarks>
  /// Eight half-samples is four whole ones, and four passes take the search to a half sample. That
  /// reaches seven and a half samples from where it started in twenty-seven comparisons, where an
  /// exhaustive search of the same reach would take nearly a thousand.
  /// </remarks>
  private const int _SEARCH_RADIUS = 8;

  /// <summary>
  /// How far a vector may reach, in half-samples, short of the range the format states.
  /// </summary>
  /// <remarks>
  /// The format's range is sixty-four half-samples either way and it is reached by wrapping: a vector
  /// at one end is written as a small difference from a prediction at the other. This encoder stays
  /// clear of the wrap and writes every difference straight, because a vector that reached the far end
  /// would have to be written as though it were at the near one, and getting that right buys nothing —
  /// no picture this encoder produces has motion of thirty whole samples between frames that a search
  /// of seven and a half would have found.
  /// </remarks>
  private const int _MAX_VECTOR = 60;

  /// <summary>
  /// The largest motion vector difference that can be written, in half-samples.
  /// </summary>
  /// <remarks>
  /// Versions 1 and 2 state a magnitude out of a table of thirty-three entries, so a difference
  /// reaches thirty-two either way; version 3 states a biased pair of six-bit numbers, so it reaches
  /// thirty-two one way and thirty-one the other. Thirty-one either way is what both can write, and a
  /// vector whose difference would exceed it is given up and replaced by its own prediction.
  /// </remarks>
  private const int _MAX_VECTOR_DIFFERENCE = 31;

  /// <summary>Which of three run-level tables version 3 is told to use: the pair versions 1 and 2 fix.</summary>
  private const int _RUN_LEVEL_TABLE_INDEX = 2;

  private readonly MsMpeg4Version _version;
  private readonly int _quantiser;
  private readonly bool _intraPicture;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _sliceHeight;
  private readonly Mpeg4Frame _source;
  private readonly Mpeg4Frame _target;
  private readonly Mpeg4Frame? _reference;
  private readonly MsMpeg4BitWriter _writer = new();
  private readonly MsMpeg4IntraPrediction _intraPrediction;
  private readonly MsMpeg4CodedBlockPrediction? _codedBlock;
  private readonly short[] _vectorX;
  private readonly short[] _vectorY;
  private readonly bool[] _isDecoded;
  private readonly int[] _lastDc = new int[3];
  private readonly int _frameRate;

  internal MsMpeg4PictureEncoder(
    MsMpeg4Version version, int quantiser, bool intraPicture, int frameRate,
    Mpeg4Frame source, Mpeg4Frame target, Mpeg4Frame? reference,
    int macroblockWidth, int macroblockHeight, int sliceHeight) {
    this._version = version;
    this._quantiser = quantiser;
    this._intraPicture = intraPicture;
    this._frameRate = frameRate;
    this._source = source;
    this._target = target;
    this._reference = reference;
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;
    this._sliceHeight = sliceHeight;

    var count = macroblockWidth * macroblockHeight;
    this._vectorX = new short[count];
    this._vectorY = new short[count];
    this._isDecoded = new bool[count];
    this._intraPrediction = new(macroblockWidth, macroblockHeight, sliceHeight);
    this._codedBlock = version == MsMpeg4Version.Version3 && intraPicture
      ? new(macroblockWidth, macroblockHeight)
      : null;
  }

  /// <summary>Codes the whole picture and hands back its bytes, the last one padded.</summary>
  internal byte[] Encode() {
    this._WriteHeader();

    for (var row = 0; row < this._macroblockHeight; ++row) {
      this._ResetLastDc();

      for (var column = 0; column < this._macroblockWidth; ++column) {
        var address = row * this._macroblockWidth + column;
        if (this._intraPicture)
          this._EncodeIntraMacroblock(address);
        else
          this._EncodePredictedMacroblock(address);
      }
    }

    if (this._intraPicture)
      this._WriteExtensionHeader();

    this._target.PadBorders();
    return this._writer.ToArray();
  }

  // ============================================================================================
  // The picture header
  // ============================================================================================

  private void _WriteHeader() {
    if (this._version == MsMpeg4Version.Version1) {
      this._writer.Write(0, 16);
      this._writer.Write(0x0100, 16);

      // The picture number, which nothing ever reads.
      this._writer.Write(0, 5);
    }

    this._writer.Write(this._intraPicture ? MsMpeg4PictureHeader.IntraCoded : MsMpeg4PictureHeader.PredictiveCoded, 2);
    this._writer.Write(this._quantiser, 5);

    if (this._intraPicture) {
      // Version 1 states the height of a slice and the others state how many there are, biased by
      // twenty-two. This encoder makes one slice of the whole picture wherever a five-bit field can
      // say so, which for version 1 means a picture up to thirty-one macroblock rows tall.
      this._writer.Write(
        this._version == MsMpeg4Version.Version1 ? this._sliceHeight : 0x16 + this._macroblockHeight / this._sliceHeight,
        5);

      if (this._version != MsMpeg4Version.Version3)
        return;

      _WriteZeroOneOrTwo(this._writer, _RUN_LEVEL_TABLE_INDEX);
      _WriteZeroOneOrTwo(this._writer, _RUN_LEVEL_TABLE_INDEX);
      this._writer.Write(0, 1);
      return;
    }

    // Version 1 spends a bit per macroblock saying whether it is skipped and has no field saying so;
    // the other two say it once here, and this encoder always spends the bits because a picture in
    // which nothing moves is the case worth having cheap.
    if (this._version != MsMpeg4Version.Version1)
      this._writer.Write(1, 1);

    if (this._version != MsMpeg4Version.Version3)
      return;

    _WriteZeroOneOrTwo(this._writer, _RUN_LEVEL_TABLE_INDEX);
    this._writer.Write(0, 1);
    this._writer.Write(0, 1);
  }

  /// <summary>
  /// Writes what an intra picture carries after its last macroblock.
  /// </summary>
  /// <remarks>
  /// A frame rate, a bit rate and, in version 3, a bit saying whether the interpolation's rounding
  /// alternates from picture to picture. Only the last of those changes anything, and this encoder
  /// writes it clear — alternating the rounding saves a fraction of a level of drift over a long run
  /// of predicted pictures and costs a decoder that has to be told which picture it is on.
  /// <para/>
  /// Written at all, where the reference encoder omits it, because a decoder decides whether the
  /// header is there by counting the bits left over: omitting it makes a decoder report a missing
  /// header on every intra picture, which is a true statement about a stream nobody meant to write
  /// that way.
  /// </remarks>
  private void _WriteExtensionHeader() {
    this._writer.Write(Math.Min(this._frameRate, 31), 5);
    this._writer.Write(0, 11);

    if (this._version == MsMpeg4Version.Version3)
      this._writer.Write(0, 1);
  }

  private static void _WriteZeroOneOrTwo(MsMpeg4BitWriter writer, int value) {
    if (value == 0) {
      writer.Write(0, 1);
      return;
    }

    writer.Write(2 | (value >= 2 ? 1 : 0), 2);
  }

  // ============================================================================================
  // Intra macroblocks
  // ============================================================================================

  private void _EncodeIntraMacroblock(int address) {
    Span<int> levels = stackalloc int[6 * 64];
    var pattern = 0;

    for (var index = 0; index < 6; ++index) {
      var block = levels.Slice(index * 64, 64);
      this._QuantiseIntra(address, index, block);

      // The DC is always sent, so a block is "coded" only where something past it survived.
      for (var i = 1; i < 64; ++i)
        if (block[i] != 0) {
          pattern |= 1 << (5 - index);
          break;
        }
    }

    this._WriteIntraMacroblockHeader(address, pattern);
    this._WriteIntraBlocks(address, levels, pattern);

    this._isDecoded[address] = true;
    this._vectorX[address] = 0;
    this._vectorY[address] = 0;
  }

  /// <summary>
  /// States what an intra macroblock of an intra picture is, which is where the three versions differ
  /// most.
  /// </summary>
  /// <remarks>
  /// Version 3 says the whole six-bit pattern in one codeword with its luminance half predicted from
  /// the neighbouring blocks; version 2 says the chrominance half out of a table of its own and the
  /// luminance half out of H.263's; version 1 borrows both of H.263's whole. Only version 2 and
  /// version 3 have a bit for the alternating current prediction, and this encoder always writes it
  /// clear.
  /// </remarks>
  private void _WriteIntraMacroblockHeader(int address, int pattern) {
    var chroma = pattern & 3;
    var luminance = pattern >> 2;

    switch (this._version) {
      case MsMpeg4Version.Version3: {
        var coded = chroma;
        for (var index = 0; index < 4; ++index) {
          var bit = (pattern >> (5 - index)) & 1;
          coded |= (bit ^ this._codedBlock!.Predict(address, index)) << (5 - index);
          this._codedBlock.Record(address, index, bit);
        }

        this._writer.Write(
          MsMpeg4Data.IntraMacroblockPatternCodes[coded], MsMpeg4Data.IntraMacroblockPatternLengths[coded]);
        this._writer.Write(0, 1);
        return;
      }

      case MsMpeg4Version.Version2: {
        this._writer.Write(
          MsMpeg4Data.V2IntraChromaPatternCodes[chroma], MsMpeg4Data.V2IntraChromaPatternLengths[chroma]);
        this._writer.Write(0, 1);
        this._writer.Write(
          MsMpeg4Data.CodedBlockPatternYCodes[luminance], MsMpeg4Data.CodedBlockPatternYLengths[luminance]);
        return;
      }

      default: {
        this._writer.Write(MsMpeg4Data.IntraMacroblockCodes[chroma], MsMpeg4Data.IntraMacroblockLengths[chroma]);
        this._writer.Write(
          MsMpeg4Data.CodedBlockPatternYCodes[luminance], MsMpeg4Data.CodedBlockPatternYLengths[luminance]);
        return;
      }
    }
  }

  private void _WriteIntraBlocks(int address, scoped Span<int> levels, int pattern) {
    Span<int> samples = stackalloc int[64];

    for (var index = 0; index < 6; ++index) {
      var block = levels.Slice(index * 64, 64);
      var isLuminance = index < 4;
      var step = this._StepOf(isLuminance);
      var codes = MsMpeg4Tables.RunLevel[isLuminance ? _RUN_LEVEL_TABLE_INDEX : 3 + _RUN_LEVEL_TABLE_INDEX];
      var absentDc = MsMpeg4IntraPrediction.AbsentDc(step);

      int predicted;
      if (this._version == MsMpeg4Version.Version1) {
        var plane = isLuminance ? 0 : index - 3;
        predicted = this._lastDc[plane];
        this._lastDc[plane] = block[0];
        this._WriteVersion2Dc(block[0] - predicted, isLuminance);
        this._intraPrediction.Record(address, index, block);
      } else {
        var fromAbove = this._intraPrediction.PredictsFromAbove(address, index, absentDc);
        predicted = this._intraPrediction.PredictedDc(address, index, fromAbove, absentDc);

        if (this._version == MsMpeg4Version.Version2)
          this._WriteVersion2Dc(block[0] - predicted, isLuminance);
        else
          this._WriteVersion3Dc(block[0] - predicted, isLuminance);

        this._intraPrediction.Record(address, index, block);
      }

      if ((pattern & (1 << (5 - index))) != 0)
        this._WriteCoefficients(block, Mpeg4Quantisation.ZigZag, codes, first: 1, intra: true);

      samples[0] = block[0] * step;
      var multiplier = 2 * this._quantiser;
      var offset = (this._quantiser - 1) | 1;
      for (var i = 1; i < 64; ++i) {
        var level = block[i];
        samples[i] = level == 0 ? 0 : level < 0 ? level * multiplier - offset : level * multiplier + offset;
      }

      Mpeg4InverseDct.Transform(samples);
      this._Store(address, index, samples);
    }
  }

  /// <summary>
  /// Quantises one intra block: its DC by its own step and the rest by the H.263 rule.
  /// </summary>
  /// <remarks>
  /// The levels come out in raster order and not in scan order, because that is the order the
  /// prediction and the reconstruction both want them in; only the run-level coding walks the scan.
  /// </remarks>
  private void _QuantiseIntra(int address, int index, scoped Span<int> levels) {
    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    this._Read(this._source, address, index, samples);
    MsMpeg4ForwardDct.Transform(samples, coefficients);

    var step = this._StepOf(index < 4);
    levels[0] = (int)Math.Round(coefficients[0] / step, MidpointRounding.AwayFromZero);
    levels[0] = Math.Clamp(levels[0], 0, 255);

    for (var i = 1; i < 64; ++i)
      levels[i] = _QuantiseCoefficient(coefficients[i], this._quantiser);
  }

  private int _StepOf(bool isLuminance) => this._version != MsMpeg4Version.Version3
    ? MsMpeg4BlockDecoder.FixedDcStep
    : isLuminance
      ? MsMpeg4Data.Version3LuminanceDcStep[this._quantiser]
      : MsMpeg4Data.Version3ChrominanceDcStep[this._quantiser];

  // ============================================================================================
  // Predicted macroblocks
  // ============================================================================================

  private void _EncodePredictedMacroblock(int address) {
    var predictedX = this._PredictVector(address, horizontal: true);
    var predictedY = this._PredictVector(address, horizontal: false);
    var (vectorX, vectorY) = this._Search(address, predictedX, predictedY);

    // A vector whose difference cannot be written is given up rather than clipped, because clipping
    // the difference would move the vector somewhere the search never looked at.
    if (Math.Abs(vectorX - predictedX) > _MAX_VECTOR_DIFFERENCE
        || Math.Abs(vectorY - predictedY) > _MAX_VECTOR_DIFFERENCE) {
      vectorX = predictedX;
      vectorY = predictedY;
    }

    Span<int> levels = stackalloc int[6 * 64];
    Span<int> prediction = stackalloc int[6 * 64];
    var pattern = 0;

    var chromaX = Mpeg4MotionCompensation.ToChroma(4 * vectorX);
    var chromaY = Mpeg4MotionCompensation.ToChroma(4 * vectorY);

    for (var index = 0; index < 6; ++index) {
      var (x, y) = index < 4 ? (vectorX, vectorY) : (chromaX, chromaY);
      this._Predict(prediction.Slice(index * 64, 64), address, index, x, y);

      var block = levels.Slice(index * 64, 64);
      this._QuantisePredicted(address, index, prediction.Slice(index * 64, 64), block);

      for (var i = 0; i < 64; ++i)
        if (block[i] != 0) {
          pattern |= 1 << (5 - index);
          break;
        }
    }

    if (pattern == 0 && vectorX == 0 && vectorY == 0) {
      this._writer.Write(1, 1);
      this._Reconstruct(address, prediction, levels, pattern: 0);
      this._Remember(address, 0, 0);
      return;
    }

    this._writer.Write(0, 1);
    this._WritePredictedMacroblockHeader(pattern);
    this._WriteVector(vectorX - predictedX, vectorY - predictedY);

    var codes = MsMpeg4Tables.RunLevel[3 + _RUN_LEVEL_TABLE_INDEX];
    for (var index = 0; index < 6; ++index)
      if ((pattern & (1 << (5 - index))) != 0)
        this._WriteCoefficients(levels.Slice(index * 64, 64), Mpeg4Quantisation.ZigZag, codes, first: 0, intra: false);

    this._Reconstruct(address, prediction, levels, pattern);
    this._Remember(address, vectorX, vectorY);
  }

  private void _WritePredictedMacroblockHeader(int pattern) {
    switch (this._version) {
      case MsMpeg4Version.Version3:
        this._writer.Write(
          MsMpeg4Data.MacroblockNonIntraCodes[pattern + 64], MsMpeg4Data.MacroblockNonIntraLengths[pattern + 64]);
        return;

      case MsMpeg4Version.Version2: {
        var chroma = pattern & 3;
        this._writer.Write(MsMpeg4Data.V2MacroblockTypeCodes[chroma], MsMpeg4Data.V2MacroblockTypeLengths[chroma]);

        // The luminance half is stated complemented unless both chrominance bits are set, which is the
        // one exception version 2 has and version 1 has not.
        var written = (chroma == 3 ? pattern : pattern ^ 0x3C) >> 2;
        this._writer.Write(
          MsMpeg4Data.CodedBlockPatternYCodes[written], MsMpeg4Data.CodedBlockPatternYLengths[written]);
        return;
      }

      default: {
        var chroma = pattern & 3;
        this._writer.Write(MsMpeg4Data.InterMacroblockCodes[chroma], MsMpeg4Data.InterMacroblockLengths[chroma]);
        var written = (pattern ^ 0x3C) >> 2;
        this._writer.Write(
          MsMpeg4Data.CodedBlockPatternYCodes[written], MsMpeg4Data.CodedBlockPatternYLengths[written]);
        return;
      }
    }
  }

  private void _WriteVector(int differenceX, int differenceY) {
    if (this._version == MsMpeg4Version.Version3) {
      MsMpeg4Tables.MotionVectorCodes[0].Write(this._writer, differenceX + 32, differenceY + 32);
      return;
    }

    this._WriteVectorComponent(differenceX);
    this._WriteVectorComponent(differenceY);
  }

  /// <summary>
  /// Writes one half of a vector difference the way versions 1 and 2 do: a magnitude, then a sign.
  /// </summary>
  /// <remarks>
  /// A difference of nought is the whole of the codeword and carries no sign after it, which is what
  /// makes a run of macroblocks moving together nearly free.
  /// </remarks>
  private void _WriteVectorComponent(int difference) {
    var magnitude = Math.Abs(difference);
    this._writer.Write(
      MsMpeg4Data.MotionVectorMagnitudeCodes[magnitude], MsMpeg4Data.MotionVectorMagnitudeLengths[magnitude]);

    if (magnitude != 0)
      this._writer.Write(difference < 0 ? 1 : 0, 1);
  }

  private void _QuantisePredicted(
    int address, int index, scoped ReadOnlySpan<int> prediction, scoped Span<int> levels) {
    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    this._Read(this._source, address, index, samples);

    for (var i = 0; i < 64; ++i)
      samples[i] -= prediction[i];

    MsMpeg4ForwardDct.Transform(samples, coefficients);

    for (var i = 0; i < 64; ++i)
      levels[i] = _QuantiseCoefficient(coefficients[i], this._quantiser);
  }

  private void _Reconstruct(int address, scoped Span<int> prediction, scoped Span<int> levels, int pattern) {
    Span<int> samples = stackalloc int[64];
    var multiplier = 2 * this._quantiser;
    var offset = (this._quantiser - 1) | 1;

    for (var index = 0; index < 6; ++index) {
      var predicted = prediction.Slice(index * 64, 64);

      if ((pattern & (1 << (5 - index))) == 0) {
        this._Store(address, index, predicted);
        continue;
      }

      var block = levels.Slice(index * 64, 64);
      for (var i = 0; i < 64; ++i) {
        var level = block[i];
        samples[i] = level == 0 ? 0 : level < 0 ? level * multiplier - offset : level * multiplier + offset;
      }

      Mpeg4InverseDct.Transform(samples);
      for (var i = 0; i < 64; ++i)
        samples[i] += predicted[i];

      this._Store(address, index, samples);
    }
  }

  private void _Remember(int address, int vectorX, int vectorY) {
    this._isDecoded[address] = true;
    this._vectorX[address] = (short)vectorX;
    this._vectorY[address] = (short)vectorY;
    this._intraPrediction.MarkUnavailable(address);
  }

  // ============================================================================================
  // Motion estimation
  // ============================================================================================

  /// <summary>
  /// Finds a motion vector for one macroblock: a three-step search from the prediction, then a
  /// half-sample refinement.
  /// </summary>
  /// <remarks>
  /// Started from the prediction rather than from nothing, because the difference from the prediction
  /// is what is written and a search that wandered away from it would spend bits saying so. The zero
  /// vector is tried as well and wins ties, since it is what makes an unchanging macroblock skippable.
  /// </remarks>
  private (int X, int Y) _Search(int address, int predictedX, int predictedY) {
    var (bestX, bestY) = (0, 0);
    var best = this._Distortion(address, 0, 0);

    var (startX, startY) = (predictedX & ~1, predictedY & ~1);
    var cost = this._Distortion(address, startX, startY);
    if (cost < best)
      (best, bestX, bestY) = (cost, startX, startY);

    for (var step = _SEARCH_RADIUS; step >= 1; step /= 2) {
      var (centreX, centreY) = (bestX, bestY);
      for (var dy = -1; dy <= 1; ++dy)
        for (var dx = -1; dx <= 1; ++dx) {
          if (dx == 0 && dy == 0)
            continue;

          // The last pass moves by a half sample; the ones before it move by whole ones.
          var x = Math.Clamp(centreX + step * dx, -_MAX_VECTOR, _MAX_VECTOR);
          var y = Math.Clamp(centreY + step * dy, -_MAX_VECTOR, _MAX_VECTOR);
          var candidate = this._Distortion(address, x, y);
          if (candidate < best)
            (best, bestX, bestY) = (candidate, x, y);
        }
    }

    return (bestX, bestY);
  }

  /// <summary>The absolute difference between a macroblock's luminance and what a vector predicts.</summary>
  private int _Distortion(int address, int vectorX, int vectorY) {
    Span<int> prediction = stackalloc int[64];
    Span<int> samples = stackalloc int[64];
    var total = 0;

    for (var index = 0; index < 4; ++index) {
      this._Predict(prediction, address, index, vectorX, vectorY);
      this._Read(this._source, address, index, samples);

      for (var i = 0; i < 64; ++i)
        total += Math.Abs(samples[i] - prediction[i]);
    }

    return total;
  }

  // ============================================================================================
  // The block layer
  // ============================================================================================

  /// <summary>
  /// Writes one block's coefficients as run-level codes, choosing the cheapest form for each.
  /// </summary>
  /// <remarks>
  /// The three escape forms are tried in the order the format's own bit patterns make cheapest: an
  /// ordinary code where the table has a row for the triple, then the first escape where the magnitude
  /// is only a little past what the table can say for that run, then the second where the run is a
  /// little past what it can say for that magnitude, and only then the third, which spends fifteen
  /// bits saying the triple outright. Version 1 has only the third and reaches it without spending the
  /// two bits that choose it.
  /// </remarks>
  private void _WriteCoefficients(
    scoped ReadOnlySpan<int> levels, int[] scan, MsMpeg4RunLevelTable codes, int first, bool intra) {
    var lastIndex = first - 1;
    for (var i = first; i < 64; ++i)
      if (levels[scan[i]] != 0)
        lastIndex = i;

    var runOffset = intra || this._version == MsMpeg4Version.Version2 ? 0 : 1;
    var previous = first - 1;

    for (var i = first; i <= lastIndex; ++i) {
      var level = levels[scan[i]];
      if (level == 0)
        continue;

      var run = i - previous - 1;
      var last = i == lastIndex;
      var sign = level < 0 ? 1 : 0;
      var magnitude = Math.Abs(level);
      previous = i;

      var index = codes.IndexOf(last, run, magnitude);
      if (index != codes.EscapeIndex) {
        this._writer.Write(codes.CodeOf(index), codes.LengthOf(index));
        this._writer.Write(sign, 1);
        continue;
      }

      this._writer.Write(codes.CodeOf(codes.EscapeIndex), codes.LengthOf(codes.EscapeIndex));

      if (this._version == MsMpeg4Version.Version1) {
        _WriteThirdEscape(this._writer, last, run, level, prefix: false);
        continue;
      }

      var reduced = magnitude - codes.LargestLevel(last, run);
      index = reduced >= 1 ? codes.IndexOf(last, run, reduced) : codes.EscapeIndex;
      if (index != codes.EscapeIndex) {
        this._writer.Write(1, 1);
        this._writer.Write(codes.CodeOf(index), codes.LengthOf(index));
        this._writer.Write(sign, 1);
        continue;
      }

      // The second escape says a run past what the table can state for this magnitude, so a magnitude
      // the table has no entry for at any run cannot be said that way at all.
      var shortened = magnitude <= _LARGEST_TABLE_LEVEL ? run - codes.LargestRun(last, magnitude) - runOffset : -1;
      index = shortened >= 0 ? codes.IndexOf(last, shortened, magnitude) : codes.EscapeIndex;
      if (index != codes.EscapeIndex) {
        this._writer.Write(0, 1);
        this._writer.Write(1, 1);
        this._writer.Write(codes.CodeOf(index), codes.LengthOf(index));
        this._writer.Write(sign, 1);
        continue;
      }

      _WriteThirdEscape(this._writer, last, run, level, prefix: true);
    }
  }

  private static void _WriteThirdEscape(MsMpeg4BitWriter writer, bool last, int run, int level, bool prefix) {
    if (prefix) {
      writer.Write(0, 1);
      writer.Write(0, 1);
    }

    writer.Write(last ? 1 : 0, 1);
    writer.Write(run, 6);
    writer.Write(level & 0xFF, 8);
  }

  private void _WriteVersion2Dc(int differential, bool isLuminance) {
    var (codes, lengths) = MsMpeg4Tables.V2DcCodes[isLuminance ? 0 : 1];
    var at = differential + MsMpeg4Tables.V2DcBias;
    this._writer.Write(codes[at], lengths[at]);
  }

  private void _WriteVersion3Dc(int differential, bool isLuminance) {
    var magnitude = Math.Abs(differential);
    var code = Math.Min(magnitude, MsMpeg4Tables.V3DcEscape);
    var table = isLuminance ? 0 : 1;
    var codes = table == 0 ? MsMpeg4Data.Dc0LuminanceCodes : MsMpeg4Data.Dc0ChrominanceCodes;
    var lengths = table == 0 ? MsMpeg4Data.Dc0LuminanceLengths : MsMpeg4Data.Dc0ChrominanceLengths;

    this._writer.Write(codes[code], lengths[code]);

    if (code == MsMpeg4Tables.V3DcEscape)
      this._writer.Write(magnitude, 8);

    if (magnitude != 0)
      this._writer.Write(differential < 0 ? 1 : 0, 1);
  }

  /// <summary>
  /// Quantises one coefficient by the H.263 rule, inverted.
  /// </summary>
  /// <remarks>
  /// The reconstruction levels are the odd multiples of the quantiser, so the level whose
  /// reconstruction is nearest a coefficient is that coefficient over twice the quantiser, rounded.
  /// Rounding rather than truncating costs a little in bits and gains rather more in accuracy, and it
  /// is what makes a picture coded at the finest quantiser come back to what went in.
  /// </remarks>
  private static int _QuantiseCoefficient(double coefficient, int quantiser) {
    var magnitude = Math.Abs(coefficient);
    var level = (int)Math.Round(magnitude / (2d * quantiser), MidpointRounding.AwayFromZero);
    if (level > _MAX_LEVEL)
      level = _MAX_LEVEL;

    return coefficient < 0 ? -level : level;
  }

  // ============================================================================================
  // Vectors, planes and slices
  // ============================================================================================

  private int _PredictVector(int address, bool horizontal) {
    var vectors = horizontal ? this._vectorX : this._vectorY;
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    var above = address - this._macroblockWidth;
    var hasAbove = row > 0 && this._SameSlice(address, above);

    var left = this._CandidateOf(column > 0 ? address - 1 : -1, vectors);
    var top = this._CandidateOf(hasAbove ? above : -1, vectors);
    var topRight = this._CandidateOf(hasAbove && column + 1 < this._macroblockWidth ? above + 1 : -1, vectors);

    return _Median(left, top, topRight);
  }

  private (int Value, bool Valid) _CandidateOf(int neighbour, short[] vectors)
    => neighbour < 0 || !this._isDecoded[neighbour] ? (0, false) : (vectors[neighbour], true);

  private bool _SameSlice(int address, int other)
    => address / this._macroblockWidth / this._sliceHeight == other / this._macroblockWidth / this._sliceHeight;

  private static int _Median((int Value, bool Valid) a, (int Value, bool Valid) b, (int Value, bool Valid) c) {
    var count = (a.Valid ? 1 : 0) + (b.Valid ? 1 : 0) + (c.Valid ? 1 : 0);
    switch (count) {
      case 0:
        return 0;

      case 1:
        return a.Valid ? a.Value : b.Valid ? b.Value : c.Value;
    }

    var x = a.Valid ? a.Value : 0;
    var y = b.Valid ? b.Value : 0;
    var z = c.Valid ? c.Value : 0;

    if (x > y)
      (x, y) = (y, x);

    if (y > z)
      y = z;

    return x > y ? x : y;
  }

  private void _ResetLastDc() {
    var absent = MsMpeg4IntraPrediction.AbsentDc(MsMpeg4BlockDecoder.FixedDcStep);
    this._lastDc[0] = absent;
    this._lastDc[1] = absent;
    this._lastDc[2] = absent;
  }

  private void _Predict(Span<int> prediction, int address, int index, int vectorX, int vectorY) {
    var (plane, stride, origin, width, height) = this._reference!.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);
    var border = index < 4 ? Mpeg4Frame.Border : Mpeg4Frame.Border / 2;

    Mpeg4MotionCompensation.PredictHalfSample(
      prediction, plane, stride, origin, border, width, height, left, top, vectorX, vectorY, rounding: 0);
  }

  private void _Read(Mpeg4Frame frame, int address, int index, scoped Span<int> samples) {
    var (plane, stride, origin, _, _) = frame.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);

    for (var y = 0; y < 8; ++y) {
      var row = origin + (top + y) * stride + left;
      for (var x = 0; x < 8; ++x)
        samples[y * 8 + x] = plane[row + x];
    }
  }

  private void _Store(int address, int index, scoped ReadOnlySpan<int> samples) {
    var (plane, stride, origin, _, _) = this._target.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);

    for (var y = 0; y < 8; ++y) {
      var row = origin + (top + y) * stride + left;
      for (var x = 0; x < 8; ++x) {
        var value = samples[y * 8 + x];
        plane[row + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  private (int Left, int Top) _BlockOrigin(int address, int index) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    return index < 4
      ? (column * 16 + (index & 1) * 8, row * 16 + (index >> 1) * 8)
      : (column * 8, row * 8);
  }
}
