using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Vp8;

namespace FileFormat.Codecs.Vp7;

/// <summary>
/// Decodes an On2 VP7 stream one frame at a time, keeping the two reference frames and the entropy
/// state later frames need.
/// </summary>
/// <remarks>
/// Converted from the VP7 paths of FFmpeg's <c>libavcodec/vp8.c</c> and <c>vp8dsp.c</c>
/// (LGPL-2.1-or-later). VP7 is VP8's parent and shares its boolean coder, prediction modes, six-tap
/// motion filter and coefficient tree, which this decoder takes from the package's own VP8 decoder;
/// what differs is converted here: the frame header with its four macroblock features and fading,
/// the quantiser tables, a scored census over twelve neighbours in place of VP8's three, Y2 DC
/// prediction across inter macroblocks, coefficient tokens that may end after a zero, a scan order
/// the stream can restate, its own transforms and loop filter, and 128 wherever VP8 puts 127 or 129
/// outside the picture.
/// </remarks>
internal sealed class Vp7Decoder {

  private const byte _OUTSIDE = 128;

  /// <summary>Everything a profile-1 frame may decline to keep: the per-stream probabilities and scan.</summary>
  private sealed class Probabilities {
    internal readonly byte[] Coefficients = new byte[Vp8Entropy.COEFFICIENT_PROBABILITY_COUNT];
    internal readonly byte[] MotionVectors = new byte[2 * _MV_PROBABILITIES];
    internal readonly byte[] LumaModes = new byte[4];
    internal readonly byte[] ChromaModes = new byte[3];
    internal readonly byte[] Scan = new byte[16];
    internal int Intra;
    internal int Last;

    internal void CopyTo(Probabilities other) {
      this.Coefficients.CopyTo(other.Coefficients, 0);
      this.MotionVectors.CopyTo(other.MotionVectors, 0);
      this.LumaModes.CopyTo(other.LumaModes, 0);
      this.ChromaModes.CopyTo(other.ChromaModes, 0);
      this.Scan.CopyTo(other.Scan, 0);
      other.Intra = this.Intra;
      other.Last = this.Last;
    }
  }

  private const int _MV_PROBABILITIES = 17;
  private const int _MV_LONG_BITS = 9;

  private readonly Probabilities _probabilities = new();
  private readonly Probabilities _saved = new();
  private readonly List<Vp8Frame> _pool = [];
  private readonly short[] _coefficients = new short[25 * 16];
  private readonly int[] _nonZero = new int[24];
  private readonly short[] _interDcPrediction = new short[4];
  private readonly bool[] _featureEnabled = new bool[4];
  private readonly int[] _featurePresent = new int[4];
  private readonly byte[] _featureIndex = new byte[12];
  private readonly byte[] _leftContexts = new byte[9];

  private byte[] _packet = [];
  private byte[] _aboveContexts = [];
  private Vp8MacroblockGrid? _grid;
  private Vp8Frame? _last;
  private Vp8Frame? _golden;
  private int _columns;
  private int _rows;
  private int _profile;

  private int _lumaDc, _lumaAc, _y2Dc, _y2Ac, _chromaDc, _chromaAc;

  /// <summary>The previous and golden references as the next frame will predict from them, before any fade.</summary>
  internal Vp8Frame? Last => this._last;

  internal Vp8Frame? Golden => this._golden;

  /// <summary>
  /// Whether the last frame faded the previous one in place: the case in which FFmpeg's decoder writes
  /// into a picture it has already handed out, so that its copy of that picture depends on whether the
  /// caller read it before the next frame was decoded.
  /// </summary>
  internal bool FadedInPlace { get; private set; }

  /// <summary>The brightness offset and scale the last frame faded the previous one by.</summary>
  internal (int Alpha, int Beta) Fade { get; private set; }

  internal int Width { get; private set; }
  internal int Height { get; private set; }

  /// <summary>Decodes one coded frame, every one of which VP7 shows.</summary>
  internal Vp8Frame Decode(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException($"A VP7 frame begins with a header of three or four bytes; this one is {data.Length}.");

    if (this._packet.Length < data.Length)
      this._packet = new byte[data.Length];
    data.CopyTo(this._packet);
    var packet = this._packet;

    var profile = (packet[0] >> 1) & 7;
    if (profile > 1)
      throw new NotSupportedException($"This VP7 frame states profile {profile}; VP7 defines profiles 0 and 1.");

    var isKeyFrame = (packet[0] & 1) == 0;
    var firstPartitionSize = (packet[0] | (packet[1] << 8) | (packet[2] << 16)) >> 4;
    var headerSize = 4 - profile;
    if (headerSize + firstPartitionSize > data.Length)
      throw new InvalidDataException(
        $"This VP7 frame states a first partition of {firstPartitionSize} byte(s), which does not fit in the "
        + $"{data.Length - headerSize} that follow its header. The packet is truncated.");

    this._profile = profile;
    var c = new Vp8BoolDecoder(packet, headerSize, firstPartitionSize);
    var probabilities = this._probabilities;

    if (isKeyFrame) {
      var width = c.ReadLiteral(12);
      var height = c.ReadLiteral(12);
      c.ReadLiteral(2); // horizontal and vertical scale: a display hint FFmpeg does not apply either
      c.ReadLiteral(2);
      if (width == 0 || height == 0)
        throw new InvalidDataException($"This VP7 key frame states a picture of {width}x{height}, which has no samples.");

      if (width != this.Width || height != this.Height || this._grid == null)
        this._Resize(width, height);

      Vp8Tables.DefaultCoefficientProbabilities.CopyTo(probabilities.Coefficients, 0);
      Vp8Trees.DefaultLumaModeProbabilities.CopyTo(probabilities.LumaModes);
      Vp8Trees.DefaultChromaModeProbabilities.CopyTo(probabilities.ChromaModes);
      Vp7Tables.DefaultMotionVectorProbabilities.CopyTo(probabilities.MotionVectors);
      Vp8Trees.ZigZag.CopyTo(probabilities.Scan);
    } else if (this._grid == null || this._last == null || this._golden == null)
      throw new InvalidDataException(
        "This VP7 stream begins with an interframe, which is a difference from frames there are none of; decoding has to start at a key frame.");

    if (isKeyFrame || profile > 0)
      Array.Clear(this._interDcPrediction);

    // B. Four macroblock-level features. FFmpeg reads and ignores them, and so does this.
    for (var i = 0; i < 4; ++i) {
      this._featureEnabled[i] = c.ReadFlag() != 0;
      if (!this._featureEnabled[i])
        continue;

      this._featurePresent[i] = c.ReadLiteral(8);
      for (var j = 0; j < 3; ++j)
        this._featureIndex[i * 3 + j] = (byte)(c.ReadFlag() != 0 ? c.ReadLiteral(8) : 255);

      var size = Vp7Tables.FeatureValueSize[profile * 4 + i];
      if (size != 0)
        for (var j = 0; j < 4; ++j)
          if (c.ReadFlag() != 0)
            c.ReadLiteral(size);
    }

    var tokenStart = headerSize + firstPartitionSize;
    var tokens = new Vp8BoolDecoder(packet, tokenStart, data.Length - tokenStart);

    // C. Dequantisation indices.
    var yac = c.ReadLiteral(7);
    var ydc = c.ReadFlag() != 0 ? c.ReadLiteral(7) : yac;
    var y2dc = c.ReadFlag() != 0 ? c.ReadLiteral(7) : yac;
    var y2ac = c.ReadFlag() != 0 ? c.ReadLiteral(7) : yac;
    var uvdc = c.ReadFlag() != 0 ? c.ReadLiteral(7) : yac;
    var uvac = c.ReadFlag() != 0 ? c.ReadLiteral(7) : yac;
    this._lumaDc = Vp7Tables.LumaDcQuantiser[ydc];
    this._lumaAc = Vp7Tables.LumaAcQuantiser[yac];
    this._y2Dc = Vp7Tables.Y2DcQuantiser[y2dc];
    this._y2Ac = Vp7Tables.Y2AcQuantiser[y2ac];
    this._chromaDc = Math.Min((int)Vp7Tables.LumaDcQuantiser[uvdc], 132);
    this._chromaAc = Vp7Tables.LumaAcQuantiser[uvac];

    // D. The golden frame update flag, interframes only.
    var refreshGolden = isKeyFrame || c.ReadFlag() != 0;

    var keepProbabilities = true;
    var fadePresent = true;
    if (profile > 0) {
      keepProbabilities = c.ReadFlag() != 0;
      if (!keepProbabilities)
        probabilities.CopyTo(this._saved);
      if (!isKeyFrame)
        fadePresent = c.ReadFlag() != 0;
    }

    // E. Fading of the previous frame.
    int alpha = 0, beta = 0;
    if (fadePresent && c.ReadFlag() != 0) {
      alpha = (sbyte)c.ReadLiteral(8);
      beta = (sbyte)c.ReadLiteral(8);
    }

    // F. The loop filter type, where profile 0 keeps it.
    var simpleFilter = false;
    if (profile == 0)
      simpleFilter = c.ReadFlag() != 0;

    // G. A restated coefficient scan order.
    if (c.ReadFlag() != 0)
      for (var i = 1; i < 16; ++i)
        probabilities.Scan[i] = Vp8Trees.ZigZag[c.ReadLiteral(4)];

    // H. Loop filter levels.
    if (profile > 0)
      simpleFilter = c.ReadFlag() != 0;
    var filterLevel = c.ReadLiteral(6);
    var sharpness = c.ReadLiteral(3);

    // I. Coefficient probability updates, as VP8's.
    var coefficients = probabilities.Coefficients;
    for (var i = 0; i < coefficients.Length; ++i)
      if (c.ReadBool(Vp8Tables.CoefficientUpdateProbabilities[i]) != 0)
        coefficients[i] = (byte)c.ReadLiteral(8);

    // J. What only an interframe carries.
    if (!isKeyFrame) {
      probabilities.Intra = c.ReadLiteral(8);
      probabilities.Last = c.ReadLiteral(8);
      if (c.ReadFlag() != 0)
        for (var i = 0; i < 4; ++i)
          probabilities.LumaModes[i] = (byte)c.ReadLiteral(8);
      if (c.ReadFlag() != 0)
        for (var i = 0; i < 3; ++i)
          probabilities.ChromaModes[i] = (byte)c.ReadLiteral(8);

      var update = Vp8Trees.MotionVectorUpdateProbabilities;
      for (var component = 0; component < 2; ++component)
        for (var j = 0; j < _MV_PROBABILITIES; ++j)
          if (c.ReadBool(update[component * Vp8Trees.MV_PROBABILITY_COUNT + j]) != 0) {
            var value = c.ReadLiteral(7) << 1;
            probabilities.MotionVectors[component * _MV_PROBABILITIES + j] = (byte)(value == 0 ? 1 : value);
          }
    }

    this.FadedInPlace = false;
    this.Fade = (alpha, beta);
    if (!isKeyFrame && (alpha != 0 || beta != 0))
      this._Fade(alpha, beta);

    var grid = this._grid!;
    this._ReadModes(ref c, grid, isKeyFrame);

    var current = this._TakeFreeFrame();
    this._Reconstruct(ref tokens, current, grid);
    Vp7LoopFilter.Apply(current, filterLevel, sharpness, simpleFilter, isKeyFrame);

    if (!keepProbabilities)
      this._saved.CopyTo(probabilities);

    if (refreshGolden)
      this._golden = current;
    this._last = current;
    return current;
  }

  private void _Resize(int width, int height) {
    this.Width = width;
    this.Height = height;
    this._columns = (width + 15) / 16;
    this._rows = (height + 15) / 16;
    this._grid = new(this._columns, this._rows);
    this._aboveContexts = new byte[this._columns * 9];
    this._pool.Clear();
    this._last = this._golden = null;
  }

  /// <summary>
  /// Brightens or darkens the previous frame before it is predicted from, writing a new previous
  /// frame where the golden frame is the same picture so that the golden frame keeps its samples.
  /// </summary>
  private void _Fade(int alpha, int beta) {
    var source = this._last!;
    var target = source;
    if (ReferenceEquals(this._golden, this._last)) {
      target = this._TakeFreeFrame();
      source.Cb.CopyTo(target.Cb, 0);
      source.Cr.CopyTo(target.Cr, 0);
      this._last = target;
    } else
      this.FadedInPlace = true;

    var from = source.Luma;
    var to = target.Luma;
    for (var i = 0; i < from.Length; ++i) {
      int y = from[i];
      to[i] = _Clamp(y + ((y * beta) >> 8) + alpha);
    }
  }

  // ============================================================================================
  // Modes and motion vectors
  // ============================================================================================

  private void _ReadModes(ref Vp8BoolDecoder c, Vp8MacroblockGrid grid, bool isKeyFrame) {
    var probabilities = this._probabilities;
    for (var row = 0; row < grid.Rows; ++row)
      for (var column = 0; column < grid.Columns; ++column) {
        var index = grid.IndexOf(row, column);

        for (var i = 0; i < 4; ++i)
          if (this._featureEnabled[i] && c.ReadBool(this._featurePresent[i]) != 0) {
            var probability = this._featureIndex.AsSpan(i * 3, 3);
            if (c.ReadBool(probability[0]) == 0)
              c.ReadBool(probability[1]);
            else
              c.ReadBool(probability[2]);
          }

        if (isKeyFrame) {
          _ReadKeyFrameModes(ref c, grid, index);
          continue;
        }

        if (c.ReadBool(probabilities.Intra) == 0) {
          var lumaMode = c.ReadTree(Vp8Trees.LumaMode, probabilities.LumaModes, 0);
          if (lumaMode == Vp8Mode.SUBBLOCK_PREDICTION) {
            var modes = grid.SubblockModes(index);
            for (var subblock = 0; subblock < 16; ++subblock)
              modes[subblock] = (byte)c.ReadTree(Vp8Trees.SubblockMode, Vp8Trees.SubblockModeProbabilities, 0);
          }

          grid.LumaMode[index] = (byte)lumaMode;
          grid.ChromaMode[index] = (byte)c.ReadTree(Vp8Trees.ChromaMode, probabilities.ChromaModes, 0);
          grid.ReferenceFrame[index] = Vp8Reference.CURRENT;
          grid.MotionVector[index] = Vp8MotionVector.Zero;
          continue;
        }

        grid.ReferenceFrame[index] = (byte)(c.ReadBool(probabilities.Last) != 0 ? Vp8Reference.GOLDEN : Vp8Reference.LAST);
        this._ReadMotionVectors(ref c, grid, index, row, column);
      }
  }

  /// <summary>
  /// A key frame macroblock's modes. A whole-block mode stands, for its neighbours' subblock mode
  /// context, as VP7's own choice: DC as DC and the other three as TM, where VP8 maps each to its
  /// own counterpart.
  /// </summary>
  private static void _ReadKeyFrameModes(ref Vp8BoolDecoder c, Vp8MacroblockGrid grid, int index) {
    var lumaMode = c.ReadTree(Vp8Trees.KeyFrameLumaMode, Vp8Trees.KeyFrameLumaModeProbabilities, 0);
    var modes = grid.SubblockModes(index);
    if (lumaMode == Vp8Mode.SUBBLOCK_PREDICTION) {
      var above = grid.SubblockModes(grid.Above(index));
      var left = grid.SubblockModes(grid.Left(index));
      for (var subblock = 0; subblock < 16; ++subblock) {
        int aboveMode = subblock < 4 ? above[subblock + 12] : modes[subblock - 4];
        int leftMode = (subblock & 3) == 0 ? left[subblock + 3] : modes[subblock - 1];
        modes[subblock] = (byte)c.ReadTree(
          Vp8Trees.SubblockMode, Vp8Tables.KeyFrameSubblockModeProbabilities,
          (aboveMode * Vp8Mode.SUBBLOCK_MODE_COUNT + leftMode) * (Vp8Mode.SUBBLOCK_MODE_COUNT - 1));
      }
    } else
      modes.Fill((byte)(lumaMode == Vp8Mode.DC_PREDICTION ? Vp8Mode.B_DC_PREDICTION : Vp8Mode.B_TRUE_MOTION_PREDICTION));

    grid.LumaMode[index] = (byte)lumaMode;
    grid.ChromaMode[index] = (byte)c.ReadTree(Vp8Trees.ChromaMode, Vp8Trees.KeyFrameChromaModeProbabilities, 0);
    grid.ReferenceFrame[index] = Vp8Reference.CURRENT;
    grid.MotionVector[index] = Vp8MotionVector.Zero;
  }

  /// <summary>
  /// The census over twelve neighbours and the inter mode it selects probabilities for:
  /// FFmpeg's <c>vp7_decode_mvs</c>, including the reference decoder's wrap of a column offset
  /// across the padding column into the row before, which <c>vp7_calculate_mb_offset</c> replicates.
  /// </summary>
  /// <summary>
  /// The census over twelve neighbours: FFmpeg's <c>vp7_decode_mvs</c> up to the mode, including
  /// the reference decoder's wrap of a column offset across the padding column into the row before,
  /// which <c>vp7_calculate_mb_offset</c> replicates. <paramref name="near"/> receives zero, nearest
  /// and near; <paramref name="count"/> their weights.
  /// </summary>
  internal static void Survey(
    Vp8MacroblockGrid grid, int row, int column, int profile, Span<Vp8MotionVector> near, Span<int> count) {
    near.Clear();
    count.Clear();
    const int ZERO = 0, NEAREST = 1, NEAR = 2;

    var width = grid.Columns + 1;
    var boundary = profile == 0 ? 1 : 0;
    var predictors = Vp7Tables.MotionVectorPredictors;
    for (var i = 0; i < 12; ++i) {
      int yOffset = predictors[i * 4], xOffset = predictors[i * 4 + 1];
      int subblock = predictors[i * 4 + 2], score = predictors[i * 4 + 3];
      var at = (row + yOffset) * width + column + xOffset;
      var slot = ZERO;
      if (at >= boundary && at % width != width - 1) {
        var edge = grid.IndexOf(at / width, at % width);
        var mv = grid.LumaMode[edge] == Vp8Mode.SPLIT_MV ? grid.SubblockMotionVectors(edge)[subblock] : grid.MotionVector[edge];
        if (!mv.IsZero) {
          if (!near[NEAREST].IsZero) {
            if (mv == near[NEAREST])
              slot = NEAREST;
            else if (!near[NEAR].IsZero) {
              if (mv != near[NEAR])
                continue;
              slot = NEAR;
            } else {
              near[NEAR] = mv;
              slot = NEAR;
            }
          } else {
            near[NEAREST] = mv;
            slot = NEAREST;
          }
        }
      }

      count[slot] += score;
    }
  }

  /// <summary>The vector an explicitly coded one is an offset from.</summary>
  internal static Vp8MotionVector Best(ReadOnlySpan<Vp8MotionVector> near, ReadOnlySpan<int> count)
    => count[1] > count[2]
      ? (count[0] > count[1] ? Vp8MotionVector.Zero : near[1])
      : (count[0] > count[2] ? Vp8MotionVector.Zero : near[2]);

  private void _ReadMotionVectors(ref Vp8BoolDecoder c, Vp8MacroblockGrid grid, int index, int row, int column) {
    Span<Vp8MotionVector> near = stackalloc Vp8MotionVector[3];
    Span<int> count = stackalloc int[3];
    const int ZERO = 0, NEAREST = 1, NEAR = 2;
    Survey(grid, row, column, this._profile, near, count);

    var contexts = Vp7Tables.ModeContexts;
    if (c.ReadBool(contexts[count[ZERO] * 4]) == 0) {
      grid.LumaMode[index] = grid.ChromaMode[index] = Vp8Mode.ZERO_MV;
      grid.MotionVector[index] = Vp8MotionVector.Zero;
      return;
    }

    if (c.ReadBool(contexts[count[NEAREST] * 4 + 1]) == 0) {
      grid.LumaMode[index] = grid.ChromaMode[index] = Vp8Mode.NEAREST_MV;
      grid.MotionVector[index] = near[NEAREST];
      return;
    }

    if (c.ReadBool(contexts[count[NEAR] * 4 + 2]) == 0) {
      grid.LumaMode[index] = grid.ChromaMode[index] = Vp8Mode.NEAR_MV;
      grid.MotionVector[index] = near[NEAR];
      return;
    }

    var best = Best(near, count);

    if (c.ReadBool(contexts[count[NEAR] * 4 + 3]) == 0) {
      grid.LumaMode[index] = grid.ChromaMode[index] = Vp8Mode.NEW_MV;
      grid.MotionVector[index] = this._ReadMotionVector(ref c).Plus(best);
      return;
    }

    grid.LumaMode[index] = grid.ChromaMode[index] = Vp8Mode.SPLIT_MV;
    var partitioning = c.ReadTree(Vp8Trees.SplitPartition, Vp8Trees.SplitPartitionProbabilities, 0);
    var membership = Vp8Split.Membership.Slice(partitioning * 16, 16);
    var subsets = Vp8Split.SubsetCount[partitioning];
    var vectors = grid.SubblockMotionVectors(index);

    for (var subset = 0; subset < subsets; ++subset) {
      var first = 0;
      while (membership[first] != subset)
        ++first;

      var left = grid.SubblockMotionVectorLeft(index, first);
      var above = grid.SubblockMotionVectorAbove(index, first);
      var mode = c.ReadTree(Vp8Trees.SubblockMotionVectorReference, Vp7Tables.SubblockMotionVectorProbabilities, 0);
      var vector = mode switch {
        Vp8Mode.LEFT_4X4 => left,
        Vp8Mode.ABOVE_4X4 => above,
        Vp8Mode.ZERO_4X4 => Vp8MotionVector.Zero,
        _ => this._ReadMotionVector(ref c).Plus(best),
      };

      for (var subblock = first; subblock < 16; ++subblock)
        if (membership[subblock] == subset)
          vectors[subblock] = vector;
    }

    grid.MotionVector[index] = vectors[15];
  }

  private Vp8MotionVector _ReadMotionVector(ref Vp8BoolDecoder c) {
    var row = _ReadComponent(ref c, this._probabilities.MotionVectors, 0);
    var column = _ReadComponent(ref c, this._probabilities.MotionVectors, _MV_PROBABILITIES);
    return new(row, column);
  }

  /// <summary>One component, VP7's way: eight long bits where VP8 has ten, doubled into eighth samples.</summary>
  private static int _ReadComponent(ref Vp8BoolDecoder c, byte[] probabilities, int offset) {
    int magnitude;
    if (c.ReadBool(probabilities[offset]) != 0) {
      magnitude = 0;
      for (var bit = 0; bit < 3; ++bit)
        magnitude += c.ReadBool(probabilities[offset + _MV_LONG_BITS + bit]) << bit;
      for (var bit = 7; bit > 3; --bit)
        magnitude += c.ReadBool(probabilities[offset + _MV_LONG_BITS + bit]) << bit;
      if ((magnitude & 0xF0) == 0 || c.ReadBool(probabilities[offset + _MV_LONG_BITS + 3]) != 0)
        magnitude += 8;
    } else
      magnitude = c.ReadTree(Vp8Trees.SmallMotionVector, probabilities, offset + Vp8Trees.MV_SHORT_TREE);

    if (magnitude != 0 && c.ReadBool(probabilities[offset + 1]) != 0)
      magnitude = -magnitude;
    return magnitude * 2;
  }

  // ============================================================================================
  // Residue and reconstruction
  // ============================================================================================

  private void _Reconstruct(ref Vp8BoolDecoder tokens, Vp8Frame frame, Vp8MacroblockGrid grid) {
    Array.Clear(this._aboveContexts);
    Array.Clear(this._leftContexts);

    for (var row = 0; row < grid.Rows; ++row)
      for (var column = 0; column < grid.Columns; ++column) {
        var index = grid.IndexOf(row, column);
        var lumaMode = grid.LumaMode[index];
        var reference = grid.ReferenceFrame[index];
        var skipped = !this._ReadResidue(ref tokens, lumaMode, reference, this._aboveContexts.AsSpan(column * 9, 9));

        if (reference == Vp8Reference.CURRENT)
          this._PredictIntra(frame, grid, index, row, column, lumaMode, skipped);
        else
          this._PredictInter(frame, grid, index, row, column, lumaMode);

        if (skipped)
          continue;

        var lumaStride = frame.LumaWidth;
        if (lumaMode != Vp8Mode.SUBBLOCK_PREDICTION)
          for (var block = 0; block < 16; ++block)
            this._AddResidue(block, frame.Luma, row * 16 * lumaStride + column * 16 + (block >> 2) * 4 * lumaStride + (block & 3) * 4, lumaStride);

        var chromaStride = frame.ChromaWidth;
        for (var block = 16; block < 24; ++block) {
          var within = block & 3;
          this._AddResidue(block, block < 20 ? frame.Cb : frame.Cr,
            row * 8 * chromaStride + column * 8 + (within >> 1) * 4 * chromaStride + (within & 1) * 4, chromaStride);
        }
      }
  }

  /// <summary>Reads one macroblock's coefficients; false when none at all were coded.</summary>
  /// <remarks>
  /// The left contexts carry from the end of one macroblock row into the start of the next, which is
  /// VP7's and not VP8's: only the first row starts from zero.
  /// </remarks>
  private bool _ReadResidue(ref Vp8BoolDecoder tokens, int lumaMode, int reference, Span<byte> above) {
    var coefficients = this._coefficients.AsSpan();
    coefficients.Clear();
    var left = this._leftContexts;
    var scan = this._probabilities.Scan;
    var total = 0;
    var blockDc = 0;
    var lumaStart = 0;
    var lumaPlane = Vp8CoefficientPlane.LUMA_WITH_DC;

    if (lumaMode != Vp8Mode.SUBBLOCK_PREDICTION) {
      var y2 = coefficients.Slice(24 * 16, 16);
      var nonZero = this._ReadBlock(ref tokens, y2, Vp8CoefficientPlane.Y2, 0, above[8] + left[8], this._y2Dc, this._y2Ac, Vp8Trees.ZigZag);
      left[8] = above[8] = (byte)(nonZero != 0 ? 1 : 0);

      if (reference != Vp8Reference.CURRENT)
        nonZero |= PredictDc(y2, this._interDcPrediction.AsSpan((reference - 1) * 2, 2));

      if (nonZero != 0) {
        total += nonZero;
        blockDc = 1;
        if (nonZero == 1)
          Vp7Transform.InvertWalshHadamardDc(y2, coefficients);
        else
          Vp7Transform.InvertWalshHadamard(y2, coefficients);
      }

      lumaStart = 1;
      lumaPlane = Vp8CoefficientPlane.LUMA_AFTER_Y2;
    }

    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var block = y * 4 + x;
        var nonZero = this._ReadBlock(ref tokens, coefficients.Slice(block * 16, 16), lumaPlane, lumaStart,
          left[y] + above[x], this._lumaDc, this._lumaAc, scan);
        this._nonZero[block] = nonZero + blockDc;
        above[x] = left[y] = (byte)(nonZero != 0 ? 1 : 0);
        total += nonZero;
      }

    for (var plane = 4; plane < 6; ++plane)
      for (var y = 0; y < 2; ++y)
        for (var x = 0; x < 2; ++x) {
          var block = 16 + (plane - 4) * 4 + (y << 1) + x;
          var nonZero = this._ReadBlock(ref tokens, coefficients.Slice(block * 16, 16), Vp8CoefficientPlane.CHROMA, 0,
            left[plane + 2 * y] + above[plane + 2 * x], this._chromaDc, this._chromaAc, scan);
          this._nonZero[block] = nonZero;
          above[plane + 2 * x] = left[plane + 2 * y] = (byte)(nonZero != 0 ? 1 : 0);
          total += nonZero;
        }

    return total != 0;
  }

  /// <summary>
  /// One block's tokens: FFmpeg's <c>decode_block_coeffs</c> with its VP7 behaviour, where a zero
  /// may be followed by the end of the block. Returns the position after the last coefficient read.
  /// </summary>
  private int _ReadBlock(
    ref Vp8BoolDecoder c, Span<short> block, int plane, int i, int context, int dcFactor, int acFactor, ReadOnlySpan<byte> scan) {
    var probabilities = this._probabilities.Coefficients;
    var bands = Vp8Trees.CoefficientBands;
    var at = Vp8Tables.CoefficientProbabilityOffset(plane, bands[i], context);
    if (c.ReadBool(probabilities[at]) == 0)
      return 0;

    var checkEnd = false;
    while (true) {
      if (checkEnd && c.ReadBool(probabilities[at]) == 0)
        break;
      checkEnd = true;

      if (c.ReadBool(probabilities[at + 1]) == 0) {
        if (++i == 16)
          break;
        at = Vp8Tables.CoefficientProbabilityOffset(plane, bands[i], 0);
        continue;
      }

      int coefficient;
      int next;
      if (c.ReadBool(probabilities[at + 2]) == 0) {
        coefficient = 1;
        next = 1;
      } else {
        if (c.ReadBool(probabilities[at + 3]) == 0) {
          coefficient = c.ReadBool(probabilities[at + 4]);
          if (coefficient != 0)
            coefficient += c.ReadBool(probabilities[at + 5]);
          coefficient += 2;
        } else if (c.ReadBool(probabilities[at + 6]) == 0) {
          if (c.ReadBool(probabilities[at + 7]) == 0)
            coefficient = 5 + c.ReadBool(159);
          else {
            coefficient = 7 + (c.ReadBool(165) << 1);
            coefficient += c.ReadBool(145);
          }
        } else {
          var a = c.ReadBool(probabilities[at + 8]);
          var b = c.ReadBool(probabilities[at + 9 + a]);
          var category = (a << 1) + b;
          coefficient = 3 + (8 << category);
          var extra = Vp8Trees.CategoryProbabilities.Slice(
            Vp8Trees.CategoryProbabilityOffset[category + 2], Vp8Trees.CategoryBits[category + 2]);
          var value = 0;
          foreach (var probability in extra)
            value = (value << 1) + c.ReadBool(probability);
          coefficient += value;
        }

        next = 2;
      }

      var signed = c.ReadFlag() != 0 ? -coefficient : coefficient;
      block[scan[i]] = (short)(signed * (i == 0 ? dcFactor : acFactor));
      if (++i == 16)
        break;
      at = Vp8Tables.CoefficientProbabilityOffset(plane, bands[i], next);
    }

    return i;
  }

  /// <summary>
  /// VP7's prediction of an inter macroblock's Y2 DC from the last one coded against the same
  /// reference: once the same non-zero DC has come four times running, it is added. FFmpeg's
  /// <c>inter_predict_dc</c>, its sixteen-bit arithmetic included.
  /// </summary>
  internal static int PredictDc(Span<short> block, Span<short> prediction) {
    var dc = block[0];
    var applied = 0;
    if (prediction[1] > 3) {
      dc = (short)(dc + prediction[0]);
      applied = 1;
    }

    if (prediction[0] == 0 || dc == 0 || (prediction[0] ^ dc) < 0) {
      block[0] = prediction[0] = dc;
      prediction[1] = 0;
    } else {
      if (prediction[0] == dc)
        ++prediction[1];
      block[0] = prediction[0] = dc;
    }

    return applied;
  }

  private void _AddResidue(int block, byte[] plane, int offset, int stride) {
    var count = this._nonZero[block];
    if (count == 0)
      return;

    var coefficients = this._coefficients.AsSpan(block * 16, 16);
    if (count == 1)
      Vp7Transform.AddDc(coefficients, plane, offset, stride);
    else
      Vp7Transform.Add(coefficients, plane, offset, stride);
  }

  private void _PredictIntra(Vp8Frame frame, Vp8MacroblockGrid grid, int index, int row, int column, int lumaMode, bool skipped) {
    var lumaStride = frame.LumaWidth;
    var chromaStride = frame.ChromaWidth;
    var x = column * 16;
    var y = row * 16;

    Span<byte> edge = stackalloc byte[21];
    Span<byte> left = stackalloc byte[16];

    Vp8IntraPrediction.GatherAbove(frame.Luma, lumaStride, frame.LumaWidth, x, y, 16, 4, edge, _OUTSIDE, _OUTSIDE);
    Vp8IntraPrediction.GatherLeft(frame.Luma, lumaStride, x, y, 16, left, _OUTSIDE);

    if (lumaMode == Vp8Mode.SUBBLOCK_PREDICTION)
      this._PredictSubblocks(frame, grid, index, x, y, edge, left, skipped);
    else
      Vp8IntraPrediction.PredictBlock(frame.Luma, lumaStride, x, y, 16, lumaMode, edge, left, row > 0, column > 0);

    var chromaX = column * 8;
    var chromaY = row * 8;
    var chromaMode = grid.ChromaMode[index];
    foreach (var plane in (ReadOnlySpan<byte[]>)[frame.Cb, frame.Cr]) {
      Vp8IntraPrediction.GatherAbove(plane, chromaStride, frame.ChromaWidth, chromaX, chromaY, 8, 0, edge, _OUTSIDE, _OUTSIDE);
      Vp8IntraPrediction.GatherLeft(plane, chromaStride, chromaX, chromaY, 8, left, _OUTSIDE);
      Vp8IntraPrediction.PredictBlock(plane, chromaStride, chromaX, chromaY, 8, chromaMode, edge, left, row > 0, column > 0);
    }
  }

  /// <summary>The sixteen subblocks of a B_PRED macroblock, each predicted and reconstructed before the next.</summary>
  private void _PredictSubblocks(
    Vp8Frame frame, Vp8MacroblockGrid grid, int index, int x, int y,
    ReadOnlySpan<byte> macroblockAbove, ReadOnlySpan<byte> macroblockLeft, bool skipped) {
    var stride = frame.LumaWidth;
    var plane = frame.Luma;
    var modes = grid.SubblockModes(index);

    Span<byte> above = stackalloc byte[8];
    Span<byte> left = stackalloc byte[4];
    Span<byte> edge = stackalloc byte[9];

    for (var block = 0; block < 16; ++block) {
      var subRow = block >> 2;
      var subColumn = block & 3;
      var blockX = x + subColumn * 4;
      var blockY = y + subRow * 4;
      var aboveRow = (blockY - 1) * stride;

      if (subRow == 0)
        macroblockAbove.Slice(1 + subColumn * 4, 8).CopyTo(above);
      else {
        for (var i = 0; i < 4; ++i)
          above[i] = plane[aboveRow + blockX + i];
        if (subColumn < 3)
          for (var i = 0; i < 4; ++i)
            above[4 + i] = plane[aboveRow + blockX + 4 + i];
        else
          macroblockAbove.Slice(17, 4).CopyTo(above[4..]);
      }

      if (subColumn == 0)
        macroblockLeft.Slice(subRow * 4, 4).CopyTo(left);
      else
        for (var i = 0; i < 4; ++i)
          left[i] = plane[(blockY + i) * stride + blockX - 1];

      var corner = subRow == 0
        ? macroblockAbove[subColumn * 4]
        : subColumn == 0
          ? macroblockLeft[subRow * 4 - 1]
          : plane[aboveRow + blockX - 1];

      edge[0] = left[3];
      edge[1] = left[2];
      edge[2] = left[1];
      edge[3] = left[0];
      edge[4] = corner;
      above[..4].CopyTo(edge[5..]);

      Vp8IntraPrediction.PredictSubblock(plane, stride, blockX, blockY, modes[block], edge, above, left);
      if (!skipped)
        this._AddResidue(block, plane, blockY * stride + blockX, stride);
    }
  }

  private void _PredictInter(Vp8Frame frame, Vp8MacroblockGrid grid, int index, int row, int column, int lumaMode) {
    var reference = grid.ReferenceFrame[index] == Vp8Reference.GOLDEN ? this._golden! : this._last!;
    var lumaStride = frame.LumaWidth;
    var chromaStride = frame.ChromaWidth;
    var x = column * 16;
    var y = row * 16;

    var isSplit = lumaMode == Vp8Mode.SPLIT_MV;
    var vectors = grid.SubblockMotionVectors(index);
    var whole = grid.MotionVector[index];

    for (var block = 0; block < 16; ++block)
      Vp8InterPrediction.PredictBlock(
        reference.Luma, frame.Luma, lumaStride, frame.LumaHeight,
        x + (block & 3) * 4, y + (block >> 2) * 4, isSplit ? vectors[block] : whole, bicubic: true);

    ReadOnlySpan<int> quarterOrigins = [0, 2, 8, 10];
    for (var quarter = 0; quarter < 4; ++quarter) {
      var chroma = isSplit
        ? Vp8InterPrediction.ChromaVector(vectors, quarterOrigins[quarter], wholePixelsOnly: false)
        : Vp8InterPrediction.ChromaVector(whole, wholePixelsOnly: false);
      var blockX = column * 8 + (quarter & 1) * 4;
      var blockY = row * 8 + (quarter >> 1) * 4;
      Vp8InterPrediction.PredictBlock(reference.Cb, frame.Cb, chromaStride, frame.ChromaHeight, blockX, blockY, chroma, bicubic: true);
      Vp8InterPrediction.PredictBlock(reference.Cr, frame.Cr, chromaStride, frame.ChromaHeight, blockX, blockY, chroma, bicubic: true);
    }
  }

  private Vp8Frame _TakeFreeFrame() {
    foreach (var candidate in this._pool)
      if (candidate != this._last && candidate != this._golden)
        return candidate;

    var frame = new Vp8Frame(this._columns, this._rows);
    this._pool.Add(frame);
    return frame;
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
