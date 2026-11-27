using System;
using FileFormat.Codecs.Vp8;
using FileFormat.Codecs.Vp9;

namespace FileFormat.Codecs.Vp7;

/// <summary>What a VP7 stream is written with beyond the pictures themselves.</summary>
/// <param name="Quantizer">The one quantiser index, 0-127, every plane is coded at.</param>
/// <param name="Profile">0, which every known file uses, or 1.</param>
/// <param name="SimpleFilter">The simple loop filter rather than the normal one.</param>
/// <param name="FilterLevel">The loop filter level, 0-63; 0 turns it off.</param>
/// <param name="GoldenInterval">Every how many interframes the golden frame is refreshed; 0 for key frames only.</param>
/// <param name="Fade">Whether a change of overall brightness is coded as a fade of the previous frame.</param>
internal sealed record Vp7EncoderSettings(
  int Quantizer = 24, int Profile = 0, bool SimpleFilter = false, int FilterLevel = 12, int GoldenInterval = 4, bool Fade = true);

/// <summary>
/// Encodes On2 VP7 key frames and interframes: 16x16 and per-subblock intra prediction, one vector a
/// macroblock against the previous or the golden frame through the census VP7 predicts vectors with,
/// Y2 DC prediction followed exactly, and fades.
/// </summary>
/// <remarks>
/// This package's own: FFmpeg has no VP7 encoder and On2's is not available. Everything the decoder
/// reconstructs from — prediction, the inverse transforms, the census, the DC predictor — is the
/// decoder's own code, so what this encodes against is what any decoder will rebuild; and the
/// reference for the next frame is the decoder's reading of each packet. Split vectors, the
/// macroblock features, restated scans and probability updates are not written; the probabilities
/// stay at their defaults.
/// </remarks>
internal sealed class Vp7Encoder {

  private const int _SEARCH = 16 * 8;
  private const int _MAX_VECTOR = 24 * 8;
  private const int _INTRA_PENALTY = 384;
  private const int _SUBBLOCK_PENALTY = 256;

  private readonly int _columns;
  private readonly int _rows;
  private readonly int _width;
  private readonly int _height;
  private readonly Vp7EncoderSettings _settings;
  private readonly Vp7Decoder _decoder = new();
  private readonly Vp8MacroblockGrid _grid;
  private readonly Vp8Frame _recon;
  private readonly short[] _levels;
  private readonly short[] _interDcPrediction = new short[4];
  private readonly short[] _coefficients = new short[25 * 16];
  private readonly int[] _nonZero = new int[24];
  private readonly int _lumaDc, _lumaAc, _y2Dc, _y2Ac, _chromaDc, _chromaAc;
  private int _sinceKey;

  internal Vp7Encoder(int width, int height, Vp7EncoderSettings settings) {
    this._width = width;
    this._height = height;
    this._columns = (width + 15) / 16;
    this._rows = (height + 15) / 16;
    this._settings = settings;
    this._grid = new(this._columns, this._rows);
    this._recon = new(this._columns, this._rows);
    this._levels = new short[this._columns * this._rows * 25 * 16];
    var q = settings.Quantizer;
    this._lumaDc = Vp7Tables.LumaDcQuantiser[q];
    this._lumaAc = Vp7Tables.LumaAcQuantiser[q];
    this._y2Dc = Vp7Tables.Y2DcQuantiser[q];
    this._y2Ac = Vp7Tables.Y2AcQuantiser[q];
    this._chromaDc = Math.Min((int)Vp7Tables.LumaDcQuantiser[q], 132);
    this._chromaAc = Vp7Tables.LumaAcQuantiser[q];
  }

  /// <summary>The last packet as the decoder reads it.</summary>
  internal Vp8Frame? Reconstruction => this._decoder.Last;

  internal Vp7Decoder Decoder => this._decoder;

  /// <summary>The encoder's own reconstruction of the last picture, before the loop filter.</summary>
  internal Vp8Frame Working => this._recon;

  /// <summary>Codes one picture from planes at the macroblock-aligned size.</summary>
  internal byte[] Encode(byte[] luma, byte[] cb, byte[] cr, bool keyFrame) {
    if (this._decoder.Last == null)
      keyFrame = true;

    var profile = this._settings.Profile;
    var last = this._decoder.Last;
    var golden = this._decoder.Golden;
    var refreshGolden = keyFrame;
    int alpha = 0, beta = 0;

    if (keyFrame)
      this._sinceKey = 0;
    else {
      ++this._sinceKey;
      refreshGolden = this._settings.GoldenInterval > 0 && this._sinceKey % this._settings.GoldenInterval == 0;
      if (this._settings.Fade)
        alpha = _FadeFor(luma, last!.Luma);
      if (alpha != 0)
        last = _Faded(last!, alpha, beta);
    }

    if (keyFrame || profile > 0)
      Array.Clear(this._interDcPrediction);

    var source = new Source(luma, cb, cr, this._columns * 16);
    var intraCount = 0;
    var goldenCount = 0;
    var interCount = 0;
    for (var row = 0; row < this._rows; ++row)
      for (var column = 0; column < this._columns; ++column) {
        this._EncodeMacroblock(source, keyFrame ? null : last, keyFrame ? null : golden, row, column);
        var index = this._grid.IndexOf(row, column);
        if (this._grid.ReferenceFrame[index] == Vp8Reference.CURRENT)
          ++intraCount;
        else {
          ++interCount;
          if (this._grid.ReferenceFrame[index] == Vp8Reference.GOLDEN)
            ++goldenCount;
        }
      }

    var intraProbability = _Probability(intraCount, intraCount + interCount);
    var lastProbability = _Probability(interCount - goldenCount, interCount);

    var header = new Vp9BoolEncoder();
    this._WriteHeader(header, keyFrame, refreshGolden, alpha, beta, intraProbability, lastProbability);
    this._WriteModes(header, keyFrame, intraProbability, lastProbability);
    var first = header.Finish();
    var tokens = this._WriteTokens();

    var headerSize = 4 - profile;
    var packet = new byte[headerSize + first.Length + tokens.Length];
    var tag = (keyFrame ? 0 : 1) | (profile << 1) | (first.Length << 4);
    packet[0] = (byte)tag;
    packet[1] = (byte)(tag >> 8);
    packet[2] = (byte)(tag >> 16);
    first.CopyTo(packet, headerSize);
    tokens.CopyTo(packet, headerSize + first.Length);

    this._decoder.Decode(packet);
    return packet;
  }

  private static int _Probability(int zeros, int total)
    => total == 0 ? 128 : Math.Clamp((zeros * 256 + total / 2) / total, 1, 255);

  /// <summary>The fade that brings the previous frame's mean brightness to this picture's, or 0.</summary>
  private static int _FadeFor(byte[] luma, byte[] previous) {
    long difference = 0;
    for (var i = 0; i < luma.Length; ++i)
      difference += luma[i] - previous[i];
    var alpha = (int)Math.Round((double)difference / luma.Length);
    return Math.Abs(alpha) < 2 ? 0 : Math.Clamp(alpha, -128, 127);
  }

  /// <summary>The previous frame as the decoder will predict from it once it has applied the fade.</summary>
  private Vp8Frame _Faded(Vp8Frame previous, int alpha, int beta) {
    var faded = new Vp8Frame(this._columns, this._rows);
    previous.Cb.CopyTo(faded.Cb, 0);
    previous.Cr.CopyTo(faded.Cr, 0);
    for (var i = 0; i < previous.Luma.Length; ++i) {
      int y = previous.Luma[i];
      faded.Luma[i] = (byte)Math.Clamp(y + ((y * beta) >> 8) + alpha, 0, 255);
    }
    return faded;
  }

  // ============================================================================================
  // Decisions and reconstruction
  // ============================================================================================

  private readonly record struct Source(byte[] Luma, byte[] Cb, byte[] Cr, int Stride);

  private void _EncodeMacroblock(Source source, Vp8Frame? last, Vp8Frame? golden, int row, int column) {
    var grid = this._grid;
    var index = grid.IndexOf(row, column);
    var recon = this._recon;
    var stride = recon.LumaWidth;
    var x = column * 16;
    var y = row * 16;

    // Intra: the best whole-block luma mode by the sum of absolute differences.
    var bestIntra = Vp8Mode.DC_PREDICTION;
    var bestIntraCost = int.MaxValue;
    Span<byte> edge = stackalloc byte[21];
    Span<byte> left = stackalloc byte[16];
    Vp8IntraPrediction.GatherAbove(recon.Luma, stride, stride, x, y, 16, 4, edge, 128, 128);
    Vp8IntraPrediction.GatherLeft(recon.Luma, stride, x, y, 16, left, 128);
    for (var mode = 0; mode < 4; ++mode) {
      Vp8IntraPrediction.PredictBlock(recon.Luma, stride, x, y, 16, mode, edge, left, row > 0, column > 0);
      var cost = _Sad(source.Luma, recon.Luma, stride, x, y, 16);
      if (cost < bestIntraCost) {
        bestIntraCost = cost;
        bestIntra = mode;
      }
    }

    // Inter: a vector searched against the previous frame, and against the golden one if it differs.
    var inter = false;
    var interCost = int.MaxValue;
    var vector = Vp8MotionVector.Zero;
    var reference = Vp8Reference.LAST;
    if (last != null) {
      Span<Vp8MotionVector> near = stackalloc Vp8MotionVector[3];
      Span<int> count = stackalloc int[3];
      Vp7Decoder.Survey(grid, row, column, this._settings.Profile, near, count);

      (vector, interCost) = this._Search(source, last, x, y, near);
      if (golden != null && !ReferenceEquals(golden, this._decoder.Last)) {
        var (goldenVector, goldenCost) = this._Search(source, golden, x, y, near);
        if (goldenCost < interCost) {
          (vector, interCost) = (goldenVector, goldenCost);
          reference = Vp8Reference.GOLDEN;
        }
      }
      inter = interCost <= bestIntraCost + _INTRA_PENALTY;

      if (inter) {
        var best = Vp7Decoder.Best(near, count);
        int mode;
        if (vector.IsZero)
          mode = Vp8Mode.ZERO_MV;
        else if (vector == near[1])
          mode = Vp8Mode.NEAREST_MV;
        else if (vector == near[2])
          mode = Vp8Mode.NEAR_MV;
        else if (Math.Abs(vector.Row - best.Row) <= 510 && Math.Abs(vector.Column - best.Column) <= 510)
          mode = Vp8Mode.NEW_MV;
        else {
          mode = Vp8Mode.ZERO_MV;
          vector = Vp8MotionVector.Zero;
        }

        grid.LumaMode[index] = grid.ChromaMode[index] = (byte)mode;
        grid.ReferenceFrame[index] = (byte)reference;
        grid.MotionVector[index] = vector;
        var from = reference == Vp8Reference.GOLDEN ? golden! : last;
        this._PredictInter(from, x, y, vector);
        this._CodeResidue(source, index, row, column, wholeBlock: true, inter: true);
        return;
      }
    }

    // Intra after all: per-subblock prediction where it beats the best whole-block mode.
    grid.ReferenceFrame[index] = Vp8Reference.CURRENT;
    grid.MotionVector[index] = Vp8MotionVector.Zero;

    var subblockCost = this._TrySubblocks(source, index, row, column);
    if (subblockCost + _SUBBLOCK_PENALTY < bestIntraCost) {
      grid.LumaMode[index] = Vp8Mode.SUBBLOCK_PREDICTION;
    } else {
      grid.LumaMode[index] = (byte)bestIntra;
      grid.SubblockModes(index).Fill((byte)(bestIntra == Vp8Mode.DC_PREDICTION ? Vp8Mode.B_DC_PREDICTION : Vp8Mode.B_TRUE_MOTION_PREDICTION));
      Vp8IntraPrediction.PredictBlock(recon.Luma, stride, x, y, 16, bestIntra, edge, left, row > 0, column > 0);
    }

    // Chroma: the mode that fits both planes best.
    var chromaStride = recon.ChromaWidth;
    var cx = column * 8;
    var cy = row * 8;
    var bestChroma = 0;
    var bestChromaCost = int.MaxValue;
    for (var mode = 0; mode < 4; ++mode) {
      var cost = 0;
      foreach (var (plane, original) in (ReadOnlySpan<(byte[], byte[])>)[(recon.Cb, source.Cb), (recon.Cr, source.Cr)]) {
        Vp8IntraPrediction.GatherAbove(plane, chromaStride, chromaStride, cx, cy, 8, 0, edge, 128, 128);
        Vp8IntraPrediction.GatherLeft(plane, chromaStride, cx, cy, 8, left, 128);
        Vp8IntraPrediction.PredictBlock(plane, chromaStride, cx, cy, 8, mode, edge, left, row > 0, column > 0);
        cost += _Sad(original, plane, chromaStride, cx, cy, 8);
      }
      if (cost < bestChromaCost) {
        bestChromaCost = cost;
        bestChroma = mode;
      }
    }
    foreach (var plane in (ReadOnlySpan<byte[]>)[recon.Cb, recon.Cr]) {
      Vp8IntraPrediction.GatherAbove(plane, chromaStride, chromaStride, cx, cy, 8, 0, edge, 128, 128);
      Vp8IntraPrediction.GatherLeft(plane, chromaStride, cx, cy, 8, left, 128);
      Vp8IntraPrediction.PredictBlock(plane, chromaStride, cx, cy, 8, bestChroma, edge, left, row > 0, column > 0);
    }
    grid.ChromaMode[index] = (byte)bestChroma;

    this._CodeResidue(source, index, row, column, wholeBlock: grid.LumaMode[index] != Vp8Mode.SUBBLOCK_PREDICTION, inter: false);
  }

  /// <summary>
  /// Whole-sample search around zero and the census's nearest vector, then half and quarter samples,
  /// by the sum of absolute luma differences through the six-tap filter.
  /// </summary>
  private (Vp8MotionVector Vector, int Cost) _Search(Source source, Vp8Frame reference, int x, int y, ReadOnlySpan<Vp8MotionVector> near) {
    var best = Vp8MotionVector.Zero;
    var bestCost = this._InterCost(source, reference, x, y, best);
    foreach (var centre in (ReadOnlySpan<Vp8MotionVector>)[Vp8MotionVector.Zero, near[1]]) {
      var r0 = centre.Row & ~7;
      var c0 = centre.Column & ~7;
      for (var dr = -_SEARCH; dr <= _SEARCH; dr += 8)
        for (var dc = -_SEARCH; dc <= _SEARCH; dc += 8) {
          var candidate = new Vp8MotionVector(r0 + dr, c0 + dc);
          if (Math.Abs(candidate.Row) > _MAX_VECTOR || Math.Abs(candidate.Column) > _MAX_VECTOR)
            continue;
          var cost = _WholeSampleSad(source.Luma, reference.Luma, reference.LumaWidth, reference.LumaHeight, x, y, candidate);
          if (cost < bestCost) {
            bestCost = cost;
            best = candidate;
          }
        }
    }

    bestCost = this._InterCost(source, reference, x, y, best);
    foreach (var step in (ReadOnlySpan<int>)[4, 2]) {
      var centre = best;
      for (var dr = -step; dr <= step; dr += step)
        for (var dc = -step; dc <= step; dc += step) {
          if (dr == 0 && dc == 0)
            continue;
          var candidate = new Vp8MotionVector(centre.Row + dr, centre.Column + dc);
          if (Math.Abs(candidate.Row) > _MAX_VECTOR || Math.Abs(candidate.Column) > _MAX_VECTOR)
            continue;
          var cost = this._InterCost(source, reference, x, y, candidate);
          if (cost < bestCost) {
            bestCost = cost;
            best = candidate;
          }
        }
    }

    // A predictor the census already offers costs nothing to name, so prefer it when it is as good.
    foreach (var candidate in near[1..]) {
      if (candidate.IsZero)
        continue;
      var cost = this._InterCost(source, reference, x, y, candidate);
      if (cost <= bestCost + 32) {
        bestCost = cost;
        best = candidate;
        break;
      }
    }

    return (best, bestCost);
  }

  private int _InterCost(Source source, Vp8Frame reference, int x, int y, Vp8MotionVector vector) {
    var stride = this._recon.LumaWidth;
    for (var block = 0; block < 16; ++block)
      Vp8InterPrediction.PredictBlock(reference.Luma, this._recon.Luma, stride, this._recon.LumaHeight,
        x + (block & 3) * 4, y + (block >> 2) * 4, vector, bicubic: true);
    return _Sad(source.Luma, this._recon.Luma, stride, x, y, 16);
  }

  private static int _WholeSampleSad(byte[] source, byte[] reference, int stride, int height, int x, int y, Vp8MotionVector vector) {
    var sx = x + (vector.Column >> 3);
    var sy = y + (vector.Row >> 3);
    var sum = 0;
    for (var row = 0; row < 16; ++row) {
      var ry = Math.Clamp(sy + row, 0, height - 1) * stride;
      var at = (y + row) * stride + x;
      for (var column = 0; column < 16; ++column)
        sum += Math.Abs(source[at + column] - reference[ry + Math.Clamp(sx + column, 0, stride - 1)]);
    }
    return sum;
  }

  private void _PredictInter(Vp8Frame reference, int x, int y, Vp8MotionVector vector) {
    var recon = this._recon;
    for (var block = 0; block < 16; ++block)
      Vp8InterPrediction.PredictBlock(reference.Luma, recon.Luma, recon.LumaWidth, recon.LumaHeight,
        x + (block & 3) * 4, y + (block >> 2) * 4, vector, bicubic: true);
    var chroma = Vp8InterPrediction.ChromaVector(vector, wholePixelsOnly: false);
    for (var quarter = 0; quarter < 4; ++quarter) {
      var bx = x / 2 + (quarter & 1) * 4;
      var by = y / 2 + (quarter >> 1) * 4;
      Vp8InterPrediction.PredictBlock(reference.Cb, recon.Cb, recon.ChromaWidth, recon.ChromaHeight, bx, by, chroma, bicubic: true);
      Vp8InterPrediction.PredictBlock(reference.Cr, recon.Cr, recon.ChromaWidth, recon.ChromaHeight, bx, by, chroma, bicubic: true);
    }
  }

  /// <summary>
  /// Codes the sixteen luma subblocks with their own modes, each reconstructed before the next is
  /// chosen, and answers the sum of their prediction errors. Leaves the macroblock reconstructed,
  /// its levels stored and its modes in the grid.
  /// </summary>
  private int _TrySubblocks(Source source, int index, int row, int column) {
    var grid = this._grid;
    var recon = this._recon;
    var stride = recon.LumaWidth;
    var plane = recon.Luma;
    var x = column * 16;
    var y = row * 16;
    var modes = grid.SubblockModes(index);
    var levels = this._levels.AsSpan(this._MacroblockOffset(row, column), 25 * 16);
    levels.Clear();

    Span<byte> macroblockAbove = stackalloc byte[21];
    Span<byte> macroblockLeft = stackalloc byte[16];
    Vp8IntraPrediction.GatherAbove(plane, stride, stride, x, y, 16, 4, macroblockAbove, 128, 128);
    Vp8IntraPrediction.GatherLeft(plane, stride, x, y, 16, macroblockLeft, 128);

    Span<byte> above = stackalloc byte[8];
    Span<byte> left = stackalloc byte[4];
    Span<byte> edge = stackalloc byte[9];
    Span<int> residual = stackalloc int[16];
    var total = 0;

    for (var block = 0; block < 16; ++block) {
      var subRow = block >> 2;
      var subColumn = block & 3;
      var bx = x + subColumn * 4;
      var by = y + subRow * 4;
      var aboveRow = (by - 1) * stride;

      if (subRow == 0)
        macroblockAbove.Slice(1 + subColumn * 4, 8).CopyTo(above);
      else {
        for (var i = 0; i < 4; ++i)
          above[i] = plane[aboveRow + bx + i];
        if (subColumn < 3)
          for (var i = 0; i < 4; ++i)
            above[4 + i] = plane[aboveRow + bx + 4 + i];
        else
          macroblockAbove.Slice(17, 4).CopyTo(above[4..]);
      }

      if (subColumn == 0)
        macroblockLeft.Slice(subRow * 4, 4).CopyTo(left);
      else
        for (var i = 0; i < 4; ++i)
          left[i] = plane[(by + i) * stride + bx - 1];

      var corner = subRow == 0 ? macroblockAbove[subColumn * 4]
        : subColumn == 0 ? macroblockLeft[subRow * 4 - 1]
        : plane[aboveRow + bx - 1];
      edge[0] = left[3];
      edge[1] = left[2];
      edge[2] = left[1];
      edge[3] = left[0];
      edge[4] = corner;
      above[..4].CopyTo(edge[5..]);

      var bestMode = 0;
      var bestCost = int.MaxValue;
      for (var mode = 0; mode < Vp8Mode.SUBBLOCK_MODE_COUNT; ++mode) {
        Vp8IntraPrediction.PredictSubblock(plane, stride, bx, by, mode, edge, above, left);
        var cost = _Sad(source.Luma, plane, stride, bx, by, 4);
        if (cost < bestCost) {
          bestCost = cost;
          bestMode = mode;
        }
      }
      modes[block] = (byte)bestMode;
      total += bestCost;
      Vp8IntraPrediction.PredictSubblock(plane, stride, bx, by, bestMode, edge, above, left);

      for (var i = 0; i < 16; ++i)
        residual[i] = source.Luma[(by + (i >> 2)) * stride + bx + (i & 3)] - plane[(by + (i >> 2)) * stride + bx + (i & 3)];
      var blockLevels = levels.Slice(block * 16, 16);
      var nonZero = _Quantize(residual, blockLevels, 0, this._lumaDc, this._lumaAc, null);
      if (nonZero != 0) {
        var coefficients = this._coefficients.AsSpan(0, 16);
        _Dequantize(blockLevels, coefficients, 0, this._lumaDc, this._lumaAc);
        if (nonZero == 1)
          Vp7Transform.AddDc(coefficients, plane, by * stride + bx, stride);
        else
          Vp7Transform.Add(coefficients, plane, by * stride + bx, stride);
      }
    }

    return total;
  }

  /// <summary>
  /// Transforms, quantises, stores and reconstructs a macroblock's residue against the prediction
  /// already in the reconstruction, exactly as the decoder will add it back.
  /// </summary>
  private void _CodeResidue(Source source, int index, int row, int column, bool wholeBlock, bool inter) {
    var recon = this._recon;
    var stride = recon.LumaWidth;
    var chromaStride = recon.ChromaWidth;
    var levels = this._levels.AsSpan(this._MacroblockOffset(row, column), 25 * 16);
    var coefficients = this._coefficients.AsSpan();
    coefficients.Clear();
    Span<int> residual = stackalloc int[16];
    Span<double> transformed = stackalloc double[16];

    if (wholeBlock) {
      levels.Clear();
      Span<double> dcs = stackalloc double[16];
      for (var block = 0; block < 16; ++block) {
        var at = (row * 16 + (block >> 2) * 4) * stride + column * 16 + (block & 3) * 4;
        for (var i = 0; i < 16; ++i)
          residual[i] = source.Luma[at + (i >> 2) * stride + (i & 3)] - recon.Luma[at + (i >> 2) * stride + (i & 3)];
        _Forward(residual, transformed);
        dcs[block] = transformed[0];
        this._nonZero[block] = _QuantizeTransformed(transformed, levels.Slice(block * 16, 16), 1, this._lumaDc, this._lumaAc, null);
      }

      // Y2: the sixteen DCs transformed again, the DC of that predicted across inter macroblocks.
      var y2Levels = levels.Slice(24 * 16, 16);
      Span<double> y2 = stackalloc double[16];
      _ForwardTransformed(dcs, y2);
      var prediction = inter ? this._interDcPrediction.AsSpan((this._grid.ReferenceFrame[index] - 1) * 2, 2) : default;
      var y2NonZero = _QuantizeTransformed(y2, y2Levels, 0, this._y2Dc, this._y2Ac, inter && prediction[1] > 3 ? prediction[0] : null);

      var y2Block = coefficients.Slice(24 * 16, 16);
      _Dequantize(y2Levels, y2Block, 0, this._y2Dc, this._y2Ac);
      if (inter)
        y2NonZero |= Vp7Decoder.PredictDc(y2Block, prediction);
      var blockDc = 0;
      if (y2NonZero != 0) {
        blockDc = 1;
        if (y2NonZero == 1)
          Vp7Transform.InvertWalshHadamardDc(y2Block, coefficients);
        else
          Vp7Transform.InvertWalshHadamard(y2Block, coefficients);
      }

      for (var block = 0; block < 16; ++block) {
        var blockCoefficients = coefficients.Slice(block * 16, 16);
        _Dequantize(levels.Slice(block * 16, 16), blockCoefficients, 1, this._lumaDc, this._lumaAc);
        var count = this._nonZero[block] + blockDc;
        var at = (row * 16 + (block >> 2) * 4) * stride + column * 16 + (block & 3) * 4;
        if (count == 1)
          Vp7Transform.AddDc(blockCoefficients, recon.Luma, at, stride);
        else if (count > 1)
          Vp7Transform.Add(blockCoefficients, recon.Luma, at, stride);
      }
    }

    for (var block = 16; block < 24; ++block) {
      var plane = block < 20 ? recon.Cb : recon.Cr;
      var original = block < 20 ? source.Cb : source.Cr;
      var within = block & 3;
      var at = (row * 8 + (within >> 1) * 4) * chromaStride + column * 8 + (within & 1) * 4;
      for (var i = 0; i < 16; ++i)
        residual[i] = original[at + (i >> 2) * chromaStride + (i & 3)] - plane[at + (i >> 2) * chromaStride + (i & 3)];
      var blockLevels = levels.Slice(block * 16, 16);
      var nonZero = _Quantize(residual, blockLevels, 0, this._chromaDc, this._chromaAc, null);
      if (nonZero == 0)
        continue;
      var blockCoefficients = coefficients.Slice(block * 16, 16);
      _Dequantize(blockLevels, blockCoefficients, 0, this._chromaDc, this._chromaAc);
      if (nonZero == 1)
        Vp7Transform.AddDc(blockCoefficients, plane, at, chromaStride);
      else
        Vp7Transform.Add(blockCoefficients, plane, at, chromaStride);
    }
  }

  private int _MacroblockOffset(int row, int column) => (row * this._columns + column) * 25 * 16;

  // ============================================================================================
  // Transform and quantisation
  // ============================================================================================

  private static readonly double[] _Basis = _BuildBasis();

  /// <summary>
  /// The matrix of VP7's inverse transform: entry (n, k) is w(k) cos((2n+1)k pi/8), w(0) = sqrt 2 and
  /// w(k) = 2 otherwise. Its columns are orthogonal with squared length 8, and the decoder divides
  /// by 16, so the forward transform is a quarter of its transpose applied on both sides.
  /// </summary>
  private static double[] _BuildBasis() {
    var basis = new double[16];
    for (var n = 0; n < 4; ++n)
      for (var k = 0; k < 4; ++k)
        basis[n * 4 + k] = (k == 0 ? Math.Sqrt(2) : 2) * Math.Cos((2 * n + 1) * k * Math.PI / 8);
    return basis;
  }

  private static void _Forward(ReadOnlySpan<int> residual, Span<double> output) {
    Span<double> values = stackalloc double[16];
    for (var i = 0; i < 16; ++i)
      values[i] = residual[i];
    _ForwardTransformed(values, output);
  }

  private static void _ForwardTransformed(ReadOnlySpan<double> input, Span<double> output) {
    Span<double> rows = stackalloc double[16];
    for (var r = 0; r < 4; ++r)
      for (var k = 0; k < 4; ++k) {
        var sum = 0.0;
        for (var m = 0; m < 4; ++m)
          sum += input[r * 4 + m] * _Basis[m * 4 + k];
        rows[r * 4 + k] = sum;
      }

    for (var j = 0; j < 4; ++j)
      for (var k = 0; k < 4; ++k) {
        var sum = 0.0;
        for (var n = 0; n < 4; ++n)
          sum += _Basis[n * 4 + j] * rows[n * 4 + k];
        output[j * 4 + k] = sum / 4;
      }
  }

  private static int _Quantize(ReadOnlySpan<int> residual, Span<short> levels, int start, int dc, int ac, short? dcPrediction) {
    Span<double> transformed = stackalloc double[16];
    _Forward(residual, transformed);
    return _QuantizeTransformed(transformed, levels, start, dc, ac, dcPrediction);
  }

  /// <summary>
  /// Quantises into raster positions and answers what the decoder will count: one past the last
  /// scan position coded, or zero.
  /// </summary>
  /// <param name="dcPrediction">What the decoder will add to the DC, when it will.</param>
  private static int _QuantizeTransformed(ReadOnlySpan<double> transformed, Span<short> levels, int start, int dc, int ac, short? dcPrediction) {
    var zigzag = Vp8Trees.ZigZag;
    var last = -1;
    for (var i = start; i < 16; ++i) {
      var position = zigzag[i];
      var factor = i == 0 ? dc : ac;
      var value = transformed[position];
      if (i == 0 && dcPrediction is { } predicted)
        value -= predicted;
      var magnitude = Math.Abs(value) / factor + (i == 0 ? 0.5 : 0.35);
      var level = (int)magnitude;
      level = Math.Min(level, Math.Min(2048 + 66, 32767 / factor));
      levels[position] = (short)(value < 0 ? -level : level);
      if (level != 0)
        last = i;
    }
    return last + 1;
  }

  private static void _Dequantize(ReadOnlySpan<short> levels, Span<short> coefficients, int start, int dc, int ac) {
    var zigzag = Vp8Trees.ZigZag;
    for (var i = start; i < 16; ++i) {
      var position = zigzag[i];
      if (levels[position] != 0)
        coefficients[position] = (short)(levels[position] * (i == 0 ? dc : ac));
    }
  }

  private static int _Sad(byte[] source, byte[] plane, int stride, int x, int y, int size) {
    var sum = 0;
    for (var row = 0; row < size; ++row) {
      var at = (y + row) * stride + x;
      for (var column = 0; column < size; ++column)
        sum += Math.Abs(source[at + column] - plane[at + column]);
    }
    return sum;
  }

  // ============================================================================================
  // The first partition
  // ============================================================================================

  private void _WriteHeader(Vp9BoolEncoder w, bool keyFrame, bool refreshGolden, int alpha, int beta, int intraProbability, int lastProbability) {
    var profile = this._settings.Profile;
    if (keyFrame) {
      w.WriteLiteral(12, this._width);
      w.WriteLiteral(12, this._height);
      w.WriteLiteral(2, 0);
      w.WriteLiteral(2, 0);
    }

    for (var i = 0; i < 4; ++i)
      w.WriteFlag(0); // no macroblock features

    w.WriteLiteral(7, this._settings.Quantizer);
    for (var i = 0; i < 5; ++i)
      w.WriteFlag(0); // every other quantiser index is the same

    if (!keyFrame)
      w.WriteFlag(refreshGolden ? 1 : 0);

    var fade = alpha != 0 || beta != 0;
    if (profile > 0) {
      w.WriteFlag(1); // the probabilities read are kept
      if (!keyFrame)
        w.WriteFlag(fade ? 1 : 0);
    }

    // Profile 1 interframes say first whether there is a fade at all.
    if (profile == 0 || keyFrame || fade) {
      w.WriteFlag(fade ? 1 : 0);
      if (fade) {
        w.WriteLiteral(8, alpha & 0xFF);
        w.WriteLiteral(8, beta & 0xFF);
      }
    }

    if (profile == 0)
      w.WriteFlag(this._settings.SimpleFilter ? 1 : 0);
    w.WriteFlag(0); // the scan order is not restated
    if (profile > 0)
      w.WriteFlag(this._settings.SimpleFilter ? 1 : 0);
    w.WriteLiteral(6, this._settings.FilterLevel);
    w.WriteLiteral(3, 0);

    foreach (var probability in Vp8Tables.CoefficientUpdateProbabilities)
      w.WriteBool(probability, 0);

    if (!keyFrame) {
      w.WriteLiteral(8, intraProbability);
      w.WriteLiteral(8, lastProbability);
      w.WriteFlag(0);
      w.WriteFlag(0);
      var update = Vp8Trees.MotionVectorUpdateProbabilities;
      for (var component = 0; component < 2; ++component)
        for (var j = 0; j < 17; ++j)
          w.WriteBool(update[component * Vp8Trees.MV_PROBABILITY_COUNT + j], 0);
    }
  }

  private void _WriteModes(Vp9BoolEncoder w, bool keyFrame, int intraProbability, int lastProbability) {
    var grid = this._grid;
    Span<Vp8MotionVector> near = stackalloc Vp8MotionVector[3];
    Span<int> count = stackalloc int[3];
    var contexts = Vp7Tables.ModeContexts;

    for (var row = 0; row < this._rows; ++row)
      for (var column = 0; column < this._columns; ++column) {
        var index = grid.IndexOf(row, column);
        var lumaMode = grid.LumaMode[index];

        if (keyFrame) {
          w.WriteTree(Vp8Trees.KeyFrameLumaMode, Vp8Trees.KeyFrameLumaModeProbabilities, lumaMode);
          if (lumaMode == Vp8Mode.SUBBLOCK_PREDICTION) {
            var modes = grid.SubblockModes(index);
            var above = grid.SubblockModes(grid.Above(index));
            var left = grid.SubblockModes(grid.Left(index));
            for (var subblock = 0; subblock < 16; ++subblock) {
              int aboveMode = subblock < 4 ? above[subblock + 12] : modes[subblock - 4];
              int leftMode = (subblock & 3) == 0 ? left[subblock + 3] : modes[subblock - 1];
              var offset = (aboveMode * Vp8Mode.SUBBLOCK_MODE_COUNT + leftMode) * (Vp8Mode.SUBBLOCK_MODE_COUNT - 1);
              w.WriteTree(Vp8Trees.SubblockMode, Vp8Tables.KeyFrameSubblockModeProbabilities.AsSpan(offset, 9), modes[subblock]);
            }
          }
          w.WriteTree(Vp8Trees.ChromaMode, Vp8Trees.KeyFrameChromaModeProbabilities, grid.ChromaMode[index]);
          continue;
        }

        var reference = grid.ReferenceFrame[index];
        if (reference == Vp8Reference.CURRENT) {
          w.WriteBool(intraProbability, 0);
          w.WriteTree(Vp8Trees.LumaMode, Vp8Trees.DefaultLumaModeProbabilities, lumaMode);
          if (lumaMode == Vp8Mode.SUBBLOCK_PREDICTION) {
            var modes = grid.SubblockModes(index);
            for (var subblock = 0; subblock < 16; ++subblock)
              w.WriteTree(Vp8Trees.SubblockMode, Vp8Trees.SubblockModeProbabilities, modes[subblock]);
          }
          w.WriteTree(Vp8Trees.ChromaMode, Vp8Trees.DefaultChromaModeProbabilities, grid.ChromaMode[index]);
          continue;
        }

        w.WriteBool(intraProbability, 1);
        w.WriteBool(lastProbability, reference == Vp8Reference.GOLDEN ? 1 : 0);
        Vp7Decoder.Survey(grid, row, column, this._settings.Profile, near, count);
        if (lumaMode == Vp8Mode.ZERO_MV) {
          w.WriteBool(contexts[count[0] * 4], 0);
          continue;
        }
        w.WriteBool(contexts[count[0] * 4], 1);
        if (lumaMode == Vp8Mode.NEAREST_MV) {
          w.WriteBool(contexts[count[1] * 4 + 1], 0);
          continue;
        }
        w.WriteBool(contexts[count[1] * 4 + 1], 1);
        if (lumaMode == Vp8Mode.NEAR_MV) {
          w.WriteBool(contexts[count[2] * 4 + 2], 0);
          continue;
        }
        w.WriteBool(contexts[count[2] * 4 + 2], 1);
        w.WriteBool(contexts[count[2] * 4 + 3], 0);
        var best = Vp7Decoder.Best(near, count);
        var vector = grid.MotionVector[index];
        var mv = Vp7Tables.DefaultMotionVectorProbabilities;
        _WriteComponent(w, mv[..17], (vector.Row - best.Row) / 2);
        _WriteComponent(w, mv[17..], (vector.Column - best.Column) / 2);
      }
  }

  private static void _WriteComponent(Vp9BoolEncoder w, ReadOnlySpan<byte> p, int value) {
    var magnitude = Math.Abs(value);
    if (magnitude < 8) {
      w.WriteBool(p[0], 0);
      w.WriteTree(Vp8Trees.SmallMotionVector, p[2..], magnitude);
    } else {
      w.WriteBool(p[0], 1);
      for (var bit = 0; bit < 3; ++bit)
        w.WriteBool(p[9 + bit], (magnitude >> bit) & 1);
      for (var bit = 7; bit > 3; --bit)
        w.WriteBool(p[9 + bit], (magnitude >> bit) & 1);
      if ((magnitude & 0xF0) != 0)
        w.WriteBool(p[12], (magnitude >> 3) & 1);
    }

    if (magnitude != 0)
      w.WriteBool(p[1], value < 0 ? 1 : 0);
  }

  // ============================================================================================
  // The token partition
  // ============================================================================================

  private byte[] _WriteTokens() {
    var w = new Vp9BoolEncoder();
    var above = new byte[this._columns * 9];
    var left = new byte[9];
    var probabilities = Vp8Tables.DefaultCoefficientProbabilities;
    var zigzag = Vp8Trees.ZigZag;

    for (var row = 0; row < this._rows; ++row)
      for (var column = 0; column < this._columns; ++column) {
        var index = this._grid.IndexOf(row, column);
        var levels = this._levels.AsSpan(this._MacroblockOffset(row, column), 25 * 16);
        var top = above.AsSpan(column * 9, 9);
        var hasY2 = this._grid.LumaMode[index] != Vp8Mode.SUBBLOCK_PREDICTION;
        var lumaStart = 0;
        var lumaPlane = Vp8CoefficientPlane.LUMA_WITH_DC;

        if (hasY2) {
          var nonZero = _WriteBlock(w, probabilities, levels.Slice(24 * 16, 16), Vp8CoefficientPlane.Y2, 0, top[8] + left[8], zigzag);
          left[8] = top[8] = (byte)(nonZero != 0 ? 1 : 0);
          lumaStart = 1;
          lumaPlane = Vp8CoefficientPlane.LUMA_AFTER_Y2;
        }

        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x) {
            var nonZero = _WriteBlock(w, probabilities, levels.Slice((y * 4 + x) * 16, 16), lumaPlane, lumaStart, left[y] + top[x], zigzag);
            top[x] = left[y] = (byte)(nonZero != 0 ? 1 : 0);
          }

        for (var plane = 4; plane < 6; ++plane)
          for (var y = 0; y < 2; ++y)
            for (var x = 0; x < 2; ++x) {
              var block = 16 + (plane - 4) * 4 + (y << 1) + x;
              var nonZero = _WriteBlock(w, probabilities, levels.Slice(block * 16, 16), Vp8CoefficientPlane.CHROMA, 0,
                left[plane + 2 * y] + top[plane + 2 * x], zigzag);
              top[plane + 2 * x] = left[plane + 2 * y] = (byte)(nonZero != 0 ? 1 : 0);
            }
      }

    return w.Finish();
  }

  /// <summary>One block's tokens in VP7's form, an end of block being allowed after a zero.</summary>
  private static int _WriteBlock(Vp9BoolEncoder w, byte[] probabilities, ReadOnlySpan<short> levels, int plane, int i, int context, ReadOnlySpan<byte> scan) {
    var bands = Vp8Trees.CoefficientBands;
    var last = -1;
    for (var position = i; position < 16; ++position)
      if (levels[scan[position]] != 0)
        last = position;

    var at = Vp8Tables.CoefficientProbabilityOffset(plane, bands[i], context);
    if (last < 0) {
      w.WriteBool(probabilities[at], 0);
      return 0;
    }

    w.WriteBool(probabilities[at], 1);
    var checkEnd = false;
    while (true) {
      if (checkEnd) {
        if (i > last) {
          w.WriteBool(probabilities[at], 0);
          break;
        }
        w.WriteBool(probabilities[at], 1);
      }
      checkEnd = true;

      int value = levels[scan[i]];
      if (value == 0) {
        w.WriteBool(probabilities[at + 1], 0);
        ++i;
        at = Vp8Tables.CoefficientProbabilityOffset(plane, bands[i], 0);
        continue;
      }

      w.WriteBool(probabilities[at + 1], 1);
      var magnitude = Math.Abs(value);
      if (magnitude == 1)
        w.WriteBool(probabilities[at + 2], 0);
      else {
        w.WriteBool(probabilities[at + 2], 1);
        if (magnitude <= 4) {
          w.WriteBool(probabilities[at + 3], 0);
          if (magnitude == 2)
            w.WriteBool(probabilities[at + 4], 0);
          else {
            w.WriteBool(probabilities[at + 4], 1);
            w.WriteBool(probabilities[at + 5], magnitude == 4 ? 1 : 0);
          }
        } else {
          w.WriteBool(probabilities[at + 3], 1);
          if (magnitude <= 10) {
            w.WriteBool(probabilities[at + 6], 0);
            if (magnitude <= 6) {
              w.WriteBool(probabilities[at + 7], 0);
              w.WriteBool(159, magnitude - 5);
            } else {
              w.WriteBool(probabilities[at + 7], 1);
              w.WriteBool(165, (magnitude - 7) >> 1);
              w.WriteBool(145, (magnitude - 7) & 1);
            }
          } else {
            w.WriteBool(probabilities[at + 6], 1);
            var category = magnitude < 19 ? 0 : magnitude < 35 ? 1 : magnitude < 67 ? 2 : 3;
            w.WriteBool(probabilities[at + 8], category >> 1);
            w.WriteBool(probabilities[at + 9 + (category >> 1)], category & 1);
            var extra = magnitude - (3 + (8 << category));
            var bits = Vp8Trees.CategoryBits[category + 2];
            var categoryProbabilities = Vp8Trees.CategoryProbabilities.Slice(Vp8Trees.CategoryProbabilityOffset[category + 2], bits);
            for (var bit = 0; bit < bits; ++bit)
              w.WriteBool(categoryProbabilities[bit], (extra >> (bits - 1 - bit)) & 1);
          }
        }
      }

      w.WriteFlag(value < 0 ? 1 : 0);
      if (++i == 16)
        break;
      at = Vp8Tables.CoefficientProbabilityOffset(plane, bands[i], magnitude == 1 ? 1 : 2);
    }

    return last + 1;
  }
}
