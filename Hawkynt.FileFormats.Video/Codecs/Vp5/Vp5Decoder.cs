using System;
using System.IO;

namespace FileFormat.Codecs.Vp5;

/// <summary>
/// On2 VP5: the frame header, the entropy models, the macroblock layer and the reconstruction.
/// </summary>
/// <remarks>
/// <b>What VP5 is.</b> A 4:2:0 codec of the VP3 family with an arithmetic coder in place of VP3's
/// Huffman one. Pictures are intra or forward-predicted; there are no bidirectional pictures and so
/// no reorder queue, and the whole inter-picture dependency state is the previous reconstruction plus
/// a golden one that only a key frame replaces. A predicted macroblock chooses one of ten modes —
/// which reference, and whether it carries no vector, a vector copied from a neighbour, a vector
/// coded as a delta, or four vectors, one per luminance block.
/// <para/>
/// <b>Where it came from.</b> Converted from FFmpeg's <c>libavcodec/vp5.c</c>, <c>vp56.c</c>,
/// <c>vp5dsp.c</c> and <c>vp3dsp.c</c>, which are LGPL-2.1-or-later and so absorbable here; see
/// <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this file. That is the first rung of this repository's
/// sourcing ladder and it applies because there is no rung above it to take: On2 published a VP6
/// bitstream specification and never published a VP5 one, so no normative text exists to prefer.
/// <para/>
/// <b>Interlacing.</b> A key frame may flag it, and then each macroblock carries a bit saying whether
/// it is field-coded. In a field-coded macroblock the two luminance block rows are the two fields
/// rather than the top and bottom halves, and prediction reads every other line of a window twice as
/// tall. The chrominance blocks are never split, and they come out of that window at a different
/// depth from the luminance ones — an asymmetry with no obvious reason behind it, kept because it is
/// what real files decode as.
/// </remarks>
internal sealed class Vp5Decoder {

  private readonly byte[] _coefficientModel = new byte[11];
  private readonly byte[] _contextModel = new byte[5];
  private readonly byte[] _typeProbabilities = new byte[10];
  private readonly byte[] _vectorProbabilities = new byte[7];
  private readonly short[][] _blockCoefficients = [
    new short[64], new short[64], new short[64], new short[64], new short[64], new short[64],
  ];
  private readonly byte[] _prediction = new byte[64];
  private readonly byte[] _window = new byte[13 * 12];
  private readonly Vp5MotionVector[] _motion = new Vp5MotionVector[6];
  private readonly Vp5MotionVector[] _candidates = new Vp5MotionVector[2];
  private readonly Vp5DcPredictor[] _left = new Vp5DcPredictor[4];
  private readonly short[,] _previousDc = new short[3, 3];
  private readonly int[] _aboveIndex = new int[6];
  private readonly byte[,] _coefficientContexts = new byte[4, 64];
  private readonly byte[] _lastCoefficient = new byte[4];
  private readonly Vp5Model _model = new();

  private Vp5RangeDecoder _entropy;
  private Vp5Macroblock[] _macroblocks = [];
  private Vp5DcPredictor[] _above = [];
  private Vp5Frame? _current;
  private Vp5Frame? _previous;
  private Vp5Frame? _golden;
  private Vp5MacroblockType _previousType;
  private bool _isKeyFrame;
  private bool _interlaced;
  private bool _interlacedBlock;
  private int _interlaceProbability;
  private bool _hasDecodedAPicture;
  private int _quantizer = -1;
  private int _dequantDc;
  private int _dequantAc;

  /// <summary>The coded picture width, which is what a VP5 decoder hands out; VP5 does not crop.</summary>
  internal int Width => this.MacroblockWidth * 16;

  /// <summary>The coded picture height.</summary>
  internal int Height => this.MacroblockHeight * 16;

  internal int MacroblockWidth { get; private set; }
  internal int MacroblockHeight { get; private set; }

  /// <summary>Decodes one coded picture and returns the reconstruction, in coded row order.</summary>
  /// <remarks>
  /// The frame is this decoder's own buffer and not a copy. It stays intact across the next call,
  /// which needs it as the previous picture, and is overwritten by the one after that; a caller who
  /// wants to keep the samples has to take them before then. Nothing is copied per frame precisely
  /// because of that — the two buffers change places instead.
  /// </remarks>
  internal Vp5Frame Decode(ReadOnlyMemory<byte> packet) {
    this._ParseHeader(packet);

    if (this._isKeyFrame) {
      this._InitializeDefaultModels();
      for (var i = 0; i < this._macroblocks.Length; ++i)
        this._macroblocks[i].Type = Vp5MacroblockType.Intra;
    } else {
      this._ParseMacroblockTypeModels();
      this._ParseVectorModels();
      this._previousType = Vp5MacroblockType.InterNoVectorPrevious;
    }

    this._ParseCoefficientModels();
    if (this._interlaced)
      this._interlaceProbability = this._entropy.ReadLiteral(8);

    this._ResetDirectCurrentPrediction();

    for (var row = 0; row < this.MacroblockHeight; ++row) {
      // The left-hand predictors name no picture at the start of a row, which is not the same as
      // naming the current one: zeroing this array would make every intra block find a neighbour
      // that is not there.
      for (var i = 0; i < this._left.Length; ++i)
        this._left[i] = new() { Reference = Vp5Reference.None };

      Array.Clear(this._coefficientContexts);
      this._lastCoefficient.AsSpan().Fill(24);
      this._ResetAboveIndices();

      for (var column = 0; column < this.MacroblockWidth; ++column) {
        this._DecodeMacroblock(row, column);
        this._AdvanceAboveIndices();
      }
    }

    var decoded = this._current!;
    if (this._isKeyFrame)
      this._golden!.CopyFrom(decoded);

    // The reconstruction just finished becomes the previous picture, and the buffer it displaced is
    // what the next one is built in. Nothing is copied: only the two references change places.
    (this._previous, this._current) = (decoded, this._previous);
    this._hasDecodedAPicture = true;
    return decoded;
  }

  // ==============================================================================================
  // Frame header
  // ==============================================================================================

  private void _ParseHeader(ReadOnlyMemory<byte> packet) {
    if (packet.IsEmpty)
      throw new InvalidDataException("A VP5 frame carries no range-coder bytes at all.");

    this._entropy = new(packet);
    this._isKeyFrame = this._entropy.ReadFlag() == 0;
    _ = this._entropy.ReadFlag();
    this._SetQuantizer(this._entropy.ReadLiteral(6));

    if (!this._isKeyFrame) {
      if (!this._hasDecodedAPicture)
        throw new InvalidDataException(
          "This VP5 stream begins with an inter frame, so there is no reference picture to predict it from.");
      return;
    }

    _ = this._entropy.ReadLiteral(8);
    var majorVersion = this._entropy.ReadLiteral(5);
    if (majorVersion > 5)
      throw new InvalidDataException($"VP5 bitstream version {majorVersion} is outside the defined range of 0 to 5.");

    _ = this._entropy.ReadLiteral(2);
    this._interlaced = this._entropy.ReadFlag() != 0;

    var rows = this._entropy.ReadLiteral(8);
    var columns = this._entropy.ReadLiteral(8);
    if (rows == 0 || columns == 0)
      throw new InvalidDataException(
        $"A VP5 key frame states a coded size of {columns << 4}x{rows << 4}, which has no area.");

    var displayRows = this._entropy.ReadLiteral(8);
    var displayColumns = this._entropy.ReadLiteral(8);
    if (displayRows == 0 || displayColumns == 0 || displayRows > rows || displayColumns > columns)
      throw new InvalidDataException(
        $"A VP5 key frame states a display size of {displayColumns << 4}x{displayRows << 4}, which does not "
        + $"fit inside the coded {columns << 4}x{rows << 4}.");

    _ = this._entropy.ReadLiteral(2);
    this._EnsureGeometry(columns, rows);
  }

  /// <summary>
  /// Allocates for a stated picture size, reusing what is already there when it has not changed.
  /// </summary>
  /// <remarks>
  /// No ceiling is checked because the format already has one: both macroblock counts are eight-bit
  /// fields, so the largest picture VP5 can state at all is 255 macroblocks a side — 4080 samples, or
  /// about fifty megabytes across the three buffers this holds. That is large but it is not unbounded,
  /// and a limit invented here would refuse files the format allows.
  /// </remarks>
  private void _EnsureGeometry(int macroblockWidth, int macroblockHeight) {
    if (this.MacroblockWidth == macroblockWidth && this.MacroblockHeight == macroblockHeight && this._current != null)
      return;

    this.MacroblockWidth = macroblockWidth;
    this.MacroblockHeight = macroblockHeight;
    this._macroblocks = new Vp5Macroblock[macroblockWidth * macroblockHeight];
    this._above = new Vp5DcPredictor[4 * macroblockWidth + 6];
    this._current = new(macroblockWidth, macroblockHeight);
    this._previous = new(macroblockWidth, macroblockHeight);
    this._golden = new(macroblockWidth, macroblockHeight);
    this._hasDecodedAPicture = false;
  }

  private void _SetQuantizer(int quantizer) {
    this._quantizer = quantizer;
    this._dequantDc = Vp5Tables.DcDequant[quantizer] << 2;
    this._dequantAc = Vp5Tables.AcDequant[quantizer] << 2;
  }

  // ==============================================================================================
  // Entropy models
  // ==============================================================================================

  private void _InitializeDefaultModels() {
    for (var component = 0; component < 2; ++component) {
      this._model.VectorSign[component] = 0x80;
      this._model.VectorDeltaCoded[component] = 0x80;
      this._model.VectorLowBits[component, 0] = 0x55;
      this._model.VectorLowBits[component, 1] = 0x80;
      for (var node = 0; node < 7; ++node)
        this._model.VectorHighBits[component, node] = 0x80;
    }

    for (var context = 0; context < 3; ++context)
    for (var type = 0; type < 10; ++type)
    for (var statistic = 0; statistic < 2; ++statistic)
      this._model.TypeStats[context, type, statistic] = Vp5Tables.DefaultMacroblockTypeStats[context, type, statistic];
  }

  /// <summary>
  /// Reads the macroblock-type statistics and turns them into the probabilities the type tree uses.
  /// </summary>
  /// <remarks>
  /// The statistics are a pair of counts per type per context, updated by signed deltas. The
  /// probabilities are then computed from them, once per possible preceding type, by weighting each
  /// type by its second count and setting the weight of the preceding type itself to nothing — which
  /// is what makes "the same type again" a separate decision, taken first, at node zero.
  /// </remarks>
  private void _ParseMacroblockTypeModels() {
    for (var context = 0; context < 3; ++context) {
      if (this._entropy.ReadBool(174) != 0) {
        var preset = this._entropy.ReadLiteral(4);
        for (var type = 0; type < 10; ++type)
        for (var statistic = 0; statistic < 2; ++statistic)
          this._model.TypeStats[context, type, statistic] =
            Vp5Tables.PredefinedMacroblockTypeStats[preset, context, type, statistic];
      }

      if (this._entropy.ReadBool(254) == 0)
        continue;

      for (var type = 0; type < 10; ++type)
      for (var statistic = 0; statistic < 2; ++statistic) {
        if (this._entropy.ReadBool(205) == 0)
          continue;

        var negative = this._entropy.ReadFlag();
        var delta = this._entropy.ReadTree(Vp5Tables.MacroblockTypeModelTree, Vp5Tables.MacroblockTypeModelModel);
        if (delta == 0)
          delta = 4 * this._entropy.ReadLiteral(7);

        this._model.TypeStats[context, type, statistic] =
          unchecked((byte)(this._model.TypeStats[context, type, statistic] + ((delta ^ -negative) + negative)));
      }
    }

    Span<int> weights = stackalloc int[10];
    for (var context = 0; context < 3; ++context) {
      for (var type = 0; type < 10; ++type)
        weights[type] = 100 * this._model.TypeStats[context, type, 1];

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
        var same = this._model.TypeStats[context, previous, 0];
        var other = this._model.TypeStats[context, previous, 1];

        this._model.TypeProbabilities[context, previous, 0] = (byte)(255 - 255 * same / (1 + same + other));
        this._model.TypeProbabilities[context, previous, 1] = (byte)(1 + 255 * p0234 / (1 + p0234 + p156789));
        this._model.TypeProbabilities[context, previous, 2] = (byte)(1 + 255 * p02 / (1 + p0234));
        this._model.TypeProbabilities[context, previous, 3] = (byte)(1 + 255 * p17 / (1 + p156789));
        this._model.TypeProbabilities[context, previous, 4] = (byte)(1 + 255 * weights[0] / (1 + p02));
        this._model.TypeProbabilities[context, previous, 5] = (byte)(1 + 255 * weights[3] / (1 + p34));
        this._model.TypeProbabilities[context, previous, 6] = (byte)(1 + 255 * weights[1] / (1 + p17));
        this._model.TypeProbabilities[context, previous, 7] = (byte)(1 + 255 * p56 / (1 + p5689));
        this._model.TypeProbabilities[context, previous, 8] = (byte)(1 + 255 * weights[5] / (1 + p56));
        this._model.TypeProbabilities[context, previous, 9] = (byte)(1 + 255 * weights[8] / (1 + p89));

        weights[previous] = saved;
      }
    }
  }

  private void _ParseVectorModels() {
    for (var component = 0; component < 2; ++component) {
      if (this._entropy.ReadBool(Vp5Tables.VectorModelUpdate[component, 0]) != 0)
        this._model.VectorDeltaCoded[component] = this._entropy.ReadProbability();
      if (this._entropy.ReadBool(Vp5Tables.VectorModelUpdate[component, 1]) != 0)
        this._model.VectorSign[component] = this._entropy.ReadProbability();
      if (this._entropy.ReadBool(Vp5Tables.VectorModelUpdate[component, 2]) != 0)
        this._model.VectorLowBits[component, 0] = this._entropy.ReadProbability();
      if (this._entropy.ReadBool(Vp5Tables.VectorModelUpdate[component, 3]) != 0)
        this._model.VectorLowBits[component, 1] = this._entropy.ReadProbability();
    }

    for (var component = 0; component < 2; ++component)
    for (var node = 0; node < 7; ++node)
      if (this._entropy.ReadBool(Vp5Tables.VectorModelUpdate[component, node + 4]) != 0)
        this._model.VectorHighBits[component, node] = this._entropy.ReadProbability();
  }

  /// <summary>
  /// Reads the coefficient probabilities and expands them into the context-conditioned tables.
  /// </summary>
  /// <remarks>
  /// Only eleven probabilities a plane are transmitted for the direct-current coefficient and eleven
  /// per code type and coefficient group for the rest. The per-context tables the coefficient loop
  /// actually reads are an affine function of those, held in
  /// <see cref="Vp5Tables.DcContextLinear"/> and <see cref="Vp5Tables.AcContextLinear"/>.
  /// <para/>
  /// A probability that is not retransmitted carries over from the last frame on an inter picture and
  /// takes the running default on a key frame. The running default is not a constant: it is whatever
  /// the last transmitted probability at any node was, which is why the two branches read the same
  /// local and why the loop order is part of the format.
  /// </remarks>
  private void _ParseCoefficientModels() {
    Span<byte> running = stackalloc byte[11];
    running.Fill(0x80);

    for (var planeType = 0; planeType < 2; ++planeType)
    for (var node = 0; node < 11; ++node)
      if (this._entropy.ReadBool(Vp5Tables.DcCoefficientUpdate[planeType, node]) != 0) {
        running[node] = this._entropy.ReadProbability();
        this._model.DcValue[planeType, node] = running[node];
      } else if (this._isKeyFrame)
        this._model.DcValue[planeType, node] = running[node];

    for (var codeType = 0; codeType < 3; ++codeType)
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var group = 0; group < 6; ++group)
    for (var node = 0; node < 11; ++node)
      if (this._entropy.ReadBool(Vp5Tables.RunAcCoefficientUpdate[codeType, planeType, group, node]) != 0) {
        running[node] = this._entropy.ReadProbability();
        this._model.RunAcValue[planeType, codeType, group, node] = running[node];
      } else if (this._isKeyFrame)
        this._model.RunAcValue[planeType, codeType, group, node] = running[node];

    for (var planeType = 0; planeType < 2; ++planeType)
    for (var context = 0; context < 36; ++context)
    for (var node = 0; node < 5; ++node) {
      var scaled = (this._model.DcValue[planeType, node] * Vp5Tables.DcContextLinear[node, context, 0] + 128) >> 8;
      this._model.DcCodingType[planeType, context, node] =
        (byte)Math.Clamp(scaled + Vp5Tables.DcContextLinear[node, context, 1], 1, 254);
    }

    for (var codeType = 0; codeType < 3; ++codeType)
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var group = 0; group < 3; ++group)
    for (var context = 0; context < 6; ++context)
    for (var node = 0; node < 5; ++node) {
      var scaled = (this._model.RunAcValue[planeType, codeType, group, node]
        * Vp5Tables.AcContextLinear[codeType, group, node, context, 0] + 128) >> 8;
      this._model.AcCodingType[planeType, codeType, group, context, node] =
        (byte)Math.Clamp(scaled + Vp5Tables.AcContextLinear[codeType, group, node, context, 1], 1, 254);
    }
  }

  // ==============================================================================================
  // Macroblock layer
  // ==============================================================================================

  private void _DecodeMacroblock(int row, int column) {
    if (this._interlaced) {
      // The flag is coded against what the macroblock to the left did, and at the start of a row
      // against the frame's own probability, so a run of one kind costs less than an alternation.
      var probability = this._interlaceProbability;
      if (column > 0)
        probability = this._interlacedBlock
          ? probability - (probability >> 1)
          : probability + ((256 - probability) >> 1);

      this._interlacedBlock = this._entropy.ReadBool(probability) != 0;
    }

    var type = this._isKeyFrame ? Vp5MacroblockType.Intra : this._DecodeMotion(row, column);

    for (var block = 0; block < 6; ++block)
      Array.Clear(this._blockCoefficients[block]);

    if (this._entropy.IsExhausted)
      throw new InvalidDataException(
        $"This VP5 frame's coefficient data ran out at macroblock {column},{row} of {this.MacroblockWidth}x{this.MacroblockHeight}.");

    this._ParseCoefficients();
    this._RenderMacroblock(row, column, type);
  }

  /// <summary>
  /// Collects the two nearest distinct non-zero vectors among the twelve neighbours that predict from
  /// the same reference, and reports the context their count selects.
  /// </summary>
  /// <remarks>
  /// The context numbering is not the count. Two predictors select context 0, none selects 1 and one
  /// selects 2, which is the order the format puts them in and not an ordering by how much is known.
  /// </remarks>
  private int _FindMotionCandidates(int row, int column, Vp5Reference reference) {
    var count = 0;
    var first = Vp5MotionVector.Zero;
    var second = Vp5MotionVector.Zero;

    foreach (var (dx, dy) in Vp5Tables.CandidatePredictorPositions) {
      var x = column + dx;
      var y = row + dy;
      if ((uint)x >= (uint)this.MacroblockWidth || (uint)y >= (uint)this.MacroblockHeight)
        continue;

      var neighbour = this._macroblocks[y * this.MacroblockWidth + x];
      if (Vp5Tables.ReferenceOfMacroblockType[(int)neighbour.Type] != reference)
        continue;

      var vector = neighbour.MotionVector;
      if (vector == Vp5MotionVector.Zero || vector == first)
        continue;

      if (count == 0) {
        first = vector;
        count = 1;
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

  private Vp5MacroblockType _DecodeMotion(int row, int column) {
    var context = this._FindMotionCandidates(row, column, Vp5Reference.Previous);
    var type = this._ParseMacroblockType(context);
    this._previousType = type;

    if (type == Vp5MacroblockType.InterFourVectors) {
      this._DecodeFourVectors();
      this._macroblocks[row * this.MacroblockWidth + column] = new() { Type = type, MotionVector = this._motion[3] };
      return type;
    }

    var vector = type switch {
      Vp5MacroblockType.InterVector1Previous => this._candidates[0],
      Vp5MacroblockType.InterVector2Previous => this._candidates[1],
      Vp5MacroblockType.InterVector1Golden => this._GoldenCandidate(row, column, 0),
      Vp5MacroblockType.InterVector2Golden => this._GoldenCandidate(row, column, 1),
      Vp5MacroblockType.InterDeltaPrevious => this._ParseVectorAdjustment(),
      Vp5MacroblockType.InterDeltaGolden => this._GoldenAdjustment(row, column),
      _ => Vp5MotionVector.Zero,
    };

    for (var block = 0; block < 6; ++block)
      this._motion[block] = vector;

    this._macroblocks[row * this.MacroblockWidth + column] = new() { Type = type, MotionVector = vector };
    return type;
  }

  private Vp5MotionVector _GoldenCandidate(int row, int column, int which) {
    this._FindMotionCandidates(row, column, Vp5Reference.Golden);
    return this._candidates[which];
  }

  /// <summary>
  /// A delta against the golden reference, whose candidate search runs first and is then discarded.
  /// </summary>
  /// <remarks>
  /// The search is not pointless even though the delta does not use its result: it is a delta against
  /// nothing at all, and what the search changes is the decoder's own candidate state, which the next
  /// macroblock has no use for either. Keeping it matters only because it is what the reference does,
  /// and diverging from the reference here would be a silent difference.
  /// </remarks>
  private Vp5MotionVector _GoldenAdjustment(int row, int column) {
    this._FindMotionCandidates(row, column, Vp5Reference.Golden);
    return this._ParseVectorAdjustment();
  }

  private Vp5MacroblockType _ParseMacroblockType(int context) {
    for (var node = 0; node < 10; ++node)
      this._typeProbabilities[node] = this._model.TypeProbabilities[context, (int)this._previousType, node];

    if (this._entropy.ReadBool(this._typeProbabilities[0]) != 0)
      return this._previousType;

    return (Vp5MacroblockType)this._entropy.ReadTree(Vp5Tables.MacroblockTypeTree, this._typeProbabilities);
  }

  private void _DecodeFourVectors() {
    Span<int> types = stackalloc int[4];
    for (var block = 0; block < 4; ++block) {
      var coded = this._entropy.ReadLiteral(2);

      // Two bits name four of the ten modes, and the mapping is not the identity: 0 stays 0 and the
      // other three step over mode 1, which is intra and cannot be one block of a predicted
      // macroblock.
      types[block] = coded == 0 ? 0 : coded + 1;
    }

    var sumX = 0;
    var sumY = 0;
    for (var block = 0; block < 4; ++block) {
      this._motion[block] = (Vp5MacroblockType)types[block] switch {
        Vp5MacroblockType.InterNoVectorPrevious => Vp5MotionVector.Zero,
        Vp5MacroblockType.InterDeltaPrevious => this._ParseVectorAdjustment(),
        Vp5MacroblockType.InterVector1Previous => this._candidates[0],
        _ => this._candidates[1],
      };

      sumX += this._motion[block].X;
      sumY += this._motion[block].Y;
    }

    // The chrominance vector is the average of the four luminance ones, rounded away from zero.
    var chroma = new Vp5MotionVector((short)_RoundedShift(sumX, 2), (short)_RoundedShift(sumY, 2));
    this._motion[4] = chroma;
    this._motion[5] = chroma;
  }

  private static int _RoundedShift(int value, int bits) {
    var half = 1 << (bits - 1);
    return value > 0 ? (value + half) >> bits : (value + half - 1) >> bits;
  }

  private Vp5MotionVector _ParseVectorAdjustment() {
    short x = 0;
    short y = 0;

    for (var component = 0; component < 2; ++component) {
      var delta = 0;
      if (this._entropy.ReadBool(this._model.VectorDeltaCoded[component]) != 0) {
        var negative = this._entropy.ReadBool(this._model.VectorSign[component]);
        var low = this._entropy.ReadBool(this._model.VectorLowBits[component, 0]);
        low |= this._entropy.ReadBool(this._model.VectorLowBits[component, 1]) << 1;

        for (var node = 0; node < 7; ++node)
          this._vectorProbabilities[node] = this._model.VectorHighBits[component, node];

        delta = low | (this._entropy.ReadTree(Vp5Tables.VectorAdjustmentTree, this._vectorProbabilities) << 2);
        delta = (delta ^ -negative) + negative;
      }

      if (component == 0)
        x = (short)delta;
      else
        y = (short)delta;
    }

    return new(x, y);
  }

  // ==============================================================================================
  // Coefficients
  // ==============================================================================================

  /// <summary>Reads the six blocks of one macroblock's transform coefficients.</summary>
  /// <remarks>
  /// Each block is a walk along the scan order in which every position is one of three things: a
  /// coefficient, a zero, or the end of the block. Which probabilities decide that depends on the
  /// code type — whether the position before it held a coefficient, a zero, or nothing yet — on the
  /// coefficient group the scan position falls in, and, for the first three groups, on what the
  /// position held in the block above and to the left.
  /// <para/>
  /// The end-of-block decision is only offered when the previous position was not a zero, which is
  /// what <c>codeType != 0</c> guards: after a zero, a second zero or a coefficient are the only
  /// possibilities and the block cannot end.
  /// </remarks>
  private void _ParseCoefficients() {
    var planeType = 0;

    for (var block = 0; block < 6; ++block) {
      if (block > 3)
        planeType = 1;

      var predictor = Vp5Tables.BlockToContext[block];
      var aboveIndex = this._aboveIndex[block];
      var context = 6 * this._coefficientContexts[predictor, 0] + this._above[aboveIndex].NotNullDc;

      for (var node = 0; node < 11; ++node)
        this._coefficientModel[node] = this._model.DcValue[planeType, node];
      for (var node = 0; node < 5; ++node)
        this._contextModel[node] = this._model.DcCodingType[planeType, context, node];

      var coefficients = this._blockCoefficients[block];
      var scanPosition = 0;
      var codeType = 1;

      for (;;) {
        if (this._entropy.ReadBool(this._contextModel[0]) != 0) {
          int magnitude;
          int sign;

          if (this._entropy.ReadBool(this._contextModel[2]) != 0) {
            if (this._entropy.ReadBool(this._contextModel[3]) != 0) {
              this._coefficientContexts[predictor, scanPosition] = 4;
              var token = this._entropy.ReadTree(Vp5Tables.CoefficientTree, this._coefficientModel);
              sign = this._entropy.ReadFlag();
              magnitude = Vp5Tables.CoefficientBias[token + 5];
              // int and not var: the table is bytes, and a byte counting down from zero wraps to 255.
              for (int bit = Vp5Tables.CoefficientBitLength[token]; bit >= 0; --bit)
                magnitude += this._entropy.ReadBool(Vp5Tables.CoefficientParseTable[token, bit]) << bit;
            } else {
              if (this._entropy.ReadBool(this._contextModel[4]) != 0) {
                magnitude = 3 + this._entropy.ReadBool(this._coefficientModel[5]);
                this._coefficientContexts[predictor, scanPosition] = 3;
              } else {
                magnitude = 2;
                this._coefficientContexts[predictor, scanPosition] = 2;
              }

              sign = this._entropy.ReadFlag();
            }

            codeType = 2;
          } else {
            codeType = 1;
            this._coefficientContexts[predictor, scanPosition] = 1;
            sign = this._entropy.ReadFlag();
            magnitude = 1;
          }

          var value = (magnitude ^ -sign) + sign;
          if (scanPosition != 0)
            value *= this._dequantAc;

          coefficients[Vp5ScanOrder.Positions[scanPosition]] = unchecked((short)value);
        } else {
          if (codeType != 0 && this._entropy.ReadBool(this._contextModel[1]) == 0)
            break;

          codeType = 0;
          this._coefficientContexts[predictor, scanPosition] = 0;
        }

        if (++scanPosition >= 64)
          break;

        var group = Vp5Tables.CoefficientGroups[scanPosition];
        context = this._coefficientContexts[predictor, scanPosition];

        for (var node = 0; node < 11; ++node)
          this._coefficientModel[node] = this._model.RunAcValue[planeType, codeType, group, node];

        if (group > 2)
          this._coefficientModel.AsSpan(0, 5).CopyTo(this._contextModel);
        else
          for (var node = 0; node < 5; ++node)
            this._contextModel[node] = this._model.AcCodingType[planeType, codeType, group, context, node];
      }

      // Everything the block did not reach this time but did last time is marked as having ended,
      // which is a fifth context distinct from both "zero" and "never coded".
      var previousLast = Math.Min(this._lastCoefficient[predictor], (byte)24);
      this._lastCoefficient[predictor] = (byte)scanPosition;
      if (scanPosition < previousLast)
        for (var position = scanPosition; position <= previousLast; ++position)
          this._coefficientContexts[predictor, position] = 5;

      this._above[aboveIndex].NotNullDc = this._coefficientContexts[predictor, 0];
    }
  }

  // ==============================================================================================
  // Reconstruction
  // ==============================================================================================

  private void _RenderMacroblock(int row, int column, Vp5MacroblockType type) {
    var referenceKind = Vp5Tables.ReferenceOfMacroblockType[(int)type];
    this._AddDirectCurrentPredictors(referenceKind);

    var reference = referenceKind switch {
      Vp5Reference.Previous => this._previous,
      Vp5Reference.Golden => this._golden,
      _ => null,
    };

    // In an interlaced macroblock the two luminance block rows are the two fields rather than the
    // top and bottom halves of the macroblock: blocks 0 and 1 hold its even lines and blocks 2 and 3
    // its odd ones, each stepping two lines at a time. The chrominance blocks are not split.
    var fieldCoded = this._interlaced && this._interlacedBlock;

    for (var block = 0; block < 6; ++block) {
      var plane = Vp5Tables.BlockToPlane[block];
      var luma = plane == 0;
      var scale = luma ? 16 : 8;
      var secondBlockRow = luma && block is 2 or 3;
      var x = column * scale + (luma && block is 1 or 3 ? 8 : 0);
      var y = row * scale + (secondBlockRow ? fieldCoded ? 1 : 8 : 0);
      var rowStep = luma && fieldCoded ? 2 : 1;
      var coefficients = this._blockCoefficients[block].AsSpan();
      var target = this._prediction.AsSpan();

      switch (type) {
        case Vp5MacroblockType.Intra:
          Vp5InverseDct.Put(coefficients, target);
          break;
        case Vp5MacroblockType.InterNoVectorPrevious:
        case Vp5MacroblockType.InterNoVectorGolden:
          _CopyBlock(reference!, plane, x, y, rowStep, target);
          Vp5InverseDct.Add(coefficients, target);
          break;
        default:
          this._PredictBlock(reference!, block, plane, x, y, fieldCoded, target);
          Vp5InverseDct.Add(coefficients, target);
          break;
      }

      _StoreBlock(this._current!, plane, x, y, rowStep, target);
    }
  }

  /// <summary>
  /// Turns each block's coded direct-current term into an absolute one and dequantises it.
  /// </summary>
  /// <remarks>
  /// The predictor is the average of whichever neighbours predict from the same reference — the one
  /// to the left, the one above, and, in VP5 and not in VP6, the two diagonally above — or, where
  /// none does, whatever the last block of that plane and reference came out at. Two neighbours are
  /// averaged and a third is never taken, which is why the search stops at two.
  /// </remarks>
  private void _AddDirectCurrentPredictors(Vp5Reference reference) {
    for (var block = 0; block < 6; ++block) {
      var aboveIndex = this._aboveIndex[block];
      ref var left = ref this._left[Vp5Tables.BlockToContext[block]];
      var count = 0;
      var dc = 0;

      if (left.Reference == reference) {
        dc += left.Dc;
        ++count;
      }

      if (this._above[aboveIndex].Reference == reference) {
        dc += this._above[aboveIndex].Dc;
        ++count;
      }

      for (var side = -1; side <= 1 && count < 2; side += 2) {
        if (this._above[aboveIndex + side].Reference != reference)
          continue;

        dc += this._above[aboveIndex + side].Dc;
        ++count;
      }

      var plane = Vp5Tables.BlockToPlane[block];
      if (count == 0)
        dc = this._previousDc[plane, (int)reference];
      else if (count == 2)
        dc /= 2;

      var coefficients = this._blockCoefficients[block];
      var absolute = unchecked((short)(coefficients[0] + dc));
      this._previousDc[plane, (int)reference] = absolute;
      this._above[aboveIndex].Dc = absolute;
      this._above[aboveIndex].Reference = reference;
      left.Dc = absolute;
      left.Reference = reference;
      coefficients[0] = unchecked((short)(absolute * this._dequantDc));
    }
  }

  /// <summary>
  /// Builds one 8x8 prediction from a reference picture through a motion vector.
  /// </summary>
  /// <remarks>
  /// VP5 has half-sample luminance vectors and quarter-sample chrominance ones, and it interpolates
  /// neither with a filter: a fractional vector averages the two samples it lies between, without
  /// rounding up. What sharpens the result instead is an edge filter run over the reference window
  /// first, at the sample column and row the vector's fractional part selects, with a strength the
  /// frame's quantiser sets. The filter runs over a copy and never over the reference itself.
  /// <para/>
  /// The window is twelve by twelve because the filter reaches two samples back and one forward from
  /// its edge, and the edge can sit anywhere in the block. Outside the picture the nearest sample is
  /// repeated.
  /// </remarks>
  private void _PredictBlock(
    Vp5Frame reference, int block, int plane, int originX, int originY, bool fieldCoded, Span<byte> destination) {
    var motion = this._motion[block];
    var divisor = Vp5Tables.CoordinateDivisor[block];
    var wholeX = motion.X / divisor;
    var wholeY = motion.Y / divisor;
    var luma = plane == 0;

    // Where the window starts and how far apart its rows are. A progressive block reads twelve
    // consecutive lines starting two above itself. A field-coded one reads out of a twenty-four line
    // span starting four above: the luminance blocks take every other line of it, so that the window
    // stays inside one field, and the chrominance blocks — which are not field-coded — take
    // consecutive lines and therefore sit four rows into the span rather than two.
    var rowBase = originY + wholeY - (fieldCoded ? 4 : 2);
    var rowStep = fieldCoded && luma ? 2 : 1;
    var blockRow = fieldCoded && !luma ? 4 : 2;

    var samples = reference.Plane(plane);
    var width = reference.PlaneWidth(plane);
    var height = reference.PlaneHeight(plane);
    var window = this._window.AsSpan();

    for (var row = 0; row < 13; ++row) {
      var sourceY = Math.Clamp(rowBase + row * rowStep, 0, height - 1) * width;
      for (var col = 0; col < 12; ++col)
        window[row * 12 + col] = samples[sourceY + Math.Clamp(originX + wholeX + col - 2, 0, width - 1)];
    }

    var threshold = Vp5Tables.FilterThreshold[this._quantizer];
    var edgeX = wholeX & 7;
    var edgeY = wholeY & 7;
    if (edgeX != 0)
      _FilterAcrossColumn(window, 10 - edgeX, threshold);
    if (edgeY != 0)
      _FilterAcrossRow(window, 10 - edgeY, threshold);

    var mask = divisor - 1;
    var offsetX = (motion.X & mask) == 0 ? 0 : motion.X > 0 ? 1 : -1;
    var offsetY = (motion.Y & mask) == 0 ? 0 : motion.Y > 0 ? 1 : -1;

    if (offsetX == 0 && offsetY == 0) {
      for (var row = 0; row < 8; ++row)
        window.Slice((row + blockRow) * 12 + 2, 8).CopyTo(destination[(row * 8)..]);
      return;
    }

    for (var row = 0; row < 8; ++row)
    for (var col = 0; col < 8; ++col) {
      var at = (row + blockRow) * 12 + col + 2;
      destination[row * 8 + col] = (byte)((window[at] + window[at + offsetY * 12 + offsetX]) >> 1);
    }
  }

  /// <summary>The edge filter run down one column of the twelve-by-twelve window.</summary>
  private static void _FilterAcrossColumn(Span<byte> window, int x, int threshold) {
    for (var row = 0; row < 12; ++row) {
      var at = row * 12 + x;
      var adjustment = _EdgeAdjustment(window[at - 2], window[at - 1], window[at], window[at + 1], threshold);
      window[at - 1] = _Clamp(window[at - 1] + adjustment);
      window[at] = _Clamp(window[at] - adjustment);
    }
  }

  /// <summary>The edge filter run along one row of the twelve-by-twelve window.</summary>
  private static void _FilterAcrossRow(Span<byte> window, int y, int threshold) {
    for (var column = 0; column < 12; ++column) {
      var at = y * 12 + column;
      var adjustment = _EdgeAdjustment(window[at - 24], window[at - 12], window[at], window[at + 12], threshold);
      window[at - 12] = _Clamp(window[at - 12] + adjustment);
      window[at] = _Clamp(window[at] - adjustment);
    }
  }

  /// <summary>
  /// How far apart to push the two samples either side of an edge, bounded by the threshold.
  /// </summary>
  /// <remarks>
  /// A difference smaller than the threshold is taken as detail and passed through; one larger than
  /// twice it is taken as a real edge and left alone; between the two the adjustment tapers back to
  /// nothing. Written branchlessly through the sign bit, as the reference writes it, because the
  /// arithmetic-shift-and-exclusive-or form is the definition here rather than an optimisation of a
  /// simpler one.
  /// </remarks>
  private static int _EdgeAdjustment(int before, int left, int right, int after, int threshold) {
    var value = (before + 3 * (right - left) - after + 4) >> 3;
    var sign = value >> 31;
    value = (value ^ sign) - sign;
    value *= value < 2 * threshold ? 1 : 0;
    value -= threshold;
    var innerSign = value >> 31;
    value = (value ^ innerSign) - innerSign;
    value = threshold - value;
    return (value + sign) ^ sign;
  }

  private static void _CopyBlock(Vp5Frame source, int plane, int x, int y, int rowStep, Span<byte> destination) {
    var samples = source.Plane(plane);
    var width = source.PlaneWidth(plane);
    for (var row = 0; row < 8; ++row)
      samples.AsSpan((y + row * rowStep) * width + x, 8).CopyTo(destination[(row * 8)..]);
  }

  private static void _StoreBlock(Vp5Frame target, int plane, int x, int y, int rowStep, ReadOnlySpan<byte> source) {
    var samples = target.Plane(plane);
    var width = target.PlaneWidth(plane);
    for (var row = 0; row < 8; ++row)
      source.Slice(row * 8, 8).CopyTo(samples.AsSpan((y + row * rowStep) * width + x, 8));
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  // ==============================================================================================
  // Per-frame and per-row context
  // ==============================================================================================

  private void _ResetDirectCurrentPrediction() {
    Array.Clear(this._previousDc);

    // Chrominance starts from mid-grey and luminance from black, which is the format's own asymmetry
    // and not a choice: the two chrominance planes are signed around 128 and the luminance one is not.
    this._previousDc[1, (int)Vp5Reference.Current] = 128;
    this._previousDc[2, (int)Vp5Reference.Current] = 128;

    for (var i = 0; i < this._above.Length; ++i)
      this._above[i] = new() { Reference = Vp5Reference.None };

    // The two entries the chrominance block indices start one past are marked as belonging to the
    // current picture so that the first chrominance block of a row finds no usable neighbour above
    // rather than the last luminance block of the row before.
    this._above[2 * this.MacroblockWidth + 2].Reference = Vp5Reference.Current;
    this._above[3 * this.MacroblockWidth + 4].Reference = Vp5Reference.Current;
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
    this._aboveIndex[0] += 2;
    this._aboveIndex[1] += 2;
    this._aboveIndex[2] += 2;
    this._aboveIndex[3] += 2;
    this._aboveIndex[4] += 1;
    this._aboveIndex[5] += 1;
  }
}

/// <summary>The entropy model state VP5 carries from one frame to the next.</summary>
internal sealed class Vp5Model {
  internal readonly byte[] VectorSign = new byte[2];
  internal readonly byte[] VectorDeltaCoded = new byte[2];
  internal readonly byte[,] VectorLowBits = new byte[2, 2];
  internal readonly byte[,] VectorHighBits = new byte[2, 7];
  internal readonly byte[,] DcValue = new byte[2, 11];
  internal readonly byte[,,,] RunAcValue = new byte[2, 3, 6, 11];
  internal readonly byte[,,] DcCodingType = new byte[2, 36, 5];
  internal readonly byte[,,,,] AcCodingType = new byte[2, 3, 3, 6, 5];
  internal readonly byte[,,] TypeStats = new byte[3, 10, 2];
  internal readonly byte[,,] TypeProbabilities = new byte[3, 10, 10];
}
