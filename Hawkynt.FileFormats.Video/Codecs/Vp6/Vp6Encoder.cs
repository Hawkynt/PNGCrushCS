using System;
using System.Collections.Generic;
using FileFormat.Codecs.Vp3;
using FileFormat.Codecs.Vp5;
using FileFormat.Codecs.Vp9;

namespace FileFormat.Codecs.Vp6;

/// <summary>
/// Writes one VP6 bitstream: intra key frames, and inter frames whose macroblocks are copied, moved by
/// a searched whole-sample vector, or intra-coded, all through the range coder in a single partition.
/// </summary>
/// <remarks>
/// FFmpeg decodes VP6 and has no encoder, so this is an original inverse of <see cref="Vp6Decoder"/>.
/// Every probability it codes with is the one the decoder will hold at that point, computed by the
/// same <see cref="Vp6Model"/> methods; every prediction it codes a residual against is the decoder's
/// own, asked of the decoder through <see cref="Vp6Decoder.Predict"/>; and the reference it predicts
/// the next frame from is what the decoder makes of the frame just written. The two cannot drift.
/// <para/>
/// The models are the defaults throughout — no probability or scan-order update is transmitted — and
/// the coefficient filter is the bilinear one with no loop filter, which is the simplest valid form of
/// every choice the header offers.
/// </remarks>
internal sealed class Vp6Encoder {

  private const int _SUB_VERSION = 7;
  private const int _FILTER_HEADER = 0x02;
  private const int _SEARCH_RANGE = 8;
  private const int _INTRA_BIAS = 128;

  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _quantizer;
  private readonly int _dequantDc;
  private readonly int _dequantAc;
  private readonly Vp6Model _model = new();
  private readonly Vp6Decoder _decoder;
  private readonly Vp5Macroblock[] _macroblocks;
  private readonly Vp5DcPredictor[] _above;
  private readonly Vp5DcPredictor[] _left = new Vp5DcPredictor[4];
  private readonly short[,] _previousDc = new short[3, 3];
  private readonly int[] _aboveIndex = new int[6];
  private readonly Vp5MotionVector[] _candidates = new Vp5MotionVector[2];
  private readonly short[][] _levels = [new short[64], new short[64], new short[64], new short[64], new short[64], new short[64]];
  private Vp5MacroblockType _previousType;
  private int _vectorCandidatePosition;

  internal Vp6Encoder(int macroblockWidth, int macroblockHeight, int quantizer, bool lumaOnly) {
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;
    this._quantizer = quantizer;
    this._dequantDc = Vp5Tables.DcDequant[quantizer] << 2;
    this._dequantAc = Vp5Tables.AcDequant[quantizer] << 2;
    this._decoder = new(lumaOnly);
    this._macroblocks = new Vp5Macroblock[macroblockWidth * macroblockHeight];
    this._above = new Vp5DcPredictor[4 * macroblockWidth + 6];
  }

  /// <summary>What the decoder made of the last frame written: the reference the next one predicts from.</summary>
  internal Vp5Frame? Reconstruction => this._decoder.LastDecoded;

  /// <summary>Codes one picture, given as coded-size planes in coded row order.</summary>
  internal byte[] Encode(byte[] luma, byte[] cb, byte[] cr, bool keyFrame) {
    var reference = this._decoder.LastDecoded;
    if (reference == null)
      keyFrame = true;

    var writer = new Vp9BoolEncoder();
    if (keyFrame) {
      writer.WriteLiteral(2, 0);
      writer.WriteFlag(0); // filter mode is not 2
      writer.WriteFlag(0); // nor 1: bilinear
      writer.WriteFlag(0); // range-coded coefficients
      this._InitializeKeyFrameModels();
    } else {
      writer.WriteFlag(0); // the golden picture is not replaced
      writer.WriteFlag(0); // no loop filter
      writer.WriteFlag(0); // range-coded coefficients
      for (var context = 0; context < 3; ++context) {
        writer.WriteBool(174, 0);
        writer.WriteBool(254, 0);
      }
      this._model.ComputeTypeProbabilities();
      for (var component = 0; component < 2; ++component) {
        writer.WriteBool(Vp6Tables.SignDctUpdate[component, 0], 0);
        writer.WriteBool(Vp6Tables.SignDctUpdate[component, 1], 0);
      }
      for (var component = 0; component < 2; ++component)
      for (var node = 0; node < 7; ++node)
        writer.WriteBool(Vp6Tables.PdvUpdate[component, node], 0);
      for (var component = 0; component < 2; ++component)
      for (var node = 0; node < 8; ++node)
        writer.WriteBool(Vp6Tables.FdvUpdate[component, node], 0);
      this._previousType = Vp5MacroblockType.InterNoVectorPrevious;
    }

    // No coefficient probability, scan order or run probability is updated.
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var node = 0; node < 11; ++node)
      writer.WriteBool(Vp6Tables.DcValueUpdate[planeType, node], 0);
    writer.WriteFlag(0);
    for (var group = 0; group < 2; ++group)
    for (var node = 0; node < 14; ++node)
      writer.WriteBool(Vp6Tables.RunValueUpdate[group, node], 0);
    for (var codeType = 0; codeType < 3; ++codeType)
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var group = 0; group < 6; ++group)
    for (var node = 0; node < 11; ++node)
      writer.WriteBool(Vp6Tables.RunAcUpdate[codeType, planeType, group, node], 0);
    this._model.ComputeDcCodingTypes();

    this._ResetPrediction();
    var picture = new Picture(luma, cb, cr, this._macroblockWidth * 16, this._macroblockHeight * 16);

    for (var row = 0; row < this._macroblockHeight; ++row) {
      for (var i = 0; i < 4; ++i)
        this._left[i] = new() { Reference = Vp5Reference.None };
      this._aboveIndex[0] = 1;
      this._aboveIndex[1] = 2;
      this._aboveIndex[2] = 1;
      this._aboveIndex[3] = 2;
      this._aboveIndex[4] = 2 * this._macroblockWidth + 3;
      this._aboveIndex[5] = 3 * this._macroblockWidth + 5;

      for (var column = 0; column < this._macroblockWidth; ++column) {
        this._EncodeMacroblock(writer, picture, keyFrame ? null : reference, row, column);
        for (var y = 0; y < 4; ++y)
          this._aboveIndex[y] += 2;
        this._aboveIndex[4] += 1;
        this._aboveIndex[5] += 1;
      }
    }

    var payload = writer.Finish();
    var header = keyFrame ? 6 : 1;
    var packet = new byte[header + payload.Length];
    packet[0] = (byte)((keyFrame ? 0 : 0x80) | (this._quantizer << 1));
    if (keyFrame) {
      packet[1] = (byte)((_SUB_VERSION << 3) | _FILTER_HEADER);
      packet[2] = (byte)this._macroblockHeight;
      packet[3] = (byte)this._macroblockWidth;
      packet[4] = (byte)this._macroblockHeight;
      packet[5] = (byte)this._macroblockWidth;
    }
    payload.CopyTo(packet, header);

    this._decoder.Decode(packet);
    return packet;
  }

  private void _InitializeKeyFrameModels() {
    var model = this._model;
    model.VectorDct[0] = 0xA2;
    model.VectorDct[1] = 0xA4;
    model.VectorSign[0] = 0x80;
    model.VectorSign[1] = 0x80;
    for (var context = 0; context < 3; ++context)
    for (var type = 0; type < 10; ++type)
    for (var statistic = 0; statistic < 2; ++statistic)
      model.TypeStats[context, type, statistic] = Vp5Tables.DefaultMacroblockTypeStats[context, type, statistic];
    for (var component = 0; component < 2; ++component) {
      for (var node = 0; node < 8; ++node)
        model.VectorFdv[component, node] = Vp6Tables.DefaultFdvVectorModel[component, node];
      for (var node = 0; node < 7; ++node)
        model.VectorPdv[component, node] = Vp6Tables.DefaultPdvVectorModel[component, node];
      for (var node = 0; node < 14; ++node)
        model.RunValue[component, node] = Vp6Tables.DefaultRunValueModel[component, node];
    }
    Vp6Tables.DefaultCoefficientReorder.CopyTo(model.CoefficientReorder, 0);
    model.InitializeCoefficientOrder(_SUB_VERSION);

    // A key frame that transmits no probability takes 0x80 for every coefficient node.
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var node = 0; node < 11; ++node)
      model.DcValue[planeType, node] = 0x80;
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var codeType = 0; codeType < 3; ++codeType)
    for (var group = 0; group < 6; ++group)
    for (var node = 0; node < 11; ++node)
      model.RunAc[planeType, codeType, group, node] = 0x80;

    for (var i = 0; i < this._macroblocks.Length; ++i)
      this._macroblocks[i].Type = Vp5MacroblockType.Intra;
  }

  private void _ResetPrediction() {
    Array.Clear(this._previousDc);
    this._previousDc[1, (int)Vp5Reference.Current] = 128;
    this._previousDc[2, (int)Vp5Reference.Current] = 128;
    for (var i = 0; i < this._above.Length; ++i)
      this._above[i] = new() { Reference = Vp5Reference.None };
    this._above[2 * this._macroblockWidth + 2].Reference = Vp5Reference.Current;
    this._above[3 * this._macroblockWidth + 4].Reference = Vp5Reference.Current;
  }

  // ==============================================================================================
  // Macroblock
  // ==============================================================================================

  private void _EncodeMacroblock(Vp9BoolEncoder writer, Picture picture, Vp5Frame? reference, int row, int column) {
    var type = Vp5MacroblockType.Intra;
    var motion = Vp5MotionVector.Zero;
    var index = row * this._macroblockWidth + column;

    if (reference != null) {
      (type, motion) = this._ChooseMode(picture, reference, row, column);
      this._WriteMotion(writer, row, column, ref type, motion);
      this._macroblocks[index] = new() { Type = type, MotionVector = type == Vp5MacroblockType.Intra ? Vp5MotionVector.Zero : motion };
    }

    // Residuals, transform and quantisation.
    Span<int> samples = stackalloc int[64];
    Span<double> natural = stackalloc double[64];
    Span<byte> prediction = stackalloc byte[64];
    for (var block = 0; block < 6; ++block) {
      var plane = Vp5Tables.BlockToPlane[block];
      var luma = plane == 0;
      var x = luma ? column * 16 + (block is 1 or 3 ? 8 : 0) : column * 8;
      var y = luma ? row * 16 + (block is 2 or 3 ? 8 : 0) : row * 8;

      if (type == Vp5MacroblockType.Intra)
        prediction.Fill(128);
      else
        this._decoder.Predict(reference!, block, motion, x, y, prediction);

      picture.Residual(plane, x, y, prediction, samples);
      Vp3ForwardDct.Transform(samples, natural);

      var levels = this._levels[block];
      for (var v = 0; v < 8; ++v)
      for (var u = 0; u < 8; ++u) {
        var dequant = u == 0 && v == 0 ? this._dequantDc : this._dequantAc;
        var limit = 32767 / dequant;
        var level = (int)Math.Round(natural[v * 8 + u] / dequant);
        levels[u * 8 + v] = (short)Math.Clamp(level, -Math.Min(limit, 2114), Math.Min(limit, 2114));
      }
    }

    // DC prediction, block by block, exactly as the decoder adds it back.
    var referenceKind = Vp5Tables.ReferenceOfMacroblockType[(int)type];
    Span<short> codedDc = stackalloc short[6];
    for (var block = 0; block < 6; ++block) {
      ref var above = ref this._above[this._aboveIndex[block]];
      ref var left = ref this._left[Vp5Tables.BlockToContext[block]];
      var count = 0;
      var predicted = 0;
      if (left.Reference == referenceKind) {
        predicted += left.Dc;
        ++count;
      }
      if (above.Reference == referenceKind) {
        predicted += above.Dc;
        ++count;
      }
      var plane = Vp5Tables.BlockToPlane[block];
      if (count == 0)
        predicted = this._previousDc[plane, (int)referenceKind];
      else if (count == 2)
        predicted /= 2;

      var absolute = this._levels[block][0];
      var coded = Math.Clamp(absolute - predicted, -2114, 2114);
      absolute = (short)(coded + predicted);
      codedDc[block] = (short)coded;
      this._previousDc[plane, (int)referenceKind] = absolute;
      above.Dc = absolute;
      above.Reference = referenceKind;
      left.Dc = absolute;
      left.Reference = referenceKind;
    }

    for (var block = 0; block < 6; ++block) {
      this._levels[block][0] = codedDc[block];
      this._WriteCoefficients(writer, block);
    }
  }

  /// <summary>Picks copy, a searched vector, or intra for one macroblock by the error each leaves.</summary>
  private (Vp5MacroblockType Type, Vp5MotionVector Motion) _ChooseMode(Picture picture, Vp5Frame reference, int row, int column) {
    var x0 = column * 16;
    var y0 = row * 16;
    var bestCost = picture.LumaSad(reference, x0, y0, 0, 0);
    var best = Vp5MotionVector.Zero;

    for (var dy = -_SEARCH_RANGE; dy <= _SEARCH_RANGE; ++dy)
    for (var dx = -_SEARCH_RANGE; dx <= _SEARCH_RANGE; ++dx) {
      if (dx == 0 && dy == 0)
        continue;
      var cost = picture.LumaSad(reference, x0, y0, dx, dy, bestCost);
      if (cost < bestCost) {
        bestCost = cost;
        best = new((short)(dx * 4), (short)(dy * 4));
      }
    }

    var intraCost = picture.LumaActivity(x0, y0) + _INTRA_BIAS;
    if (intraCost < bestCost)
      return (Vp5MacroblockType.Intra, Vp5MotionVector.Zero);

    return (best == Vp5MotionVector.Zero ? Vp5MacroblockType.InterNoVectorPrevious : Vp5MacroblockType.InterDeltaPrevious, best);
  }

  private void _WriteMotion(Vp9BoolEncoder writer, int row, int column, ref Vp5MacroblockType type, Vp5MotionVector motion) {
    var context = this._FindMotionCandidates(row, column);

    // A vector a neighbour already carries is named rather than coded.
    if (type == Vp5MacroblockType.InterDeltaPrevious && motion != Vp5MotionVector.Zero) {
      if (motion == this._candidates[0])
        type = Vp5MacroblockType.InterVector1Previous;
      else if (motion == this._candidates[1] && this._candidates[1] != Vp5MotionVector.Zero)
        type = Vp5MacroblockType.InterVector2Previous;
    }

    Span<byte> probabilities = stackalloc byte[10];
    for (var node = 0; node < 10; ++node)
      probabilities[node] = this._model.TypeProbabilities[context, (int)this._previousType, node];

    if (type == this._previousType) {
      writer.WriteBool(probabilities[0], 1);
    } else {
      writer.WriteBool(probabilities[0], 0);
      _WriteTree(writer, Vp5Tables.MacroblockTypeTree, probabilities, (int)type);
    }
    this._previousType = type;

    if (type != Vp5MacroblockType.InterDeltaPrevious)
      return;

    var basis = this._vectorCandidatePosition < 2 ? this._candidates[0] : Vp5MotionVector.Zero;
    this._WriteVectorComponent(writer, 0, motion.X - basis.X);
    this._WriteVectorComponent(writer, 1, motion.Y - basis.Y);
  }

  private void _WriteVectorComponent(Vp9BoolEncoder writer, int component, int delta) {
    var model = this._model;
    var magnitude = Math.Abs(delta);
    if (magnitude > 255)
      throw new InvalidOperationException("A VP6 vector delta beyond 255 cannot be coded.");

    if (magnitude < 8) {
      writer.WriteBool(model.VectorDct[component], 0);
      Span<byte> probabilities = stackalloc byte[7];
      for (var node = 0; node < 7; ++node)
        probabilities[node] = model.VectorPdv[component, node];
      _WriteTree(writer, Vp5Tables.VectorAdjustmentTree, probabilities, magnitude);
    } else {
      writer.WriteBool(model.VectorDct[component], 1);
      ReadOnlySpan<int> order = [0, 1, 2, 7, 6, 5, 4];
      foreach (var bit in order)
        writer.WriteBool(model.VectorFdv[component, bit], (magnitude >> bit) & 1);
      if ((magnitude & 0xF0) != 0)
        writer.WriteBool(model.VectorFdv[component, 3], (magnitude >> 3) & 1);
    }

    if (magnitude != 0)
      writer.WriteBool(model.VectorSign[component], delta < 0 ? 1 : 0);
  }

  /// <summary>The decoder's candidate search over the previous picture's references, mirrored.</summary>
  private int _FindMotionCandidates(int row, int column) {
    var count = 0;
    var first = Vp5MotionVector.Zero;
    var second = Vp5MotionVector.Zero;
    var positions = Vp5Tables.CandidatePredictorPositions;

    for (var position = 0; position < positions.Length; ++position) {
      var x = column + positions[position].X;
      var y = row + positions[position].Y;
      if ((uint)x >= (uint)this._macroblockWidth || (uint)y >= (uint)this._macroblockHeight)
        continue;

      var neighbour = this._macroblocks[y * this._macroblockWidth + x];
      if (Vp5Tables.ReferenceOfMacroblockType[(int)neighbour.Type] != Vp5Reference.Previous)
        continue;

      var vector = neighbour.MotionVector;
      if (vector == first || vector == Vp5MotionVector.Zero)
        continue;

      if (count == 0) {
        first = vector;
        count = 1;
        this._vectorCandidatePosition = position;
        continue;
      }

      second = vector;
      count = 2;
      break;
    }

    this._candidates[0] = first;
    this._candidates[1] = second;
    return count switch { 2 => 0, 0 => 1, _ => 2 };
  }

  // ==============================================================================================
  // Coefficients
  // ==============================================================================================

  private void _WriteCoefficients(Vp9BoolEncoder writer, int block) {
    var model = this._model;
    var planeType = block > 3 ? 1 : 0;
    var storage = this._levels[block];
    Span<int> levels = stackalloc int[64];
    var last = -1;
    for (var i = 0; i < 64; ++i) {
      levels[i] = storage[Vp5ScanOrder.Positions[model.IndexToPosition[i]]];
      if (levels[i] != 0)
        last = i;
    }

    ref var left = ref this._left[Vp5Tables.BlockToContext[block]];
    ref var above = ref this._above[this._aboveIndex[block]];
    var context = left.NotNullDc + above.NotNullDc;

    Span<byte> model1 = stackalloc byte[11];
    Span<byte> model2 = stackalloc byte[11];
    Span<byte> model3 = stackalloc byte[14];
    for (var node = 0; node < 11; ++node)
      model1[node] = model.DcValue[planeType, node];
    for (var node = 0; node < 5; ++node)
      model2[node] = model.DcCodingType[planeType, context, node];

    var index = 0;
    var codeType = 1;
    for (;;) {
      var value = levels[index];
      int run;
      if (value != 0) {
        if (!(index > 1 && codeType == 0))
          writer.WriteBool(model2[0], 1);

        var magnitude = Math.Abs(value);
        if (magnitude == 1) {
          writer.WriteBool(model2[2], 0);
          codeType = 1;
        } else {
          writer.WriteBool(model2[2], 1);
          if (magnitude <= 4) {
            writer.WriteBool(model2[3], 0);
            if (magnitude == 2) {
              writer.WriteBool(model2[4], 0);
            } else {
              writer.WriteBool(model2[4], 1);
              writer.WriteBool(model1[5], magnitude - 3);
            }
          } else {
            writer.WriteBool(model2[3], 1);
            var token = 5;
            while (token > 0 && magnitude < Vp5Tables.CoefficientBias[token + 5])
              --token;
            _WriteTree(writer, Vp5Tables.CoefficientTree, model1, token);
            var extra = magnitude - Vp5Tables.CoefficientBias[token + 5];
            for (int bit = Vp5Tables.CoefficientBitLength[token]; bit >= 0; --bit)
              writer.WriteBool(Vp5Tables.CoefficientParseTable[token, bit], (extra >> bit) & 1);
          }
          codeType = 2;
        }

        writer.WriteFlag(value < 0 ? 1 : 0);
        run = 1;
      } else {
        writer.WriteBool(model2[0], 0);
        codeType = 0;
        run = 1;
        if (index > 0) {
          if (index > last) {
            writer.WriteBool(model2[1], 0);
            break;
          }

          writer.WriteBool(model2[1], 1);
          var next = index;
          while (levels[next] == 0)
            ++next;
          run = next - index;

          for (var node = 0; node < 14; ++node)
            model3[node] = model.RunValue[index >= 6 ? 1 : 0, node];
          if (run <= 8) {
            _WriteTree(writer, Vp6Tables.RunTree, model3, run);
          } else {
            _WriteTree(writer, Vp6Tables.RunTree, model3, 0);
            for (var bit = 0; bit < 6; ++bit)
              writer.WriteBool(model3[bit + 8], ((run - 9) >> bit) & 1);
          }
        }
      }

      index += run;
      if (index >= 64)
        break;

      var group = Vp6Tables.CoefficientGroups[index];
      for (var node = 0; node < 11; ++node)
        model1[node] = model.RunAc[planeType, codeType, group, node];
      model1[..11].CopyTo(model2);
    }

    var notNull = (byte)(levels[0] != 0 ? 1 : 0);
    left.NotNullDc = notNull;
    above.NotNullDc = notNull;
  }

  /// <summary>Writes <paramref name="value"/> through a VP5/VP6 tree, the path the decoder's walk would take.</summary>
  private static void _WriteTree(Vp9BoolEncoder writer, ReadOnlySpan<Vp5TreeNode> tree, ReadOnlySpan<byte> probabilities, int value) {
    Span<(int Probability, int Bit)> path = stackalloc (int, int)[16];
    var depth = _FindLeaf(tree, 0, value, path, 0);
    if (depth < 0)
      throw new ArgumentException($"The tree has no leaf for {value}.", nameof(value));
    for (var i = 0; i < depth; ++i)
      writer.WriteBool(probabilities[path[i].Probability], path[i].Bit);
  }

  private static int _FindLeaf(ReadOnlySpan<Vp5TreeNode> tree, int node, int value, Span<(int, int)> path, int depth) {
    if (tree[node].Value <= 0)
      return -tree[node].Value == value ? depth : -1;

    path[depth] = (tree[node].ProbabilityIndex, 0);
    var found = _FindLeaf(tree, node + 1, value, path, depth + 1);
    if (found >= 0)
      return found;

    path[depth] = (tree[node].ProbabilityIndex, 1);
    return _FindLeaf(tree, node + tree[node].Value, value, path, depth + 1);
  }

  // ==============================================================================================
  // Source picture
  // ==============================================================================================

  private readonly struct Picture(byte[] luma, byte[] cb, byte[] cr, int width, int height) {
    private byte[] _Plane(int plane) => plane switch { 0 => luma, 1 => cb, _ => cr };
    private int _Width(int plane) => plane == 0 ? width : width / 2;

    public void Residual(int plane, int x, int y, ReadOnlySpan<byte> prediction, Span<int> samples) {
      var data = this._Plane(plane);
      var stride = this._Width(plane);
      for (var r = 0; r < 8; ++r)
      for (var c = 0; c < 8; ++c)
        samples[r * 8 + c] = data[(y + r) * stride + x + c] - prediction[r * 8 + c];
    }

    public int LumaSad(Vp5Frame reference, int x0, int y0, int dx, int dy, int limit = int.MaxValue) {
      var sad = 0;
      for (var r = 0; r < 16; ++r) {
        var sy = Math.Clamp(y0 + r + dy, 0, height - 1) * width;
        var oy = (y0 + r) * width;
        for (var c = 0; c < 16; ++c)
          sad += Math.Abs(luma[oy + x0 + c] - reference.Luma[sy + Math.Clamp(x0 + c + dx, 0, width - 1)]);
        if (sad >= limit)
          return sad;
      }
      return sad;
    }

    public int LumaActivity(int x0, int y0) {
      var sum = 0;
      for (var r = 0; r < 16; ++r)
      for (var c = 0; c < 16; ++c)
        sum += luma[(y0 + r) * width + x0 + c];
      var mean = sum / 256;
      var activity = 0;
      for (var r = 0; r < 16; ++r)
      for (var c = 0; c < 16; ++c)
        activity += Math.Abs(luma[(y0 + r) * width + x0 + c] - mean);
      return activity;
    }
  }
}
