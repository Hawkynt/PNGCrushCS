using System;
using System.IO;
using FileFormat.Codecs.Vp3;

namespace FileFormat.Codecs.Vp56;

/// <summary>
/// Shared VP5/VP6 macroblock, reference-frame, motion-vector and reconstruction engine.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's LGPL-2.1-or-later <c>libavcodec/vp56.c</c>. VP5 and VP6 do not have B
/// pictures: every inter picture is displayable immediately and predicts from either the immediately
/// previous reconstruction or the retained golden reconstruction. Consequently the complete frame
/// dependency state is exactly current/previous/golden; there is no display-order reorder queue.
/// </remarks>
internal abstract class Vp56Decoder {
  private readonly Vp56Model _model = new();
  private readonly short[][] _blockCoefficients = [
    new short[64], new short[64], new short[64], new short[64], new short[64], new short[64],
  ];
  private readonly int[] _idctSelector = new int[6];
  private readonly Vp56MotionVector[] _motion = new Vp56MotionVector[6];
  private readonly Vp56MotionVector[] _candidates = new Vp56MotionVector[2];
  private readonly Vp56DcPredictor[] _left = new Vp56DcPredictor[4];
  private readonly short[,] _previousDc = new short[3, 3];
  private readonly int[] _aboveIndex = new int[6];
  private readonly short[] _residual = new short[64];

  private Vp56Macroblock[] _macroblocks = [];
  private Vp56DcPredictor[] _above = [];
  private Vp56Frame? _previous;
  private Vp56Frame? _golden;
  private Vp56Frame? _current;
  private Vp56MacroblockType _previousMacroblockType;
  private int _candidatePosition;
  private bool _started;

  protected Vp56RangeDecoder Entropy;
  protected Vp56Model Model => this._model;
  protected short[][] BlockCoefficients => this._blockCoefficients;
  protected int[] IdctSelector => this._idctSelector;
  protected Vp56MotionVector[] Motion => this._motion;

  protected bool IsKeyFrame { get; set; }
  protected bool GoldenRefresh { get; set; }
  protected bool Interlaced { get; set; }
  protected bool InterlacedBlock { get; private set; }
  protected int InterlaceProbability { get; private set; }
  protected int Quantizer { get; private set; } = -1;
  protected int DequantDc { get; private set; }
  protected int DequantAc { get; private set; }
  protected int MacroblockWidth { get; private set; }
  protected int MacroblockHeight { get; private set; }

  internal int Width { get; private set; }
  internal int Height { get; private set; }
  internal int CodedWidth => this.MacroblockWidth * 16;
  internal int CodedHeight => this.MacroblockHeight * 16;

  /// <summary>Whether this variant uses VP5's extra above-left/above-right DC predictors.</summary>
  protected abstract bool UsesVp5DcPrediction { get; }

  /// <summary>Parses the variant-specific frame header and positions all entropy partitions.</summary>
  protected abstract Vp56Header ParseHeader(ReadOnlyMemory<byte> packet);

  protected abstract void InitializeDefaultModels();
  protected abstract void ParseVectorModels();
  protected abstract void ParseCoefficientModels();
  protected abstract void ParseCoefficients();
  protected abstract Vp56MotionVector ParseVectorAdjustment();

  /// <summary>
  /// Predicts one eight-by-eight block for a non-zero motion vector, including this codec's
  /// interpolation and any prediction-time loop filter.
  /// </summary>
  protected abstract void PredictMotionBlock(
    Vp56Frame reference,
    int plane,
    int originX,
    int originY,
    int rowStep,
    Vp56MotionVector motion,
    Span<byte> destination);

  /// <summary>Called at the start of every macroblock row for variant-specific row contexts.</summary>
  protected virtual void BeginMacroblockRow() { }

  /// <summary>Called after DC prediction has exposed the block's reference type to variant contexts.</summary>
  protected virtual void OnDcPredictorUpdated(int block, Vp56DcPredictor above) { }

  /// <summary>Updates the quantiser and the two scalar VP56 dequantisation factors.</summary>
  protected void SetQuantizer(int quantizer) {
    if ((uint)quantizer >= 64u)
      throw new InvalidDataException($"VP5/VP6 quantiser {quantizer} is outside 0..63.");

    this.Quantizer = quantizer;
    this.DequantDc = Vp56Data.DcDequant[quantizer] << 2;
    this.DequantAc = Vp56Data.AcDequant[quantizer] << 2;
  }

  /// <summary>Decodes one coded VP5/VP6 picture.</summary>
  internal Vp56Frame Decode(ReadOnlyMemory<byte> packet) {
    var header = this.ParseHeader(packet);
    this.IsKeyFrame = header.IsKeyFrame;
    this.GoldenRefresh = header.GoldenRefresh;

    if (!this._started && !header.IsKeyFrame)
      throw new InvalidDataException("A VP5/VP6 stream begins with an inter frame and has no reference picture.");

    if (header.IsKeyFrame)
      this._EnsureGeometry(header.CodedWidth, header.CodedHeight, header.DisplayWidth, header.DisplayHeight);
    else if (this._current is null)
      throw new InvalidDataException("A VP5/VP6 inter frame arrived before coded dimensions were established.");

    if (header.DisplayWidth > 0)
      this.Width = header.DisplayWidth;
    if (header.DisplayHeight > 0)
      this.Height = header.DisplayHeight;

    if (header.IsKeyFrame) {
      this.InitializeDefaultModels();
      for (var i = 0; i < this._macroblocks.Length; ++i) {
        this._macroblocks[i].Type = Vp56MacroblockType.Intra;
        this._macroblocks[i].MotionVector = Vp56MotionVector.Zero;
      }
    } else {
      this._ParseMacroblockTypeModels();
      this.ParseVectorModels();
      this._previousMacroblockType = Vp56MacroblockType.InterNoVectorPrevious;
    }

    this.ParseCoefficientModels();

    if (this.Interlaced)
      this.InterlaceProbability = this.Entropy.ReadLiteral(8);

    this._ResetDcPrediction();

    for (var row = 0; row < this.MacroblockHeight; ++row) {
      Array.Clear(this._left);
      this.BeginMacroblockRow();
      this._ResetAboveIndices();
      this.InterlacedBlock = false;

      for (var column = 0; column < this.MacroblockWidth; ++column) {
        this._DecodeMacroblock(row, column);
        this._AdvanceAboveIndices();
      }
    }

    var decoded = this._current!;
    if (header.IsKeyFrame || this.GoldenRefresh)
      this._golden!.CopyFrom(decoded);

    (this._previous, this._current) = (decoded, this._previous!);
    this._started = true;
    return this._previous;
  }

  private void _EnsureGeometry(int codedWidth, int codedHeight, int displayWidth, int displayHeight) {
    if (codedWidth <= 0 || codedHeight <= 0 || (codedWidth & 15) != 0 || (codedHeight & 15) != 0)
      throw new InvalidDataException($"VP5/VP6 coded dimensions {codedWidth}×{codedHeight} are not positive macroblock dimensions.");
    if (displayWidth <= 0 || displayHeight <= 0 || displayWidth > codedWidth || displayHeight > codedHeight)
      throw new InvalidDataException($"VP5/VP6 display dimensions {displayWidth}×{displayHeight} do not fit coded {codedWidth}×{codedHeight}.");

    var macroblockWidth = codedWidth >> 4;
    var macroblockHeight = codedHeight >> 4;
    if (macroblockWidth > 1000 || macroblockHeight > 1000)
      throw new InvalidDataException($"VP5/VP6 picture {codedWidth}×{codedHeight} exceeds the codec's 1000×1000 macroblock limit.");

    this.Width = displayWidth;
    this.Height = displayHeight;

    if (this.MacroblockWidth == macroblockWidth && this.MacroblockHeight == macroblockHeight && this._current is not null)
      return;

    this.MacroblockWidth = macroblockWidth;
    this.MacroblockHeight = macroblockHeight;
    this._macroblocks = new Vp56Macroblock[macroblockWidth * macroblockHeight];
    this._above = new Vp56DcPredictor[4 * macroblockWidth + 6];
    this._previous = new(macroblockWidth, macroblockHeight);
    this._golden = new(macroblockWidth, macroblockHeight);
    this._current = new(macroblockWidth, macroblockHeight);
    this._started = false;
  }

  private void _ParseMacroblockTypeModels() {
    var model = this._model;

    for (var context = 0; context < 3; ++context) {
      if (this.Entropy.ReadBool(174) != 0) {
        var preset = this.Entropy.ReadLiteral(4);
        for (var type = 0; type < 10; ++type)
        for (var statistic = 0; statistic < 2; ++statistic)
          model.MacroblockTypeStats[context, type, statistic] =
            Vp56Data.PredefinedMacroblockTypeStats[preset, context, type, statistic];
      }

      if (this.Entropy.ReadBool(254) == 0)
        continue;

      for (var type = 0; type < 10; ++type)
      for (var statistic = 0; statistic < 2; ++statistic) {
        if (this.Entropy.ReadBool(205) == 0)
          continue;

        var negative = this.Entropy.ReadFlag() != 0;
        var delta = this.Entropy.ReadTree(Vp56Data.MacroblockTypeModelTree, Vp56Data.MacroblockTypeModelModel);
        if (delta == 0)
          delta = 4 * this.Entropy.ReadLiteral(7);
        if (negative)
          delta = -delta;

        model.MacroblockTypeStats[context, type, statistic] =
          unchecked((byte)(model.MacroblockTypeStats[context, type, statistic] + delta));
      }
    }

    Span<int> weights = stackalloc int[10];
    for (var context = 0; context < 3; ++context) {
      for (var type = 0; type < 10; ++type)
        weights[type] = 100 * model.MacroblockTypeStats[context, type, 1];

      for (var previous = 0; previous < 10; ++previous) {
        var saved = weights[previous];
        weights[previous] = 0;

        var p02 = weights[0] + weights[2];
        var p34 = weights[3] + weights[4];
        var p0234 = p02 + p34;
        var p17 = weights[1] + weights[7];
        var p56 = weights[5] + weights[6];
        var p89 = weights[8] + weights[9];
        var p5689 = p56 + p89;
        var p156789 = p17 + p5689;
        var stat0 = model.MacroblockTypeStats[context, previous, 0];
        var stat1 = model.MacroblockTypeStats[context, previous, 1];

        model.MacroblockType[context, previous, 0] = (byte)(255 - 255 * stat0 / (1 + stat0 + stat1));
        model.MacroblockType[context, previous, 1] = (byte)(1 + 255 * p0234 / (1 + p0234 + p156789));
        model.MacroblockType[context, previous, 2] = (byte)(1 + 255 * p02 / (1 + p0234));
        model.MacroblockType[context, previous, 3] = (byte)(1 + 255 * p17 / (1 + p156789));
        model.MacroblockType[context, previous, 4] = (byte)(1 + 255 * weights[0] / (1 + p02));
        model.MacroblockType[context, previous, 5] = (byte)(1 + 255 * weights[3] / (1 + p34));
        model.MacroblockType[context, previous, 6] = (byte)(1 + 255 * weights[1] / (1 + p17));
        model.MacroblockType[context, previous, 7] = (byte)(1 + 255 * p56 / (1 + p5689));
        model.MacroblockType[context, previous, 8] = (byte)(1 + 255 * weights[5] / (1 + p56));
        model.MacroblockType[context, previous, 9] = (byte)(1 + 255 * weights[8] / (1 + p89));

        weights[previous] = saved;
      }
    }
  }

  private Vp56MacroblockType _ParseMacroblockType(int context) {
    Span<byte> probabilities = stackalloc byte[10];
    for (var i = 0; i < 10; ++i)
      probabilities[i] = this._model.MacroblockType[context, (int)this._previousMacroblockType, i];

    if (this.Entropy.ReadBool(probabilities[0]) != 0)
      return this._previousMacroblockType;

    return (Vp56MacroblockType)this.Entropy.ReadTree(Vp56Data.MacroblockTypeTree, probabilities);
  }

  private int _FindMotionCandidates(int row, int column, Vp56Reference reference) {
    var count = 0;
    var first = Vp56MotionVector.Zero;
    var second = Vp56MotionVector.Zero;
    this._candidatePosition = 12;

    for (var position = 0; position < Vp56Data.CandidatePredictorPositions.Length; ++position) {
      var (dx, dy) = Vp56Data.CandidatePredictorPositions[position];
      var x = column + dx;
      var y = row + dy;
      if ((uint)x >= (uint)this.MacroblockWidth || (uint)y >= (uint)this.MacroblockHeight)
        continue;

      var macroblock = this._macroblocks[y * this.MacroblockWidth + x];
      if (Vp56Data.ReferenceOfMacroblockType[(int)macroblock.Type] != reference)
        continue;

      var vector = macroblock.MotionVector;
      if (vector == Vp56MotionVector.Zero || vector == first)
        continue;

      if (count == 0) {
        first = vector;
        count = 1;
        this._candidatePosition = position;
        continue;
      }

      second = vector;
      count = 2;
      break;
    }

    this._candidates[0] = first;
    this._candidates[1] = second;

    // FFmpeg/On2's context numbering is deliberately odd: two predictors => 0, none => 1, one => 2.
    return count switch { 2 => 0, 0 => 1, _ => 2 };
  }

  protected Vp56MotionVector PrimaryMotionCandidate => this._candidates[0];
  protected int MotionCandidatePosition => this._candidatePosition;

  private Vp56MacroblockType _DecodeMotion(int row, int column) {
    var context = this._FindMotionCandidates(row, column, Vp56Reference.Previous);
    var type = this._ParseMacroblockType(context);
    this._previousMacroblockType = type;

    var vector = Vp56MotionVector.Zero;
    switch (type) {
      case Vp56MacroblockType.InterVector1Previous:
        vector = this._candidates[0];
        break;
      case Vp56MacroblockType.InterVector2Previous:
        vector = this._candidates[1];
        break;
      case Vp56MacroblockType.InterVector1Golden:
        this._FindMotionCandidates(row, column, Vp56Reference.Golden);
        vector = this._candidates[0];
        break;
      case Vp56MacroblockType.InterVector2Golden:
        this._FindMotionCandidates(row, column, Vp56Reference.Golden);
        vector = this._candidates[1];
        break;
      case Vp56MacroblockType.InterDeltaPrevious:
        vector = this.ParseVectorAdjustment();
        break;
      case Vp56MacroblockType.InterDeltaGolden:
        this._FindMotionCandidates(row, column, Vp56Reference.Golden);
        vector = this.ParseVectorAdjustment();
        break;
      case Vp56MacroblockType.InterFourVectors:
        this._DecodeFourVectors();
        this._macroblocks[row * this.MacroblockWidth + column] = new() {
          Type = type,
          MotionVector = this._motion[3],
        };
        return type;
    }

    for (var block = 0; block < 6; ++block)
      this._motion[block] = vector;

    this._macroblocks[row * this.MacroblockWidth + column] = new() { Type = type, MotionVector = vector };
    return type;
  }

  private void _DecodeFourVectors() {
    Span<Vp56MacroblockType> types = stackalloc Vp56MacroblockType[4];
    for (var block = 0; block < 4; ++block) {
      var coded = this.Entropy.ReadLiteral(2);
      types[block] = (Vp56MacroblockType)(coded == 0 ? 0 : coded + 1);
    }

    var sumX = 0;
    var sumY = 0;
    for (var block = 0; block < 4; ++block) {
      this._motion[block] = types[block] switch {
        Vp56MacroblockType.InterNoVectorPrevious => Vp56MotionVector.Zero,
        Vp56MacroblockType.InterDeltaPrevious => this.ParseVectorAdjustment(),
        Vp56MacroblockType.InterVector1Previous => this._candidates[0],
        Vp56MacroblockType.InterVector2Previous => this._candidates[1],
        _ => throw new InvalidDataException($"VP5/VP6 four-vector block selected impossible mode {(int)types[block]}.")
      };
      sumX += this._motion[block].X;
      sumY += this._motion[block].Y;
    }

    var chroma = new Vp56MotionVector((short)_RoundedShift(sumX, 2), (short)_RoundedShift(sumY, 2));
    this._motion[4] = chroma;
    this._motion[5] = chroma;
  }

  private static int _RoundedShift(int value, int bits) {
    var half = 1 << (bits - 1);
    return value > 0 ? (value + half) >> bits : (value + half - 1) >> bits;
  }

  private void _DecodeMacroblock(int row, int column) {
    if (this.Interlaced) {
      var probability = this.InterlaceProbability;
      if (column > 0) {
        if (this.InterlacedBlock)
          probability -= probability >> 1;
        else
          probability += (256 - probability) >> 1;
      }
      this.InterlacedBlock = this.Entropy.ReadBool(probability) != 0;
    } else {
      this.InterlacedBlock = false;
    }

    var type = this.IsKeyFrame ? Vp56MacroblockType.Intra : this._DecodeMotion(row, column);
    for (var block = 0; block < 6; ++block) {
      Array.Clear(this._blockCoefficients[block]);
      this._idctSelector[block] = 63;
    }

    this.ParseCoefficients();
    this._RenderMacroblock(row, column, type);
  }

  private void _RenderMacroblock(int row, int column, Vp56MacroblockType type) {
    var referenceKind = Vp56Data.ReferenceOfMacroblockType[(int)type];
    this._AddDcPredictors(referenceKind);

    var reference = referenceKind switch {
      Vp56Reference.Previous => this._previous,
      Vp56Reference.Golden => this._golden,
      _ => null,
    };
    if (type != Vp56MacroblockType.Intra && reference is null)
      throw new InvalidDataException("VP5/VP6 inter macroblock refers to a reference picture that does not exist.");

    for (var block = 0; block < 6; ++block) {
      var plane = Vp56Data.BlockToPlane[block];
      var x = column * (plane == 0 ? 16 : 8) + (block is 1 or 3 ? 8 : 0);
      var rowOffset = block is 2 or 3 ? (this.InterlacedBlock ? 1 : 8) : 0;
      var y = row * (plane == 0 ? 16 : 8) + (plane == 0 ? rowOffset : 0);
      var rowStep = plane == 0 && this.InterlacedBlock ? 2 : 1;
      Span<byte> predictor = stackalloc byte[64];

      switch (type) {
        case Vp56MacroblockType.Intra:
          predictor.Fill(128);
          break;
        case Vp56MacroblockType.InterNoVectorPrevious:
        case Vp56MacroblockType.InterNoVectorGolden:
          _CopyBlock(reference!, plane, x, y, rowStep, predictor);
          break;
        default:
          this.PredictMotionBlock(reference!, plane, x, y, rowStep, this._motion[block], predictor);
          break;
      }

      this._AddResidual(block, predictor);
      _StoreBlock(this._current!, plane, x, y, rowStep, predictor);
    }
  }

  private void _AddDcPredictors(Vp56Reference reference) {
    for (var block = 0; block < 6; ++block) {
      ref var above = ref this._above[this._aboveIndex[block]];
      ref var left = ref this._left[Vp56Data.BlockToPredictor[block]];
      var count = 0;
      var dc = 0;

      if (left.Reference == reference) {
        dc += left.Dc;
        ++count;
      }
      if (above.Reference == reference) {
        dc += above.Dc;
        ++count;
      }

      if (this.UsesVp5DcPrediction) {
        for (var side = -1; side <= 1 && count < 2; side += 2) {
          var index = this._aboveIndex[block] + side;
          if ((uint)index >= (uint)this._above.Length || this._above[index].Reference != reference)
            continue;
          dc += this._above[index].Dc;
          ++count;
        }
      }

      var plane = Vp56Data.BlockToPlane[block];
      if (count == 0)
        dc = this._previousDc[plane, (int)reference];
      else if (count == 2)
        dc /= 2;

      var predicted = unchecked((short)(this._blockCoefficients[block][0] + dc));
      this._blockCoefficients[block][0] = predicted;
      this._previousDc[plane, (int)reference] = predicted;
      above.Dc = predicted;
      above.Reference = reference;
      left.Dc = predicted;
      left.Reference = reference;
      this.OnDcPredictorUpdated(block, above);
      this._blockCoefficients[block][0] = unchecked((short)(predicted * this.DequantDc));
    }
  }

  private void _AddResidual(int block, Span<byte> samples) {
    var coefficients = this._blockCoefficients[block];
    var anyAc = false;
    for (var i = 1; i < 64; ++i)
      anyAc |= coefficients[i] != 0;

    if (!anyAc) {
      // This is the exact DC-only path used by VP3's DSP: after the first 1-D pass the remaining
      // C4 multiply and the final /16 collapse to the same direct result, including the +8 rounding.
      var firstPass = unchecked((short)((Vp3Tables.Cosines[4] * coefficients[0]) >> 16));
      var value = (Vp3Tables.Cosines[4] * firstPass + (8 << 16)) >> 20;
      if (value == 0)
        return;
      for (var i = 0; i < 64; ++i)
        samples[i] = _Clamp(samples[i] + value);
      return;
    }

    Vp3InverseDct.Transform(coefficients, this._residual);
    for (var i = 0; i < 64; ++i)
      samples[i] = _Clamp(samples[i] + this._residual[i]);
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  private static void _CopyBlock(Vp56Frame source, int plane, int x, int y, int rowStep, Span<byte> destination) {
    var samples = source.Plane(plane);
    var width = source.PlaneWidth(plane);
    var height = source.PlaneHeight(plane);
    for (var row = 0; row < 8; ++row) {
      var sourceY = Math.Clamp(y + row * rowStep, 0, height - 1);
      for (var column = 0; column < 8; ++column)
        destination[row * 8 + column] = samples[sourceY * width + Math.Clamp(x + column, 0, width - 1)];
    }
  }

  private static void _StoreBlock(Vp56Frame target, int plane, int x, int y, int rowStep, ReadOnlySpan<byte> source) {
    var samples = target.Plane(plane);
    var width = target.PlaneWidth(plane);
    var height = target.PlaneHeight(plane);
    for (var row = 0; row < 8; ++row) {
      var targetY = y + row * rowStep;
      if ((uint)targetY >= (uint)height)
        continue;
      source.Slice(row * 8, 8).CopyTo(samples.AsSpan(targetY * width + x, 8));
    }
  }

  private void _ResetDcPrediction() {
    Array.Clear(this._previousDc);
    this._previousDc[1, (int)Vp56Reference.Current] = 128;
    this._previousDc[2, (int)Vp56Reference.Current] = 128;

    Array.Clear(this._above);
    for (var i = 0; i < this._above.Length; ++i)
      this._above[i].Reference = Vp56Reference.None;
    this._above[2 * this.MacroblockWidth + 2].Reference = Vp56Reference.Current;
    this._above[3 * this.MacroblockWidth + 4].Reference = Vp56Reference.Current;
  }

  private void _ResetAboveIndices() {
    this._aboveIndex[0] = 1;
    this._aboveIndex[1] = 2;
    this._aboveIndex[2] = 1;
    this._aboveIndex[3] = 2;
    this._aboveIndex[4] = 2 * this.MacroblockWidth + 3;
    this._aboveIndex[5] = 3 * this.MacroblockWidth + 5;
  }

  private void _AdvanceAboveIndices() {
    for (var block = 0; block < 4; ++block)
      this._aboveIndex[block] += 2;
    this._aboveIndex[4]++;
    this._aboveIndex[5]++;
  }
}

internal readonly record struct Vp56Header(
  bool IsKeyFrame,
  int CodedWidth,
  int CodedHeight,
  int DisplayWidth,
  int DisplayHeight,
  bool GoldenRefresh = false);
