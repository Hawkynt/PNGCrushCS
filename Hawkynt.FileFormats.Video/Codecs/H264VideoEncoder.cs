using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H264;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes progressive eight-bit 4:2:0 H.264 / AVC as Main-profile CAVLC I/P/B pictures.
/// </summary>
/// <remarks>
/// The write path is deliberately exact before it is clever. The first picture is an IDR made from
/// <c>I_PCM</c> macroblocks. Reference pictures use <c>P_Skip</c> wherever the macroblock equals the
/// previous reconstructed reference and <c>I_PCM</c> otherwise. One non-reference B picture is placed
/// between reference anchors; a macroblock that is exactly the rounded average of the two zero-motion
/// references is coded as <c>B_Bi_16x16</c>, with <c>I_PCM</c> as its exact fallback. This exercises the
/// real decoded-picture buffer, both reference lists and display/decode reordering without allowing a
/// lossy transform/quantizer to contaminate later reference pictures.
/// <para/>
/// Samples are emitted in the length-prefixed representation used by MP4, Matroska and FLV, with the
/// SPS/PPS in an <c>AVCDecoderConfigurationRecord</c>. <c>H264VideoWriter</c> converts that same stream
/// description and those packets to Annex B when a raw <c>.264</c> stream is requested.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H264VideoEncoder : IVideoCodecEncoder<H264VideoEncoder> {

  private const int _PROFILE_IDC = 77; // Main
  private const int _PROFILE_COMPATIBILITY = 0;
  private const int _LEVEL_IDC = 62;
  private const int _MAX_LEVEL_62_MACROBLOCKS = 139_264;
  private const int _MAX_LEVEL_62_DIMENSION_MBS = 1_055;
  private const int _NAL_LENGTH_SIZE = 4;
  private const int _FRAME_NUM_BITS = 16;
  private const int _POC_BITS = 16;
  private const int _FRAME_NUM_MASK = (1 << _FRAME_NUM_BITS) - 1;
  private const int _POC_MASK = (1 << _POC_BITS) - 1;
  private const int _QP = 18;
  private const int _IDR_INTERVAL = 120;
  private const int _INTEGER_SEARCH_RANGE = 16;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("avc1");

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly byte[] _sequenceParameterSet;
  private readonly byte[] _pictureParameterSet;
  private readonly byte[] _configuration;
  private readonly byte[] _sampleEntry;
  private readonly Queue<CodedPacket> _readyPackets = [];

  private MediaStreamInfo? _stream;
  private Frame420? _previousReference;
  private PendingFrame? _pendingB;
  private int _displayIndex;
  private int _gopBaseDisplayIndex;
  private int _lastReferenceFrameNum;

  private H264VideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._codedWidth = (this._width + 15) & ~15;
    this._codedHeight = (this._height + 15) & ~15;
    this._macroblockWidth = this._codedWidth / 16;
    this._macroblockHeight = this._codedHeight / 16;

    this._sequenceParameterSet = this._SequenceParameterSet();
    this._pictureParameterSet = _PictureParameterSet();
    this._configuration = _DecoderConfiguration(this._sequenceParameterSet, this._pictureParameterSet);
    this._sampleEntry = this._AvcSampleEntry();
  }

  public static string CodecName => "H.264/AVC (ITU-T H.264 | ISO/IEC 14496-10)";

  public static CodecTag Codec => _Tag;

  public static H264VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("H.264 can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(stream),
        $"H.264 needs a positive picture size; {stream.Width}x{stream.Height} was requested.");

    // Progressive 4:2:0 has a 2x2 chroma crop unit. Odd display dimensions therefore cannot be
    // represented without changing the sampling grid this encoder promises.
    if ((stream.Width & 1) != 0 || (stream.Height & 1) != 0)
      throw new NotSupportedException(
        $"This H.264 encoder writes progressive 4:2:0, whose chroma and frame-crop grids are 2x2; "
        + $"{stream.Width}x{stream.Height} cannot be represented exactly. Both dimensions must be even.");

    var macroblockWidth = (stream.Width + 15L) / 16;
    var macroblockHeight = (stream.Height + 15L) / 16;
    var macroblocks = macroblockWidth * macroblockHeight;
    if (macroblocks > _MAX_LEVEL_62_MACROBLOCKS
        || macroblockWidth > _MAX_LEVEL_62_DIMENSION_MBS
        || macroblockHeight > _MAX_LEVEL_62_DIMENSION_MBS)
      throw new NotSupportedException(
        $"This H.264 encoder writes level 6.2; {stream.Width}x{stream.Height} needs "
        + $"{macroblockWidth}x{macroblockHeight} macroblocks ({macroblocks} total), outside that level's picture bounds.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    this._ValidateGeometry(frame);

    var current = new PendingFrame(this._To420(frame), presentationTimestamp, this._displayIndex++);
    if (this._previousReference == null) {
      this._StartIdr(current);
    } else if (current.DisplayIndex - this._gopBaseDisplayIndex >= _IDR_INTERVAL) {
      // Do not let a B picture depend across an IDR boundary. If one display picture is waiting for
      // a future anchor, promote it to P first, then close the old GOP before the new IDR.
      if (this._pendingB != null) {
        var trailingFrameNum = _NextFrameNum(this._lastReferenceFrameNum);
        var trailingPacket = this._EncodeP(
          this._pendingB, trailingFrameNum, this._pendingB.PresentationTimestamp, out var trailingReference);
        this._readyPackets.Enqueue(trailingPacket);
        this._previousReference = trailingReference;
        this._lastReferenceFrameNum = trailingFrameNum;
        this._pendingB = null;
      }

      this._StartIdr(current);
    } else if (this._pendingB == null) {
      this._pendingB = current;
    } else {
      var b = this._pendingB;
      var referenceFrameNum = _NextFrameNum(this._lastReferenceFrameNum);
      var pPacket = this._EncodeP(current, referenceFrameNum, b.PresentationTimestamp, out var futureReference);
      this._readyPackets.Enqueue(pPacket);
      this._readyPackets.Enqueue(this._EncodeB(
        b,
        this._previousReference,
        futureReference,
        _NextFrameNum(referenceFrameNum),
        current.PresentationTimestamp));
      this._previousReference = futureReference;
      this._lastReferenceFrameNum = referenceFrameNum;
      this._pendingB = null;
    }

    if (this._readyPackets.Count == 0) {
      packet = default;
      return false;
    }

    packet = this._readyPackets.Dequeue();
    return true;
  }

  public IEnumerable<CodedPacket> Flush() {
    while (this._readyPackets.Count > 0)
      yield return this._readyPackets.Dequeue();

    if (this._pendingB == null)
      yield break;

    var trailing = this._pendingB;
    this._pendingB = null;
    var frameNum = _NextFrameNum(this._lastReferenceFrameNum);
    yield return this._EncodeP(trailing, frameNum, trailing.PresentationTimestamp, out var reference);
    this._previousReference = reference;
    this._lastReferenceFrameNum = frameNum;
  }

  private void _StartIdr(PendingFrame frame) {
    this._gopBaseDisplayIndex = frame.DisplayIndex;
    this._readyPackets.Enqueue(this._EncodeIdr(frame));
    this._previousReference = frame.Samples;
    this._lastReferenceFrameNum = 0;
    this._pendingB = null;
  }

  private static int _NextFrameNum(int previousReferenceFrameNum) => (previousReferenceFrameNum + 1) & _FRAME_NUM_MASK;

  public MediaStreamInfo DescribeStream()
    => this._stream ??= new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MPEG4/ISO/AVC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 12,
      Name = this._requested.Name,
      Language = this._requested.Language,
      CodecPrivateData = this._sampleEntry,
    };

  private void _ValidateGeometry(RawImage frame) {
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This H.264 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");
  }

  private CodedPacket _EncodeIdr(PendingFrame frame) {
    var rbsp = new H264BitWriter();
    this._WriteSliceHeader(rbsp, SliceKind.I, frame.DisplayIndex, frameNum: 0, idr: true);

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX)
        this._WritePcmMacroblock(rbsp, frame.Samples, mbX, mbY, 25);

    return this._Packet(0x65, rbsp, frame, frame.PresentationTimestamp, keyFrame: true);
  }

  private CodedPacket _EncodeP(
    PendingFrame frame,
    int frameNum,
    long? decodeTimestamp,
    out Frame420 reconstructed) {
    var reference = this._previousReference!;
    var rbsp = new H264BitWriter();
    this._WriteSliceHeader(rbsp, SliceKind.P, frame.DisplayIndex, frameNum, idr: false);

    reconstructed = this._EmptyFrame();
    var residualContext = new ResidualContext(this._macroblockWidth, this._macroblockHeight);
    var vectors = new MotionVector[this._macroblockWidth * this._macroblockHeight];
    var usesList0 = new bool[vectors.Length];

    for (var mbAddr = 0; mbAddr < vectors.Length; ++mbAddr) {
      rbsp.WriteUnsignedExpGolomb(0); // mb_skip_run: explicitly code every P macroblock.
      rbsp.WriteUnsignedExpGolomb(0); // P_L0_16x16

      var mbX = mbAddr % this._macroblockWidth;
      var mbY = mbAddr / this._macroblockWidth;
      var predictor = _PredictMotion(vectors, usesList0, mbAddr, this._macroblockWidth, this._macroblockHeight);
      var motion = this._SearchMotion(frame.Samples, reference, mbX, mbY, predictor);
      rbsp.WriteSignedExpGolomb(motion.X - predictor.X);
      rbsp.WriteSignedExpGolomb(motion.Y - predictor.Y);

      Span<byte> predY = stackalloc byte[16 * 16];
      Span<byte> predCb = stackalloc byte[8 * 8];
      Span<byte> predCr = stackalloc byte[8 * 8];
      this._PredictMacroblock(reference, mbX, mbY, motion, predY, predCb, predCr);
      var residual = this._QuantizeMacroblock(
        frame.Samples, predY, predCb, predCr, mbX, mbY, reconstructed);
      this._WriteInterResidual(rbsp, residual, residualContext, mbX, mbY);

      vectors[mbAddr] = motion;
      usesList0[mbAddr] = true;
    }

    return this._Packet(0x41, rbsp, frame, decodeTimestamp, keyFrame: false);
  }

  private CodedPacket _EncodeB(
    PendingFrame frame,
    Frame420 previous,
    Frame420 future,
    int frameNum,
    long? decodeTimestamp) {
    var rbsp = new H264BitWriter();
    this._WriteSliceHeader(rbsp, SliceKind.B, frame.DisplayIndex, frameNum, idr: false);

    var count = this._macroblockWidth * this._macroblockHeight;
    var vectors0 = new MotionVector[count];
    var vectors1 = new MotionVector[count];
    var usesList0 = new bool[count];
    var usesList1 = new bool[count];
    var residualContext = new ResidualContext(this._macroblockWidth, this._macroblockHeight);
    var reconstructed = this._EmptyFrame();

    for (var mbAddr = 0; mbAddr < count; ++mbAddr) {
      rbsp.WriteUnsignedExpGolomb(0); // mb_skip_run: explicit L0/L1/Bi motion follows.
      var mbX = mbAddr % this._macroblockWidth;
      var mbY = mbAddr / this._macroblockWidth;
      var predictor0 = _PredictMotion(vectors0, usesList0, mbAddr, this._macroblockWidth, this._macroblockHeight);
      var predictor1 = _PredictMotion(vectors1, usesList1, mbAddr, this._macroblockWidth, this._macroblockHeight);
      var motion0 = this._SearchMotion(frame.Samples, previous, mbX, mbY, predictor0);
      var motion1 = this._SearchMotion(frame.Samples, future, mbX, mbY, predictor1);

      Span<byte> y0 = stackalloc byte[16 * 16];
      Span<byte> cb0 = stackalloc byte[8 * 8];
      Span<byte> cr0 = stackalloc byte[8 * 8];
      Span<byte> y1 = stackalloc byte[16 * 16];
      Span<byte> cb1 = stackalloc byte[8 * 8];
      Span<byte> cr1 = stackalloc byte[8 * 8];
      this._PredictMacroblock(previous, mbX, mbY, motion0, y0, cb0, cr0);
      this._PredictMacroblock(future, mbX, mbY, motion1, y1, cb1, cr1);

      Span<byte> biY = stackalloc byte[16 * 16];
      _Average(y0, y1, biY);
      var cost0 = this._PredictionSad(frame.Samples.Y, mbX * 16, mbY * 16, this._codedWidth, y0, 16, 16);
      var cost1 = this._PredictionSad(frame.Samples.Y, mbX * 16, mbY * 16, this._codedWidth, y1, 16, 16);
      var costBi = this._PredictionSad(frame.Samples.Y, mbX * 16, mbY * 16, this._codedWidth, biY, 16, 16);
      var mode = cost0 <= cost1 && cost0 <= costBi ? BPredictionMode.L0
        : cost1 <= costBi ? BPredictionMode.L1
        : BPredictionMode.Bi;
      rbsp.WriteUnsignedExpGolomb((int)mode);

      if (mode is BPredictionMode.L0 or BPredictionMode.Bi) {
        rbsp.WriteSignedExpGolomb(motion0.X - predictor0.X);
        rbsp.WriteSignedExpGolomb(motion0.Y - predictor0.Y);
      }
      if (mode is BPredictionMode.L1 or BPredictionMode.Bi) {
        rbsp.WriteSignedExpGolomb(motion1.X - predictor1.X);
        rbsp.WriteSignedExpGolomb(motion1.Y - predictor1.Y);
      }

      Span<byte> predY = stackalloc byte[16 * 16];
      Span<byte> predCb = stackalloc byte[8 * 8];
      Span<byte> predCr = stackalloc byte[8 * 8];
      switch (mode) {
        case BPredictionMode.L0:
          y0.CopyTo(predY);
          cb0.CopyTo(predCb);
          cr0.CopyTo(predCr);
          break;
        case BPredictionMode.L1:
          y1.CopyTo(predY);
          cb1.CopyTo(predCb);
          cr1.CopyTo(predCr);
          break;
        default:
          _Average(y0, y1, predY);
          _Average(cb0, cb1, predCb);
          _Average(cr0, cr1, predCr);
          break;
      }

      var residual = this._QuantizeMacroblock(
        frame.Samples, predY, predCb, predCr, mbX, mbY, reconstructed);
      this._WriteInterResidual(rbsp, residual, residualContext, mbX, mbY);

      if (mode is BPredictionMode.L0 or BPredictionMode.Bi) {
        vectors0[mbAddr] = motion0;
        usesList0[mbAddr] = true;
      }
      if (mode is BPredictionMode.L1 or BPredictionMode.Bi) {
        vectors1[mbAddr] = motion1;
        usesList1[mbAddr] = true;
      }
    }

    return this._Packet(0x01, rbsp, frame, decodeTimestamp, keyFrame: false);
  }

  private Frame420 _EmptyFrame() {
    var chromaSamples = this._codedWidth / 2 * (this._codedHeight / 2);
    return new(
      new byte[this._codedWidth * this._codedHeight],
      new byte[chromaSamples],
      new byte[chromaSamples]);
  }

  private MotionVector _SearchMotion(
    Frame420 source,
    Frame420 reference,
    int mbX,
    int mbY,
    MotionVector predictor) {
    var centreX = _RoundToFullSample(predictor.X);
    var centreY = _RoundToFullSample(predictor.Y);
    var best = new MotionVector(centreX, centreY);
    var bestCost = long.MaxValue;
    Span<byte> prediction = stackalloc byte[16 * 16];

    for (var dy = -_INTEGER_SEARCH_RANGE; dy <= _INTEGER_SEARCH_RANGE; ++dy)
      for (var dx = -_INTEGER_SEARCH_RANGE; dx <= _INTEGER_SEARCH_RANGE; ++dx) {
        var candidate = new MotionVector(centreX + dx * 4, centreY + dy * 4);
        var cost = this._MotionCost(source, reference, mbX, mbY, candidate, prediction);
        if (cost < bestCost) {
          best = candidate;
          bestCost = cost;
        }
      }

    best = this._RefineMotion(source, reference, mbX, mbY, best, step: 2, prediction, ref bestCost);
    best = this._RefineMotion(source, reference, mbX, mbY, best, step: 1, prediction, ref bestCost);
    return best;
  }

  private MotionVector _RefineMotion(
    Frame420 source,
    Frame420 reference,
    int mbX,
    int mbY,
    MotionVector centre,
    int step,
    Span<byte> prediction,
    ref long bestCost) {
    var best = centre;
    for (var dy = -step; dy <= step; dy += step)
      for (var dx = -step; dx <= step; dx += step) {
        var candidate = new MotionVector(centre.X + dx, centre.Y + dy);
        var cost = this._MotionCost(source, reference, mbX, mbY, candidate, prediction);
        if (cost < bestCost) {
          best = candidate;
          bestCost = cost;
        }
      }
    return best;
  }

  private long _MotionCost(
    Frame420 source,
    Frame420 reference,
    int mbX,
    int mbY,
    MotionVector motion,
    Span<byte> prediction) {
    H264MotionCompensation.PredictLuma(
      reference.Y, this._codedWidth, this._codedHeight,
      mbX * 16, mbY * 16, motion.X, motion.Y, 16, 16, prediction);
    return this._PredictionSad(
      source.Y, mbX * 16, mbY * 16, this._codedWidth, prediction, 16, 16);
  }

  private long _PredictionSad(
    byte[] source,
    int sourceX,
    int sourceY,
    int sourceStride,
    ReadOnlySpan<byte> prediction,
    int width,
    int height) {
    var result = 0L;
    for (var row = 0; row < height; ++row) {
      var sourceAt = (sourceY + row) * sourceStride + sourceX;
      var predAt = row * width;
      for (var column = 0; column < width; ++column)
        result += Math.Abs(source[sourceAt + column] - prediction[predAt + column]);
    }
    return result;
  }

  private void _PredictMacroblock(
    Frame420 reference,
    int mbX,
    int mbY,
    MotionVector motion,
    Span<byte> y,
    Span<byte> cb,
    Span<byte> cr) {
    H264MotionCompensation.PredictLuma(
      reference.Y, this._codedWidth, this._codedHeight,
      mbX * 16, mbY * 16, motion.X, motion.Y, 16, 16, y);
    var chromaWidth = this._codedWidth / 2;
    var chromaHeight = this._codedHeight / 2;
    H264MotionCompensation.PredictChroma(
      reference.Cb, chromaWidth, chromaHeight,
      mbX * 8, mbY * 8, motion.X, motion.Y, 8, 8, cb);
    H264MotionCompensation.PredictChroma(
      reference.Cr, chromaWidth, chromaHeight,
      mbX * 8, mbY * 8, motion.X, motion.Y, 8, 8, cr);
  }

  private ResidualMacroblock _QuantizeMacroblock(
    Frame420 source,
    ReadOnlySpan<byte> predY,
    ReadOnlySpan<byte> predCb,
    ReadOnlySpan<byte> predCr,
    int mbX,
    int mbY,
    Frame420 reconstructed) {
    var result = new ResidualMacroblock();
    Span<int> residual = stackalloc int[16];
    Span<int> reconstructedResidual = stackalloc int[16];

    for (var blkIdx = 0; blkIdx < 16; ++blkIdx) {
      var (bx, by) = _BlockPosition(blkIdx);
      for (var row = 0; row < 4; ++row)
        for (var column = 0; column < 4; ++column) {
          var local = (by + row) * 16 + bx + column;
          var sourceAt = (mbY * 16 + by + row) * this._codedWidth + mbX * 16 + bx + column;
          residual[(row << 2) + column] = source.Y[sourceAt] - predY[local];
        }

      var levels = result.Luma.AsSpan(blkIdx * 16, 16);
      H264ForwardTransform.Quantize4x4(residual, _QP, levels);
      if (_HasNonZero(levels))
        result.CbpLuma |= 1 << (blkIdx >> 2);

      H264Transform.DecodeBlock(levels, _QP, hasSeparateDc: false, 0, reconstructedResidual);
      this._StoreBlock(
        reconstructed.Y, this._codedWidth, mbX * 16 + bx, mbY * 16 + by,
        predY, 16, bx, by, reconstructedResidual);
    }

    var chromaQp = H264Transform.ChromaQp(_QP);
    this._QuantizeChroma(
      source.Cb, predCb, reconstructed.Cb, component: 0, mbX, mbY, chromaQp, result, residual, reconstructedResidual);
    this._QuantizeChroma(
      source.Cr, predCr, reconstructed.Cr, component: 1, mbX, mbY, chromaQp, result, residual, reconstructedResidual);
    return result;
  }

  private void _QuantizeChroma(
    byte[] source,
    ReadOnlySpan<byte> prediction,
    byte[] reconstructed,
    int component,
    int mbX,
    int mbY,
    int qp,
    ResidualMacroblock result,
    Span<int> residual,
    Span<int> reconstructedResidual) {
    var chromaWidth = this._codedWidth / 2;
    Span<int> dc = stackalloc int[4];
    Span<int> transformed = stackalloc int[16];

    for (var blkIdx = 0; blkIdx < 4; ++blkIdx) {
      var bx = (blkIdx & 1) * 4;
      var by = (blkIdx >> 1) * 4;
      for (var row = 0; row < 4; ++row)
        for (var column = 0; column < 4; ++column) {
          var local = (by + row) * 8 + bx + column;
          var sourceAt = (mbY * 8 + by + row) * chromaWidth + mbX * 8 + bx + column;
          residual[(row << 2) + column] = source[sourceAt] - prediction[local];
        }

      H264ForwardTransform.Forward4x4(residual, transformed);
      dc[blkIdx] = transformed[0];
      H264ForwardTransform.Quantize4x4(
        residual, qp, result.Chroma.AsSpan((component * 4 + blkIdx) * 16, 16), omitDc: true);
    }

    var dcLevels = result.ChromaDc.AsSpan(component * 4, 4);
    H264ForwardTransform.QuantizeChromaDc(dc, qp, dcLevels);
    var anyDc = _HasNonZero(dcLevels);
    var anyAc = false;
    for (var blkIdx = 0; blkIdx < 4; ++blkIdx)
      anyAc |= _HasNonZero(result.Chroma.AsSpan((component * 4 + blkIdx) * 16 + 1, 15));
    if (anyAc)
      result.CbpChroma = 2;
    else if (anyDc && result.CbpChroma == 0)
      result.CbpChroma = 1;

    Span<int> decodedDc = stackalloc int[4];
    H264Transform.DecodeChromaDc(dcLevels, qp, decodedDc);
    for (var blkIdx = 0; blkIdx < 4; ++blkIdx) {
      var bx = (blkIdx & 1) * 4;
      var by = (blkIdx >> 1) * 4;
      H264Transform.DecodeBlock(
        result.Chroma.AsSpan((component * 4 + blkIdx) * 16, 16),
        qp, hasSeparateDc: true, decodedDc[blkIdx], reconstructedResidual);
      this._StoreBlock(
        reconstructed, chromaWidth, mbX * 8 + bx, mbY * 8 + by,
        prediction, 8, bx, by, reconstructedResidual);
    }
  }

  private void _StoreBlock(
    byte[] target,
    int targetStride,
    int targetX,
    int targetY,
    ReadOnlySpan<byte> prediction,
    int predictionStride,
    int predictionX,
    int predictionY,
    ReadOnlySpan<int> residual) {
    for (var row = 0; row < 4; ++row)
      for (var column = 0; column < 4; ++column) {
        var pred = prediction[(predictionY + row) * predictionStride + predictionX + column];
        target[(targetY + row) * targetStride + targetX + column] =
          (byte)Math.Clamp(pred + residual[(row << 2) + column], 0, 255);
      }
  }

  private void _WriteInterResidual(
    H264BitWriter writer,
    ResidualMacroblock residual,
    ResidualContext context,
    int mbX,
    int mbY) {
    var codedBlockPattern = residual.CbpLuma | (residual.CbpChroma << 4);
    writer.WriteUnsignedExpGolomb(H264CavlcEncoding.InterCodedBlockPatternCodeNum(codedBlockPattern));
    if (codedBlockPattern == 0)
      return;

    writer.WriteSignedExpGolomb(0); // mb_qp_delta: this encoder holds QP constant within a picture.
    for (var i8x8 = 0; i8x8 < 4; ++i8x8) {
      if ((residual.CbpLuma & (1 << i8x8)) == 0)
        continue;
      for (var i4x4 = 0; i4x4 < 4; ++i4x4) {
        var blkIdx = i8x8 * 4 + i4x4;
        var (bx, by) = _BlockPosition(blkIdx);
        var blockX = mbX * 4 + (bx >> 2);
        var blockY = mbY * 4 + (by >> 2);
        var nC = context.LumaNc(blockX, blockY);
        var count = H264CavlcEncoding.WriteBlock(
          writer, residual.Luma.AsSpan(blkIdx * 16, 16), nC, chromaDc: false);
        context.SetLuma(blockX, blockY, count);
      }
    }

    if (residual.CbpChroma == 0)
      return;
    for (var component = 0; component < 2; ++component)
      H264CavlcEncoding.WriteBlock(
        writer, residual.ChromaDc.AsSpan(component * 4, 4), -1, chromaDc: true);
    if (residual.CbpChroma < 2)
      return;

    for (var component = 0; component < 2; ++component)
      for (var blkIdx = 0; blkIdx < 4; ++blkIdx) {
        var blockX = mbX * 2 + (blkIdx & 1);
        var blockY = mbY * 2 + (blkIdx >> 1);
        var nC = context.ChromaNc(component, blockX, blockY);
        var count = H264CavlcEncoding.WriteBlock(
          writer, residual.Chroma.AsSpan((component * 4 + blkIdx) * 16 + 1, 15), nC, chromaDc: false);
        context.SetChroma(component, blockX, blockY, count);
      }
  }

  private static MotionVector _PredictMotion(
    MotionVector[] vectors,
    bool[] usesList,
    int mbAddr,
    int mbWidth,
    int mbHeight) {
    var mbX = mbAddr % mbWidth;
    var mbY = mbAddr / mbWidth;
    var a = _MotionNeighbour(vectors, usesList, mbX - 1, mbY, mbWidth, mbHeight);
    var b = _MotionNeighbour(vectors, usesList, mbX, mbY - 1, mbWidth, mbHeight);
    var c = _MotionNeighbour(vectors, usesList, mbX + 1, mbY - 1, mbWidth, mbHeight);
    if (!c.Available)
      c = _MotionNeighbour(vectors, usesList, mbX - 1, mbY - 1, mbWidth, mbHeight);
    if (!b.Available && !c.Available && a.Available) {
      b = a;
      c = a;
    }

    var matches = (a.RefIdx == 0 ? 1 : 0) + (b.RefIdx == 0 ? 1 : 0) + (c.RefIdx == 0 ? 1 : 0);
    if (matches == 1)
      return a.RefIdx == 0 ? a.Vector : b.RefIdx == 0 ? b.Vector : c.Vector;
    return new(_MedianOf(a.Vector.X, b.Vector.X, c.Vector.X), _MedianOf(a.Vector.Y, b.Vector.Y, c.Vector.Y));
  }

  private static MotionNeighbour _MotionNeighbour(
    MotionVector[] vectors,
    bool[] usesList,
    int mbX,
    int mbY,
    int mbWidth,
    int mbHeight) {
    if (mbX < 0 || mbY < 0 || mbX >= mbWidth || mbY >= mbHeight)
      return default;
    var address = mbY * mbWidth + mbX;
    return new(true, usesList[address] ? vectors[address] : default, usesList[address] ? 0 : -1);
  }

  private static int _MedianOf(int first, int second, int third)
    => first + second + third - Math.Min(first, Math.Min(second, third)) - Math.Max(first, Math.Max(second, third));

  private static int _RoundToFullSample(int quarterSample)
    => quarterSample >= 0
      ? ((quarterSample + 2) >> 2) << 2
      : -(((-quarterSample + 2) >> 2) << 2);

  private static bool _HasNonZero(ReadOnlySpan<int> values) {
    foreach (var value in values)
      if (value != 0)
        return true;
    return false;
  }

  private static void _Average(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> output) {
    for (var i = 0; i < output.Length; ++i)
      output[i] = (byte)((first[i] + second[i] + 1) >> 1);
  }

  private static (int X, int Y) _BlockPosition(int blkIdx) {
    var quadrant = blkIdx >> 2;
    var within = blkIdx & 3;
    return (((quadrant & 1) << 3) + ((within & 1) << 2), ((quadrant >> 1) << 3) + ((within >> 1) << 2));
  }

  private void _WriteSliceHeader(H264BitWriter writer, SliceKind kind, int displayIndex, int frameNum, bool idr) {
    writer.WriteUnsignedExpGolomb(0); // first_mb_in_slice
    writer.WriteUnsignedExpGolomb(kind switch {
      SliceKind.P => 5, // all slices of this picture are P
      SliceKind.B => 6, // all slices of this picture are B
      _ => 7, // all slices of this picture are I
    });
    writer.WriteUnsignedExpGolomb(0); // pic_parameter_set_id
    writer.WriteBits(frameNum, _FRAME_NUM_BITS);
    if (idr)
      writer.WriteUnsignedExpGolomb(0); // idr_pic_id
    writer.WriteBits((displayIndex << 1) & _POC_MASK, _POC_BITS); // pic_order_cnt_lsb

    if (kind == SliceKind.B)
      writer.WriteBit(true); // direct_spatial_mv_pred_flag (required syntax, explicit Bi is used below)

    if (kind is SliceKind.P or SliceKind.B) {
      writer.WriteBit(false); // num_ref_idx_active_override_flag: one active entry from each used list
      writer.WriteBit(false); // ref_pic_list_modification_flag_l0
      if (kind == SliceKind.B)
        writer.WriteBit(false); // ref_pic_list_modification_flag_l1
    }

    if (idr) {
      writer.WriteBit(false); // no_output_of_prior_pics_flag
      writer.WriteBit(false); // long_term_reference_flag
    } else if (kind == SliceKind.P)
      writer.WriteBit(false); // adaptive_ref_pic_marking_mode_flag; sliding-window DPB

    writer.WriteSignedExpGolomb(0); // slice_qp_delta
    writer.WriteUnsignedExpGolomb(1); // disable_deblocking_filter_idc: exact reference samples stay exact
  }

  private CodedPacket _Packet(
    byte nalHeader,
    H264BitWriter rbsp,
    PendingFrame frame,
    long? decodeTimestamp,
    bool keyFrame) {
    var slice = _NalUnit(nalHeader, rbsp.FinishRbsp());
    var sample = new byte[_NAL_LENGTH_SIZE + slice.Length];
    BinaryPrimitives.WriteUInt32BigEndian(sample, checked((uint)slice.Length));
    slice.CopyTo(sample, _NAL_LENGTH_SIZE);

    return new(
      this._requested.Index,
      sample,
      PresentationTimestamp: frame.PresentationTimestamp,
      DecodeTimestamp: decodeTimestamp,
      Duration: 1,
      IsKeyFrame: keyFrame);
  }

  private bool _MacroblockEquals(Frame420 current, Frame420 reference, int mbAddr) {
    var mbX = mbAddr % this._macroblockWidth;
    var mbY = mbAddr / this._macroblockWidth;
    return _BlockEquals(current.Y, reference.Y, this._codedWidth, mbX * 16, mbY * 16, 16, 16)
      && _BlockEquals(current.Cb, reference.Cb, this._codedWidth / 2, mbX * 8, mbY * 8, 8, 8)
      && _BlockEquals(current.Cr, reference.Cr, this._codedWidth / 2, mbX * 8, mbY * 8, 8, 8);
  }

  private bool _MacroblockEqualsBiPrediction(Frame420 current, Frame420 previous, Frame420 future, int mbAddr) {
    var mbX = mbAddr % this._macroblockWidth;
    var mbY = mbAddr / this._macroblockWidth;
    return _BlockEqualsAverage(current.Y, previous.Y, future.Y, this._codedWidth, mbX * 16, mbY * 16, 16, 16)
      && _BlockEqualsAverage(current.Cb, previous.Cb, future.Cb, this._codedWidth / 2, mbX * 8, mbY * 8, 8, 8)
      && _BlockEqualsAverage(current.Cr, previous.Cr, future.Cr, this._codedWidth / 2, mbX * 8, mbY * 8, 8, 8);
  }

  private static bool _BlockEquals(
    byte[] first, byte[] second, int stride, int x, int y, int width, int height) {
    for (var row = 0; row < height; ++row) {
      var offset = (y + row) * stride + x;
      if (!first.AsSpan(offset, width).SequenceEqual(second.AsSpan(offset, width)))
        return false;
    }
    return true;
  }

  private static bool _BlockEqualsAverage(
    byte[] current, byte[] first, byte[] second, int stride, int x, int y, int width, int height) {
    for (var row = 0; row < height; ++row) {
      var offset = (y + row) * stride + x;
      for (var column = 0; column < width; ++column) {
        var at = offset + column;
        if (current[at] != (byte)((first[at] + second[at] + 1) >> 1))
          return false;
      }
    }
    return true;
  }

  private Frame420 _To420(RawImage frame) {
    var source = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, this._width / 2, this._height / 2);
    var sourceLumaSamples = this._width * this._height;
    var sourceChromaWidth = this._width / 2;
    var sourceChromaHeight = this._height / 2;
    var sourceChromaSamples = sourceChromaWidth * sourceChromaHeight;
    var luma = new byte[this._codedWidth * this._codedHeight];
    var chromaWidth = this._codedWidth / 2;
    var chromaHeight = this._codedHeight / 2;
    var cb = new byte[chromaWidth * chromaHeight];
    var cr = new byte[chromaWidth * chromaHeight];

    _PadPlane(source.AsSpan(0, sourceLumaSamples), this._width, this._height, luma, this._codedWidth, this._codedHeight);
    _PadPlane(source.AsSpan(sourceLumaSamples, sourceChromaSamples), sourceChromaWidth, sourceChromaHeight, cb, chromaWidth, chromaHeight);
    _PadPlane(source.AsSpan(sourceLumaSamples + sourceChromaSamples, sourceChromaSamples), sourceChromaWidth, sourceChromaHeight, cr, chromaWidth, chromaHeight);
    return new(luma, cb, cr);
  }

  private static void _PadPlane(
    ReadOnlySpan<byte> source,
    int sourceWidth,
    int sourceHeight,
    Span<byte> target,
    int targetWidth,
    int targetHeight) {
    for (var y = 0; y < targetHeight; ++y) {
      var sourceY = Math.Min(y, sourceHeight - 1);
      for (var x = 0; x < targetWidth; ++x)
        target[y * targetWidth + x] = source[sourceY * sourceWidth + Math.Min(x, sourceWidth - 1)];
    }
  }

  private byte[] _SequenceParameterSet() {
    var rbsp = new H264BitWriter();
    rbsp.WriteBits(_PROFILE_IDC, 8);
    rbsp.WriteBits(_PROFILE_COMPATIBILITY, 8);
    rbsp.WriteBits(_LEVEL_IDC, 8);
    rbsp.WriteUnsignedExpGolomb(0); // seq_parameter_set_id
    rbsp.WriteUnsignedExpGolomb(_FRAME_NUM_BITS - 4); // log2_max_frame_num_minus4
    rbsp.WriteUnsignedExpGolomb(0); // pic_order_cnt_type
    rbsp.WriteUnsignedExpGolomb(_POC_BITS - 4); // log2_max_pic_order_cnt_lsb_minus4
    rbsp.WriteUnsignedExpGolomb(2); // max_num_ref_frames: previous and future anchor around a B picture
    rbsp.WriteBit(false); // gaps_in_frame_num_value_allowed_flag
    rbsp.WriteUnsignedExpGolomb(this._macroblockWidth - 1);
    rbsp.WriteUnsignedExpGolomb(this._macroblockHeight - 1);
    rbsp.WriteBit(true); // frame_mbs_only_flag
    rbsp.WriteBit(true); // direct_8x8_inference_flag

    var cropRight = (this._codedWidth - this._width) / 2;
    var cropBottom = (this._codedHeight - this._height) / 2;
    var cropped = cropRight != 0 || cropBottom != 0;
    rbsp.WriteBit(cropped);
    if (cropped) {
      rbsp.WriteUnsignedExpGolomb(0); // frame_crop_left_offset
      rbsp.WriteUnsignedExpGolomb(cropRight);
      rbsp.WriteUnsignedExpGolomb(0); // frame_crop_top_offset
      rbsp.WriteUnsignedExpGolomb(cropBottom);
    }

    rbsp.WriteBit(false); // vui_parameters_present_flag
    return _NalUnit(0x67, rbsp.FinishRbsp());
  }

  private static byte[] _PictureParameterSet() {
    var rbsp = new H264BitWriter();
    rbsp.WriteUnsignedExpGolomb(0); // pic_parameter_set_id
    rbsp.WriteUnsignedExpGolomb(0); // seq_parameter_set_id
    rbsp.WriteBit(false); // entropy_coding_mode_flag: CAVLC
    rbsp.WriteBit(false); // bottom_field_pic_order_in_frame_present_flag
    rbsp.WriteUnsignedExpGolomb(0); // num_slice_groups_minus1
    rbsp.WriteUnsignedExpGolomb(0); // num_ref_idx_l0_default_active_minus1
    rbsp.WriteUnsignedExpGolomb(0); // num_ref_idx_l1_default_active_minus1
    rbsp.WriteBit(false); // weighted_pred_flag
    rbsp.WriteBits(0, 2); // weighted_bipred_idc: ordinary rounded average
    rbsp.WriteSignedExpGolomb(0); // pic_init_qp_minus26
    rbsp.WriteSignedExpGolomb(0); // pic_init_qs_minus26
    rbsp.WriteSignedExpGolomb(0); // chroma_qp_index_offset
    rbsp.WriteBit(true); // deblocking_filter_control_present_flag
    rbsp.WriteBit(false); // constrained_intra_pred_flag
    rbsp.WriteBit(false); // redundant_pic_cnt_present_flag
    return _NalUnit(0x68, rbsp.FinishRbsp());
  }

  private void _WritePcmMacroblock(H264BitWriter writer, Frame420 frame, int mbX, int mbY, int mbType) {
    writer.WriteUnsignedExpGolomb(mbType);
    writer.AlignWithZeroBits();

    for (var y = 0; y < 16; ++y) {
      var row = (mbY * 16 + y) * this._codedWidth + mbX * 16;
      for (var x = 0; x < 16; ++x)
        writer.WriteAlignedByte(frame.Y[row + x]);
    }

    var chromaWidth = this._codedWidth / 2;
    _WriteChroma(frame.Cb);
    _WriteChroma(frame.Cr);

    void _WriteChroma(byte[] plane) {
      for (var y = 0; y < 8; ++y) {
        var row = (mbY * 8 + y) * chromaWidth + mbX * 8;
        for (var x = 0; x < 8; ++x)
          writer.WriteAlignedByte(plane[row + x]);
      }
    }
  }

  /// <summary>
  /// ISO/IEC 14496-12 VisualSampleEntry with the AVCDecoderConfigurationRecord in its <c>avcC</c> box.
  /// MP4 needs the whole entry; the H.264 decoder and raw Annex-B writer deliberately know how to find
  /// the record inside it.
  /// </summary>
  private byte[] _AvcSampleEntry() {
    var result = new byte[86 + 8 + this._configuration.Length];
    BinaryPrimitives.WriteUInt32BigEndian(result, checked((uint)result.Length));
    "avc1"u8.CopyTo(result.AsSpan(4));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(14), 1); // data_reference_index
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(32), checked((ushort)this._width));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(34), checked((ushort)this._height));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(36), 0x00480000); // horizresolution 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(40), 0x00480000); // vertresolution
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(48), 1); // frame_count
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(82), 24); // depth
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(84), 0xFFFF);

    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(86), checked((uint)(8 + this._configuration.Length)));
    "avcC"u8.CopyTo(result.AsSpan(90));
    this._configuration.CopyTo(result, 94);
    return result;
  }

  private static byte[] _DecoderConfiguration(byte[] sps, byte[] pps) {
    if (sps.Length > ushort.MaxValue || pps.Length > ushort.MaxValue)
      throw new InvalidOperationException("H.264 parameter sets exceed AVCDecoderConfigurationRecord length fields.");

    var result = new byte[11 + sps.Length + pps.Length];
    var at = 0;
    result[at++] = 1; // configurationVersion
    result[at++] = _PROFILE_IDC;
    result[at++] = _PROFILE_COMPATIBILITY;
    result[at++] = _LEVEL_IDC;
    result[at++] = 0xFC | (_NAL_LENGTH_SIZE - 1);
    result[at++] = 0xE0 | 1; // one SPS
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(at), checked((ushort)sps.Length));
    at += 2;
    sps.CopyTo(result, at);
    at += sps.Length;
    result[at++] = 1; // one PPS
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(at), checked((ushort)pps.Length));
    at += 2;
    pps.CopyTo(result, at);
    return result;
  }

  private static byte[] _NalUnit(byte header, byte[] rbsp) {
    var escaped = new List<byte>(rbsp.Length + 8) { header };
    var zeroes = 0;

    foreach (var value in rbsp) {
      if (zeroes == 2 && value <= 3) {
        escaped.Add(3);
        zeroes = 0;
      }

      escaped.Add(value);
      zeroes = value == 0 ? zeroes + 1 : 0;
    }

    return [.. escaped];
  }

  private enum SliceKind : byte { P, B, I }

  private sealed record Frame420(byte[] Y, byte[] Cb, byte[] Cr);

  private sealed record PendingFrame(Frame420 Samples, long? PresentationTimestamp, int DisplayIndex);

}
