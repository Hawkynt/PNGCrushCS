using System;
using System.IO;
using FileFormat.Codecs.Vp5;

namespace FileFormat.Codecs.Vp6;

/// <summary>
/// One On2 VP6 bitstream: the frame header, the entropy models, both coefficient codings, the
/// macroblock layer and the reconstruction. A VP6A film runs two of these, one per bitstream.
/// </summary>
/// <remarks>
/// <b>Where it came from.</b> Converted from FFmpeg's <c>libavcodec/vp6.c</c>, <c>vp56.c</c>,
/// <c>vp6dsp.c</c>, <c>vp3dsp.c</c> (loop filter) and <c>h264chroma_template.c</c> (bilinear filter),
/// all LGPL-2.1-or-later; see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this file. On2 published a
/// VP6 bitstream guide, but its tables are those FFmpeg carries and its decoder is the one every VP6
/// file in circulation has been played by, so FFmpeg's behaviour is the reference matched here.
/// <para/>
/// <b>Coded space.</b> The AVI form of VP6 codes its picture bottom-up, and FFmpeg decodes it through
/// negative strides. Every decision the codec makes — prediction direction, which side a fractional
/// vector rounds to, which neighbours are above — is the same in the coded orientation, so this
/// decoder works entirely in coded rows and the caller turns the finished picture over.
/// <para/>
/// <b>Prediction.</b> Each 8x8 prediction is built from a twelve-by-twelve window of the reference
/// around the block — twelve by twenty-four, every other line, for a field-coded macroblock — in which
/// the loop filter may run first and from which the quarter-sample (luminance) or eighth-sample
/// (chrominance) filter then reads. Outside the picture the nearest sample repeats, which is what
/// FFmpeg's edge emulation produces.
/// </remarks>
internal sealed class Vp6Decoder {

  private const int _MAX_HUFFMAN_SIZE = 12;

  private readonly Vp6Model _model = new();
  private readonly short[][] _blocks = [new short[64], new short[64], new short[64], new short[64], new short[64], new short[64]];
  private readonly int[] _idctSelector = new int[6];
  private readonly Vp5MotionVector[] _motion = new Vp5MotionVector[6];
  private readonly Vp5MotionVector[] _candidates = new Vp5MotionVector[2];
  private readonly Vp5DcPredictor[] _left = new Vp5DcPredictor[4];
  private readonly short[,] _previousDc = new short[3, 3];
  private readonly int[] _aboveIndex = new int[6];
  private readonly int[] _boundingValues = new int[256];
  private readonly byte[] _window = new byte[12 * 26];
  private readonly byte[] _prediction = new byte[64];
  private readonly byte[] _typeProbabilities = new byte[10];
  private readonly Vp6Huffman?[] _dcHuffman = new Vp6Huffman?[2];
  private readonly Vp6Huffman?[] _runHuffman = new Vp6Huffman?[2];
  private readonly Vp6Huffman?[,,] _acHuffman = new Vp6Huffman?[2, 3, 4];
  private readonly int[,] _nullRuns = new int[2, 2];

  private Vp5RangeDecoder _entropy;
  private Vp5RangeDecoder _coefficients;
  private bool _separateCoefficients;
  private Vp6BitReader _bits;
  private bool _useHuffman;
  private bool _huffmanCoefficients;

  private Vp5Macroblock[] _macroblocks = [];
  private Vp5DcPredictor[] _above = [];
  private Vp5Frame? _current;
  private Vp5Frame? _previous;
  private Vp5Frame? _golden;
  private bool _hasPicture;

  private bool _isKeyFrame;
  private int _subVersion;
  private int _filterHeader;
  private bool _interlaced;
  private bool _interlacedBlock;
  private int _interlaceProbability;
  private bool _goldenFrame;
  private bool _deblockFiltering;
  private int _filterMode;
  private int _filterSelection;
  private int _maxVectorLength;
  private int _sampleVarianceThreshold;
  private int _quantizer = -1;
  private int _dequantDc;
  private int _dequantAc;
  private Vp5MacroblockType _previousType;
  private int _vectorCandidatePosition;
  private byte[] _defaultReorder = Vp6Tables.DefaultCoefficientReorder;

  internal Vp6Decoder(bool lumaOnly) => this.LumaOnly = lumaOnly;

  /// <summary>The picture the last call decoded, which the next inter frame predicts from.</summary>
  internal Vp5Frame? LastDecoded => this._hasPicture ? this._previous : null;

  /// <summary>
  /// The prediction this decoder would form for one block of a progressive macroblock through
  /// <paramref name="motion"/> from <paramref name="reference"/>, with the filter settings of the last
  /// frame it decoded. An encoder uses it to code its residual against exactly this.
  /// </summary>
  internal void Predict(Vp5Frame reference, int block, Vp5MotionVector motion, int x, int y, Span<byte> destination) {
    var plane = Vp5Tables.BlockToPlane[block];
    if (motion == Vp5MotionVector.Zero) {
      _CopyBlock(reference, plane, x, y, 1, destination);
      return;
    }
    this._PredictBlock(reference, block, motion, plane, x, y, fieldCoded: false, destination);
  }

  /// <summary>Whether only the luminance blocks are reconstructed, as VP6A's alpha bitstream wants.</summary>
  internal bool LumaOnly { get; }

  internal int MacroblockWidth { get; private set; }
  internal int MacroblockHeight { get; private set; }

  /// <summary>Whether the last header changed the coded size.</summary>
  internal bool SizeChanged { get; private set; }

  /// <summary>Decodes one bitstream and returns this decoder's reconstruction, in coded row order.</summary>
  /// <remarks>
  /// The frame is this decoder's own buffer; it stays intact until the call after next, which is
  /// when it is reused.
  /// </remarks>
  internal Vp5Frame Decode(ReadOnlyMemory<byte> data) {
    this._ParseHeader(data);

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

    this._ResetPrediction();

    for (var row = 0; row < this.MacroblockHeight; ++row) {
      for (var i = 0; i < 4; ++i)
        this._left[i] = new() { Reference = Vp5Reference.None };

      this._aboveIndex[0] = 1;
      this._aboveIndex[1] = 2;
      this._aboveIndex[2] = 1;
      this._aboveIndex[3] = 2;
      this._aboveIndex[4] = 2 * this.MacroblockWidth + 3;
      this._aboveIndex[5] = 3 * this.MacroblockWidth + 5;

      for (var column = 0; column < this.MacroblockWidth; ++column) {
        this._DecodeMacroblock(row, column);
        for (var y = 0; y < 4; ++y)
          this._aboveIndex[y] += 2;
        this._aboveIndex[4] += 1;
        this._aboveIndex[5] += 1;
      }
    }

    var decoded = this._current!;
    if (this._isKeyFrame || this._goldenFrame)
      this._golden!.CopyFrom(decoded);

    (this._previous, this._current) = (decoded, this._previous);
    this._hasPicture = true;
    return decoded;
  }

  // ==============================================================================================
  // Frame header
  // ==============================================================================================

  private void _ParseHeader(ReadOnlyMemory<byte> packet) {
    var data = packet.Span;
    if (data.Length < 1)
      throw new InvalidDataException("A VP6 frame is empty.");

    var separated = (data[0] & 1) != 0;
    this._isKeyFrame = (data[0] & 0x80) == 0;
    this._SetQuantizer((data[0] >> 1) & 0x3f);
    this.SizeChanged = false;

    var at = 0;
    var coefficientOffset = 0;
    var parseFilterInfo = false;
    var varianceShift = 0;

    if (this._isKeyFrame) {
      if (data.Length < 2)
        throw new InvalidDataException("A VP6 key frame ends inside its header.");

      var subVersion = data[1] >> 3;
      if (subVersion > 8)
        throw new InvalidDataException($"VP6 sub-version {subVersion} is outside the defined range of 0 to 8.");

      this._filterHeader = data[1] & 0x06;
      this._interlaced = (data[1] & 1) != 0;
      this._defaultReorder = this._interlaced ? Vp6Tables.InterlacedCoefficientReorder : Vp6Tables.DefaultCoefficientReorder;

      if (separated || this._filterHeader == 0) {
        if (data.Length < 4)
          throw new InvalidDataException("A VP6 key frame ends inside its partition offset.");
        coefficientOffset = ((data[2] << 8) | data[3]) - 2;
        at += 2;
      }

      if (data.Length < at + 6)
        throw new InvalidDataException("A VP6 key frame ends inside its size fields.");

      var rows = data[at + 2];
      var columns = data[at + 3];
      if (rows == 0 || columns == 0)
        throw new InvalidDataException($"A VP6 key frame states a coded size of {columns << 4}x{rows << 4}, which has no area.");

      this._EnsureGeometry(columns, rows);

      this._entropy = new(packet, at + 6);
      _ = this._entropy.ReadLiteral(2);
      parseFilterInfo = this._filterHeader != 0;
      if (subVersion < 8)
        varianceShift = 5;
      this._subVersion = subVersion;
      this._goldenFrame = false;
    } else {
      if (this._subVersion == 0 || !this._hasPicture)
        throw new InvalidDataException("This VP6 stream begins with an inter frame, so there is no reference picture to predict it from.");

      if (separated || this._filterHeader == 0) {
        if (data.Length < 3)
          throw new InvalidDataException("A VP6 inter frame ends inside its partition offset.");
        coefficientOffset = ((data[1] << 8) | data[2]) - 2;
        at += 2;
      }

      this._entropy = new(packet, at + 1);
      this._goldenFrame = this._entropy.ReadFlag() != 0;
      if (this._filterHeader != 0) {
        this._deblockFiltering = this._entropy.ReadFlag() != 0;
        if (this._deblockFiltering)
          _ = this._entropy.ReadFlag();
        if (this._subVersion > 7)
          parseFilterInfo = this._entropy.ReadFlag() != 0;
      }
    }

    if (parseFilterInfo) {
      if (this._entropy.ReadFlag() != 0) {
        this._filterMode = 2;
        this._sampleVarianceThreshold = this._entropy.ReadLiteral(5) << varianceShift;
        this._maxVectorLength = 2 << this._entropy.ReadLiteral(3);
      } else if (this._entropy.ReadFlag() != 0) {
        this._filterMode = 1;
      } else {
        this._filterMode = 0;
      }

      this._filterSelection = this._subVersion > 7 ? this._entropy.ReadLiteral(4) : 16;
    }

    this._useHuffman = this._entropy.ReadFlag() != 0;

    this._separateCoefficients = coefficientOffset != 0;
    if (this._separateCoefficients) {
      var start = at + coefficientOffset;
      if (start < 0 || start > data.Length)
        throw new InvalidDataException("A VP6 frame's coefficient partition starts outside the frame.");

      if (this._useHuffman)
        this._bits = new(packet[start..]);
      else
        this._coefficients = new(packet, start);
    }

    // FFmpeg builds the Huffman tables whenever the flag is set but reads Huffman coefficients only
    // from a separate partition; without one the range coder reads them, with whatever DC context
    // tables the last range-coded frame left. Both halves are kept as FFmpeg has them.
    this._huffmanCoefficients = this._useHuffman && this._separateCoefficients;
  }

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
    this._hasPicture = false;
    this.SizeChanged = true;
  }

  private void _SetQuantizer(int quantizer) {
    if (quantizer != this._quantizer)
      _SetBoundingValues(this._boundingValues, Vp5Tables.FilterThreshold[quantizer]);

    this._quantizer = quantizer;
    this._dequantDc = Vp5Tables.DcDequant[quantizer] << 2;
    this._dequantAc = Vp5Tables.AcDequant[quantizer] << 2;
  }

  /// <summary>FFmpeg's <c>ff_vp3dsp_set_bounding_values</c>: the loop filter's clamp curve, centred on 127.</summary>
  private static void _SetBoundingValues(int[] values, int limit) {
    Array.Clear(values);
    const int centre = 127;
    for (var x = 0; x < limit; ++x) {
      values[centre - x] = -x;
      values[centre + x] = x;
    }

    int value;
    int at;
    for (at = limit, value = limit; at < 128 && value != 0; ++at, --value) {
      values[centre + at] = value;
      values[centre - at] = -value;
    }

    if (value != 0)
      values[centre + 128] = value;
  }

  // ==============================================================================================
  // Entropy models
  // ==============================================================================================

  private void _InitializeDefaultModels() {
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

    this._defaultReorder.CopyTo(model.CoefficientReorder, 0);
    this._model.InitializeCoefficientOrder(this._subVersion);
  }

  private void _ParseMacroblockTypeModels() {
    var model = this._model;
    for (var context = 0; context < 3; ++context) {
      if (this._entropy.ReadBool(174) != 0) {
        var preset = this._entropy.ReadLiteral(4);
        for (var type = 0; type < 10; ++type)
        for (var statistic = 0; statistic < 2; ++statistic)
          model.TypeStats[context, type, statistic] = Vp5Tables.PredefinedMacroblockTypeStats[preset, context, type, statistic];
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

        model.TypeStats[context, type, statistic] =
          unchecked((byte)(model.TypeStats[context, type, statistic] + ((delta ^ -negative) + negative)));
      }
    }

    model.ComputeTypeProbabilities();
  }

  private void _ParseVectorModels() {
    var model = this._model;
    for (var component = 0; component < 2; ++component) {
      if (this._entropy.ReadBool(Vp6Tables.SignDctUpdate[component, 0]) != 0)
        model.VectorDct[component] = this._entropy.ReadProbability();
      if (this._entropy.ReadBool(Vp6Tables.SignDctUpdate[component, 1]) != 0)
        model.VectorSign[component] = this._entropy.ReadProbability();
    }

    for (var component = 0; component < 2; ++component)
    for (var node = 0; node < 7; ++node)
      if (this._entropy.ReadBool(Vp6Tables.PdvUpdate[component, node]) != 0)
        model.VectorPdv[component, node] = this._entropy.ReadProbability();

    for (var component = 0; component < 2; ++component)
    for (var node = 0; node < 8; ++node)
      if (this._entropy.ReadBool(Vp6Tables.FdvUpdate[component, node]) != 0)
        model.VectorFdv[component, node] = this._entropy.ReadProbability();
  }

  private void _ParseCoefficientModels() {
    var model = this._model;
    Span<byte> running = stackalloc byte[11];
    running.Fill(0x80);

    for (var planeType = 0; planeType < 2; ++planeType)
    for (var node = 0; node < 11; ++node)
      if (this._entropy.ReadBool(Vp6Tables.DcValueUpdate[planeType, node]) != 0) {
        running[node] = this._entropy.ReadProbability();
        model.DcValue[planeType, node] = running[node];
      } else if (this._isKeyFrame) {
        model.DcValue[planeType, node] = running[node];
      }

    if (this._entropy.ReadFlag() != 0) {
      for (var position = 1; position < 64; ++position)
        if (this._entropy.ReadBool(Vp6Tables.CoefficientReorderUpdate[position]) != 0)
          model.CoefficientReorder[position] = (byte)this._entropy.ReadLiteral(4);
      this._model.InitializeCoefficientOrder(this._subVersion);
    }

    for (var group = 0; group < 2; ++group)
    for (var node = 0; node < 14; ++node)
      if (this._entropy.ReadBool(Vp6Tables.RunValueUpdate[group, node]) != 0)
        model.RunValue[group, node] = this._entropy.ReadProbability();

    for (var codeType = 0; codeType < 3; ++codeType)
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var group = 0; group < 6; ++group)
    for (var node = 0; node < 11; ++node)
      if (this._entropy.ReadBool(Vp6Tables.RunAcUpdate[codeType, planeType, group, node]) != 0) {
        running[node] = this._entropy.ReadProbability();
        model.RunAc[planeType, codeType, group, node] = running[node];
      } else if (this._isKeyFrame) {
        model.RunAc[planeType, codeType, group, node] = running[node];
      }

    if (this._useHuffman) {
      Span<byte> probabilities = stackalloc byte[14];
      for (var planeType = 0; planeType < 2; ++planeType) {
        for (var node = 0; node < 11; ++node)
          probabilities[node] = model.DcValue[planeType, node];
        this._dcHuffman[planeType] = Vp6Huffman.Build(probabilities[..11], Vp6Tables.HuffmanCoefficientMap, 12);

        for (var node = 0; node < 14; ++node)
          probabilities[node] = model.RunValue[planeType, node];
        this._runHuffman[planeType] = Vp6Huffman.Build(probabilities, Vp6Tables.HuffmanRunMap, 9);

        for (var codeType = 0; codeType < 3; ++codeType)
        for (var group = 0; group < 4; ++group) {
          for (var node = 0; node < 11; ++node)
            probabilities[node] = model.RunAc[planeType, codeType, group, node];
          this._acHuffman[planeType, codeType, group] = Vp6Huffman.Build(probabilities[..11], Vp6Tables.HuffmanCoefficientMap, 12);
        }
      }

      Array.Clear(this._nullRuns);
    } else {
      model.ComputeDcCodingTypes();
    }
  }

  // ==============================================================================================
  // Macroblock layer
  // ==============================================================================================

  private void _DecodeMacroblock(int row, int column) {
    if (this._interlaced) {
      var probability = this._interlaceProbability;
      if (column > 0)
        probability = this._interlacedBlock
          ? probability - (probability >> 1)
          : probability + ((256 - probability) >> 1);

      this._interlacedBlock = this._entropy.ReadBool(probability) != 0;
    }

    var type = this._isKeyFrame ? Vp5MacroblockType.Intra : this._DecodeMotion(row, column);

    for (var block = 0; block < 6; ++block)
      Array.Clear(this._blocks[block]);

    if (this._huffmanCoefficients)
      this._ParseCoefficientsHuffman();
    else
      this._ParseCoefficients();

    this._RenderMacroblock(row, column, type);
  }

  private int _FindMotionCandidates(int row, int column, Vp5Reference reference) {
    var count = 0;
    var first = Vp5MotionVector.Zero;
    var second = Vp5MotionVector.Zero;
    var positions = Vp5Tables.CandidatePredictorPositions;

    for (var position = 0; position < positions.Length; ++position) {
      var x = column + positions[position].X;
      var y = row + positions[position].Y;
      if ((uint)x >= (uint)this.MacroblockWidth || (uint)y >= (uint)this.MacroblockHeight)
        continue;

      var neighbour = this._macroblocks[y * this.MacroblockWidth + x];
      if (Vp5Tables.ReferenceOfMacroblockType[(int)neighbour.Type] != reference)
        continue;

      var vector = neighbour.MotionVector;
      if (vector == first || vector == Vp5MotionVector.Zero)
        continue;

      if (count == 0) {
        first = vector;
        count = 1;
        // Only the first candidate's position is kept; with none found it stays what it was.
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

  private Vp5MacroblockType _DecodeMotion(int row, int column) {
    var context = this._FindMotionCandidates(row, column, Vp5Reference.Previous);

    for (var node = 0; node < 10; ++node)
      this._typeProbabilities[node] = this._model.TypeProbabilities[context, (int)this._previousType, node];

    var type = this._entropy.ReadBool(this._typeProbabilities[0]) != 0
      ? this._previousType
      : (Vp5MacroblockType)this._entropy.ReadTree(Vp5Tables.MacroblockTypeTree, this._typeProbabilities);
    this._previousType = type;
    var index = row * this.MacroblockWidth + column;
    this._macroblocks[index].Type = type;

    Vp5MotionVector vector;
    switch (type) {
      case Vp5MacroblockType.InterVector1Previous:
        vector = this._candidates[0];
        break;
      case Vp5MacroblockType.InterVector2Previous:
        vector = this._candidates[1];
        break;
      case Vp5MacroblockType.InterVector1Golden:
        this._FindMotionCandidates(row, column, Vp5Reference.Golden);
        vector = this._candidates[0];
        break;
      case Vp5MacroblockType.InterVector2Golden:
        this._FindMotionCandidates(row, column, Vp5Reference.Golden);
        vector = this._candidates[1];
        break;
      case Vp5MacroblockType.InterDeltaPrevious:
        vector = this._ParseVectorAdjustment();
        break;
      case Vp5MacroblockType.InterDeltaGolden:
        this._FindMotionCandidates(row, column, Vp5Reference.Golden);
        vector = this._ParseVectorAdjustment();
        break;
      case Vp5MacroblockType.InterFourVectors:
        this._DecodeFourVectors(index);
        return type;
      default:
        vector = Vp5MotionVector.Zero;
        break;
    }

    this._macroblocks[index].MotionVector = vector;
    for (var block = 0; block < 6; ++block)
      this._motion[block] = vector;
    return type;
  }

  private void _DecodeFourVectors(int index) {
    Span<int> types = stackalloc int[4];
    for (var block = 0; block < 4; ++block) {
      var coded = this._entropy.ReadLiteral(2);
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

    this._macroblocks[index].MotionVector = this._motion[3];
    var chroma = new Vp5MotionVector((short)_RoundedShift(sumX, 2), (short)_RoundedShift(sumY, 2));
    this._motion[4] = chroma;
    this._motion[5] = chroma;
  }

  private static int _RoundedShift(int value, int bits) {
    var half = 1 << (bits - 1);
    return value > 0 ? (value + half) >> bits : (value + half - 1) >> bits;
  }

  private Vp5MotionVector _ParseVectorAdjustment() {
    var model = this._model;
    var vector = this._vectorCandidatePosition < 2 ? this._candidates[0] : Vp5MotionVector.Zero;
    int x = vector.X, y = vector.Y;

    for (var component = 0; component < 2; ++component) {
      var delta = 0;
      if (this._entropy.ReadBool(model.VectorDct[component]) != 0) {
        ReadOnlySpan<int> order = [0, 1, 2, 7, 6, 5, 4];
        foreach (var bit in order)
          delta |= this._entropy.ReadBool(model.VectorFdv[component, bit]) << bit;
        if ((delta & 0xF0) != 0)
          delta |= this._entropy.ReadBool(model.VectorFdv[component, 3]) << 3;
        else
          delta |= 8;
      } else {
        Span<byte> probabilities = stackalloc byte[7];
        for (var node = 0; node < 7; ++node)
          probabilities[node] = model.VectorPdv[component, node];
        delta = this._entropy.ReadTree(Vp5Tables.VectorAdjustmentTree, probabilities);
      }

      if (delta != 0 && this._entropy.ReadBool(model.VectorSign[component]) != 0)
        delta = -delta;

      if (component == 0)
        x += delta;
      else
        y += delta;
    }

    return new((short)x, (short)y);
  }

  // ==============================================================================================
  // Coefficients
  // ==============================================================================================

  private void _ParseCoefficients() {
    ref var c = ref this._separateCoefficients ? ref this._coefficients : ref this._entropy;
    if (c.IsExhausted)
      throw new InvalidDataException("A VP6 frame's coefficient data ran out before its last macroblock.");

    var model = this._model;
    var planeType = 0;
    Span<byte> model1 = stackalloc byte[11];
    Span<byte> model2 = stackalloc byte[11];
    Span<byte> model3 = stackalloc byte[14];

    for (var block = 0; block < 6; ++block) {
      var codeType = 1;
      var run = 1;
      if (block > 3)
        planeType = 1;

      ref var left = ref this._left[Vp5Tables.BlockToContext[block]];
      ref var above = ref this._above[this._aboveIndex[block]];
      var context = left.NotNullDc + above.NotNullDc;

      for (var node = 0; node < 11; ++node)
        model1[node] = model.DcValue[planeType, node];
      for (var node = 0; node < 5; ++node)
        model2[node] = model.DcCodingType[planeType, context, node];

      var coefficients = this._blocks[block];
      var index = 0;
      for (;;) {
        if ((index > 1 && codeType == 0) || c.ReadBool(model2[0]) != 0) {
          int magnitude;
          if (c.ReadBool(model2[2]) != 0) {
            if (c.ReadBool(model2[3]) != 0) {
              var token = c.ReadTree(Vp5Tables.CoefficientTree, model1);
              magnitude = Vp5Tables.CoefficientBias[token + 5];
              for (int bit = Vp5Tables.CoefficientBitLength[token]; bit >= 0; --bit)
                magnitude += c.ReadBool(Vp5Tables.CoefficientParseTable[token, bit]) << bit;
            } else {
              magnitude = c.ReadBool(model2[4]) != 0 ? 3 + c.ReadBool(model1[5]) : 2;
            }
            codeType = 2;
          } else {
            codeType = 1;
            magnitude = 1;
          }

          var sign = c.ReadFlag();
          var value = (magnitude ^ -sign) + sign;
          if (index != 0)
            value *= this._dequantAc;
          coefficients[Vp5ScanOrder.Positions[model.IndexToPosition[index]]] = unchecked((short)value);
          run = 1;
        } else {
          codeType = 0;
          if (index > 0) {
            if (c.ReadBool(model2[1]) == 0)
              break;

            for (var node = 0; node < 14; ++node)
              model3[node] = model.RunValue[index >= 6 ? 1 : 0, node];
            run = c.ReadTree(Vp6Tables.RunTree, model3);
            if (run == 0) {
              run = 9;
              for (var bit = 0; bit < 6; ++bit)
                run += c.ReadBool(model3[bit + 8]) << bit;
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

      var notNull = (byte)(coefficients[0] != 0 ? 1 : 0);
      left.NotNullDc = notNull;
      above.NotNullDc = notNull;
      this._idctSelector[block] = model.IndexToIdctSelector[Math.Min(index, 63)];
    }
  }

  private void _ParseCoefficientsHuffman() {
    var model = this._model;
    var planeType = 0;

    for (var block = 0; block < 6; ++block) {
      var codeType = 0;
      if (block > 3)
        planeType = 1;

      var table = this._dcHuffman[planeType]!;
      var coefficients = this._blocks[block];
      var index = 0;

      for (;;) {
        var run = 1;
        if (index < 2 && this._nullRuns[index, planeType] != 0) {
          --this._nullRuns[index, planeType];
          if (index != 0)
            break;
        } else {
          if (this._bits.BitsLeft <= 0)
            throw new InvalidDataException("A VP6 frame's Huffman coefficient data ran out before its last macroblock.");

          var coefficient = table.Read(ref this._bits);
          if (coefficient == 0) {
            if (index != 0) {
              run += this._runHuffman[index >= 6 ? 1 : 0]!.Read(ref this._bits);
              if (run >= 9)
                run += this._bits.ReadBits(6);
            } else {
              this._nullRuns[0, planeType] = this._ReadNullRun();
            }
            codeType = 0;
          } else if (coefficient == 11) {
            if (index == 1)
              this._nullRuns[1, planeType] = this._ReadNullRun();
            break;
          } else {
            var magnitude = (int)Vp5Tables.CoefficientBias[coefficient];
            if (coefficient > 4)
              magnitude += this._bits.ReadBits(coefficient <= 9 ? coefficient - 4 : 11);
            codeType = 1 + (magnitude > 1 ? 1 : 0);
            var sign = this._bits.ReadBits(1);
            var value = (magnitude ^ -sign) + sign;
            if (index != 0)
              value *= this._dequantAc;
            coefficients[Vp5ScanOrder.Positions[model.IndexToPosition[index]]] = unchecked((short)value);
          }
        }

        index += run;
        if (index >= 64)
          break;

        var group = Math.Min((int)Vp6Tables.CoefficientGroups[index], 3);
        table = this._acHuffman[planeType, codeType, group]!;
      }

      this._idctSelector[block] = model.IndexToIdctSelector[Math.Min(index, 63)];
    }
  }

  /// <summary>FFmpeg's <c>vp6_get_nb_null</c>: how many blocks in a row have no DC or no AC.</summary>
  private int _ReadNullRun() {
    var value = this._bits.ReadBits(2);
    if (value == 2) {
      value += this._bits.ReadBits(2);
    } else if (value == 3) {
      value = this._bits.ReadBits(1) << 2;
      value = 6 + value + this._bits.ReadBits(2 + value);
    }
    return value;
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

    var fieldCoded = this._interlaced && this._interlacedBlock;
    var blocks = this.LumaOnly ? 4 : 6;

    for (var block = 0; block < blocks; ++block) {
      var plane = Vp5Tables.BlockToPlane[block];
      var luma = plane == 0;
      var scale = luma ? 16 : 8;
      var x = column * scale + (luma && block is 1 or 3 ? 8 : 0);
      var y = row * scale + (luma && block is 2 or 3 ? fieldCoded ? 1 : 8 : 0);
      var rowStep = luma && fieldCoded ? 2 : 1;
      var coefficients = this._blocks[block].AsSpan();
      var target = this._prediction.AsSpan();

      switch (type) {
        case Vp5MacroblockType.Intra:
          Vp6InverseDct.Put(coefficients, target, this._idctSelector[block]);
          break;
        case Vp5MacroblockType.InterNoVectorPrevious:
        case Vp5MacroblockType.InterNoVectorGolden:
          _CopyBlock(reference!, plane, x, y, rowStep, target);
          Vp6InverseDct.Add(coefficients, target, this._idctSelector[block]);
          break;
        default:
          this._PredictBlock(reference!, block, plane, x, y, fieldCoded, target);
          Vp6InverseDct.Add(coefficients, target, this._idctSelector[block]);
          break;
      }

      _StoreBlock(this._current!, plane, x, y, rowStep, target);
    }
  }

  /// <summary>DC prediction from the left and above blocks only — VP5's diagonal neighbours play no part.</summary>
  private void _AddDirectCurrentPredictors(Vp5Reference reference) {
    for (var block = 0; block < 6; ++block) {
      ref var above = ref this._above[this._aboveIndex[block]];
      ref var left = ref this._left[Vp5Tables.BlockToContext[block]];
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

      var plane = Vp5Tables.BlockToPlane[block];
      if (count == 0)
        dc = this._previousDc[plane, (int)reference];
      else if (count == 2)
        dc /= 2;

      var coefficients = this._blocks[block];
      var absolute = unchecked((short)(coefficients[0] + dc));
      this._previousDc[plane, (int)reference] = absolute;
      above.Dc = absolute;
      above.Reference = reference;
      left.Dc = absolute;
      left.Reference = reference;
      coefficients[0] = unchecked((short)(absolute * this._dequantDc));
    }
  }

  /// <summary>FFmpeg's <c>vp56_mc</c> with VP6's <c>vp6_filter</c>, over a window of the reference.</summary>
  private void _PredictBlock(
    Vp5Frame reference, int block, int plane, int originX, int originY, bool fieldCoded, Span<byte> destination)
    => this._PredictBlock(reference, block, this._motion[block], plane, originX, originY, fieldCoded, destination);

  private void _PredictBlock(
    Vp5Frame reference, int block, Vp5MotionVector motion, int plane, int originX, int originY, bool fieldCoded, Span<byte> destination) {
    var divisor = (int)Vp6Tables.CoordinateDivisor[block];
    var wholeX = motion.X / divisor;
    var wholeY = motion.Y / divisor;
    var luma = plane == 0;

    var rowBase = originY + wholeY - (fieldCoded ? 4 : 2);
    var rowStep = fieldCoded && luma ? 2 : 1;
    var blockRow = fieldCoded && !luma ? 4 : 2;
    var rows = fieldCoded && !luma ? 24 : 13;

    var samples = reference.Plane(plane);
    var width = reference.PlaneWidth(plane);
    var height = reference.PlaneHeight(plane);
    var window = this._window.AsSpan();

    for (var r = 0; r < rows; ++r) {
      var sourceY = Math.Clamp(rowBase + r * rowStep, 0, height - 1) * width;
      for (var col = 0; col < 12; ++col)
        window[r * 12 + col] = samples[sourceY + Math.Clamp(originX + wholeX + col - 2, 0, width - 1)];
    }

    if (this._deblockFiltering) {
      // The loop filter runs over the window before prediction reads it: across a column for a
      // horizontal whole-sample offset and along a row for a vertical one, twelve samples long.
      var edgeX = wholeX & 7;
      var edgeY = wholeY & 7;
      // Both are placed from the window's own origin, as FFmpeg places them from its edge buffer's,
      // which for a field-coded chrominance block is not where the block itself starts.
      if (edgeX != 0)
        this._FilterColumn(window, 10 - edgeX);
      if (edgeY != 0)
        this._FilterRow(window, 10 - edgeY);
    }

    var mask = divisor - 1;
    var overlapX = (motion.X & mask) == 0 ? 0 : motion.X > 0 ? 1 : -1;
    var overlapY = (motion.Y & mask) == 0 ? 0 : motion.Y > 0 ? 1 : -1;
    var origin = blockRow * 12 + 2;

    if (overlapX == 0 && overlapY == 0) {
      for (var r = 0; r < 8; ++r)
        window.Slice(origin + r * 12, 8).CopyTo(destination[(r * 8)..]);
      return;
    }

    this._Filter(destination, window, origin, overlapX, overlapY, motion, mask, luma);
  }

  /// <summary>FFmpeg's <c>vp6_filter</c>.</summary>
  private void _Filter(
    Span<byte> destination, ReadOnlySpan<byte> window, int origin, int overlapX, int overlapY,
    Vp5MotionVector motion, int mask, bool luma) {
    var x8 = motion.X & mask;
    var y8 = motion.Y & mask;
    var filter4 = 0;
    var offset1 = origin;
    var offset2 = origin + overlapX + overlapY * 12;

    if (luma) {
      x8 *= 2;
      y8 *= 2;
      filter4 = this._filterMode;
      if (filter4 == 2) {
        if (this._maxVectorLength != 0
            && (Math.Abs((int)motion.X) > this._maxVectorLength || Math.Abs((int)motion.Y) > this._maxVectorLength))
          filter4 = 0;
        else if (this._sampleVarianceThreshold != 0 && _BlockVariance(window, offset1) < this._sampleVarianceThreshold)
          filter4 = 0;
      }
    }

    // In coded rows: a vertical fraction pointing up, or a horizontal-only one pointing left, starts
    // the filter from the neighbouring sample. FFmpeg writes this through the sign of its stride.
    if ((y8 != 0 && overlapY < 0) || (y8 == 0 && offset1 > offset2))
      offset1 = offset2;

    var diagonalShift = ((motion.X ^ motion.Y) >> 31) != 0 ? -1 : 0;

    if (filter4 != 0) {
      if (y8 == 0)
        _FilterHv4(destination, window, offset1, 1, this._filterSelection, x8);
      else if (x8 == 0)
        _FilterHv4(destination, window, offset1, 12, this._filterSelection, y8);
      else
        _FilterDiagonal4(destination, window, offset1 + diagonalShift, this._filterSelection, x8, y8);
    } else if (x8 == 0 || y8 == 0) {
      _Bilinear(destination, 8, window, offset1, 12, 8, x8, y8);
    } else {
      Span<byte> temporary = stackalloc byte[8 * 9];
      _Bilinear(temporary, 8, window, offset1 + diagonalShift, 12, 9, x8, 0);
      _Bilinear(destination, 8, temporary, 0, 8, 8, 0, y8);
    }
  }

  private static int _BlockVariance(ReadOnlySpan<byte> window, int at) {
    int sum = 0, squares = 0;
    for (var y = 0; y < 8; y += 2)
    for (var x = 0; x < 8; x += 2) {
      int value = window[at + y * 12 + x];
      sum += value;
      squares += value * value;
    }
    return (16 * squares - sum * sum) >> 8;
  }

  private static void _FilterHv4(Span<byte> destination, ReadOnlySpan<byte> window, int at, int delta, int selection, int fraction) {
    var w0 = Vp6Tables.BlockCopyFilter[selection, fraction, 0];
    var w1 = Vp6Tables.BlockCopyFilter[selection, fraction, 1];
    var w2 = Vp6Tables.BlockCopyFilter[selection, fraction, 2];
    var w3 = Vp6Tables.BlockCopyFilter[selection, fraction, 3];
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var s = at + y * 12 + x;
      destination[y * 8 + x] = _Clip((window[s - delta] * w0 + window[s] * w1 + window[s + delta] * w2 + window[s + 2 * delta] * w3 + 64) >> 7);
    }
  }

  private static void _FilterDiagonal4(Span<byte> destination, ReadOnlySpan<byte> window, int at, int selection, int x8, int y8) {
    Span<int> temporary = stackalloc int[8 * 11];
    var h0 = Vp6Tables.BlockCopyFilter[selection, x8, 0];
    var h1 = Vp6Tables.BlockCopyFilter[selection, x8, 1];
    var h2 = Vp6Tables.BlockCopyFilter[selection, x8, 2];
    var h3 = Vp6Tables.BlockCopyFilter[selection, x8, 3];
    var start = at - 12;
    for (var y = 0; y < 11; ++y)
    for (var x = 0; x < 8; ++x) {
      var s = start + y * 12 + x;
      temporary[y * 8 + x] = _Clip((window[s - 1] * h0 + window[s] * h1 + window[s + 1] * h2 + window[s + 2] * h3 + 64) >> 7);
    }

    var v0 = Vp6Tables.BlockCopyFilter[selection, y8, 0];
    var v1 = Vp6Tables.BlockCopyFilter[selection, y8, 1];
    var v2 = Vp6Tables.BlockCopyFilter[selection, y8, 2];
    var v3 = Vp6Tables.BlockCopyFilter[selection, y8, 3];
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var t = (y + 1) * 8 + x;
      destination[y * 8 + x] = _Clip((temporary[t - 8] * v0 + temporary[t] * v1 + temporary[t + 8] * v2 + temporary[t + 16] * v3 + 64) >> 7);
    }
  }

  /// <summary>H.264's eighth-sample chroma filter, eight wide: FFmpeg's <c>put_h264_chroma_pixels_tab[0]</c>.</summary>
  private static void _Bilinear(
    Span<byte> destination, int destinationStride, ReadOnlySpan<byte> source, int at, int sourceStride, int height, int x, int y) {
    var a = (8 - x) * (8 - y);
    var b = x * (8 - y);
    var c = (8 - x) * y;
    var d = x * y;
    for (var row = 0; row < height; ++row)
    for (var col = 0; col < 8; ++col) {
      var s = at + row * sourceStride + col;
      var value = a * source[s];
      if (b != 0)
        value += b * source[s + 1];
      if (c != 0)
        value += c * source[s + sourceStride];
      if (d != 0)
        value += d * source[s + sourceStride + 1];
      destination[row * destinationStride + col] = (byte)((value + 32) >> 6);
    }
  }

  /// <summary>VP3's horizontal loop filter, twelve rows down the window's column <paramref name="x"/>.</summary>
  private void _FilterColumn(Span<byte> window, int x) {
    for (var row = 0; row < 12; ++row) {
      var at = row * 12 + x;
      var value = (window[at - 2] - window[at + 1]) + (window[at] - window[at - 1]) * 3;
      value = this._boundingValues[127 + ((value + 4) >> 3)];
      window[at - 1] = _Clip(window[at - 1] + value);
      window[at] = _Clip(window[at] - value);
    }
  }

  /// <summary>VP3's vertical loop filter, twelve samples along the window's row <paramref name="y"/>.</summary>
  private void _FilterRow(Span<byte> window, int y) {
    for (var x = 0; x < 12; ++x) {
      var at = y * 12 + x;
      var value = (window[at - 24] - window[at + 12]) + (window[at] - window[at - 12]) * 3;
      value = this._boundingValues[127 + ((value + 4) >> 3)];
      window[at - 12] = _Clip(window[at - 12] + value);
      window[at] = _Clip(window[at] - value);
    }
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

  private static byte _Clip(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  private void _ResetPrediction() {
    Array.Clear(this._previousDc);
    this._previousDc[1, (int)Vp5Reference.Current] = 128;
    this._previousDc[2, (int)Vp5Reference.Current] = 128;

    for (var i = 0; i < this._above.Length; ++i)
      this._above[i] = new() { Reference = Vp5Reference.None };

    this._above[2 * this.MacroblockWidth + 2].Reference = Vp5Reference.Current;
    this._above[3 * this.MacroblockWidth + 4].Reference = Vp5Reference.Current;
  }
}

/// <summary>The entropy model state VP6 carries from one frame to the next.</summary>
internal sealed class Vp6Model {
  internal readonly byte[] CoefficientReorder = new byte[64];
  internal readonly byte[] IndexToPosition = new byte[64];
  internal readonly byte[] IndexToIdctSelector = new byte[64];
  internal readonly byte[] VectorSign = new byte[2];
  internal readonly byte[] VectorDct = new byte[2];
  internal readonly byte[,] VectorPdv = new byte[2, 7];
  internal readonly byte[,] VectorFdv = new byte[2, 8];
  internal readonly byte[,] DcValue = new byte[2, 11];
  internal readonly byte[,,,] RunAc = new byte[2, 3, 6, 11];
  internal readonly byte[,,] DcCodingType = new byte[2, 3, 5];
  internal readonly byte[,] RunValue = new byte[2, 14];
  internal readonly byte[,,] TypeStats = new byte[3, 10, 2];
  internal readonly byte[,,] TypeProbabilities = new byte[3, 10, 10];

  /// <summary>
  /// FFmpeg's <c>vp6_coeff_order_table_init</c>: scan positions sorted by band, and the transform each
  /// coefficient count needs.
  /// </summary>
  internal void InitializeCoefficientOrder(int subVersion) {
    var index = 1;
    this.IndexToPosition[0] = 0;
    for (var band = 0; band < 16; ++band)
    for (var position = 1; position < 64; ++position)
      if (this.CoefficientReorder[position] == band)
        this.IndexToPosition[index++] = (byte)position;

    for (var i = 0; i < 64; ++i) {
      var max = 0;
      for (var j = 0; j <= i; ++j)
        max = Math.Max(max, this.IndexToPosition[j]);
      if (subVersion > 6)
        ++max;
      this.IndexToIdctSelector[i] = (byte)max;
    }
  }

  /// <summary>The DC coding-type probabilities, an affine function of the DC value ones per context.</summary>
  internal void ComputeDcCodingTypes() {
    for (var planeType = 0; planeType < 2; ++planeType)
    for (var context = 0; context < 3; ++context)
    for (var node = 0; node < 5; ++node) {
      var scaled = ((this.DcValue[planeType, node] * Vp6Tables.DcValueLinear[context, node, 0] + 128) >> 8)
        + Vp6Tables.DcValueLinear[context, node, 1];
      this.DcCodingType[planeType, context, node] = (byte)Math.Clamp(scaled, 1, 255);
    }
  }

  /// <summary>
  /// Turns the macroblock-type statistics into the probabilities the type tree uses, once per
  /// preceding type: the preceding type's own weight is set to nothing, which is what makes "the same
  /// type again" the separate decision taken first.
  /// </summary>
  internal void ComputeTypeProbabilities() {
    Span<int> weights = stackalloc int[10];
    for (var context = 0; context < 3; ++context) {
      for (var type = 0; type < 10; ++type)
        weights[type] = 100 * this.TypeStats[context, type, 1];

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
        var same = this.TypeStats[context, previous, 0];
        var other = this.TypeStats[context, previous, 1];

        this.TypeProbabilities[context, previous, 0] = (byte)(255 - 255 * same / (1 + same + other));
        this.TypeProbabilities[context, previous, 1] = (byte)(1 + 255 * p0234 / (1 + p0234 + p156789));
        this.TypeProbabilities[context, previous, 2] = (byte)(1 + 255 * p02 / (1 + p0234));
        this.TypeProbabilities[context, previous, 3] = (byte)(1 + 255 * p17 / (1 + p156789));
        this.TypeProbabilities[context, previous, 4] = (byte)(1 + 255 * weights[0] / (1 + p02));
        this.TypeProbabilities[context, previous, 5] = (byte)(1 + 255 * weights[3] / (1 + p34));
        this.TypeProbabilities[context, previous, 6] = (byte)(1 + 255 * weights[1] / (1 + p17));
        this.TypeProbabilities[context, previous, 7] = (byte)(1 + 255 * p56 / (1 + p5689));
        this.TypeProbabilities[context, previous, 8] = (byte)(1 + 255 * weights[5] / (1 + p56));
        this.TypeProbabilities[context, previous, 9] = (byte)(1 + 255 * weights[8] / (1 + p89));

        weights[previous] = saved;
      }
    }
  }
}
