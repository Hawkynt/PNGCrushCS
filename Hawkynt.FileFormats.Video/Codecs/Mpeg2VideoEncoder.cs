using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes progressive MPEG-2 video (ITU-T H.262 / ISO/IEC 13818-2) as Main Profile at Main Level,
/// 8-bit 4:2:0 I, P and B pictures.
/// </summary>
/// <remarks>
/// Pictures arrive in display order. Ordinary anchor intervals are coded as <c>I/P B B</c> in
/// display order and emitted as <c>I/P B B</c> only after the following anchor has been encoded,
/// which puts the bitstream itself into MPEG decode order. Presentation timestamps stay attached to
/// their pictures while decode timestamps consume the input timeline in emitted order.
/// <para/>
/// P pictures predict from the reconstructed anchor before them. A B macroblock searches both the
/// preceding and the following reconstructed anchor and then codes whichever of Table B.4's three
/// modes fits its samples best: forward only, backward only, or both averaged the way H.262 rounds
/// them. Anchor reconstruction is produced by the decoder this encoder drives with its own output,
/// so encoder and decoder references are identical by construction rather than merely close.
/// <para/>
/// Every twelfth display picture is intra. The two pictures immediately before such a boundary are
/// written as P pictures rather than B pictures: otherwise coding the future I anchor first would
/// make a nominal key frame followed by B pictures that still require the pre-seek anchor. The
/// fallback keeps every packet marked as a key frame independently usable as a random-access point.
/// <para/>
/// Interlacing, field pictures and dual-prime prediction are not written. The writer deliberately
/// stays inside progressive Main Profile at Main Level while the decoder accepts the larger subset
/// implemented by the shared MPEG engine.
/// <para/>
/// <b>Lossy.</b> H.262 quantises DCT coefficients and has no lossless mode. The encoder begins at
/// quantiser_scale_code 4 and raises it only when necessary to remain inside Main Level's 15 Mbit/s
/// rate bound. A picture that still cannot fit at code 31 is refused rather than labelled Main Level
/// while violating the level it declares.
/// <para/>
/// Every packet repeats the sequence header and sequence extension before its picture. H.262 permits
/// repeated sequence headers; an I packet is therefore independently decodable and a decoder joining
/// on a predicted packet still learns the geometry before the next key frame.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Mpeg2VideoEncoder : IVideoCodecEncoder<Mpeg2VideoEncoder> {

  private static readonly CodecTag _MPG2 = CodecTag.FromCharacters("MPG2");

  private const int _MAX_WIDTH = 720;
  private const int _MAX_HEIGHT = 576;
  private const long _MAX_LUMA_SAMPLE_RATE = 10_368_000;
  private const long _MAX_BIT_RATE = 15_000_000;
  private const int _VBV_BUFFER_SIZE_VALUE = 112;
  private const int _BIT_RATE_VALUE = (int)(_MAX_BIT_RATE / 400);

  private static readonly int[] _QuantiserScaleCandidates = [4, 6, 8, 12, 16, 20, 24, 28, 31];

  private const int _KEY_FRAME_INTERVAL = 12;
  private const int _B_FRAMES = 2;

  /// <summary>f_code used for both components of both prediction directions.</summary>
  private const int _MOTION_F_CODE = 3;
  private const int _MOTION_SCALE = 1 << (_MOTION_F_CODE - 1);
  private const int _MOTION_LIMIT = 16 * _MOTION_SCALE;
  private const int _F_CODE_UNUSED = 15;
  private const int _SEARCH_RANGE = 15;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _frameRateCode;
  private readonly Rational _frameRate;
  private readonly MpegVideoDecoder _reconstruction = new();
  private readonly List<PendingFrame> _pending = [];
  private readonly Queue<CodedPacket> _ready = new();
  private readonly Queue<long?> _decodeTimestamps = new();

  private MediaStreamInfo? _stream;
  private MpegFrame? _anchor;
  private long _displayNumber;

  private Mpeg2VideoEncoder(MediaStreamInfo stream, int frameRateCode, Rational frameRate) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._codedWidth = (stream.Width + 15) & ~15;
    this._codedHeight = (stream.Height + 15) & ~15;
    this._macroblockWidth = this._codedWidth >> 4;
    this._macroblockHeight = this._codedHeight >> 4;
    this._frameRateCode = frameRateCode;
    this._frameRate = frameRate;
  }

  public static string CodecName => "MPEG-2 video (ISO/IEC 13818-2)";

  public static CodecTag Codec => _MPG2;

  /// <summary>Builds a progressive Main-Profile/Main-Level MPEG-2 encoder.</summary>
  public static Mpeg2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("MPEG-2 video can only encode a video stream.");

    if (stream.Width is <= 0 or > _MAX_WIDTH || stream.Height is <= 0 or > _MAX_HEIGHT)
      throw new NotSupportedException(
        $"This MPEG-2 encoder writes Main Profile at Main Level, whose coded picture is at most {_MAX_WIDTH}x{_MAX_HEIGHT}; "
        + $"{stream.Width}x{stream.Height} was requested.");

    if ((stream.Width & 1) != 0 || (stream.Height & 1) != 0)
      throw new NotSupportedException(
        $"This MPEG-2 encoder writes 4:2:0 pictures, so both dimensions must be even; {stream.Width}x{stream.Height} was requested.");

    var (frameRateCode, frameRate) = _FrameRateOf(stream.FrameRate);
    var codedWidth = (stream.Width + 15) & ~15;
    var codedHeight = (stream.Height + 15) & ~15;
    var sampleRate = (Int128)codedWidth * codedHeight * frameRate.Numerator / frameRate.Denominator;
    if (sampleRate > _MAX_LUMA_SAMPLE_RATE)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} picture at {frameRate} requires {sampleRate} luminance samples/s after "
        + $"macroblock padding; Main Level permits {_MAX_LUMA_SAMPLE_RATE}.");

    return new(stream, frameRateCode, frameRate);
  }

  /// <summary>
  /// Accepts one display-order picture and returns the next coding-order packet when one is ready.
  /// </summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This MPEG-2 stream is {this._width}x{this._height}; a {frame.Width}x{frame.Height} picture arrived. "
        + "A sequence cannot change dimensions while the encoder is open.");

    var pending = new PendingFrame(this._ToFrame(frame), this._displayNumber++, presentationTimestamp);
    this._decodeTimestamps.Enqueue(presentationTimestamp);

    if (this._anchor == null)
      this._ready.Enqueue(this._EncodeAnchor(pending, isIntra: true));
    else {
      this._pending.Add(pending);
      if (this._pending.Count == _B_FRAMES + 1)
        this._EncodeGroup();
    }

    if (this._ready.TryDequeue(out packet))
      return true;

    packet = default;
    return false;
  }

  /// <summary>
  /// Emits delayed packets, then turns a short tail with no following anchor into ordinary P pictures.
  /// </summary>
  public IEnumerable<CodedPacket> Flush() {
    while (this._ready.TryDequeue(out var ready))
      yield return ready;

    foreach (var pending in this._pending) {
      var isIntra = pending.DisplayNumber % _KEY_FRAME_INTERVAL == 0;
      yield return this._EncodeAnchor(pending, isIntra);
    }

    this._pending.Clear();

    while (this._ready.TryDequeue(out var ready))
      yield return ready;
  }

  /// <summary>The stream description required by MPEG elementary streams and ordinary containers.</summary>
  public MediaStreamInfo DescribeStream() => this._stream ??= new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _MPG2,
    Handler = _MPG2,
    CodecId = "V_MPEG2",
    TimeBase = this._requested.TimeBase,
    FrameRate = this._frameRate,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = 12,
    CodecPrivateData = ReadOnlyMemory<byte>.Empty,
  };

  /// <summary>Codes the future anchor first, then the B pictures that display before it.</summary>
  private void _EncodeGroup() {
    var nextAnchor = this._pending[^1];

    // Do not leave B pictures after a seekable I packet that depend on an anchor from before it.
    if (nextAnchor.DisplayNumber % _KEY_FRAME_INTERVAL == 0) {
      foreach (var pending in this._pending)
        this._ready.Enqueue(this._EncodeAnchor(
          pending,
          isIntra: pending.DisplayNumber % _KEY_FRAME_INTERVAL == 0));

      this._pending.Clear();
      return;
    }

    var oldAnchor = this._anchor!;
    var anchorPacket = this._EncodeAnchor(nextAnchor, isIntra: false);
    var newAnchor = this._anchor!;
    this._ready.Enqueue(anchorPacket);

    for (var index = 0; index < _B_FRAMES; ++index) {
      var pending = this._pending[index];
      var bytes = this._EncodeWithRateControl(
        pending.Source,
        MpegPictureDecoder.BidirectionallyCoded,
        oldAnchor,
        newAnchor,
        pending.DisplayNumber);
      this._ready.Enqueue(this._Packet(bytes, pending.PresentationTimestamp, isKeyFrame: false));
    }

    this._pending.Clear();
  }

  private CodedPacket _EncodeAnchor(PendingFrame pending, bool isIntra) {
    var codingType = isIntra ? MpegPictureDecoder.IntraCoded : MpegPictureDecoder.PredictiveCoded;
    var bytes = this._EncodeWithRateControl(
      pending.Source,
      codingType,
      isIntra ? null : this._anchor,
      backwardReference: null,
      pending.DisplayNumber);

    this._anchor = this._ReconstructAnchor(bytes);
    return this._Packet(bytes, pending.PresentationTimestamp, isIntra);
  }

  private byte[] _EncodeWithRateControl(
    MpegFrame source,
    int codingType,
    MpegFrame? forwardReference,
    MpegFrame? backwardReference,
    long displayNumber) {
    foreach (var quantiserScaleCode in _QuantiserScaleCandidates) {
      var bytes = this._EncodePicture(
        source,
        quantiserScaleCode,
        codingType,
        forwardReference,
        backwardReference,
        displayNumber);
      if (_FitsMainLevel(bytes.Length, this._frameRate))
        return bytes;
    }

    throw new InvalidDataException(
      $"The {this._width}x{this._height} MPEG-2 picture exceeds Main Level's {_MAX_BIT_RATE / 1_000_000} Mbit/s "
      + "rate bound even at quantiser_scale_code 31.");
  }

  private MpegFrame _ReconstructAnchor(byte[] bytes) {
    this._reconstruction.DecodePacket(bytes);
    while (this._reconstruction.TryTakeReady(out _)) { }

    return this._reconstruction.CurrentAnchor
           ?? throw new InvalidDataException("The MPEG-2 encoder wrote an anchor picture that its reconstruction decoder did not retain.");
  }

  private CodedPacket _Packet(byte[] bytes, long? presentationTimestamp, bool isKeyFrame) {
    var decodeTimestamp = this._decodeTimestamps.Count == 0 ? null : this._decodeTimestamps.Dequeue();
    return new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: decodeTimestamp,
      IsKeyFrame: isKeyFrame);
  }

  private byte[] _EncodePicture(
    MpegFrame source,
    int quantiserScaleCode,
    int codingType,
    MpegFrame? forwardReference,
    MpegFrame? backwardReference,
    long displayNumber) {
    if (codingType == MpegPictureDecoder.PredictiveCoded && forwardReference == null)
      throw new InvalidOperationException("A predictive MPEG-2 picture needs a forward reference.");
    if (codingType == MpegPictureDecoder.BidirectionallyCoded && (forwardReference == null || backwardReference == null))
      throw new InvalidOperationException("A bidirectionally coded MPEG-2 picture needs both forward and backward references.");

    var writer = new MpegBitWriter();
    this._WriteSequenceHeader(writer);
    _WriteSequenceExtension(writer);
    _WritePictureHeader(writer, codingType, displayNumber);
    _WritePictureCodingExtension(writer, codingType);

    var dcPredictor = new int[3];
    var levels = new int[64 * 6];
    for (var row = 0; row < this._macroblockHeight; ++row) {
      writer.WriteStartCode((byte)(MpegStartCode.FirstSlice + row));
      writer.WriteBits(quantiserScaleCode, 5);
      writer.WriteBit(0); // extra_bit_slice

      dcPredictor[0] = dcPredictor[1] = dcPredictor[2] = 128;
      var predictedForwardX = 0;
      var predictedForwardY = 0;
      var predictedBackwardX = 0;
      var predictedBackwardY = 0;
      var pendingSkips = 0;
      var previousUsedForward = false;
      var previousUsedBackward = false;

      for (var column = 0; column < this._macroblockWidth; ++column) {
        if (codingType == MpegPictureDecoder.IntraCoded) {
          MpegVlcTables.MacroblockAddressIncrement.Write(writer, 1);
          MpegVlcTables.IntraMacroblockType.Write(writer, MpegVlcTables.TypeIntra);

          for (var block = 0; block < 6; ++block)
            this._WriteIntraBlock(writer, source, column, row, block, quantiserScaleCode, dcPredictor);

          continue;
        }

        if (codingType == MpegPictureDecoder.PredictiveCoded) {
          var (vectorX, vectorY) = this._SearchMotion(
            source, forwardReference!, column, row, predictedForwardX, predictedForwardY);
          var pattern = this._QuantiseResidual(
            source, forwardReference!, column, row, vectorX, vectorY, quantiserScaleCode, levels);

          if (vectorX == 0 && vectorY == 0 && pattern == 0
              && column != 0 && column != this._macroblockWidth - 1) {
            ++pendingSkips;
            predictedForwardX = 0;
            predictedForwardY = 0;
            continue;
          }

          _WriteAddressIncrement(writer, pendingSkips + 1);
          pendingSkips = 0;

          MpegVlcTables.PredictedMacroblockType.Write(
            writer,
            pattern != 0
              ? MpegVlcTables.TypeMotionForward | MpegVlcTables.TypePattern
              : MpegVlcTables.TypeMotionForward);

          _WriteMotionCode(writer, vectorX - predictedForwardX);
          _WriteMotionCode(writer, vectorY - predictedForwardY);
          predictedForwardX = vectorX;
          predictedForwardY = vectorY;

          _WritePatternAndBlocks(writer, pattern, levels);
          continue;
        }

        var (forwardX, forwardY) = this._SearchMotion(
          source, forwardReference!, column, row, predictedForwardX, predictedForwardY);
        var (backwardX, backwardY) = this._SearchMotion(
          source, backwardReference!, column, row, predictedBackwardX, predictedBackwardY);
        var mode = _ChooseBPrediction(
          source, forwardReference!, backwardReference!, column, row, forwardX, forwardY, backwardX, backwardY);

        var usesForward = mode is BPrediction.Forward or BPrediction.Bidirectional;
        var usesBackward = mode is BPrediction.Backward or BPrediction.Bidirectional;
        var bidirectionalPattern = this._QuantiseBidirectionalResidual(
          source,
          usesForward ? forwardReference : null,
          usesBackward ? backwardReference : null,
          column,
          row,
          forwardX,
          forwardY,
          backwardX,
          backwardY,
          quantiserScaleCode,
          levels);

        // A skipped macroblock of a B picture repeats the previous macroblock's directions and
        // predicts from the vector predictors as they stand (13818-2, 7.6.6), so it can stand in
        // only for one that says exactly that and carries no coefficients. The first and last
        // macroblock of a slice are always coded, and nothing can be repeated before one has been.
        if (bidirectionalPattern == 0
            && column != 0
            && column != this._macroblockWidth - 1
            && usesForward == previousUsedForward
            && usesBackward == previousUsedBackward
            && (!usesForward || (forwardX == predictedForwardX && forwardY == predictedForwardY))
            && (!usesBackward || (backwardX == predictedBackwardX && backwardY == predictedBackwardY))) {
          ++pendingSkips;
          continue;
        }

        _WriteAddressIncrement(writer, pendingSkips + 1);
        pendingSkips = 0;
        previousUsedForward = usesForward;
        previousUsedBackward = usesBackward;

        MpegVlcTables.BidirectionalMacroblockType.Write(
          writer,
          (usesForward ? MpegVlcTables.TypeMotionForward : 0)
          | (usesBackward ? MpegVlcTables.TypeMotionBackward : 0)
          | (bidirectionalPattern != 0 ? MpegVlcTables.TypePattern : 0));

        // Only a direction this macroblock codes moves its predictor: 13818-2 7.6.3.1 leaves the
        // predictor of a direction the macroblock does not use exactly as it stood.
        if (usesForward) {
          _WriteMotionCode(writer, forwardX - predictedForwardX);
          _WriteMotionCode(writer, forwardY - predictedForwardY);
          predictedForwardX = forwardX;
          predictedForwardY = forwardY;
        }

        if (usesBackward) {
          _WriteMotionCode(writer, backwardX - predictedBackwardX);
          _WriteMotionCode(writer, backwardY - predictedBackwardY);
          predictedBackwardX = backwardX;
          predictedBackwardY = backwardY;
        }

        _WritePatternAndBlocks(writer, bidirectionalPattern, levels);
      }
    }

    return writer.ToArray();
  }

  private static void _WritePatternAndBlocks(MpegBitWriter writer, int pattern, int[] levels) {
    if (pattern == 0)
      return;

    MpegVlcTables.CodedBlockPattern.Write(writer, pattern);
    for (var block = 0; block < 6; ++block)
      if ((pattern & (1 << (5 - block))) != 0)
        MpegInterBlockEncoder.Write(writer, levels.AsSpan(block * 64, 64), isMpeg2: true);
  }

  private static void _WriteAddressIncrement(MpegBitWriter writer, int increment) {
    while (increment > 33) {
      MpegVlcTables.MacroblockAddressIncrement.Write(writer, MpegVlcTables.Escape);
      increment -= 33;
    }

    MpegVlcTables.MacroblockAddressIncrement.Write(writer, increment);
  }

  private static void _WriteMotionCode(MpegBitWriter writer, int difference) {
    const int range = 2 * _MOTION_LIMIT;
    if (difference < -_MOTION_LIMIT)
      difference += range;
    else if (difference >= _MOTION_LIMIT)
      difference -= range;

    if (difference == 0) {
      MpegVlcTables.MotionCode.Write(writer, 0);
      return;
    }

    var magnitude = Math.Abs(difference) - 1;
    var code = magnitude / _MOTION_SCALE + 1;
    var residual = magnitude % _MOTION_SCALE;

    MpegVlcTables.MotionCode.Write(writer, difference < 0 ? -code : code);
    writer.WriteBits(residual, _MOTION_F_CODE - 1);
  }

  private void _WriteSequenceHeader(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.SequenceHeader);
    writer.WriteBits(this._width, 12);
    writer.WriteBits(this._height, 12);
    writer.WriteBits(1, 4);
    writer.WriteBits(this._frameRateCode, 4);
    writer.WriteBits(_BIT_RATE_VALUE, 18);
    writer.WriteBit(1);
    writer.WriteBits(_VBV_BUFFER_SIZE_VALUE, 10);
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(0);
  }

  private static void _WriteSequenceExtension(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.Extension);
    writer.WriteBits(1, 4);
    writer.WriteBits(0x48, 8);
    writer.WriteBit(1);
    writer.WriteBits(1, 2);
    writer.WriteBits(0, 2);
    writer.WriteBits(0, 2);
    writer.WriteBits(0, 12);
    writer.WriteBit(1);
    writer.WriteBits(0, 8);
    writer.WriteBit(0); // low_delay = 0: B pictures reorder display against decode order.
    writer.WriteBits(0, 2);
    writer.WriteBits(0, 5);
  }

  private static void _WritePictureHeader(MpegBitWriter writer, int codingType, long displayNumber) {
    writer.WriteStartCode(MpegStartCode.Picture);
    writer.WriteBits((int)(displayNumber & 0x3FF), 10);
    writer.WriteBits(codingType, 3);
    writer.WriteBits(0xFFFF, 16);

    if (codingType is MpegPictureDecoder.PredictiveCoded or MpegPictureDecoder.BidirectionallyCoded) {
      writer.WriteBit(0);
      writer.WriteBits(7, 3);
    }

    if (codingType == MpegPictureDecoder.BidirectionallyCoded) {
      writer.WriteBit(0);
      writer.WriteBits(7, 3);
    }

    writer.WriteBit(0);
  }

  private static void _WritePictureCodingExtension(MpegBitWriter writer, int codingType) {
    writer.WriteStartCode(MpegStartCode.Extension);
    writer.WriteBits(8, 4);

    var hasForward = codingType is MpegPictureDecoder.PredictiveCoded or MpegPictureDecoder.BidirectionallyCoded;
    var hasBackward = codingType == MpegPictureDecoder.BidirectionallyCoded;
    writer.WriteBits(hasForward ? _MOTION_F_CODE : _F_CODE_UNUSED, 4);
    writer.WriteBits(hasForward ? _MOTION_F_CODE : _F_CODE_UNUSED, 4);
    writer.WriteBits(hasBackward ? _MOTION_F_CODE : _F_CODE_UNUSED, 4);
    writer.WriteBits(hasBackward ? _MOTION_F_CODE : _F_CODE_UNUSED, 4);
    writer.WriteBits(0, 2);
    writer.WriteBits(3, 2);
    writer.WriteBit(0);
    writer.WriteBit(1);
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(1);
    writer.WriteBit(1);
    writer.WriteBit(0);
  }

  private (int X, int Y) _SearchMotion(
    MpegFrame source,
    MpegFrame reference,
    int macroblockX,
    int macroblockY,
    int predictedX,
    int predictedY) {
    var originX = macroblockX * 16;
    var originY = macroblockY * 16;

    var best = (X: 0, Y: 0);
    var bestCost = _MatchCost(source, reference, originX, originY, 0, 0, int.MaxValue);

    var centreX = predictedX / 2;
    var centreY = predictedY / 2;

    for (var candidateY = centreY - _SEARCH_RANGE; candidateY <= centreY + _SEARCH_RANGE; ++candidateY)
    for (var candidateX = centreX - _SEARCH_RANGE; candidateX <= centreX + _SEARCH_RANGE; ++candidateX) {
      if (2 * candidateX < -_MOTION_LIMIT || 2 * candidateX >= _MOTION_LIMIT
          || 2 * candidateY < -_MOTION_LIMIT || 2 * candidateY >= _MOTION_LIMIT)
        continue;

      if (!_VectorFits(reference, originX, originY, 2 * candidateX, 2 * candidateY))
        continue;

      var cost = _MatchCost(source, reference, originX, originY, candidateX, candidateY, bestCost);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (2 * candidateX, 2 * candidateY);
    }

    return best;
  }

  private static int _MatchCost(
    MpegFrame source,
    MpegFrame reference,
    int originX,
    int originY,
    int vectorX,
    int vectorY,
    int ceiling) {
    var cost = 0;
    for (var y = 0; y < 16 && cost < ceiling; ++y)
    for (var x = 0; x < 16; ++x)
      cost += Math.Abs(
        _Sample(source.Luma, source.LumaWidth, source.LumaHeight, originX + x, originY + y)
        - _Sample(
          reference.Luma,
          reference.LumaWidth,
          reference.LumaHeight,
          originX + x + vectorX,
          originY + y + vectorY));

    return cost;
  }

  private int _QuantiseResidual(
    MpegFrame source,
    MpegFrame reference,
    int macroblockX,
    int macroblockY,
    int vectorX,
    int vectorY,
    int quantiserScaleCode,
    int[] levels) {
    var quantiserScale = quantiserScaleCode * 2;
    var pattern = 0;

    Span<int> prediction = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    for (var block = 0; block < 6; ++block) {
      var (plane, planeWidth, originX, originY, component) = _BlockOf(source, macroblockX, macroblockY, block);
      var (referencePlane, referenceWidth, referenceHeight) = _PlaneOf(reference, component);
      var (blockVectorX, blockVectorY) = _VectorFor(component, vectorX, vectorY);

      if (!MpegMotionCompensation.TryPredict(
            prediction, 8, 0,
            referencePlane, referenceWidth, 0, referenceWidth, referenceHeight,
            originX, originY, 8, 8, blockVectorX, blockVectorY))
        throw new InvalidDataException(
          $"The motion search chose a vector of ({vectorX}, {vectorY}) half-samples for macroblock "
          + $"({macroblockX}, {macroblockY}), which reads outside the reference picture.");

      for (var index = 0; index < 64; ++index)
        residual[index] = plane[(originY + index / 8) * planeWidth + originX + index % 8] - prediction[index];

      if (MpegInterBlockEncoder.TryQuantise(residual, quantiserScale, isMpeg2: true, levels.AsSpan(block * 64, 64)))
        pattern |= 1 << (5 - block);
    }

    return pattern;
  }

  /// <summary>
  /// Picks which of a B macroblock's two references to spend, by sum of absolute luminance
  /// differences against each prediction and against their average.
  /// </summary>
  /// <remarks>
  /// A B picture that always codes both directions is legal and decodes correctly, and is also a B
  /// picture that has thrown away most of what B pictures are for: a macroblock that a scene change
  /// has made unpredictable from behind still spends a forward vector on a reference it does not
  /// resemble, and pays for its residual as well. Table B.4 gives all three modes and this chooses
  /// between them. The comparison is over luminance alone because chrominance follows the same
  /// vectors at half resolution and cannot overturn the verdict often enough to be worth four more
  /// motion compensations per candidate.
  /// </remarks>
  private static BPrediction _ChooseBPrediction(
    MpegFrame source,
    MpegFrame forwardReference,
    MpegFrame backwardReference,
    int macroblockX,
    int macroblockY,
    int forwardX,
    int forwardY,
    int backwardX,
    int backwardY) {
    var originX = macroblockX * 16;
    var originY = macroblockY * 16;

    Span<int> fromForward = stackalloc int[256];
    Span<int> fromBackward = stackalloc int[256];
    if (!MpegMotionCompensation.TryPredict(
          fromForward, 16, 0,
          forwardReference.Luma, forwardReference.LumaWidth, 0,
          forwardReference.LumaWidth, forwardReference.LumaHeight,
          originX, originY, 16, 16, forwardX, forwardY)
        || !MpegMotionCompensation.TryPredict(
          fromBackward, 16, 0,
          backwardReference.Luma, backwardReference.LumaWidth, 0,
          backwardReference.LumaWidth, backwardReference.LumaHeight,
          originX, originY, 16, 16, backwardX, backwardY))
      throw new InvalidDataException(
        "The MPEG-2 B-picture motion search chose a reference outside the picture for macroblock "
        + $"({macroblockX}, {macroblockY}).");

    var forwardCost = 0;
    var backwardCost = 0;
    var bidirectionalCost = 0;
    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x) {
      var at = y * 16 + x;
      var sample = source.Luma[(originY + y) * source.LumaWidth + originX + x];
      forwardCost += Math.Abs(sample - fromForward[at]);
      backwardCost += Math.Abs(sample - fromBackward[at]);
      bidirectionalCost += Math.Abs(sample - ((fromForward[at] + fromBackward[at] + 1) >> 1));
    }

    if (bidirectionalCost < forwardCost && bidirectionalCost < backwardCost)
      return BPrediction.Bidirectional;

    return backwardCost < forwardCost ? BPrediction.Backward : BPrediction.Forward;
  }

  /// <summary>
  /// Quantises a B macroblock's residual against whichever of its two references it codes.
  /// </summary>
  private int _QuantiseBidirectionalResidual(
    MpegFrame source,
    MpegFrame? forwardReference,
    MpegFrame? backwardReference,
    int macroblockX,
    int macroblockY,
    int forwardX,
    int forwardY,
    int backwardX,
    int backwardY,
    int quantiserScaleCode,
    int[] levels) {
    if (forwardReference == null && backwardReference == null)
      throw new InvalidOperationException("A bidirectionally coded MPEG-2 macroblock must use at least one reference.");

    var quantiserScale = quantiserScaleCode * 2;
    var pattern = 0;

    Span<int> prediction = stackalloc int[64];
    Span<int> backwardPrediction = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    for (var block = 0; block < 6; ++block) {
      var (plane, planeWidth, originX, originY, component) = _BlockOf(source, macroblockX, macroblockY, block);

      // The direction not coded contributes nothing, exactly as in the decoder: one prediction on
      // its own, or both averaged the way H.262 rounds them.
      var target = forwardReference != null ? prediction : backwardPrediction;
      if (forwardReference != null && !_PredictBlock(
            prediction, forwardReference, component, originX, originY, forwardX, forwardY))
        throw _OutOfReference(macroblockX, macroblockY);

      if (backwardReference != null && !_PredictBlock(
            backwardPrediction, backwardReference, component, originX, originY, backwardX, backwardY))
        throw _OutOfReference(macroblockX, macroblockY);

      if (forwardReference != null && backwardReference != null) {
        MpegMotionCompensation.Average(prediction, backwardPrediction);
        target = prediction;
      }

      for (var index = 0; index < 64; ++index)
        residual[index] = plane[(originY + index / 8) * planeWidth + originX + index % 8] - target[index];

      if (MpegInterBlockEncoder.TryQuantise(residual, quantiserScale, isMpeg2: true, levels.AsSpan(block * 64, 64)))
        pattern |= 1 << (5 - block);
    }

    return pattern;
  }

  private static bool _PredictBlock(
    Span<int> destination, MpegFrame reference, int component, int originX, int originY, int vectorX, int vectorY) {
    var (plane, width, height) = _PlaneOf(reference, component);
    var (blockX, blockY) = _VectorFor(component, vectorX, vectorY);
    return MpegMotionCompensation.TryPredict(
      destination, 8, 0, plane, width, 0, width, height, originX, originY, 8, 8, blockX, blockY);
  }

  private static InvalidDataException _OutOfReference(int macroblockX, int macroblockY)
    => new(
      "The MPEG-2 B-picture motion search chose a reference outside the picture for macroblock "
      + $"({macroblockX}, {macroblockY}).");

  private enum BPrediction {
    Forward,
    Backward,
    Bidirectional,
  }

  private static int _Sample(byte[] plane, int width, int height, int x, int y)
    => plane[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];

  private static bool _VectorFits(MpegFrame reference, int originX, int originY, int vectorX, int vectorY) {
    Span<int> prediction = stackalloc int[256];

    for (var component = 0; component < 3; ++component) {
      var (plane, width, height) = _PlaneOf(reference, component);
      var (blockVectorX, blockVectorY) = _VectorFor(component, vectorX, vectorY);
      var size = component == 0 ? 16 : 8;
      var x = component == 0 ? originX : originX / 2;
      var y = component == 0 ? originY : originY / 2;

      if (!MpegMotionCompensation.TryPredict(
            prediction, size, 0,
            plane, width, 0, width, height,
            x, y, size, size, blockVectorX, blockVectorY))
        return false;
    }

    return true;
  }

  private static (byte[] Plane, int Width, int Height) _PlaneOf(MpegFrame frame, int component) => component switch {
    0 => (frame.Luma, frame.LumaWidth, frame.LumaHeight),
    1 => (frame.Cb, frame.ChromaWidth, frame.ChromaHeight),
    _ => (frame.Cr, frame.ChromaWidth, frame.ChromaHeight),
  };

  private static (int X, int Y) _VectorFor(int component, int vectorX, int vectorY)
    => component == 0 ? (vectorX, vectorY) : (vectorX / 2, vectorY / 2);

  private void _WriteIntraBlock(
    MpegBitWriter writer,
    MpegFrame source,
    int macroblockX,
    int macroblockY,
    int blockIndex,
    int quantiserScaleCode,
    int[] dcPredictor) {
    var (plane, planeWidth, originX, originY, component) = _BlockOf(source, macroblockX, macroblockY, blockIndex);

    Span<int> samples = stackalloc int[64];
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        samples[y * 8 + x] = plane[(originY + y) * planeWidth + originX + x];

    Span<double> transformed = stackalloc double[64];
    MpegForwardDct.Transform(samples, transformed);

    var dc = Math.Clamp((int)Math.Round(transformed[0] / 8d, MidpointRounding.AwayFromZero), 0, 255);
    var differential = dc - dcPredictor[component];
    dcPredictor[component] = dc;
    _WriteDc(writer, component != 0, differential);

    var quantiserScale = quantiserScaleCode * 2;
    var run = 0;
    for (var scan = 1; scan < 64; ++scan) {
      var raster = MpegQuantisation.ZigZagScan[scan];
      var weight = MpegQuantisation.DefaultIntraMatrix[raster];
      var level = (int)Math.Round(transformed[raster] * 16d / (weight * quantiserScale), MidpointRounding.AwayFromZero);
      level = Math.Clamp(level, -2047, 2047);
      if (level == 0) {
        ++run;
        continue;
      }

      _WriteCoefficient(writer, run, level);
      run = 0;
    }

    MpegVlcTables.Coefficient.Write(writer, MpegVlcTables.EndOfBlock);
  }

  private static void _WriteDc(MpegBitWriter writer, bool isChroma, int differential) {
    var size = _MagnitudeBits(differential);
    (isChroma ? MpegVlcTables.Mpeg2ChrominanceDcSize : MpegVlcTables.Mpeg2LuminanceDcSize).Write(writer, size);
    if (size == 0)
      return;

    var value = differential >= 0 ? differential : differential + (1 << size) - 1;
    writer.WriteBits(value, size);
  }

  private static void _WriteCoefficient(MpegBitWriter writer, int run, int level) {
    var packed = (run << 8) | Math.Abs(level);
    if (MpegVlcTables.Coefficient.TryWrite(writer, packed)) {
      writer.WriteBit(level < 0 ? 1 : 0);
      return;
    }

    MpegVlcTables.Coefficient.Write(writer, MpegVlcTables.CoefficientEscape);
    writer.WriteBits(run, 6);
    writer.WriteBits((uint)(level & 0xFFF), 12);
  }

  private MpegFrame _ToFrame(RawImage frame) {
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var chromaWidth = this._width >> 1;
    var chromaHeight = this._height >> 1;
    var packed = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, chromaWidth, chromaHeight);
    var target = new MpegFrame(this._codedWidth, this._codedHeight, MpegChromaFormat.Yuv420);

    var lumaSamples = this._width * this._height;
    var chromaSamples = chromaWidth * chromaHeight;
    _PadPlane(packed.AsSpan(0, lumaSamples), this._width, this._height, target.Luma, target.LumaWidth, target.LumaHeight);
    _PadPlane(packed.AsSpan(lumaSamples, chromaSamples), chromaWidth, chromaHeight, target.Cb, target.ChromaWidth, target.ChromaHeight);
    _PadPlane(packed.AsSpan(lumaSamples + chromaSamples, chromaSamples), chromaWidth, chromaHeight, target.Cr, target.ChromaWidth, target.ChromaHeight);

    return target;
  }

  private static void _PadPlane(
    ReadOnlySpan<byte> source,
    int sourceWidth,
    int sourceHeight,
    Span<byte> target,
    int targetWidth,
    int targetHeight) {
    for (var y = 0; y < targetHeight; ++y) {
      var sourceRow = Math.Min(y, sourceHeight - 1);
      var sourceAt = sourceRow * sourceWidth;
      var targetAt = y * targetWidth;
      source.Slice(sourceAt, sourceWidth).CopyTo(target[targetAt..]);
      target.Slice(targetAt + sourceWidth, targetWidth - sourceWidth).Fill(source[sourceAt + sourceWidth - 1]);
    }
  }

  private static (byte[] Plane, int Width, int X, int Y, int Component) _BlockOf(
    MpegFrame frame,
    int macroblockX,
    int macroblockY,
    int blockIndex) {
    if (blockIndex < 4)
      return (
        frame.Luma,
        frame.LumaWidth,
        macroblockX * 16 + (blockIndex & 1) * 8,
        macroblockY * 16 + (blockIndex >> 1) * 8,
        0);

    return blockIndex == 4
      ? (frame.Cb, frame.ChromaWidth, macroblockX * 8, macroblockY * 8, 1)
      : (frame.Cr, frame.ChromaWidth, macroblockX * 8, macroblockY * 8, 2);
  }

  private static int _MagnitudeBits(int value) {
    var magnitude = Math.Abs(value);
    var bits = 0;
    while (magnitude != 0) {
      ++bits;
      magnitude >>= 1;
    }

    return bits;
  }

  private static bool _FitsMainLevel(int byteCount, Rational frameRate)
    => (Int128)byteCount * 8 * frameRate.Numerator <= (Int128)_MAX_BIT_RATE * frameRate.Denominator;

  private static (int Code, Rational Rate) _FrameRateOf(Rational requested) {
    if (!requested.IsKnown)
      return (3, new Rational(25, 1));

    foreach (var candidate in new (int Code, Rational Rate)[] {
               (1, new Rational(24_000, 1_001)),
               (2, new Rational(24, 1)),
               (3, new Rational(25, 1)),
               (4, new Rational(30_000, 1_001)),
               (5, new Rational(30, 1)),
             })
      if ((Int128)requested.Numerator * candidate.Rate.Denominator
          == (Int128)candidate.Rate.Numerator * requested.Denominator)
        return candidate;

    throw new NotSupportedException(
      $"This Main-Level MPEG-2 encoder writes the base frame rates 24000/1001, 24, 25, 30000/1001 and 30 fps; "
      + $"{requested} was requested.");
  }

  private readonly record struct PendingFrame(MpegFrame Source, long DisplayNumber, long? PresentationTimestamp);
}
