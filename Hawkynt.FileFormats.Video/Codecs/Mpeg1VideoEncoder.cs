using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes ISO/IEC 11172-2 MPEG-1 video as progressive 4:2:0 I, P and B pictures.</summary>
/// <remarks>
/// The encoder writes a twelve-picture GOP with two bidirectionally coded pictures between anchors:
/// <c>I B B P B B P B B P B B I</c>. P pictures predict from the previous reconstructed anchor.
/// B pictures choose per macroblock between the previous anchor, the following anchor and the rounded
/// average of both predictions, with independent forward and backward motion vectors.
/// <para/>
/// B pictures are coded after the anchor that follows them in display order. <see cref="TryEncode"/>
/// therefore buffers source pictures and may return a packet belonging to an earlier picture.
/// Presentation timestamps stay with their pictures; decode timestamps consume the input timeline in
/// coding order.
/// <para/>
/// Every anchor is reconstructed by this library's MPEG decoder before it becomes a reference. That
/// keeps encoder and decoder prediction on the same quantised samples and prevents drift across P
/// pictures. B pictures never become references and therefore do not change the anchor state.
/// <para/>
/// Motion search is whole-pixel and exhaustive around the transmitted-vector predictor. MPEG-1 also
/// permits half-pixel vectors; decoding supports them, while this encoder deliberately emits the
/// full-pel form so the search and the transmitted prediction use exactly the samples that were
/// scored. This is an encoder choice, not a restriction on accepted streams.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Mpeg1VideoEncoder : IVideoCodecEncoder<Mpeg1VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MPG1");
  private const int _QUANTISER_SCALE = 8;
  private const int _KEY_FRAME_INTERVAL = 12;
  private const int _B_FRAMES = 2;
  private const int _FORWARD_F_CODE = 2;
  private const int _BACKWARD_F_CODE = 2;
  private const int _MOTION_SCALE = 1 << (_FORWARD_F_CODE - 1);
  private const int _MOTION_LIMIT = 16 * _MOTION_SCALE;
  private const int _SEARCH_RANGE = 15;

  private static readonly IReadOnlyDictionary<int, string> _AddressIncrementCodes =
    MpegVlcTables.MacroblockAddressIncrement.Entries
      .ToDictionary(static entry => entry.Value, static entry => entry.Code);

  private static readonly IReadOnlyDictionary<int, string> _CodedBlockPatternCodes =
    MpegVlcTables.CodedBlockPattern.Entries.ToDictionary(static entry => entry.Value, static entry => entry.Code);

  private static readonly IReadOnlyDictionary<int, string> _MotionCodes =
    MpegVlcTables.MotionCode.Entries.ToDictionary(static entry => entry.Value, static entry => entry.Code);

  private static readonly (int Code, Rational Rate)[] _FrameRates = [
    (1, new(24000, 1001)),
    (2, new(24, 1)),
    (3, new(25, 1)),
    (4, new(30000, 1001)),
    (5, new(30, 1)),
    (6, new(50, 1)),
    (7, new(60000, 1001)),
    (8, new(60, 1)),
  ];

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _frameRateCode;
  private readonly MpegVideoDecoder _reconstruction = new();
  private readonly List<PendingFrame> _pending = [];
  private readonly Queue<CodedPacket> _ready = new();
  private readonly Queue<long?> _decodeTimestamps = new();

  private MediaStreamInfo? _stream;
  private long _displayIndex;
  private bool _wroteSequenceHeader;
  private bool _finished;

  private Mpeg1VideoEncoder(MediaStreamInfo stream, int frameRateCode) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (this._width + 15) / 16;
    this._macroblockHeight = (this._height + 15) / 16;
    this._frameRateCode = frameRateCode;
  }

  public static string CodecName => "MPEG-1 video (ISO/IEC 11172-2)";

  public static CodecTag Codec => _Tag;

  public static Mpeg1VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("MPEG-1 video can only encode a video stream.");

    if (stream.Width is < 1 or > 4095 || stream.Height is < 1 or > 4095)
      throw new NotSupportedException(
        $"MPEG-1 sequence headers carry twelve-bit non-zero dimensions; {stream.Width}x{stream.Height} cannot be stated.");

    var frameRateCode = _FrameRateCode(stream.FrameRate);
    if (frameRateCode == 0)
      throw new NotSupportedException(
        $"MPEG-1 can state 24000/1001, 24, 25, 30000/1001, 30, 50, 60000/1001 or 60 frames/s; "
        + $"the requested rate is {stream.FrameRate}.");

    return new(stream, frameRateCode);
  }

  /// <summary>Accepts one display-order picture and returns the next coding-order packet when available.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (this._finished)
      throw new InvalidOperationException("This MPEG-1 encoder has already been flushed.");

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This MPEG-1 stream is {this._width}x{this._height}; a {frame.Width}x{frame.Height} frame arrived.");

    var pending = new PendingFrame(_PlanesOf(frame), this._displayIndex++, presentationTimestamp);
    this._decodeTimestamps.Enqueue(presentationTimestamp);

    if (this._reconstruction.CurrentAnchor == null)
      this._EncodeAnchor(pending, isIntra: true);
    else {
      this._pending.Add(pending);
      if (this._pending.Count == _B_FRAMES + 1)
        this._EncodeGroup();
    }

    return this._TryTakeReady(out packet);
  }

  /// <summary>
  /// Emits delayed coding-order packets and turns a short tail with no following anchor into P
  /// pictures. The final picture carries the sequence-end start code.
  /// </summary>
  public IEnumerable<CodedPacket> Flush() {
    if (this._finished)
      yield break;

    this._finished = true;

    foreach (var pending in this._pending) {
      var isIntra = pending.DisplayIndex % _KEY_FRAME_INTERVAL == 0;
      this._EncodeAnchor(pending, isIntra);
    }

    this._pending.Clear();

    while (this._ready.Count > 1)
      yield return this._ready.Dequeue();

    if (this._ready.Count == 0)
      yield break;

    var last = this._ready.Dequeue();
    var bytes = new byte[last.Data.Length + 4];
    last.Data.Span.CopyTo(bytes);
    bytes[^4] = 0x00;
    bytes[^3] = 0x00;
    bytes[^2] = 0x01;
    bytes[^1] = MpegStartCode.SequenceEnd;
    yield return last with { Data = bytes };
  }

  public MediaStreamInfo DescribeStream()
    => this._stream ??= new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MPEG1",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 12,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };

  private bool _TryTakeReady(out CodedPacket packet) {
    if (this._ready.Count > 1) {
      packet = this._ready.Dequeue();
      return true;
    }

    packet = default;
    return false;
  }

  private void _EncodeGroup() {
    var oldAnchor = this._reconstruction.CurrentAnchor!;
    var next = this._pending[^1];
    var isIntra = next.DisplayIndex % _KEY_FRAME_INTERVAL == 0;

    this._EncodeAnchor(next, isIntra);
    var newAnchor = this._reconstruction.CurrentAnchor!;

    for (var index = 0; index < _B_FRAMES; ++index) {
      var between = this._pending[index];
      var writer = new MpegBitWriter();
      this._WritePicture(
        writer,
        between.Source,
        MpegPictureDecoder.BidirectionallyCoded,
        oldAnchor,
        newAnchor,
        between.DisplayIndex);
      this._ready.Enqueue(this._Packet(writer.ToArray(), between.PresentationTimestamp, isKeyFrame: false));
    }

    this._pending.Clear();
  }

  private void _EncodeAnchor(PendingFrame pending, bool isIntra) {
    var reference = isIntra ? null : this._reconstruction.CurrentAnchor;
    if (!isIntra && reference == null)
      isIntra = true;

    var writer = new MpegBitWriter();
    if (!this._wroteSequenceHeader) {
      this._WriteSequenceHeader(writer);
      this._wroteSequenceHeader = true;
    }

    this._WritePicture(
      writer,
      pending.Source,
      isIntra ? MpegPictureDecoder.IntraCoded : MpegPictureDecoder.PredictiveCoded,
      reference,
      backwardReference: null,
      pending.DisplayIndex);

    var bytes = writer.ToArray();
    this._reconstruction.DecodePacket(bytes);
    while (this._reconstruction.TryTakeReady(out _)) { }

    this._ready.Enqueue(this._Packet(bytes, pending.PresentationTimestamp, isIntra));
  }

  private CodedPacket _Packet(byte[] bytes, long? presentationTimestamp, bool isKeyFrame) {
    var decodeTimestamp = this._decodeTimestamps.Count == 0 ? null : this._decodeTimestamps.Dequeue();
    return new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: decodeTimestamp,
      Duration: 1,
      IsKeyFrame: isKeyFrame);
  }

  private void _WriteSequenceHeader(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.SequenceHeader);
    writer.WriteBits(this._width, 12);
    writer.WriteBits(this._height, 12);
    writer.WriteBits(1, 4);
    writer.WriteBits(this._frameRateCode, 4);
    writer.WriteBits(0x3FFFF, 18);
    writer.WriteBit(1);
    writer.WriteBits(0x3FF, 10);
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(0);
  }

  private void _WritePicture(
    MpegBitWriter writer,
    Yuv420Planes planes,
    int codingType,
    MpegFrame? forwardReference,
    MpegFrame? backwardReference,
    long displayIndex) {
    writer.WriteStartCode(MpegStartCode.Picture);
    writer.WriteBits((int)(displayIndex & 0x3FF), 10);
    writer.WriteBits(codingType, 3);
    writer.WriteBits(0xFFFF, 16);

    if (codingType is MpegPictureDecoder.PredictiveCoded or MpegPictureDecoder.BidirectionallyCoded) {
      writer.WriteBit(1);
      writer.WriteBits(_FORWARD_F_CODE, 3);
    }

    if (codingType == MpegPictureDecoder.BidirectionallyCoded) {
      writer.WriteBit(1);
      writer.WriteBits(_BACKWARD_F_CODE, 3);
    }

    writer.WriteBit(0);
    writer.WriteStartCode(MpegStartCode.FirstSlice);
    writer.WriteBits(_QUANTISER_SCALE, 5);
    writer.WriteBit(0);

    Span<int> block = stackalloc int[64];
    Span<int> levels = stackalloc int[64 * 6];
    var dcY = 128;
    var dcCb = 128;
    var dcCr = 128;
    var forwardPredictorX = 0;
    var forwardPredictorY = 0;
    var backwardPredictorX = 0;
    var backwardPredictorY = 0;
    var pendingSkips = 0;
    var previousUsedForward = false;
    var previousUsedBackward = false;
    var lastAddress = this._macroblockWidth * this._macroblockHeight - 1;

    for (var address = 0; address <= lastAddress; ++address) {
      var macroblockY = address / this._macroblockWidth;
      var macroblockX = address % this._macroblockWidth;

      if (codingType == MpegPictureDecoder.IntraCoded) {
        writer.WriteCode("1");
        writer.WriteCode("1");
        _WriteIntraBlocks(writer, planes, macroblockX, macroblockY, block, ref dcY, ref dcCb, ref dcCr);
        continue;
      }

      if (codingType == MpegPictureDecoder.PredictiveCoded) {
        var reference = forwardReference!;
        var (vectorX, vectorY) = _SearchMotion(
          planes, reference, macroblockX, macroblockY, forwardPredictorX, forwardPredictorY);
        var forwardPrediction = new Prediction(reference, vectorX, vectorY, null, 0, 0);
        var forwardPattern = _QuantiseResidual(planes, forwardPrediction, macroblockX, macroblockY, block, levels);

        if (vectorX == 0 && vectorY == 0 && forwardPattern == 0 && address != 0 && address != lastAddress) {
          ++pendingSkips;
          forwardPredictorX = 0;
          forwardPredictorY = 0;
          continue;
        }

        _WriteAddressIncrement(writer, pendingSkips + 1);
        pendingSkips = 0;
        writer.WriteCode(forwardPattern != 0 ? "1" : "001");
        _WriteMotionCode(writer, vectorX - forwardPredictorX, _FORWARD_F_CODE);
        _WriteMotionCode(writer, vectorY - forwardPredictorY, _FORWARD_F_CODE);
        forwardPredictorX = vectorX;
        forwardPredictorY = vectorY;
        _WriteInterBlocks(writer, forwardPattern, levels);
        continue;
      }

      var forward = forwardReference!;
      var backward = backwardReference!;
      var forwardVector = _SearchMotion(
        planes, forward, macroblockX, macroblockY, forwardPredictorX, forwardPredictorY);
      var backwardVector = _SearchMotion(
        planes, backward, macroblockX, macroblockY, backwardPredictorX, backwardPredictorY);

      var mode = _ChooseBPrediction(
        planes,
        forward,
        backward,
        macroblockX,
        macroblockY,
        forwardVector,
        backwardVector);

      var prediction = mode switch {
        BPrediction.Forward => new Prediction(forward, forwardVector.X, forwardVector.Y, null, 0, 0),
        BPrediction.Backward => new Prediction(null, 0, 0, backward, backwardVector.X, backwardVector.Y),
        _ => new Prediction(
          forward, forwardVector.X, forwardVector.Y,
          backward, backwardVector.X, backwardVector.Y),
      };

      var pattern = _QuantiseResidual(planes, prediction, macroblockX, macroblockY, block, levels);

      var usesForward = mode is BPrediction.Forward or BPrediction.Bidirectional;
      var usesBackward = mode is BPrediction.Backward or BPrediction.Bidirectional;

      // A skipped macroblock of a B picture repeats the previous macroblock's direction and is
      // predicted from the vector predictors as they stand (2.4.4.4), so it can only stand in for
      // one that says exactly that and carries no coefficients. The first and last macroblock of a
      // slice are always coded, and nothing can be repeated before a macroblock has been coded.
      if (pattern == 0
          && address != 0
          && address != lastAddress
          && usesForward == previousUsedForward
          && usesBackward == previousUsedBackward
          && (!usesForward || (forwardVector.X == forwardPredictorX && forwardVector.Y == forwardPredictorY))
          && (!usesBackward || (backwardVector.X == backwardPredictorX && backwardVector.Y == backwardPredictorY))) {
        ++pendingSkips;
        continue;
      }

      _WriteAddressIncrement(writer, pendingSkips + 1);
      pendingSkips = 0;
      previousUsedForward = usesForward;
      previousUsedBackward = usesBackward;
      writer.WriteCode((usesForward, usesBackward, pattern != 0) switch {
        (true, true, true) => "11",
        (true, true, false) => "10",
        (false, true, true) => "011",
        (false, true, false) => "010",
        (true, false, true) => "0011",
        (true, false, false) => "0010",
        _ => throw new InvalidOperationException("An MPEG-1 B macroblock must use at least one reference."),
      });

      if (usesForward) {
        _WriteMotionCode(writer, forwardVector.X - forwardPredictorX, _FORWARD_F_CODE);
        _WriteMotionCode(writer, forwardVector.Y - forwardPredictorY, _FORWARD_F_CODE);
        forwardPredictorX = forwardVector.X;
        forwardPredictorY = forwardVector.Y;
      }

      if (usesBackward) {
        _WriteMotionCode(writer, backwardVector.X - backwardPredictorX, _BACKWARD_F_CODE);
        _WriteMotionCode(writer, backwardVector.Y - backwardPredictorY, _BACKWARD_F_CODE);
        backwardPredictorX = backwardVector.X;
        backwardPredictorY = backwardVector.Y;
      }

      _WriteInterBlocks(writer, pattern, levels);
    }
  }

  private static void _WriteInterBlocks(MpegBitWriter writer, int pattern, ReadOnlySpan<int> levels) {
    if (pattern == 0)
      return;

    writer.WriteCode(_CodedBlockPatternCodes[pattern]);
    for (var index = 0; index < 6; ++index)
      if ((pattern & (1 << (5 - index))) != 0)
        MpegInterBlockEncoder.Write(writer, levels.Slice(index * 64, 64), isMpeg2: false);
  }

  private static void _WriteAddressIncrement(MpegBitWriter writer, int increment) {
    while (increment > 33) {
      writer.WriteCode(_AddressIncrementCodes[MpegVlcTables.Escape]);
      increment -= 33;
    }

    writer.WriteCode(_AddressIncrementCodes[increment]);
  }

  private static void _WriteIntraBlocks(
    MpegBitWriter writer,
    Yuv420Planes planes,
    int macroblockX,
    int macroblockY,
    scoped Span<int> block,
    ref int dcY,
    ref int dcCb,
    ref int dcCr) {
    for (var index = 0; index < 4; ++index) {
      var x = macroblockX * 16 + (index & 1) * 8;
      var y = macroblockY * 16 + (index >> 1) * 8;
      _ReadBlock(planes.Y, planes.YWidth, planes.YHeight, x, y, block);
      Mpeg1BlockEncoder.Write(writer, block, isChroma: false, _QUANTISER_SCALE, ref dcY);
    }

    var chromaX = macroblockX * 8;
    var chromaY = macroblockY * 8;
    _ReadBlock(planes.Cb, planes.ChromaWidth, planes.ChromaHeight, chromaX, chromaY, block);
    Mpeg1BlockEncoder.Write(writer, block, isChroma: true, _QUANTISER_SCALE, ref dcCb);
    _ReadBlock(planes.Cr, planes.ChromaWidth, planes.ChromaHeight, chromaX, chromaY, block);
    Mpeg1BlockEncoder.Write(writer, block, isChroma: true, _QUANTISER_SCALE, ref dcCr);
  }

  private static (int X, int Y) _SearchMotion(
    Yuv420Planes planes, MpegFrame reference, int macroblockX, int macroblockY, int predictedX, int predictedY) {
    var originX = macroblockX * 16;
    var originY = macroblockY * 16;
    var best = (X: 0, Y: 0);
    var bestCost = _MatchCost(planes, reference, originX, originY, 0, 0, int.MaxValue);

    for (var candidateY = predictedY - _SEARCH_RANGE; candidateY <= predictedY + _SEARCH_RANGE; ++candidateY)
    for (var candidateX = predictedX - _SEARCH_RANGE; candidateX <= predictedX + _SEARCH_RANGE; ++candidateX) {
      if (candidateX < -_MOTION_LIMIT || candidateX >= _MOTION_LIMIT
          || candidateY < -_MOTION_LIMIT || candidateY >= _MOTION_LIMIT)
        continue;

      var sourceX = originX + candidateX;
      var sourceY = originY + candidateY;
      if (sourceX < 0 || sourceY < 0
          || sourceX + 16 > reference.LumaWidth || sourceY + 16 > reference.LumaHeight)
        continue;

      var cost = _MatchCost(planes, reference, originX, originY, candidateX, candidateY, bestCost);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (candidateX, candidateY);
    }

    return best;
  }

  private static int _MatchCost(
    Yuv420Planes planes, MpegFrame reference, int originX, int originY, int vectorX, int vectorY, int ceiling) {
    var cost = 0;
    for (var y = 0; y < 16 && cost < ceiling; ++y)
    for (var x = 0; x < 16; ++x)
      cost += Math.Abs(
        _Sample(planes.Y, planes.YWidth, planes.YHeight, originX + x, originY + y)
        - reference.Luma[(originY + y + vectorY) * reference.LumaWidth + originX + x + vectorX]);

    return cost;
  }

  private static BPrediction _ChooseBPrediction(
    Yuv420Planes planes,
    MpegFrame forward,
    MpegFrame backward,
    int macroblockX,
    int macroblockY,
    (int X, int Y) forwardVector,
    (int X, int Y) backwardVector) {
    var originX = macroblockX * 16;
    var originY = macroblockY * 16;
    var forwardCost = 0;
    var backwardCost = 0;
    var bidirectionalCost = 0;

    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x) {
      var source = _Sample(planes.Y, planes.YWidth, planes.YHeight, originX + x, originY + y);
      var fromForward = forward.Luma[
        (originY + y + forwardVector.Y) * forward.LumaWidth + originX + x + forwardVector.X];
      var fromBackward = backward.Luma[
        (originY + y + backwardVector.Y) * backward.LumaWidth + originX + x + backwardVector.X];

      forwardCost += Math.Abs(source - fromForward);
      backwardCost += Math.Abs(source - fromBackward);
      bidirectionalCost += Math.Abs(source - ((fromForward + fromBackward + 1) >> 1));
    }

    if (bidirectionalCost < forwardCost && bidirectionalCost < backwardCost)
      return BPrediction.Bidirectional;

    return backwardCost < forwardCost ? BPrediction.Backward : BPrediction.Forward;
  }

  private static int _QuantiseResidual(
    Yuv420Planes planes,
    Prediction prediction,
    int macroblockX,
    int macroblockY,
    scoped Span<int> block,
    scoped Span<int> levels) {
    Span<int> predictedY = stackalloc int[16 * 16];
    Span<int> predictedCb = stackalloc int[8 * 8];
    Span<int> predictedCr = stackalloc int[8 * 8];
    _FormPrediction(predictedY, prediction, component: 0, macroblockX, macroblockY);
    _FormPrediction(predictedCb, prediction, component: 1, macroblockX, macroblockY);
    _FormPrediction(predictedCr, prediction, component: 2, macroblockX, macroblockY);

    var pattern = 0;
    for (var index = 0; index < 4; ++index) {
      var sourceX = macroblockX * 16 + (index & 1) * 8;
      var sourceY = macroblockY * 16 + (index >> 1) * 8;
      var predictionX = (index & 1) * 8;
      var predictionY = (index >> 1) * 8;
      _ReadResidual(
        planes.Y, planes.YWidth, planes.YHeight,
        sourceX, sourceY,
        predictedY, predictionStride: 16, predictionX, predictionY,
        block);
      if (MpegInterBlockEncoder.TryQuantise(block, _QUANTISER_SCALE, isMpeg2: false, levels.Slice(index * 64, 64)))
        pattern |= 1 << (5 - index);
    }

    var chromaX = macroblockX * 8;
    var chromaY = macroblockY * 8;
    _ReadResidual(
      planes.Cb, planes.ChromaWidth, planes.ChromaHeight,
      chromaX, chromaY,
      predictedCb, predictionStride: 8, predictionX: 0, predictionY: 0,
      block);
    if (MpegInterBlockEncoder.TryQuantise(block, _QUANTISER_SCALE, isMpeg2: false, levels.Slice(4 * 64, 64)))
      pattern |= 1 << 1;

    _ReadResidual(
      planes.Cr, planes.ChromaWidth, planes.ChromaHeight,
      chromaX, chromaY,
      predictedCr, predictionStride: 8, predictionX: 0, predictionY: 0,
      block);
    if (MpegInterBlockEncoder.TryQuantise(block, _QUANTISER_SCALE, isMpeg2: false, levels.Slice(5 * 64, 64)))
      pattern |= 1;

    return pattern;
  }

  private static void _FormPrediction(
    Span<int> destination,
    Prediction prediction,
    int component,
    int macroblockX,
    int macroblockY) {
    var tileSize = component == 0 ? 16 : 8;
    var hasForward = prediction.Forward != null;
    var hasBackward = prediction.Backward != null;

    if (hasForward)
      _PredictFrom(
        destination,
        prediction.Forward!,
        prediction.ForwardX,
        prediction.ForwardY,
        component,
        macroblockX,
        macroblockY,
        tileSize);

    if (!hasBackward)
      return;

    Span<int> backward = stackalloc int[16 * 16];
    var backwardTile = backward[..(tileSize * tileSize)];
    _PredictFrom(
      backwardTile,
      prediction.Backward!,
      prediction.BackwardX,
      prediction.BackwardY,
      component,
      macroblockX,
      macroblockY,
      tileSize);

    if (!hasForward) {
      backwardTile.CopyTo(destination);
      return;
    }

    MpegMotionCompensation.Average(destination, backwardTile);
  }

  private static void _PredictFrom(
    Span<int> destination,
    MpegFrame reference,
    int vectorX,
    int vectorY,
    int component,
    int macroblockX,
    int macroblockY,
    int tileSize) {
    var (plane, planeWidth, planeHeight) = reference.PlaneOf(component);
    var blockX = macroblockX * tileSize;
    var blockY = macroblockY * tileSize;
    var halfPelX = component == 0 ? vectorX * 2 : vectorX;
    var halfPelY = component == 0 ? vectorY * 2 : vectorY;
    if (!MpegMotionCompensation.TryPredict(
          destination,
          tileSize,
          0,
          plane,
          planeWidth,
          0,
          planeWidth,
          planeHeight,
          blockX,
          blockY,
          tileSize,
          tileSize,
          halfPelX,
          halfPelY))
      throw new InvalidOperationException(
        $"The encoder selected a motion vector ({vectorX}, {vectorY}) that falls outside its reconstructed reference.");
  }

  private static void _ReadResidual(
    byte[] source,
    int sourceWidth,
    int sourceHeight,
    int sourceX,
    int sourceY,
    ReadOnlySpan<int> prediction,
    int predictionStride,
    int predictionX,
    int predictionY,
    scoped Span<int> block) {
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      block[y * 8 + x] =
        _Sample(source, sourceWidth, sourceHeight, sourceX + x, sourceY + y)
        - prediction[(predictionY + y) * predictionStride + predictionX + x];
  }

  private static void _WriteMotionCode(MpegBitWriter writer, int difference, int fCode) {
    var motionScale = 1 << (fCode - 1);
    var motionLimit = 16 * motionScale;
    var range = 2 * motionLimit;
    if (difference < -motionLimit)
      difference += range;
    else if (difference >= motionLimit)
      difference -= range;

    if (difference == 0) {
      writer.WriteCode(_MotionCodes[0]);
      return;
    }

    var magnitude = Math.Abs(difference) - 1;
    var code = magnitude / motionScale + 1;
    var residual = magnitude % motionScale;
    writer.WriteCode(_MotionCodes[difference < 0 ? -code : code]);
    if (motionScale > 1)
      writer.WriteBits(residual, fCode - 1);
  }

  private static int _Sample(byte[] plane, int width, int height, int x, int y)
    => plane[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];

  private static void _ReadBlock(
    byte[] plane, int width, int height, int originX, int originY, scoped Span<int> block) {
    for (var y = 0; y < 8; ++y) {
      var sourceY = Math.Min(originY + y, height - 1);
      for (var x = 0; x < 8; ++x) {
        var sourceX = Math.Min(originX + x, width - 1);
        block[y * 8 + x] = plane[sourceY * width + sourceX];
      }
    }
  }

  private static Yuv420Planes _PlanesOf(RawImage frame) {
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var source = frame.Format == PixelFormat.Yuv444P8
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited);

    var width = frame.Width;
    var height = frame.Height;
    var y = source.GetPlaneData(0)[..(width * height)].ToArray();
    var sourceCb = source.GetPlaneData(1);
    var sourceCr = source.GetPlaneData(2);
    var chromaWidth = (width + 1) / 2;
    var chromaHeight = (height + 1) / 2;
    var cb = new byte[chromaWidth * chromaHeight];
    var cr = new byte[cb.Length];

    for (var cy = 0; cy < chromaHeight; ++cy)
    for (var cx = 0; cx < chromaWidth; ++cx) {
      var sumCb = 0;
      var sumCr = 0;
      var count = 0;

      for (var dy = 0; dy < 2; ++dy) {
        var sy = cy * 2 + dy;
        if (sy >= height)
          continue;

        for (var dx = 0; dx < 2; ++dx) {
          var sx = cx * 2 + dx;
          if (sx >= width)
            continue;

          var at = sy * width + sx;
          sumCb += sourceCb[at];
          sumCr += sourceCr[at];
          ++count;
        }
      }

      var target = cy * chromaWidth + cx;
      cb[target] = (byte)((sumCb + count / 2) / count);
      cr[target] = (byte)((sumCr + count / 2) / count);
    }

    return new(y, cb, cr, width, height, chromaWidth, chromaHeight);
  }

  private static int _FrameRateCode(Rational rate) {
    if (!rate.IsKnown)
      return 0;

    foreach (var (code, candidate) in _FrameRates)
      if ((Int128)rate.Numerator * candidate.Denominator == (Int128)candidate.Numerator * rate.Denominator)
        return code;

    return 0;
  }

  private enum BPrediction {
    Forward,
    Backward,
    Bidirectional,
  }

  private sealed record Yuv420Planes(
    byte[] Y,
    byte[] Cb,
    byte[] Cr,
    int YWidth,
    int YHeight,
    int ChromaWidth,
    int ChromaHeight);

  private readonly record struct PendingFrame(
    Yuv420Planes Source,
    long DisplayIndex,
    long? PresentationTimestamp);

  private readonly record struct Prediction(
    MpegFrame? Forward,
    int ForwardX,
    int ForwardY,
    MpegFrame? Backward,
    int BackwardX,
    int BackwardY);
}
