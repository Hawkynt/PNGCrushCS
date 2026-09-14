using System;
using System.IO;

namespace FileFormat.Codecs.Vp56;

/// <summary>On2 VP5 picture decoder.</summary>
/// <remarks>
/// Adapted from FFmpeg's LGPL-2.1-or-later <c>vp5.c</c>, <c>vp56.c</c> and <c>vp5dsp.c</c>.
/// VP5 has I pictures and forward-predicted P pictures only. P macroblocks may predict from the
/// immediately previous picture or the retained golden picture and may carry one or four motion
/// vectors. There is no B-picture reorder queue.
/// </remarks>
internal sealed class Vp5Decoder : Vp56Decoder {
  private readonly byte[,] _coefficientContexts = new byte[4, 64];
  private readonly byte[] _lastCoefficient = new byte[4];
  private byte[] _aboveNotNull = [];
  private int _column;

  protected override bool UsesVp5DcPrediction => true;

  protected override Vp56Header ParseHeader(ReadOnlyMemory<byte> packet) {
    if (packet.IsEmpty)
      throw new InvalidDataException("A VP5 frame has no range-coder bytes.");

    this.Entropy = new(packet);
    var isKeyFrame = this.Entropy.ReadFlag() == 0;
    _ = this.Entropy.ReadFlag(); // reserved
    this.SetQuantizer(this.Entropy.ReadLiteral(6));

    if (!isKeyFrame)
      return new(false, this.CodedWidth, this.CodedHeight, this.Width, this.Height);

    _ = this.Entropy.ReadLiteral(8); // minor version
    var majorVersion = this.Entropy.ReadLiteral(5);
    if (majorVersion > 5)
      throw new InvalidDataException($"VP5 major bitstream version {majorVersion} is outside the defined 0..5 range.");

    _ = this.Entropy.ReadLiteral(2);
    this.Interlaced = this.Entropy.ReadFlag() != 0;

    var rows = this.Entropy.ReadLiteral(8);
    var columns = this.Entropy.ReadLiteral(8);
    if (rows == 0 || columns == 0)
      throw new InvalidDataException($"VP5 key frame states an invalid coded size of {columns << 4}×{rows << 4}.");

    var displayRows = this.Entropy.ReadLiteral(8);
    var displayColumns = this.Entropy.ReadLiteral(8);
    if (displayRows == 0 || displayColumns == 0 || displayRows > rows || displayColumns > columns)
      throw new InvalidDataException(
        $"VP5 display size {displayColumns << 4}×{displayRows << 4} does not fit coded {columns << 4}×{rows << 4}.");

    _ = this.Entropy.ReadLiteral(2);
    return new(true, columns << 4, rows << 4, displayColumns << 4, displayRows << 4);
  }

  protected override void InitializeDefaultModels() {
    var model = this.Model;
    for (var component = 0; component < 2; ++component) {
      model.VectorSign[component] = 0x80;
      model.VectorDct[component] = 0x80;
      model.VectorPdi[component, 0] = 0x55;
      model.VectorPdi[component, 1] = 0x80;
      for (var node = 0; node < 7; ++node)
        model.VectorPdv[component, node] = 0x80;
    }

    Vp56Data.ResetMacroblockStats(model);
  }

  protected override void ParseVectorModels() {
    var model = this.Model;
    for (var component = 0; component < 2; ++component) {
      if (this.Entropy.ReadBool(Vp5Data.VectorModelUpdate[component, 0]) != 0)
        model.VectorDct[component] = this.Entropy.ReadProbability();
      if (this.Entropy.ReadBool(Vp5Data.VectorModelUpdate[component, 1]) != 0)
        model.VectorSign[component] = this.Entropy.ReadProbability();
      if (this.Entropy.ReadBool(Vp5Data.VectorModelUpdate[component, 2]) != 0)
        model.VectorPdi[component, 0] = this.Entropy.ReadProbability();
      if (this.Entropy.ReadBool(Vp5Data.VectorModelUpdate[component, 3]) != 0)
        model.VectorPdi[component, 1] = this.Entropy.ReadProbability();
    }

    for (var component = 0; component < 2; ++component)
    for (var node = 0; node < 7; ++node)
      if (this.Entropy.ReadBool(Vp5Data.VectorModelUpdate[component, node + 4]) != 0)
        model.VectorPdv[component, node] = this.Entropy.ReadProbability();
  }

  protected override void ParseCoefficientModels() {
    var model = this.Model;
    Span<byte> defaults = stackalloc byte[11];
    defaults.Fill(0x80);

    for (var planeType = 0; planeType < 2; ++planeType)
    for (var node = 0; node < 11; ++node) {
      if (this.Entropy.ReadBool(Vp5Data.DcCoefficientUpdate[planeType, node]) != 0) {
        defaults[node] = this.Entropy.ReadProbability();
        model.CoeffDccv[planeType, node] = defaults[node];
      } else if (this.IsKeyFrame) {
        model.CoeffDccv[planeType, node] = defaults[node];
      }
    }

    for (var codeType = 0; codeType < 3; ++codeType)
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var group = 0; group < 6; ++group)
    for (var node = 0; node < 11; ++node) {
      if (this.Entropy.ReadBool(Vp5Data.RunAcCoefficientUpdate[codeType, planeType, group, node]) != 0) {
        defaults[node] = this.Entropy.ReadProbability();
        model.CoeffRact[planeType, codeType, group, node] = defaults[node];
      } else if (this.IsKeyFrame) {
        model.CoeffRact[planeType, codeType, group, node] = defaults[node];
      }
    }

    for (var planeType = 0; planeType < 2; ++planeType)
    for (var context = 0; context < 36; ++context)
    for (var node = 0; node < 5; ++node) {
      var (scale, offset) = Vp5Data.DcContextLinear[node, context];
      var value = ((model.CoeffDccv[planeType, node] * scale + 128) >> 8) + offset;
      model.CoeffDcct[planeType, context, node] = (byte)Math.Clamp(value, 1, 254);
    }

    for (var codeType = 0; codeType < 3; ++codeType)
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var group = 0; group < 3; ++group)
    for (var context = 0; context < 6; ++context)
    for (var node = 0; node < 5; ++node) {
      var (scale, offset) = Vp5Data.AcContextLinear[codeType, group, node, context];
      var value = ((model.CoeffRact[planeType, codeType, group, node] * scale + 128) >> 8) + offset;
      model.CoeffAcct[planeType, codeType, group, context, node] = (byte)Math.Clamp(value, 1, 254);
    }

    var aboveLength = 4 * this.MacroblockWidth + 6;
    if (this._aboveNotNull.Length != aboveLength)
      this._aboveNotNull = new byte[aboveLength];
    else
      Array.Clear(this._aboveNotNull);
  }

  protected override void BeginMacroblockRow() {
    this._column = 0;
    Array.Clear(this._coefficientContexts);
    this._lastCoefficient.AsSpan().Fill(24);
  }

  protected override Vp56MotionVector ParseVectorAdjustment() {
    Span<byte> probabilities = stackalloc byte[7];
    short x = 0;
    short y = 0;

    for (var component = 0; component < 2; ++component) {
      var delta = 0;
      if (this.Entropy.ReadBool(this.Model.VectorDct[component]) != 0) {
        var negative = this.Entropy.ReadBool(this.Model.VectorSign[component]) != 0;
        var low = this.Entropy.ReadBool(this.Model.VectorPdi[component, 0]);
        low |= this.Entropy.ReadBool(this.Model.VectorPdi[component, 1]) << 1;
        for (var node = 0; node < 7; ++node)
          probabilities[node] = this.Model.VectorPdv[component, node];
        delta = low | (this.Entropy.ReadTree(Vp56Data.VectorAdjustmentTree, probabilities) << 2);
        if (negative)
          delta = -delta;
      }

      if (component == 0)
        x = (short)delta;
      else
        y = (short)delta;
    }

    return new(x, y);
  }

  protected override void ParseCoefficients() {
    Span<byte> model1 = stackalloc byte[11];
    Span<byte> model2 = stackalloc byte[5];
    var planeType = 0;

    for (var block = 0; block < 6; ++block) {
      var predictor = Vp56Data.BlockToPredictor[block];
      if (block > 3)
        planeType = 1;

      var aboveIndex = this._AboveIndex(block, this._column);
      var context = 6 * this._coefficientContexts[predictor, 0] + this._aboveNotNull[aboveIndex];
      this._CopyDccv(planeType, model1);
      this._CopyDcct(planeType, context, model2);

      var coefficientIndex = 0;
      var codeType = 1;
      for (;;) {
        if (this.Entropy.ReadBool(model2[0]) != 0) {
          int coefficient;
          int sign;

          if (this.Entropy.ReadBool(model2[2]) != 0) {
            if (this.Entropy.ReadBool(model2[3]) != 0) {
              this._coefficientContexts[predictor, coefficientIndex] = 4;
              var treeIndex = this.Entropy.ReadTree(Vp56Data.CoefficientTree, model1);
              sign = this.Entropy.ReadFlag();
              coefficient = Vp56Data.CoefficientBias[treeIndex + 5];
              for (var bit = Vp56Data.CoefficientBitLength[treeIndex]; bit >= 0; --bit)
                coefficient += this.Entropy.ReadBool(Vp56Data.CoefficientParseTable[treeIndex, bit]) << bit;
            } else {
              if (this.Entropy.ReadBool(model2[4]) != 0) {
                coefficient = 3 + this.Entropy.ReadBool(model1[5]);
                this._coefficientContexts[predictor, coefficientIndex] = 3;
              } else {
                coefficient = 2;
                this._coefficientContexts[predictor, coefficientIndex] = 2;
              }
              sign = this.Entropy.ReadFlag();
            }
            codeType = 2;
          } else {
            codeType = 1;
            this._coefficientContexts[predictor, coefficientIndex] = 1;
            sign = this.Entropy.ReadFlag();
            coefficient = 1;
          }

          if (sign != 0)
            coefficient = -coefficient;
          if (coefficientIndex != 0)
            coefficient *= this.DequantAc;

          this.BlockCoefficients[block][Vp5Data.ZigZagDirect[coefficientIndex]] = unchecked((short)coefficient);
        } else {
          if (codeType != 0 && this.Entropy.ReadBool(model2[1]) == 0)
            break;
          codeType = 0;
          this._coefficientContexts[predictor, coefficientIndex] = 0;
        }

        if (++coefficientIndex >= 64)
          break;

        var group = Vp5Data.CoefficientGroups[coefficientIndex];
        context = this._coefficientContexts[predictor, coefficientIndex];
        this._CopyRact(planeType, codeType, group, model1);
        if (group > 2)
          model1[..5].CopyTo(model2);
        else
          this._CopyAcct(planeType, codeType, group, context, model2);
      }

      var previousLast = Math.Min(this._lastCoefficient[predictor], (byte)24);
      this._lastCoefficient[predictor] = (byte)coefficientIndex;
      if (coefficientIndex < previousLast)
        for (var i = coefficientIndex; i <= previousLast; ++i)
          this._coefficientContexts[predictor, i] = 5;

      this._aboveNotNull[aboveIndex] = this._coefficientContexts[predictor, 0];
      this.IdctSelector[block] = 63;
    }

    ++this._column;
  }

  protected override void PredictMotionBlock(
    Vp56Frame reference,
    int plane,
    int originX,
    int originY,
    int rowStep,
    Vp56MotionVector motion,
    Span<byte> destination) {
    var divisor = plane == 0 ? 2 : 4;
    var integerX = motion.X / divisor;
    var integerY = motion.Y / divisor;
    var samples = reference.Plane(plane);
    var width = reference.PlaneWidth(plane);
    var height = reference.PlaneHeight(plane);

    Span<byte> window = stackalloc byte[12 * 12];
    for (var row = 0; row < 12; ++row) {
      var sourceY = Math.Clamp(originY + (integerY + row - 2) * rowStep, 0, height - 1);
      for (var column = 0; column < 12; ++column) {
        var sourceX = Math.Clamp(originX + integerX + column - 2, 0, width - 1);
        window[row * 12 + column] = samples[sourceY * width + sourceX];
      }
    }

    var threshold = Vp56Data.FilterThreshold[this.Quantizer];
    var edgeX = integerX & 7;
    var edgeY = integerY & 7;
    if (edgeX != 0)
      _FilterVerticalEdge(window, 10 - edgeX, threshold);
    if (edgeY != 0)
      _FilterHorizontalEdge(window, 10 - edgeY, threshold);

    var mask = divisor - 1;
    var offsetX = (motion.X & mask) == 0 ? 0 : motion.X > 0 ? 1 : -1;
    var offsetY = (motion.Y & mask) == 0 ? 0 : motion.Y > 0 ? 1 : -1;

    for (var row = 0; row < 8; ++row)
    for (var column = 0; column < 8; ++column) {
      var first = window[(row + 2) * 12 + column + 2];
      if (offsetX == 0 && offsetY == 0) {
        destination[row * 8 + column] = first;
        continue;
      }

      var second = window[(row + 2 + offsetY) * 12 + column + 2 + offsetX];
      destination[row * 8 + column] = (byte)((first + second) >> 1);
    }
  }

  private int _AboveIndex(int block, int column) => block switch {
    0 or 2 => 2 * column + 1,
    1 or 3 => 2 * column + 2,
    4 => 2 * this.MacroblockWidth + 3 + column,
    5 => 3 * this.MacroblockWidth + 5 + column,
    _ => throw new ArgumentOutOfRangeException(nameof(block)),
  };

  private void _CopyDccv(int planeType, Span<byte> destination) {
    for (var node = 0; node < 11; ++node)
      destination[node] = this.Model.CoeffDccv[planeType, node];
  }

  private void _CopyDcct(int planeType, int context, Span<byte> destination) {
    for (var node = 0; node < 5; ++node)
      destination[node] = this.Model.CoeffDcct[planeType, context, node];
  }

  private void _CopyRact(int planeType, int codeType, int group, Span<byte> destination) {
    for (var node = 0; node < 11; ++node)
      destination[node] = this.Model.CoeffRact[planeType, codeType, group, node];
  }

  private void _CopyAcct(int planeType, int codeType, int group, int context, Span<byte> destination) {
    for (var node = 0; node < 5; ++node)
      destination[node] = this.Model.CoeffAcct[planeType, codeType, group, context, node];
  }

  /// <summary>VP5's 12-sample horizontal-pixel edge filter, one edge point per row.</summary>
  private static void _FilterVerticalEdge(Span<byte> window, int x, int threshold) {
    for (var row = 0; row < 12; ++row) {
      var at = row * 12 + x;
      var adjustment = _FilterAdjustment(
        window[at - 2], window[at - 1], window[at], window[at + 1], threshold);
      window[at - 1] = _Clamp(window[at - 1] + adjustment);
      window[at] = _Clamp(window[at] - adjustment);
    }
  }

  /// <summary>VP5's 12-sample vertical-pixel edge filter, one edge point per column.</summary>
  private static void _FilterHorizontalEdge(Span<byte> window, int y, int threshold) {
    for (var column = 0; column < 12; ++column) {
      var at = y * 12 + column;
      var adjustment = _FilterAdjustment(
        window[at - 24], window[at - 12], window[at], window[at + 12], threshold);
      window[at - 12] = _Clamp(window[at - 12] + adjustment);
      window[at] = _Clamp(window[at] - adjustment);
    }
  }

  private static int _FilterAdjustment(int minusTwo, int minusOne, int zero, int plusOne, int threshold) {
    var value = (minusTwo + 3 * (zero - minusOne) - plusOne + 4) >> 3;
    var sign = value >> 31;
    value ^= sign;
    value -= sign;
    value *= value < 2 * threshold ? 1 : 0;
    value -= threshold;
    var innerSign = value >> 31;
    value ^= innerSign;
    value -= innerSign;
    value = threshold - value;
    value += sign;
    value ^= sign;
    return value;
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
