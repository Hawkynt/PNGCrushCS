using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// Decodes one coded picture: its macroblocks and their blocks (ISO/IEC 14496-2, 6.2.6 and 7.4
/// through 7.6).
/// </summary>
/// <remarks>
/// One of these exists for the length of one picture, because everything it holds is reset by the
/// next one: the quantiser, the motion vectors every later macroblock's predictor is the median of,
/// the coefficients the intra prediction reaches back into, and the references. A decoder that kept
/// any of them across pictures would still produce a picture, and it would be wrong from its second
/// frame onward.
/// </remarks>
internal sealed class Mpeg4PictureDecoder {

  /// <summary>INTER: predicted, one vector for the whole macroblock (ISO/IEC 14496-2, Table 6-19).</summary>
  private const int _INTER = 0;

  /// <summary>INTER+Q: predicted, and carrying a change to the quantiser.</summary>
  private const int _INTER_WITH_QUANTISER = 1;

  /// <summary>INTER4V: predicted with one vector for each luminance block.</summary>
  private const int _INTER_FOUR_VECTORS = 2;

  /// <summary>INTRA: coded on its own.</summary>
  private const int _INTRA = 3;

  /// <summary>INTRA+Q: coded on its own, and carrying a change to the quantiser.</summary>
  private const int _INTRA_WITH_QUANTISER = 4;

  private readonly Mpeg4VideoObjectLayer _layer;
  private readonly Mpeg4VideoObjectPlane _plane;
  private readonly Mpeg4Frame _target;
  private readonly Mpeg4Frame? _forwardReference;
  private readonly Mpeg4Frame? _backwardReference;
  private readonly Mpeg4AnchorMotion? _anchorMotion;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly Mpeg4IntraPrediction _intraPrediction;

  /// <summary>How far apart in time the two pictures a bidirectional one is predicted from are.</summary>
  private readonly int _referenceDistance;

  /// <summary>How far this picture sits from the earlier of the two.</summary>
  private readonly int _forwardDistance;

  /// <summary>Every macroblock's four vectors, in the units the picture codes them in.</summary>
  private readonly short[] _vectorX;

  private readonly short[] _vectorY;

  /// <summary>Whether each macroblock has been decoded at all, which the predictor's edge rules need.</summary>
  private readonly bool[] _isDecoded;

  /// <summary>Where the video packet the current macroblock belongs to began.</summary>
  private int _packetStart;

  private int _quantiser;

  /// <summary>The forward and backward vector predictors a bidirectionally coded picture carries.</summary>
  private int _forwardPredictorX;

  private int _forwardPredictorY;
  private int _backwardPredictorX;
  private int _backwardPredictorY;

  /// <summary>
  /// The quantiser the previous coded macroblock used, or minus one at the start of a video packet.
  /// </summary>
  /// <remarks>
  /// Only one thing reads this, and it is the one thing that decides how an intra block's DC is
  /// coded: the threshold of clause 7.4.1.4 is compared against the <i>previous</i> macroblock's
  /// quantiser and not the current one, except at the first macroblock of a picture or a video
  /// packet, where there is no previous one and the current one stands in. Comparing the current
  /// quantiser everywhere is wrong exactly at the macroblocks that change it, and a block whose DC is
  /// read the wrong way takes the rest of the picture with it.
  /// </remarks>
  private int _runningQuantiser = -1;

  private Mpeg4PictureDecoder(
    Mpeg4VideoObjectLayer layer, Mpeg4VideoObjectPlane plane, Mpeg4Frame target,
    Mpeg4Frame? forwardReference, Mpeg4Frame? backwardReference, Mpeg4AnchorMotion? anchorMotion,
    int referenceDistance, int forwardDistance) {
    this._layer = layer;
    this._plane = plane;
    this._target = target;
    this._forwardReference = forwardReference;
    this._backwardReference = backwardReference;
    this._anchorMotion = anchorMotion;
    this._referenceDistance = referenceDistance;
    this._forwardDistance = forwardDistance;
    this._macroblockWidth = layer.MacroblockWidth;
    this._macroblockHeight = layer.MacroblockHeight;
    this._quantiser = plane.Quantiser;

    var count = this._macroblockWidth * this._macroblockHeight;
    this._vectorX = new short[count * 4];
    this._vectorY = new short[count * 4];
    this._isDecoded = new bool[count];
    this._intraPrediction = new(this._macroblockWidth, this._macroblockHeight);
    this.Motion = new(count);
  }

  /// <summary>The picture being reconstructed.</summary>
  internal Mpeg4Frame Target => this._target;

  /// <summary>What a later bidirectionally coded picture needs to know about this one.</summary>
  internal Mpeg4AnchorMotion Motion { get; }

  /// <summary>
  /// Prepares to decode a picture whose header has been read.
  /// </summary>
  internal static Mpeg4PictureDecoder BeginPicture(
    Mpeg4VideoObjectLayer layer, Mpeg4VideoObjectPlane plane, Mpeg4Frame target,
    Mpeg4Frame? forwardReference, Mpeg4Frame? backwardReference, Mpeg4AnchorMotion? anchorMotion,
    int referenceDistance, int forwardDistance) {
    ArgumentNullException.ThrowIfNull(layer);
    ArgumentNullException.ThrowIfNull(plane);
    ArgumentNullException.ThrowIfNull(target);

    if (plane.CodingType == Mpeg4VideoObjectPlane.BidirectionallyCoded
        && (forwardReference == null || backwardReference == null))
      throw new InvalidDataException(
        "An MPEG-4 bidirectionally coded picture arrived before both of the pictures it is predicted from had been "
        + "decoded. Decoding must begin at an intra picture.");

    if (plane.CodingType != Mpeg4VideoObjectPlane.IntraCoded && forwardReference == null)
      throw new InvalidDataException(
        "An MPEG-4 predicted picture arrived before any intra picture, so there is nothing for it to be predicted "
        + "from. Decoding must begin at an intra picture.");

    return new(
      layer, plane, target, forwardReference, backwardReference, anchorMotion, referenceDistance, forwardDistance);
  }

  // ============================================================================================
  // The macroblock walk — ISO/IEC 14496-2, 6.2.5 and 6.2.6
  // ============================================================================================

  /// <summary>Decodes every macroblock of the picture, taking any video packet headers between them.</summary>
  internal void DecodePicture(ref Mpeg4BitReader reader) {
    var count = this._macroblockWidth * this._macroblockHeight;

    for (var address = 0; address < count; ++address) {
      if (this._layer.MayCarryResyncMarkers && address > 0 && this._IsAtResyncMarker(ref reader))
        this._TakeVideoPacketHeader(ref reader, address);

      switch (this._plane.CodingType) {
        case Mpeg4VideoObjectPlane.IntraCoded:
          this._DecodeIntraMacroblock(ref reader, address);
          break;

        case Mpeg4VideoObjectPlane.PredictiveCoded:
          this._DecodePredictedMacroblock(ref reader, address);
          break;

        default:
          this._DecodeBidirectionalMacroblock(ref reader, address);
          break;
      }
    }
  }

  /// <summary>
  /// Whether this macroblock takes any bits from the bitstream at all.
  /// </summary>
  /// <remarks>
  /// Almost all of them do — every macroblock of an intra picture carries its type, and every
  /// macroblock of a predicted one carries at least the bit saying whether it is coded. The exception
  /// is a macroblock of a bidirectionally coded picture whose co-located macroblock in the anchor
  /// that follows was not coded: ISO/IEC 14496-2 6.2.6 takes that one out of the bitstream entirely.
  /// <para/>
  /// Which matters here for one reason. A resync marker is recognised by looking at the bits in front
  /// of a macroblock, and a macroblock that occupies no bits leaves the position in front of it and
  /// the position behind it the same — so a marker that belongs after it looks exactly like a marker
  /// that belongs before it. Not looking in front of such a macroblock resolves that: the marker is
  /// then found where it really is, in front of the next macroblock that does carry something.
  /// </remarks>
  private bool _CarriesBits(int address)
    => this._plane.CodingType != Mpeg4VideoObjectPlane.BidirectionallyCoded
       || this._anchorMotion == null
       || !this._anchorMotion.IsNotCoded[address];

  /// <summary>
  /// Whether a resync marker begins at the current position (ISO/IEC 14496-2, 6.3.5.2).
  /// </summary>
  /// <remarks>
  /// The marker is a run of zeroes and a one, and how long the run is depends on the picture type and
  /// its motion vector range — so the test is for the shortest form the picture could carry. Encoders
  /// pad to a byte boundary before it, and those padding bits are ones rather than zeroes, which is
  /// what keeps the run from starting early.
  /// </remarks>
  private bool _IsAtResyncMarker(ref Mpeg4BitReader reader) {
    // The stuffing is checked and not merely stepped over. Skipping a fixed number of bits and
    // looking for zeroes past them finds a marker one macroblock too early wherever the bits that
    // ought to be stuffing are a macroblock instead — which is exactly what a run of skipped
    // macroblocks in a predicted picture looks like, since each of those is a single set bit.
    var stuffing = _StuffingBefore(reader.BitPosition);
    if (reader.NextBits(stuffing) != (1 << (stuffing - 1)) - 1)
      return false;

    var probe = reader;
    probe.Skip(stuffing);

    var length = this._ResyncMarkerLength();
    return probe.BitsRemaining >= length && probe.NextBits(length) == 1;
  }

  /// <summary>
  /// How many bits of stuffing sit in front of a byte-aligned marker (ISO/IEC 14496-2, 6.2.5.2).
  /// </summary>
  /// <remarks>
  /// Between one and eight, and never nought. The stuffing is a zero bit and then ones until the next
  /// byte boundary, so a position that is already aligned still carries a whole byte of it — which is
  /// what makes the stuffing recognisable rather than merely absent. Treating an aligned position as
  /// needing none is a decoder that finds every resync marker except the ones an encoder was most
  /// likely to emit, since a macroblock ending on a byte boundary is one time in eight.
  /// </remarks>
  private static int _StuffingBefore(int position) => 8 - (position & 7);

  /// <summary>How many bits the resync marker of this picture occupies (ISO/IEC 14496-2, 6.3.5.2).</summary>
  private int _ResyncMarkerLength() => this._plane.CodingType switch {
    Mpeg4VideoObjectPlane.IntraCoded => 17,
    Mpeg4VideoObjectPlane.PredictiveCoded => 16 + this._plane.ForwardFCode,
    _ => Math.Max(18, 16 + Math.Max(this._plane.ForwardFCode, this._plane.BackwardFCode)),
  };

  /// <summary>
  /// Reads a video packet header and returns the macroblock its data starts at.
  /// </summary>
  /// <remarks>
  /// A video packet restarts everything that is predicted across macroblocks — the vectors, the intra
  /// coefficients and the quantiser — so that a decoder joining after a lost packet can carry on. Not
  /// restarting them would leave every macroblock of the packet predicted from a macroblock the
  /// standard says is unavailable.
  /// </remarks>
  private void _TakeVideoPacketHeader(ref Mpeg4BitReader reader, int address) {
    // Read it on a copy first and keep it only if it names this macroblock. What looks like a marker
    // in front of a macroblock that occupies no bits is a marker that belongs behind it, because the
    // position either side of such a macroblock is the same position — and the number the header
    // carries is the only thing in the bitstream that says which.
    var probe = reader;
    probe.Skip(_StuffingBefore(probe.BitPosition));
    probe.Skip(this._ResyncMarkerLength());

    var count = this._macroblockWidth * this._macroblockHeight;
    var number = probe.ReadBits(_BitsFor(count));
    if (number != address) {
      if (!this._CarriesBits(address))
        return;

      throw new InvalidDataException(
        $"An MPEG-4 video packet states macroblock_number {number} where macroblock {address} was due. The video "
        + "packets of a picture partition it, so one that begins anywhere but at the macroblock after the last "
        + "packet's would leave macroblocks with nothing coded for them.");
    }

    var quantiser = probe.ReadBits(this._layer.QuantiserPrecision);
    if (quantiser == 0)
      throw new InvalidDataException(
        "An MPEG-4 video packet states quant_scale 0, which ISO/IEC 14496-2 6.3.5 forbids.");

    if (probe.ReadBit() == 1)
      throw new NotSupportedException(
        "An MPEG-4 video packet sets header_extension_code, which repeats the picture header inside the packet "
        + "(ISO/IEC 14496-2 6.2.5.2). That is not implemented.");

    reader = probe;
    this._quantiser = quantiser;

    // Everything predicted across macroblocks starts afresh here, which is what makes a video packet
    // the unit a decoder can resynchronise on.
    this._packetStart = number;
    this._runningQuantiser = -1;
    this._forwardPredictorX = this._forwardPredictorY = this._backwardPredictorX = this._backwardPredictorY = 0;
    this._intraPrediction.BeginVideoPacket(number);
  }

  private static int _BitsFor(int count) {
    var bits = 1;
    while (1 << bits < count)
      ++bits;

    return bits;
  }

  // ============================================================================================
  // Intra pictures — ISO/IEC 14496-2, 6.2.6
  // ============================================================================================

  private void _DecodeIntraMacroblock(ref Mpeg4BitReader reader, int address) {
    int macroblockType;
    int chromaPattern;

    for (; ; ) {
      var mcbpc = Mpeg4VlcTables.IntraMacroblockType.Read(ref reader);
      if (mcbpc == Mpeg4VlcTables.McbpcStuffing)
        continue;

      macroblockType = Mpeg4VlcTables.TypeOf(mcbpc);
      chromaPattern = Mpeg4VlcTables.ChromaPatternOf(mcbpc);
      break;
    }

    this._DecodeIntra(ref reader, address, macroblockType, chromaPattern);
  }

  /// <summary>Reads the rest of an intra macroblock, in a picture of any type.</summary>
  private void _DecodeIntra(ref Mpeg4BitReader reader, int address, int macroblockType, int chromaPattern) {
    var predictCoefficients = reader.ReadBit() == 1;
    var luminancePattern = Mpeg4VlcTables.LuminancePattern.Read(ref reader);

    if (macroblockType == _INTRA_WITH_QUANTISER)
      this._ApplyQuantiserDifference(ref reader);

    this._isDecoded[address] = true;
    for (var block = 0; block < 4; ++block) {
      this._vectorX[address * 4 + block] = 0;
      this._vectorY[address * 4 + block] = 0;
      this.Motion.VectorX[address * 4 + block] = 0;
      this.Motion.VectorY[address * 4 + block] = 0;
    }

    var useDcVlc = this._UsesDcVariableLengthCodes();
    var pattern = (luminancePattern << 2) | chromaPattern;
    Span<int> block64 = stackalloc int[64];

    for (var index = 0; index < 6; ++index) {
      Mpeg4BlockDecoder.ReadIntra(
        ref reader, block64, this._intraPrediction, address, index,
        this._quantiser, this._layer, predictCoefficients, _IsCoded(pattern, index), useDcVlc);

      this._Store(address, index, block64);
    }

    this._runningQuantiser = this._quantiser;
  }

  /// <summary>
  /// Whether an intra block's DC is coded with its own tables rather than as an ordinary coefficient
  /// (ISO/IEC 14496-2, 7.4.1.4 and Table 6-21).
  /// </summary>
  /// <remarks>
  /// The picture states a quantiser above which the DC stops being special, because at a coarse
  /// quantiser the DC's own tables cost more than they save. Nought means always special and seven
  /// means never; the six values between are thresholds two apart starting at thirteen.
  /// </remarks>
  private bool _UsesDcVariableLengthCodes() {
    var threshold = this._plane.IntraDcThreshold;
    if (threshold == 0)
      return true;

    if (threshold == 7)
      return false;

    var running = this._runningQuantiser < 0 ? this._quantiser : this._runningQuantiser;
    return running < 11 + 2 * threshold;
  }

  // ============================================================================================
  // Predicted pictures
  // ============================================================================================

  private void _DecodePredictedMacroblock(ref Mpeg4BitReader reader, int address) {
    if (reader.ReadBit() == 1) {
      this._CopyFromReference(address);
      return;
    }

    int macroblockType;
    int chromaPattern;
    for (; ; ) {
      var mcbpc = Mpeg4VlcTables.PredictedMacroblockType.Read(ref reader);
      if (mcbpc == Mpeg4VlcTables.McbpcStuffing)
        continue;

      macroblockType = Mpeg4VlcTables.TypeOf(mcbpc);
      chromaPattern = Mpeg4VlcTables.ChromaPatternOf(mcbpc);
      break;
    }

    if (macroblockType is _INTRA or _INTRA_WITH_QUANTISER) {
      this._DecodeIntra(ref reader, address, macroblockType, chromaPattern);
      return;
    }

    var luminancePattern = Mpeg4VlcTables.LuminancePattern.Read(ref reader) ^ 0xF;

    if (macroblockType == _INTER_WITH_QUANTISER)
      this._ApplyQuantiserDifference(ref reader);

    var vectorCount = macroblockType == _INTER_FOUR_VECTORS ? 4 : 1;
    Span<int> vectorsX = stackalloc int[4];
    Span<int> vectorsY = stackalloc int[4];

    for (var block = 0; block < vectorCount; ++block) {
      vectorsX[block] = this._ReadVector(ref reader, address, block, horizontal: true, this._plane.ForwardFCode);
      vectorsY[block] = this._ReadVector(ref reader, address, block, horizontal: false, this._plane.ForwardFCode);
      this._vectorX[address * 4 + block] = (short)vectorsX[block];
      this._vectorY[address * 4 + block] = (short)vectorsY[block];
    }

    if (vectorCount == 1)
      for (var block = 1; block < 4; ++block) {
        vectorsX[block] = vectorsX[0];
        vectorsY[block] = vectorsY[0];
        this._vectorX[address * 4 + block] = (short)vectorsX[0];
        this._vectorY[address * 4 + block] = (short)vectorsY[0];
      }

    this._isDecoded[address] = true;
    this._intraPrediction.MarkUnavailable(address);
    this._RecordMotion(address, vectorsX, vectorsY, notCoded: false);

    var pattern = (luminancePattern << 2) | chromaPattern;
    this._ReconstructPredicted(ref reader, address, pattern, vectorsX, vectorsY);
    this._runningQuantiser = this._quantiser;
  }

  /// <summary>
  /// Copies a macroblock nothing was coded for out of the reference picture.
  /// </summary>
  /// <remarks>
  /// A macroblock whose <c>not_coded</c> bit is set is the co-located one of the reference with a zero
  /// vector, and its vector counts as zero for every later macroblock's predictor.
  /// </remarks>
  private void _CopyFromReference(int address) {
    Span<int> zero = stackalloc int[4];
    this._isDecoded[address] = true;
    this._intraPrediction.MarkUnavailable(address);

    for (var block = 0; block < 4; ++block) {
      this._vectorX[address * 4 + block] = 0;
      this._vectorY[address * 4 + block] = 0;
    }

    this._RecordMotion(address, zero, zero, notCoded: true);

    Span<int> prediction = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      this._Predict(prediction, this._forwardReference!, address, index, 0, 0, this._plane.RoundingType);
      this._Store(address, index, prediction);
    }
  }

  private void _ReconstructPredicted(
    ref Mpeg4BitReader reader, int address, int pattern, scoped ReadOnlySpan<int> vectorsX,
    scoped ReadOnlySpan<int> vectorsY) {
    Span<int> block = stackalloc int[64];
    Span<int> prediction = stackalloc int[64];

    for (var index = 0; index < 6; ++index) {
      var (vectorX, vectorY) = index < 4
        ? (vectorsX[index], vectorsY[index])
        : _ChromaVector(vectorsX, vectorsY);

      this._Predict(prediction, this._forwardReference!, address, index, vectorX, vectorY, this._plane.RoundingType);

      if (_IsCoded(pattern, index)) {
        Mpeg4BlockDecoder.ReadInter(ref reader, block, this._quantiser, this._layer);
        for (var i = 0; i < 64; ++i)
          block[i] += prediction[i];
      } else {
        prediction.CopyTo(block);
      }

      this._Store(address, index, block);
    }
  }

  // ============================================================================================
  // Bidirectionally coded pictures
  // ============================================================================================

  private void _DecodeBidirectionalMacroblock(ref Mpeg4BitReader reader, int address) {
    // Both predictors start afresh on every macroblock row, which is what makes a bidirectionally
    // coded picture decodable a row at a time and is not what a predicted picture does.
    if (address % this._macroblockWidth == 0)
      this._forwardPredictorX = this._forwardPredictorY = this._backwardPredictorX = this._backwardPredictorY = 0;

    this._isDecoded[address] = true;

    Span<int> forwardX = stackalloc int[4];
    Span<int> forwardY = stackalloc int[4];
    Span<int> backwardX = stackalloc int[4];
    Span<int> backwardY = stackalloc int[4];

    // A macroblock the following anchor did not code carries no bits here at all: the standard's
    // co_located_not_coded takes the whole macroblock out of the bitstream, and it is reconstructed
    // as a forward prediction with a zero vector. A decoder that read a MODB for it would be one
    // codeword into the next macroblock.
    if (this._anchorMotion != null && this._anchorMotion.IsNotCoded[address]) {
      this._ReconstructBidirectional(
        ref reader, address, 0, forwardX, forwardY, backwardX, backwardY, Mpeg4VlcTables.Forward);
      return;
    }

    var mode = Mpeg4VlcTables.BidirectionalMode.Read(ref reader);

    // MODB of one is the whole macroblock: no type, no pattern, no vector. It means the direct mode
    // with a delta vector of zero, and reading a delta for it — which the direct mode otherwise
    // carries — puts the decoder a codeword into the next macroblock.
    if (mode == 0) {
      this._DeriveDirectVectors(address, 0, 0, forwardX, forwardY, backwardX, backwardY);
      this._ReconstructBidirectional(
        ref reader, address, 0, forwardX, forwardY, backwardX, backwardY, Mpeg4VlcTables.Direct);
      return;
    }

    var type = Mpeg4VlcTables.BidirectionalMacroblockType.Read(ref reader);
    var pattern = mode == 2 ? reader.ReadBits(6) : 0;

    if (type != Mpeg4VlcTables.Direct && pattern != 0) {
      var quantiser = this._quantiser + Mpeg4VlcTables.BidirectionalQuantiserDifference.Read(ref reader);
      var highest = (1 << this._layer.QuantiserPrecision) - 1;
      this._quantiser = quantiser < 1 ? 1 : quantiser > highest ? highest : quantiser;
    }

    switch (type) {
      case Mpeg4VlcTables.Forward:
        this._ReadBidirectionalVector(ref reader, forwardX, forwardY, forward: true);
        break;

      case Mpeg4VlcTables.Backward:
        this._ReadBidirectionalVector(ref reader, backwardX, backwardY, forward: false);
        break;

      case Mpeg4VlcTables.Interpolated:
        this._ReadBidirectionalVector(ref reader, forwardX, forwardY, forward: true);
        this._ReadBidirectionalVector(ref reader, backwardX, backwardY, forward: false);
        break;

      default:
        this._ReadDirectVectors(ref reader, address, forwardX, forwardY, backwardX, backwardY);
        break;
    }

    this._ReconstructBidirectional(ref reader, address, pattern, forwardX, forwardY, backwardX, backwardY, type);
  }

  /// <summary>
  /// Reads one of a bidirectionally coded macroblock's own vectors, which is one vector for the whole
  /// macroblock.
  /// </summary>
  /// <remarks>
  /// The predictor is the last vector of the same direction rather than a median of neighbours, and
  /// it is updated only by macroblocks that carry a vector of that direction — a direct-mode or
  /// uncoded macroblock leaves both predictors where they were.
  /// </remarks>
  private void _ReadBidirectionalVector(
    ref Mpeg4BitReader reader, scoped Span<int> vectorX, scoped Span<int> vectorY, bool forward) {
    var fCode = forward ? this._plane.ForwardFCode : this._plane.BackwardFCode;
    ref var predictorX = ref (forward ? ref this._forwardPredictorX : ref this._backwardPredictorX);
    ref var predictorY = ref (forward ? ref this._forwardPredictorY : ref this._backwardPredictorY);

    predictorX = _Reconstruct(predictorX, Mpeg4VlcTables.ReadMotionVectorDifference(ref reader, fCode), fCode);
    predictorY = _Reconstruct(predictorY, Mpeg4VlcTables.ReadMotionVectorDifference(ref reader, fCode), fCode);

    for (var block = 0; block < 4; ++block) {
      vectorX[block] = predictorX;
      vectorY[block] = predictorY;
    }
  }

  /// <summary>
  /// Derives the four forward and four backward vectors of a direct-mode macroblock (ISO/IEC 14496-2,
  /// 7.6.9.5).
  /// </summary>
  /// <remarks>
  /// Direct mode carries no vectors of its own, only a small delta. It takes the vectors of the
  /// co-located macroblock of the anchor that follows this picture and scales them by where in time
  /// this picture sits between the two it is predicted from — so a macroblock moving steadily needs
  /// almost no bits at all, and so a bidirectionally coded picture cannot be decoded without keeping
  /// the anchor's motion after the anchor itself is finished.
  /// <para/>
  /// The delta's own predictor is always zero and its motion code is always one, which is why it is
  /// read here rather than through the ordinary vector path.
  /// </remarks>
  private void _ReadDirectVectors(
    ref Mpeg4BitReader reader, int address,
    scoped Span<int> forwardX, scoped Span<int> forwardY, scoped Span<int> backwardX, scoped Span<int> backwardY) {
    var deltaX = Mpeg4VlcTables.ReadMotionVectorDifference(ref reader, 1);
    var deltaY = Mpeg4VlcTables.ReadMotionVectorDifference(ref reader, 1);

    this._DeriveDirectVectors(address, deltaX, deltaY, forwardX, forwardY, backwardX, backwardY);
  }

  private void _DeriveDirectVectors(
    int address, int deltaX, int deltaY,
    scoped Span<int> forwardX, scoped Span<int> forwardY, scoped Span<int> backwardX, scoped Span<int> backwardY) {
    var distance = this._referenceDistance;
    if (distance == 0)
      throw new InvalidDataException(
        "A direct-mode macroblock of an MPEG-4 bidirectionally coded picture needs the time between the two pictures "
        + "it is predicted from, and this stream states that they are at the same instant. ISO/IEC 14496-2 7.6.9.5 "
        + "divides by that difference.");

    for (var block = 0; block < 4; ++block) {
      var index = address * 4 + block;
      var anchorX = this._anchorMotion == null ? 0 : this._anchorMotion.VectorX[index];
      var anchorY = this._anchorMotion == null ? 0 : this._anchorMotion.VectorY[index];

      forwardX[block] = this._forwardDistance * anchorX / distance + deltaX;
      forwardY[block] = this._forwardDistance * anchorY / distance + deltaY;

      backwardX[block] = deltaX == 0
        ? (this._forwardDistance - distance) * anchorX / distance
        : forwardX[block] - anchorX;
      backwardY[block] = deltaY == 0
        ? (this._forwardDistance - distance) * anchorY / distance
        : forwardY[block] - anchorY;
    }
  }

  private void _ReconstructBidirectional(
    ref Mpeg4BitReader reader, int address, int pattern,
    scoped Span<int> forwardX, scoped Span<int> forwardY, scoped Span<int> backwardX, scoped Span<int> backwardY,
    int type) {
    var usesForward = type is Mpeg4VlcTables.Forward or Mpeg4VlcTables.Interpolated or Mpeg4VlcTables.Direct;
    var usesBackward = type is Mpeg4VlcTables.Backward or Mpeg4VlcTables.Interpolated or Mpeg4VlcTables.Direct;

    Span<int> block = stackalloc int[64];
    Span<int> prediction = stackalloc int[64];
    Span<int> backward = stackalloc int[64];

    var (chromaForwardX, chromaForwardY) = _ChromaVector(forwardX, forwardY);
    var (chromaBackwardX, chromaBackwardY) = _ChromaVector(backwardX, backwardY);

    for (var index = 0; index < 6; ++index) {
      var vectorBlock = index < 4 ? index : 0;

      if (usesForward)
        this._Predict(
          prediction, this._forwardReference!, address, index,
          index < 4 ? forwardX[vectorBlock] : chromaForwardX,
          index < 4 ? forwardY[vectorBlock] : chromaForwardY, 0);

      if (usesBackward) {
        var target = usesForward ? backward : prediction;
        this._Predict(
          target, this._backwardReference!, address, index,
          index < 4 ? backwardX[vectorBlock] : chromaBackwardX,
          index < 4 ? backwardY[vectorBlock] : chromaBackwardY, 0);

        if (usesForward)
          Mpeg4MotionCompensation.Average(prediction, backward);
      }

      if ((pattern & (1 << (5 - index))) != 0) {
        Mpeg4BlockDecoder.ReadInter(ref reader, block, this._quantiser, this._layer);
        for (var i = 0; i < 64; ++i)
          block[i] += prediction[i];
      } else {
        prediction.CopyTo(block);
      }

      this._Store(address, index, block);
    }
  }

  // ============================================================================================
  // Motion vectors — ISO/IEC 14496-2, 7.6.2
  // ============================================================================================

  private int _ReadVector(ref Mpeg4BitReader reader, int address, int block, bool horizontal, int fCode)
    => _Reconstruct(
      this._PredictVector(address, block, horizontal),
      Mpeg4VlcTables.ReadMotionVectorDifference(ref reader, fCode), fCode);

  /// <summary>
  /// Adds a difference to a predictor and brings the result back into the range the motion code
  /// allows (ISO/IEC 14496-2, 7.6.3).
  /// </summary>
  /// <remarks>
  /// The wraparound is a single add or subtract of the whole range and not a clamp. It is how the
  /// far end of the range is reached at all: a vector near one end predicts a vector near the other
  /// with a small difference, and clamping instead would produce a vector nobody coded.
  /// </remarks>
  private static int _Reconstruct(int predictor, int difference, int fCode) {
    var scale = 1 << (fCode - 1);
    var low = -32 * scale;
    var high = 32 * scale - 1;
    var range = 64 * scale;

    var vector = predictor + difference;
    if (vector < low)
      return vector + range;

    return vector > high ? vector - range : vector;
  }

  /// <summary>
  /// The median of the three candidate predictors of ISO/IEC 14496-2 Figure 7-8.
  /// </summary>
  private int _PredictVector(int address, int block, bool horizontal) {
    var vectors = horizontal ? this._vectorX : this._vectorY;
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    var left = this._CandidateOf(address, column > 0 ? address - 1 : -1, block, 0, vectors);
    var above = this._CandidateOf(address, row > 0 ? address - this._macroblockWidth : -1, block, 1, vectors);
    var aboveRight = this._CandidateOf(
      address,
      row > 0 && column + 1 < this._macroblockWidth ? address - this._macroblockWidth + 1 : -1,
      block, 2, vectors);

    return _Median(left.Value, above.Value, aboveRight.Value, left.Valid, above.Valid, aboveRight.Valid);
  }

  /// <summary>
  /// One candidate predictor: which block of which macroblock stands where, and whether it is there.
  /// </summary>
  /// <remarks>
  /// The candidates are blocks and not macroblocks, which is what makes the four vectors of an
  /// INTER4V macroblock predict from each other rather than all from the macroblock beside them.
  /// Figure 7-8 sets out all twelve cases; the four inside the current macroblock are what make the
  /// order of the four vectors matter.
  /// </remarks>
  private (int Value, bool Valid) _CandidateOf(
    int address, int neighbour, int block, int direction, short[] vectors) {
    var (source, inNeighbour) = (block, direction) switch {
      (0, 0) => (1, true), (0, 1) => (2, true), (0, 2) => (2, true),
      (1, 0) => (0, false), (1, 1) => (3, true), (1, 2) => (2, true),
      (2, 0) => (3, true), (2, 1) => (0, false), (2, 2) => (1, false),
      _ => (direction == 0 ? 2 : direction == 1 ? 0 : 1, false),
    };

    if (!inNeighbour)
      return (vectors[address * 4 + source], this._isDecoded[address] || source < block);

    if (neighbour < 0 || !this._isDecoded[neighbour] || neighbour < this._packetStart)
      return (0, false);

    return (vectors[neighbour * 4 + source], true);
  }

  /// <summary>
  /// The median of three candidates, with the substitutions ISO/IEC 14496-2 7.6.2 makes where one or
  /// more of them is not there.
  /// </summary>
  private static int _Median(int a, int b, int c, bool validA, bool validB, bool validC) {
    var count = (validA ? 1 : 0) + (validB ? 1 : 0) + (validC ? 1 : 0);
    switch (count) {
      case 0:
        return 0;

      case 1:
        return validA ? a : validB ? b : c;

      case 2:
        // The one that is missing is taken as zero, which is what the standard's table amounts to
        // once the median of three with a zero in it is written out.
        a = validA ? a : 0;
        b = validB ? b : 0;
        c = validC ? c : 0;
        break;
    }

    if (a > b)
      (a, b) = (b, a);

    if (b > c)
      b = c;

    return a > b ? a : b;
  }

  /// <summary>
  /// The chrominance vector, derived from the macroblock's luminance vectors (ISO/IEC 14496-2, 7.6.2).
  /// </summary>
  private static (int X, int Y) _ChromaVector(scoped ReadOnlySpan<int> vectorsX, scoped ReadOnlySpan<int> vectorsY)
    => (Mpeg4MotionCompensation.ToChroma(vectorsX[0] + vectorsX[1] + vectorsX[2] + vectorsX[3]),
      Mpeg4MotionCompensation.ToChroma(vectorsY[0] + vectorsY[1] + vectorsY[2] + vectorsY[3]));

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  private void _Predict(
    Span<int> prediction, Mpeg4Frame reference, int address, int index, int vectorX, int vectorY, int rounding) {
    var (plane, stride, origin, width, height) = reference.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);
    var border = index < 4 ? Mpeg4Frame.Border : Mpeg4Frame.Border / 2;

    Mpeg4MotionCompensation.PredictHalfSample(
      prediction, plane, stride, origin, border, width, height, left, top, vectorX, vectorY, rounding);
  }

  private void _ApplyQuantiserDifference(ref Mpeg4BitReader reader) {
    var difference = reader.ReadBits(2) switch { 0 => -1, 1 => -2, 2 => 1, _ => 2 };
    var quantiser = this._quantiser + difference;
    this._quantiser = quantiser < 1 ? 1 : quantiser > (1 << this._layer.QuantiserPrecision) - 1
      ? (1 << this._layer.QuantiserPrecision) - 1
      : quantiser;
  }

  private void _RecordMotion(
    int address, scoped ReadOnlySpan<int> vectorsX, scoped ReadOnlySpan<int> vectorsY, bool notCoded) {
    this.Motion.IsNotCoded[address] = notCoded;
    for (var block = 0; block < 4; ++block) {
      this.Motion.VectorX[address * 4 + block] = (short)vectorsX[block];
      this.Motion.VectorY[address * 4 + block] = (short)vectorsY[block];
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

  /// <summary>Whether one of a macroblock's six blocks carries coefficients.</summary>
  private static bool _IsCoded(int pattern, int index) => (pattern & (1 << (5 - index))) != 0;

  /// <summary>Where one of a macroblock's six blocks sits in its plane (ISO/IEC 14496-2, Figure 6-5).</summary>
  private (int Left, int Top) _BlockOrigin(int address, int index) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    return index < 4
      ? (column * 16 + (index & 1) * 8, row * 16 + (index >> 1) * 8)
      : (column * 8, row * 8);
  }
}
