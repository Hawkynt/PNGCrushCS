using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Vc1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes progressive Main-profile VC-1 with I, P and direct B pictures.</summary>
/// <remarks>
/// One B picture is buffered between anchor pictures. Anchors are written before the B picture that displays before
/// them, exactly as VC-1 requires: display-order <c>I0, B1, P2</c> becomes coded-order <c>I0, P2, B1</c>. P pictures
/// use zero motion-vector differentials and transformed residuals; B pictures use direct zero-vector prediction from
/// both reconstructed anchors and transformed residuals. Zero-vector motion is intentionally simple rate control, not
/// fake prediction: the packets are real P/B syntax and the decoder really uses their forward/backward references.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Vc1VideoEncoder : IVideoCodecEncoder<Vc1VideoEncoder> {

  private sealed record PendingPicture(Vc1Frame Samples, long? PresentationTimestamp);

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WMV3");

  /// <summary>Main profile, progressive, one B picture between anchors, uniform sequence quantiser.</summary>
  private static ReadOnlySpan<byte> _SequenceHeader => [0x40, 0x01, 0x00, 0x1D];

  private const int _PICTURE_QUANTISER = 3;
  private const int _DC_STEP_SIZE = 8;
  private const int _AC_STEP_SIZE = _PICTURE_QUANTISER * 2;
  private const int _DEFAULT_DC_PREDICTOR = 128;
  private const int _DC_ESCAPE_INDEX = 119;
  private const int _MODE3_LEVEL_BITS = 11;
  private const int _MODE3_RUN_BITS = 6;
  private const int _ZERO_MV_WITH_RESIDUAL_INDEX = 36;

  private readonly MediaStreamInfo _stream;
  private readonly Vc1SequenceHeader _sequence;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly Vc1PictureDecoder _intraReconstructor;
  private readonly Vc1PredictivePictureDecoder _predictiveReconstructor;
  private readonly Queue<CodedPacket> _ready = [];
  private Vc1Frame? _pastAnchor;
  private PendingPicture? _pendingB;
  private int _frameCount;

  private Vc1VideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._macroblockHeight = (stream.Height + 15) / 16;
    this._sequence = Vc1SequenceHeader.ReadFrom(_SequenceHeader);
    this._intraReconstructor = new(this._sequence, this._macroblockWidth, this._macroblockHeight);
    this._predictiveReconstructor = new(this._sequence, this._macroblockWidth, this._macroblockHeight);

    var format = new byte[BitmapInfoHeader.StructSize + _SequenceHeader.Length];
    var header = new BitmapInfoHeader(
      HeaderSize: format.Length,
      Width: stream.Width,
      Height: stream.Height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);
    header.WriteTo(format);
    _SequenceHeader.CopyTo(format.AsSpan(BitmapInfoHeader.StructSize));

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => Vc1VideoDecoder.CodecName;

  public static CodecTag Codec => _Tag;

  public static Vc1VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("VC-1 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A VC-1 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");

    var macroblockWidth = ((long)stream.Width + 15) / 16;
    var macroblockHeight = ((long)stream.Height + 15) / 16;
    var paddedPixels = checked(macroblockWidth * 16 * macroblockHeight * 16);
    if (paddedPixels > int.MaxValue || (long)stream.Width * stream.Height * 3 > int.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is larger than one managed VC-1 frame can hold.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"VC-1 geometry is fixed at {this._width}x{this._height} for the life of the stream; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var rgb = frame.Format == PixelFormat.Rgb24 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Rgb24);
    var samples = this._ToYuv420(rgb.PixelData);
    this._Consume(samples, presentationTimestamp);

    if (this._ready.Count != 0) {
      packet = this._ready.Dequeue();
      return true;
    }

    packet = default;
    return false;
  }

  public IEnumerable<CodedPacket> Flush() {
    if (this._pendingB != null) {
      var pending = this._pendingB;
      this._pendingB = null;
      var reference = this._pastAnchor
                      ?? throw new InvalidOperationException("A delayed VC-1 picture has no anchor to reference.");
      var data = this._EncodePredictedPicture(pending.Samples, reference);
      this._pastAnchor = this._predictiveReconstructor.DecodePredicted(data, reference, out _);
      this._ready.Enqueue(new(
        this._stream.Index,
        data,
        PresentationTimestamp: pending.PresentationTimestamp,
        DecodeTimestamp: pending.PresentationTimestamp,
        IsKeyFrame: false));
    }

    while (this._ready.Count != 0)
      yield return this._ready.Dequeue();
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private void _Consume(Vc1Frame samples, long? presentationTimestamp) {
    if (this._pastAnchor == null) {
      var data = this._EncodeIntraPicture(samples);
      this._pastAnchor = this._intraReconstructor.Decode(data, default, out _);
      this._ready.Enqueue(new(
        this._stream.Index,
        data,
        PresentationTimestamp: presentationTimestamp,
        DecodeTimestamp: presentationTimestamp,
        IsKeyFrame: true));
      return;
    }

    if (this._pendingB == null) {
      this._pendingB = new(samples, presentationTimestamp);
      return;
    }

    var pending = this._pendingB;
    var past = this._pastAnchor;
    var pData = this._EncodePredictedPicture(samples, past);
    var future = this._predictiveReconstructor.DecodePredicted(pData, past, out _);
    var bData = this._EncodeBidirectionalPicture(pending.Samples, past, future);

    this._ready.Enqueue(new(
      this._stream.Index,
      pData,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: pending.PresentationTimestamp ?? presentationTimestamp,
      IsKeyFrame: false));
    this._ready.Enqueue(new(
      this._stream.Index,
      bData,
      PresentationTimestamp: pending.PresentationTimestamp,
      DecodeTimestamp: presentationTimestamp ?? pending.PresentationTimestamp,
      IsKeyFrame: false));

    this._pastAnchor = future;
    this._pendingB = null;
  }

  // ============================================================================================
  // Picture syntax
  // ============================================================================================

  private byte[] _EncodeIntraPicture(Vc1Frame samples) {
    var writer = new Vc1BitWriter();
    writer.WriteBits(this._frameCount++ & 3, 2);
    writer.WriteBit(false); // I: PTYPE 01 when MAXBFRAMES > 0.
    writer.WriteBit(true);
    writer.WriteBits(0, 7); // BF
    writer.WriteBits(_PICTURE_QUANTISER, 5);
    writer.WriteBit(false); // HALFQP
    writer.WriteBit(false); // TRANSACFRM
    writer.WriteBit(false); // TRANSACFRM2
    writer.WriteBit(false); // TRANSDCTAB

    var luma = new Vc1IntraPrediction(this._macroblockWidth * 2, this._macroblockHeight * 2);
    var cb = new Vc1IntraPrediction(this._macroblockWidth, this._macroblockHeight);
    var cr = new Vc1IntraPrediction(this._macroblockWidth, this._macroblockHeight);
    var lumaCoded = new byte[checked(this._macroblockWidth * this._macroblockHeight * 4)];
    var escape = new Vc1EscapeState();
    Span<int> blocks = stackalloc int[6 * 64];

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        var pattern = 0;
        for (var blockIndex = 0; blockIndex < 6; ++blockIndex) {
          var isLuma = blockIndex < 4;
          var (plane, stride) = samples.PlaneOf(blockIndex);
          var x = isLuma ? (mbX * 16) + ((blockIndex & 1) * 8) : mbX * 8;
          var y = isLuma ? (mbY * 16) + ((blockIndex >> 1) * 8) : mbY * 8;
          var block = blocks.Slice(blockIndex * 64, 64);

          Vc1ForwardTransform.Quantise8x8(plane, stride, x, y, _DC_STEP_SIZE, _AC_STEP_SIZE, block);
          if (_HasAc(block))
            pattern |= 1 << (5 - blockIndex);
        }

        writer.WriteCode(Vc1Tables.IPictureCbpcy, this._EncodeIntraCodedBlockPattern(pattern, mbX, mbY, lumaCoded));
        writer.WriteBit(false); // ACPRED

        for (var blockIndex = 0; blockIndex < 6; ++blockIndex) {
          var isLuma = blockIndex < 4;
          var prediction = isLuma ? luma : blockIndex == 4 ? cb : cr;
          var column = isLuma ? (mbX * 2) + (blockIndex & 1) : mbX;
          var row = isLuma ? (mbY * 2) + (blockIndex >> 1) : mbY;
          var block = blocks.Slice(blockIndex * 64, 64);
          var (predictor, _) = prediction.Predict(column, row, _DEFAULT_DC_PREDICTOR);

          _WriteDcDifferential(writer, block[0] - predictor, isLuma);
          if ((pattern & (1 << (5 - blockIndex))) != 0)
            _WriteIntraAcCoefficients(writer, block, isLuma, escape);
          prediction.Store(column, row, block);
        }
      }

    return writer.ToArray();
  }

  private byte[] _EncodePredictedPicture(Vc1Frame samples, Vc1Frame reference) {
    var writer = new Vc1BitWriter();
    writer.WriteBits(this._frameCount++ & 3, 2);
    writer.WriteBit(true); // PTYPE P
    writer.WriteBits(_PICTURE_QUANTISER, 5);
    writer.WriteBit(false); // HALFQP
    writer.WriteBit(true); // MVMODE: 1-MV at this quantiser.
    _WriteRawBitPlaneHeader(writer); // SKIPMB
    writer.WriteBits(0, 2); // MVTAB
    writer.WriteBits(0, 2); // CBPTAB
    writer.WriteBit(false); // TRANSACFRM index zero
    writer.WriteBit(false); // TRANSDCTAB

    var residual = _Residual(samples, reference, null);
    var escape = new Vc1EscapeState();
    Span<int> blocks = stackalloc int[6 * 64];

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        var pattern = this._QuantiseInterMacroblock(residual, mbX, mbY, blocks);
        if (pattern == 0) {
          writer.WriteBit(true); // SKIPMBBIT
          continue;
        }

        writer.WriteBit(false);
        writer.WriteCode(Vc1PredictiveTables.MotionVectorDifferential0, _ZERO_MV_WITH_RESIDUAL_INDEX);
        writer.WriteCode(Vc1PredictiveTables.CodedBlockPattern0, pattern);
        this._WriteInterBlocks(writer, pattern, blocks, escape);
      }

    return writer.ToArray();
  }

  private byte[] _EncodeBidirectionalPicture(Vc1Frame samples, Vc1Frame past, Vc1Frame future) {
    var writer = new Vc1BitWriter();
    writer.WriteBits(this._frameCount++ & 3, 2);
    writer.WriteBit(false);
    writer.WriteBit(false); // PTYPE B/BI
    writer.WriteBits(0, 3); // BFRACTION 1/2
    writer.WriteBits(_PICTURE_QUANTISER, 5);
    writer.WriteBit(false); // HALFQP
    writer.WriteBit(true); // MVMODE 1-MV quarter-pel
    _WriteRawBitPlaneHeader(writer); // DIRECTMB
    _WriteRawBitPlaneHeader(writer); // SKIPMB
    writer.WriteBits(0, 2); // MVTAB
    writer.WriteBits(0, 2); // CBPTAB
    writer.WriteBit(false); // TRANSACFRM
    writer.WriteBit(false); // TRANSDCTAB

    var residual = _Residual(samples, past, future);
    var escape = new Vc1EscapeState();
    Span<int> blocks = stackalloc int[6 * 64];

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        var pattern = this._QuantiseInterMacroblock(residual, mbX, mbY, blocks);
        writer.WriteBit(true); // DIRECTBBIT
        if (pattern == 0) {
          writer.WriteBit(true); // SKIPMBBIT
          continue;
        }

        writer.WriteBit(false);
        writer.WriteCode(Vc1PredictiveTables.CodedBlockPattern0, pattern);
        this._WriteInterBlocks(writer, pattern, blocks, escape);
      }

    return writer.ToArray();
  }

  private int _QuantiseInterMacroblock(Vc1Frame residual, int mbX, int mbY, Span<int> blocks) {
    var pattern = 0;
    for (var blockIndex = 0; blockIndex < 6; ++blockIndex) {
      var isLuma = blockIndex < 4;
      var (plane, stride) = residual.PlaneOf(blockIndex);
      var x = isLuma ? (mbX * 16) + ((blockIndex & 1) * 8) : mbX * 8;
      var y = isLuma ? (mbY * 16) + ((blockIndex >> 1) * 8) : mbY * 8;
      var block = blocks.Slice(blockIndex * 64, 64);
      Vc1ForwardTransform.Quantise8x8(plane, stride, x, y, _AC_STEP_SIZE, _AC_STEP_SIZE, block);
      if (_HasAny(block))
        pattern |= 1 << (5 - blockIndex);
    }

    return pattern;
  }

  private void _WriteInterBlocks(Vc1BitWriter writer, int pattern, ReadOnlySpan<int> blocks, Vc1EscapeState escape) {
    for (var blockIndex = 0; blockIndex < 6; ++blockIndex) {
      if ((pattern & (1 << (5 - blockIndex))) == 0)
        continue;
      _WriteRunLevelCoefficients(
        writer,
        blocks.Slice(blockIndex * 64, 64),
        Vc1PredictiveTables.Inter8x8Scan,
        firstPosition: 0,
        Vc1Tables.HighRateInterCodes,
        Vc1Tables.HighRateInterEscapeIndex,
        escape);
    }
  }

  private int _EncodeIntraCodedBlockPattern(int pattern, int mbX, int mbY, byte[] coded) {
    var y0 = (pattern >> 5) & 1;
    var y1 = (pattern >> 4) & 1;
    var y2 = (pattern >> 3) & 1;
    var y3 = (pattern >> 2) & 1;

    var left = mbX > 0;
    var above = mbY > 0;
    var l1 = left ? _Coded(coded, mbX - 1, mbY, 1) : 0;
    var l3 = left ? _Coded(coded, mbX - 1, mbY, 3) : 0;
    var t2 = above ? _Coded(coded, mbX, mbY - 1, 2) : 0;
    var t3 = above ? _Coded(coded, mbX, mbY - 1, 3) : 0;
    var lt3 = left && above ? _Coded(coded, mbX - 1, mbY - 1, 3) : 0;

    var encoded = ((y0 ^ (lt3 == t2 ? l1 : t2)) << 5)
                  | ((y1 ^ (t2 == t3 ? y0 : t3)) << 4)
                  | ((y2 ^ (l1 == y0 ? l3 : y0)) << 3)
                  | ((y3 ^ (y0 == y1 ? y2 : y1)) << 2)
                  | (pattern & 0x03);

    var at = ((mbY * this._macroblockWidth) + mbX) * 4;
    coded[at] = (byte)y0;
    coded[at + 1] = (byte)y1;
    coded[at + 2] = (byte)y2;
    coded[at + 3] = (byte)y3;
    return encoded;
  }

  private int _Coded(byte[] coded, int mbX, int mbY, int block)
    => coded[(((mbY * this._macroblockWidth) + mbX) * 4) + block];

  private static bool _HasAc(ReadOnlySpan<int> block) {
    for (var i = 1; i < 64; ++i)
      if (block[i] != 0)
        return true;
    return false;
  }

  private static bool _HasAny(ReadOnlySpan<int> block) {
    foreach (var value in block)
      if (value != 0)
        return true;
    return false;
  }

  private static void _WriteIntraAcCoefficients(
    Vc1BitWriter writer,
    ReadOnlySpan<int> block,
    bool luma,
    Vc1EscapeState escape) {
    var table = luma ? Vc1Tables.HighRateIntraCodes : Vc1Tables.HighRateInterCodes;
    var escapeIndex = luma ? Vc1Tables.HighRateIntraEscapeIndex : Vc1Tables.HighRateInterEscapeIndex;
    _WriteRunLevelCoefficients(writer, block, Vc1Tables.NormalScan, 1, table, escapeIndex, escape);
  }

  private static void _WriteRunLevelCoefficients(
    Vc1BitWriter writer,
    ReadOnlySpan<int> block,
    ReadOnlySpan<byte> scan,
    int firstPosition,
    ReadOnlySpan<int> table,
    int escapeIndex,
    Vc1EscapeState escape) {
    var last = 63;
    while (last >= firstPosition && block[scan[last]] == 0)
      --last;
    if (last < firstPosition)
      throw new InvalidOperationException("A coded VC-1 block contains no non-zero coefficient.");

    var run = 0;
    for (var position = firstPosition; position <= last; ++position) {
      var level = block[scan[position]];
      if (level == 0) {
        ++run;
        continue;
      }

      writer.WriteCode(table, escapeIndex);
      writer.WriteBit(false);
      writer.WriteBit(false); // Escape mode 3.
      writer.WriteBit(position == last);

      if (escape.First) {
        writer.WriteBits(0b00011, 5); // eleven-bit conservative level field at PQUANT 3.
        writer.WriteBits(0b11, 2); // six-bit run field.
        escape.First = false;
        escape.LevelCodeSize = _MODE3_LEVEL_BITS;
        escape.RunCodeSize = _MODE3_RUN_BITS;
      }

      var magnitude = level < 0 ? -level : level;
      if ((uint)magnitude >= 1u << escape.LevelCodeSize)
        throw new InvalidOperationException(
          $"A quantised VC-1 level of {level} does not fit the {escape.LevelCodeSize}-bit Mode 3 field.");

      writer.WriteBits(run, escape.RunCodeSize);
      writer.WriteBit(level < 0);
      writer.WriteBits(magnitude, escape.LevelCodeSize);
      run = 0;
    }
  }

  private static void _WriteDcDifferential(Vc1BitWriter writer, int differential, bool luma) {
    var magnitude = differential < 0 ? -differential : differential;
    var table = luma ? Vc1Tables.LowMotionLumaDc : Vc1Tables.LowMotionChromaDc;

    if (magnitude == 0) {
      writer.WriteCode(table, 0);
      return;
    }

    if (magnitude < _DC_ESCAPE_INDEX)
      writer.WriteCode(table, magnitude);
    else {
      if (magnitude > byte.MaxValue)
        throw new InvalidOperationException($"A DC differential of {differential} does not fit VC-1's 8-bit escape at PQINDEX 3.");
      writer.WriteCode(table, _DC_ESCAPE_INDEX);
      writer.WriteBits(magnitude, 8);
    }

    writer.WriteBit(differential < 0);
  }

  private static void _WriteRawBitPlaneHeader(Vc1BitWriter writer) {
    writer.WriteBit(false); // INVERT; ignored by raw mode.
    writer.WriteBits(0, 4); // IMODE 0000b = Raw.
  }

  private static Vc1Frame _Residual(Vc1Frame samples, Vc1Frame firstReference, Vc1Frame? secondReference) {
    var result = new Vc1Frame(samples.LumaWidth / 16, samples.LumaHeight / 16);
    _ResidualPlane(samples.Luma, firstReference.Luma, secondReference?.Luma, result.Luma);
    _ResidualPlane(samples.Cb, firstReference.Cb, secondReference?.Cb, result.Cb);
    _ResidualPlane(samples.Cr, firstReference.Cr, secondReference?.Cr, result.Cr);
    return result;
  }

  private static void _ResidualPlane(int[] samples, int[] first, int[]? second, int[] result) {
    for (var i = 0; i < result.Length; ++i) {
      var prediction = second == null ? first[i] : (first[i] + second[i] + 1) >> 1;
      result[i] = samples[i] - prediction;
    }
  }

  // ============================================================================================
  // RGB -> studio-swing BT.601 YCbCr 4:2:0
  // ============================================================================================

  private Vc1Frame _ToYuv420(ReadOnlySpan<byte> rgb) {
    var result = new Vc1Frame(this._macroblockWidth, this._macroblockHeight);

    for (var y = 0; y < result.LumaHeight; ++y) {
      var sourceY = Math.Min(y, this._height - 1);
      for (var x = 0; x < result.LumaWidth; ++x) {
        var sourceX = Math.Min(x, this._width - 1);
        var at = ((sourceY * this._width) + sourceX) * 3;
        result.Luma[(y * result.LumaWidth) + x] = _Luma(rgb[at], rgb[at + 1], rgb[at + 2]);
      }
    }

    for (var y = 0; y < result.ChromaHeight; ++y)
      for (var x = 0; x < result.ChromaWidth; ++x) {
        var x0 = Math.Min(x * 2, this._width - 1);
        var x1 = Math.Min(x0 + 1, this._width - 1);
        var y0 = Math.Min(y * 2, this._height - 1);
        var y1 = Math.Min(y0 + 1, this._height - 1);

        var cb = _Chroma(rgb, x0, y0, this._width, redDifference: false)
                 + _Chroma(rgb, x1, y0, this._width, redDifference: false)
                 + _Chroma(rgb, x0, y1, this._width, redDifference: false)
                 + _Chroma(rgb, x1, y1, this._width, redDifference: false);
        var cr = _Chroma(rgb, x0, y0, this._width, redDifference: true)
                 + _Chroma(rgb, x1, y0, this._width, redDifference: true)
                 + _Chroma(rgb, x0, y1, this._width, redDifference: true)
                 + _Chroma(rgb, x1, y1, this._width, redDifference: true);

        var at = (y * result.ChromaWidth) + x;
        result.Cb[at] = (cb + 2) >> 2;
        result.Cr[at] = (cr + 2) >> 2;
      }

    return result;
  }

  private static int _Luma(int red, int green, int blue)
    => _Clamp8((((66 * red) + (129 * green) + (25 * blue) + 128) >> 8) + 16);

  private static int _Chroma(ReadOnlySpan<byte> rgb, int x, int y, int width, bool redDifference) {
    var at = ((y * width) + x) * 3;
    var red = rgb[at];
    var green = rgb[at + 1];
    var blue = rgb[at + 2];
    var scaled = redDifference
      ? (112 * red) - (94 * green) - (18 * blue)
      : (-38 * red) - (74 * green) + (112 * blue);
    return _Clamp8(((scaled + 128) >> 8) + 128);
  }

  private static int _Clamp8(int value) => value < 0 ? 0 : value > 255 ? 255 : value;
}
