using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes ISO/IEC 11172-2 MPEG-1 video as progressive 4:2:0 I and P pictures.</summary>
/// <remarks>
/// Writes I and P pictures. A group of pictures opens with an I picture and continues with P
/// pictures that predict forwards from the anchor before them, which is the arrangement every
/// MPEG-1 decoder must handle. Coding order equals display order, so no packet is reordered and
/// each keeps the timestamp of the frame that produced it.
/// <para/>
/// B pictures are not written. They would reorder coding against display, which changes this
/// encoder's contract with its caller rather than only its bitstream, and they gain nothing a P
/// picture cannot already express here. Reading them is supported in full.
/// <para/>
/// The reference a P picture predicts from is read back out of a decoder this encoder drives with
/// its own output, so the prediction starts from the picture the receiving decoder will hold. An
/// encoder predicting from its source instead drifts a little further from its decoder with every
/// predicted picture.
/// <para/>
/// Each picture is one slice containing every macroblock in raster order. Right and bottom padding
/// repeats the edge sample; the sequence header keeps the caller's actual dimensions, so those
/// samples are coding padding only and are cropped by a decoder. Chrominance is 4:2:0, produced by
/// averaging each 2x2 luma footprint under ITU-R BT.601 limited-range conversion where the input is
/// not already planar YUV.
/// <para/>
/// The block quantiser is the encoder model described by ISO/IEC 11172-2 Annex D: DC uses the fixed
/// step of eight and AC uses the default intra matrix with a fixed quantiser scale. MPEG-1 is lossy;
/// this encoder makes no lossless claim.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Mpeg1VideoEncoder : IVideoCodecEncoder<Mpeg1VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MPG1");
  private const int _QUANTISER_SCALE = 8;

  /// <summary>Pictures per group: one I picture and eleven P pictures.</summary>
  /// <remarks>
  /// Twelve is the length the MPEG-1 constrained-parameters material uses and what decoders were
  /// tested against. It also bounds error propagation: nothing predicted is ever more than eleven
  /// pictures away from an independently decodable one.
  /// </remarks>
  private const int _GROUP_SIZE = 12;

  /// <summary>forward_f_code, which fixes the range a forward vector can state.</summary>
  /// <remarks>
  /// The decoder folds the reconstructed vector into <c>[-16 * f, 16 * f - 1]</c> where
  /// <c>f = 1 &lt;&lt; (f_code - 1)</c> — the fold is applied to the vector itself, not to the
  /// difference that was coded — so f_code is what decides how far anything may move, and a search
  /// beyond it does not merely cost bits but comes back as a different vector. Two gives
  /// <c>[-32, 31]</c> whole pixels, which covers the motion in a picture of the sizes MPEG-1 is
  /// used at; one would cap motion at sixteen pixels and cap it silently.
  /// </remarks>
  private const int _FORWARD_F_CODE = 2;

  /// <summary>f, the scale a motion difference is stated in.</summary>
  private const int _MOTION_SCALE = 1 << (_FORWARD_F_CODE - 1);

  /// <summary>The largest whole-pixel displacement the forward vectors can state.</summary>
  private const int _MOTION_LIMIT = 16 * _MOTION_SCALE;

  /// <summary>
  /// How far a motion search looks, in whole pixels, around the vector predicted from the
  /// macroblock before it.
  /// </summary>
  private const int _SEARCH_RANGE = 15;

  /// <summary>Table B.1 reversed: an address increment to the code that states it.</summary>
  private static readonly IReadOnlyDictionary<int, string> _AddressIncrementCodes =
    MpegVlcTables.MacroblockAddressIncrement.Entries
      .ToDictionary(static entry => entry.Value, static entry => entry.Code);

  /// <summary>Table B.9 reversed: a coded block pattern to the code that states it.</summary>
  private static readonly IReadOnlyDictionary<int, string> _CodedBlockPatternCodes =
    MpegVlcTables.CodedBlockPattern.Entries.ToDictionary(static entry => entry.Value, static entry => entry.Code);

  /// <summary>Table B.10 reversed: a motion difference to the code that states it.</summary>
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

  private CodedPacket? _pending;
  private MediaStreamInfo? _stream;
  private int _pictureIndex;
  private bool _finished;

  /// <summary>Drives on this encoder's own output, to hand back the reference a P picture predicts from.</summary>
  private readonly MpegVideoDecoder _reconstruction = new();

  /// <summary>How far into the current group of pictures the next frame is; zero codes an I picture.</summary>
  private int _groupPosition;

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

  /// <summary>Creates an MPEG-1 encoder for a geometry and one of the eight frame rates the syntax can state.</summary>
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

  /// <summary>Codes one picture: intra at the head of a group, forward-predicted otherwise.</summary>
  /// <remarks>
  /// One picture is held so <see cref="Flush"/> can put the sequence-end code after the final
  /// picture rather than manufacture a packet that is not a picture. There is no coding-order
  /// reordering: every returned packet keeps the timestamp of the frame that produced it.
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (this._finished)
      throw new InvalidOperationException("This MPEG-1 encoder has already been flushed.");

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This MPEG-1 stream is {this._width}x{this._height}; a {frame.Width}x{frame.Height} frame arrived.");

    var planes = _PlanesOf(frame);
    var writer = new MpegBitWriter();
    if (this._pictureIndex == 0)
      this._WriteSequenceHeader(writer);

    // The first picture of every group is intra, and so is any picture with no reference yet.
    var reference = this._reconstruction.CurrentAnchor;
    var isIntra = this._groupPosition == 0 || reference == null;
    this._WritePicture(writer, planes, isIntra ? null : reference);
    this._groupPosition = (this._groupPosition + 1) % _GROUP_SIZE;

    var bytes = writer.ToArray();

    // Reconstruct by decoding what was just written. This is what makes the next P picture predict
    // from the same samples its decoder will have.
    this._reconstruction.DecodePacket(bytes);

    var current = new CodedPacket(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: isIntra);

    ++this._pictureIndex;

    if (this._pending is not { } ready) {
      this._pending = current;
      packet = default;
      return false;
    }

    this._pending = current;
    packet = ready;
    return true;
  }

  /// <summary>Returns the last picture with the sequence-end start code following it.</summary>
  public IEnumerable<CodedPacket> Flush() {
    if (this._finished)
      yield break;

    this._finished = true;
    if (this._pending is not { } pending)
      yield break;

    var bytes = new byte[pending.Data.Length + 4];
    pending.Data.Span.CopyTo(bytes);
    bytes[^4] = 0x00;
    bytes[^3] = 0x00;
    bytes[^2] = 0x01;
    bytes[^1] = MpegStartCode.SequenceEnd;

    this._pending = null;
    yield return pending with { Data = bytes };
  }

  /// <summary>The stream description muxers need to name the elementary MPEG-1 payload.</summary>
  public MediaStreamInfo DescribeStream()
    => this._stream ??= new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MPEG1",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 12,
    };

  private void _WriteSequenceHeader(MpegBitWriter writer) {
    writer.StartCode(MpegStartCode.SequenceHeader);
    writer.Write(this._width, 12);
    writer.Write(this._height, 12);
    writer.Write(1, 4);                    // pel_aspect_ratio: square pels
    writer.Write(this._frameRateCode, 4);
    writer.Write(0x3FFFF, 18);             // bit_rate: variable/unspecified
    writer.WriteBit(1);                    // marker_bit
    writer.Write(0x3FF, 10);               // largest VBV buffer size the field can state
    writer.WriteBit(0);                    // constrained_parameters_flag
    writer.WriteBit(0);                    // load_intra_quantizer_matrix: use the default
    writer.WriteBit(0);                    // load_non_intra_quantizer_matrix
  }

  private void _WritePicture(MpegBitWriter writer, Yuv420Planes planes, MpegFrame? reference) {
    writer.StartCode(MpegStartCode.Picture);
    writer.Write(this._pictureIndex & 0x3FF, 10); // temporal_reference
    writer.Write(reference == null ? MpegPictureDecoder.IntraCoded : MpegPictureDecoder.PredictiveCoded, 3);
    writer.Write(0xFFFF, 16);                    // vbv_delay: unspecified

    if (reference != null) {
      // full_pel_forward_vector: the vectors below count whole pixels, so no half-pixel
      // interpolation stands between the prediction and the samples the search compared.
      writer.WriteBit(1);
      writer.Write(_FORWARD_F_CODE, 3);
    }

    writer.WriteBit(0);                          // extra_bit_picture

    // One slice beginning in the first macroblock row. A slice may continue across rows; using one
    // for the whole picture also keeps pictures taller than the 175 start-code row values encodable.
    writer.StartCode(MpegStartCode.FirstSlice);
    writer.Write(_QUANTISER_SCALE, 5);
    writer.WriteBit(0); // extra_bit_slice

    Span<int> block = stackalloc int[64];
    Span<int> levels = stackalloc int[64 * 6];
    var dcY = 128;
    var dcCb = 128;
    var dcCr = 128;

    // The forward vector is coded as its difference from the macroblock before it, and both the
    // predictor and the intra/skip rules reset at the start of a slice. There is one slice here.
    var predictedX = 0;
    var predictedY = 0;

    // Macroblocks that neither moved nor left a residual are not written at all: the next coded
    // macroblock's address increment steps over them. This is what makes a predicted picture cheap,
    // and without it one costs more than coding the picture whole -- every macroblock would spend a
    // type and two vectors saying nothing happened.
    var pendingSkips = 0;
    var lastAddress = this._macroblockWidth * this._macroblockHeight - 1;

    for (var address = 0; address <= lastAddress; ++address) {
      var macroblockY = address / this._macroblockWidth;
      var macroblockX = address % this._macroblockWidth;

      if (reference == null) {
        writer.WriteCode("1"); // macroblock_address_increment = 1, Table B.1
        writer.WriteCode("1"); // I-picture macroblock_type = intra, Table B.2
        _WriteIntraBlocks(writer, planes, macroblockX, macroblockY, block, ref dcY, ref dcCb, ref dcCr);
        continue;
      }

      var (vectorX, vectorY) = _SearchMotion(
        planes, reference, macroblockX, macroblockY, predictedX, predictedY);

      var pattern = _QuantiseResidual(
        planes, reference, macroblockX, macroblockY, vectorX, vectorY, block, levels);

      // A skipped macroblock means exactly a zero vector and no residual. The first and last
      // macroblock of a slice are always coded: the first fixes where the slice starts, and a slice
      // that ended on a skip would not say where it ended.
      if (vectorX == 0 && vectorY == 0 && pattern == 0 && address != 0 && address != lastAddress) {
        ++pendingSkips;
        // Skipping resets the decoder's vector predictors, so the encoder's must follow.
        predictedX = 0;
        predictedY = 0;
        continue;
      }

      _WriteAddressIncrement(writer, pendingSkips + 1);
      pendingSkips = 0;

      // Table B.3.
      writer.WriteCode(pattern != 0 ? "1" : "001");

      _WriteMotionCode(writer, vectorX - predictedX);
      _WriteMotionCode(writer, vectorY - predictedY);
      predictedX = vectorX;
      predictedY = vectorY;

      if (pattern == 0)
        continue;

      writer.WriteCode(_CodedBlockPatternCodes[pattern]);
      for (var index = 0; index < 6; ++index)
        if ((pattern & (1 << (5 - index))) != 0)
          Mpeg1InterBlockEncoder.Write(writer, levels.Slice(index * 64, 64));
    }
  }

  /// <summary>Writes macroblock_address_increment, escaping the part above thirty-three.</summary>
  /// <remarks>
  /// Table B.1 states one to thirty-three directly. A larger step is written as macroblock_escape
  /// codes, each worth a further thirty-three, followed by the remainder — which is how a run of
  /// skipped macroblocks longer than a table entry is spelled.
  /// </remarks>
  private static void _WriteAddressIncrement(MpegBitWriter writer, int increment) {
    while (increment > 33) {
      writer.WriteCode(_AddressIncrementCodes[MpegVlcTables.Escape]);
      increment -= 33;
    }

    writer.WriteCode(_AddressIncrementCodes[increment]);
  }

  private void _WriteIntraBlocks(
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

  /// <summary>
  /// Finds the whole-pixel vector whose 16x16 luminance prediction differs least from the source.
  /// </summary>
  /// <remarks>
  /// A plain exhaustive search over the window, scored by absolute difference. The search is
  /// centred on the predicted vector rather than on zero, because that is what the coded difference
  /// is measured from: a window around the predictor is the set of vectors the syntax can state
  /// cheaply.
  /// <para/>
  /// The zero vector is the incumbent and is only displaced by a strictly better one. That is not a
  /// tie-break detail: a macroblock that did not move must come out of here with a zero vector, or
  /// it cannot be skipped, and on flat or repeating content -- which is most of a background --
  /// many vectors score identically. Taking the first equal-scoring candidate instead picks
  /// whichever corner the scan began at, and the picture then spends a type and two vectors per
  /// macroblock saying nothing happened.
  /// </remarks>
  private (int X, int Y) _SearchMotion(
    Yuv420Planes planes, MpegFrame reference, int macroblockX, int macroblockY, int predictedX, int predictedY) {
    var originX = macroblockX * 16;
    var originY = macroblockY * 16;

    var best = (X: 0, Y: 0);
    var bestCost = _MatchCost(planes, reference, originX, originY, 0, 0, int.MaxValue);

    for (var candidateY = predictedY - _SEARCH_RANGE; candidateY <= predictedY + _SEARCH_RANGE; ++candidateY)
    for (var candidateX = predictedX - _SEARCH_RANGE; candidateX <= predictedX + _SEARCH_RANGE; ++candidateX) {
      // The vector itself, not the difference, is what the decoder folds into the range f_code
      // states. A candidate outside it would be reconstructed as a different vector entirely.
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

  /// <summary>
  /// Absolute difference between a macroblock and the prediction one vector offers, abandoned as
  /// soon as it cannot beat <paramref name="ceiling"/>.
  /// </summary>
  private static int _MatchCost(
    Yuv420Planes planes, MpegFrame reference, int originX, int originY, int vectorX, int vectorY, int ceiling) {
    var cost = 0;
    for (var y = 0; y < 16 && cost < ceiling; ++y)
    for (var x = 0; x < 16; ++x)
      cost += Math.Abs(
        _Sample(planes.Y, planes.YWidth, planes.YHeight, originX + x, originY + y)
        - _Sample(reference.Luma, reference.LumaWidth, reference.LumaHeight,
            originX + x + vectorX, originY + y + vectorY));

    return cost;
  }

  /// <summary>
  /// Quantises the six residual blocks of one motion-compensated macroblock into
  /// <paramref name="levels"/> and returns the coded block pattern.
  /// </summary>
  private int _QuantiseResidual(
    Yuv420Planes planes,
    MpegFrame reference,
    int macroblockX,
    int macroblockY,
    int vectorX,
    int vectorY,
    scoped Span<int> block,
    scoped Span<int> levels) {
    var pattern = 0;

    for (var index = 0; index < 4; ++index) {
      var x = macroblockX * 16 + (index & 1) * 8;
      var y = macroblockY * 16 + (index >> 1) * 8;
      _ReadResidual(
        planes.Y, planes.YWidth, planes.YHeight,
        reference.Luma, reference.LumaWidth, reference.LumaHeight,
        x, y, vectorX, vectorY, block);
      if (Mpeg1InterBlockEncoder.TryQuantise(block, _QUANTISER_SCALE, levels.Slice(index * 64, 64)))
        pattern |= 1 << (5 - index);
    }

    // 4:2:0 chrominance is half the size in both directions, so the vector halves with it. The
    // standard's own scaling truncates towards zero, which an arithmetic shift would not do for
    // negative vectors.
    var chromaVectorX = vectorX / 2;
    var chromaVectorY = vectorY / 2;
    var chromaX = macroblockX * 8;
    var chromaY = macroblockY * 8;

    _ReadResidual(
      planes.Cb, planes.ChromaWidth, planes.ChromaHeight,
      reference.Cb, reference.ChromaWidth, reference.ChromaHeight,
      chromaX, chromaY, chromaVectorX, chromaVectorY, block);
    if (Mpeg1InterBlockEncoder.TryQuantise(block, _QUANTISER_SCALE, levels.Slice(4 * 64, 64)))
      pattern |= 1 << 1;

    _ReadResidual(
      planes.Cr, planes.ChromaWidth, planes.ChromaHeight,
      reference.Cr, reference.ChromaWidth, reference.ChromaHeight,
      chromaX, chromaY, chromaVectorX, chromaVectorY, block);
    if (Mpeg1InterBlockEncoder.TryQuantise(block, _QUANTISER_SCALE, levels.Slice(5 * 64, 64)))
      pattern |= 1 << 0;

    return pattern;
  }

  /// <summary>Writes one forward vector component as motion_code and its residual.</summary>
  /// <remarks>
  /// The decoder reads <c>delta = (|motion_code| - 1) * f + residual + 1</c>, signed by
  /// motion_code, and zero alone means no displacement. This inverts exactly that. The difference
  /// is first folded by the range, because both this vector and the one it is predicted from lie
  /// inside <c>[-16f, 16f)</c> while their difference need not: the decoder folds the sum back, so
  /// a folded difference reconstructs the vector that was searched for.
  /// </remarks>
  private static void _WriteMotionCode(MpegBitWriter writer, int difference) {
    var range = 2 * _MOTION_LIMIT;
    if (difference < -_MOTION_LIMIT)
      difference += range;
    else if (difference >= _MOTION_LIMIT)
      difference -= range;

    if (difference == 0) {
      writer.WriteCode(_MotionCodes[0]);
      return;
    }

    var magnitude = Math.Abs(difference) - 1;
    var code = magnitude / _MOTION_SCALE + 1;
    var residual = magnitude % _MOTION_SCALE;

    writer.WriteCode(_MotionCodes[difference < 0 ? -code : code]);
    if (_MOTION_SCALE > 1)
      writer.Write(residual, _FORWARD_F_CODE - 1);
  }

  /// <summary>One sample of a plane, with the edge repeated past its bounds.</summary>
  private static int _Sample(byte[] plane, int width, int height, int x, int y)
    => plane[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];

  /// <summary>Reads one 8x8 block of source-minus-prediction.</summary>
  private static void _ReadResidual(
    byte[] source, int sourceWidth, int sourceHeight,
    byte[] reference, int referenceWidth, int referenceHeight,
    int originX, int originY, int vectorX, int vectorY, scoped Span<int> block) {
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      block[y * 8 + x] =
        _Sample(source, sourceWidth, sourceHeight, originX + x, originY + y)
        - _Sample(reference, referenceWidth, referenceHeight, originX + x + vectorX, originY + y + vectorY);
  }

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

  private sealed record Yuv420Planes(
    byte[] Y,
    byte[] Cb,
    byte[] Cr,
    int YWidth,
    int YHeight,
    int ChromaWidth,
    int ChromaHeight);
}
