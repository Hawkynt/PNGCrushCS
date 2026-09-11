using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes ISO/IEC 11172-2 MPEG-1 video as progressive 4:2:0 intra pictures.</summary>
/// <remarks>
/// MPEG-1 defines I, P and B pictures, but it does not require an encoder to use all three. This
/// encoder deliberately writes only I pictures: every picture is independently decodable, there is
/// no hidden frame reordering and no motion-estimation heuristic pretending to be part of the
/// standard. The trade is compression ratio, not interoperability.
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

  /// <summary>Codes one independently decodable I picture.</summary>
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

    this._WritePicture(writer, planes);

    var current = new CodedPacket(
      this._requested.Index,
      writer.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);

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

  private void _WritePicture(MpegBitWriter writer, Yuv420Planes planes) {
    writer.StartCode(MpegStartCode.Picture);
    writer.Write(this._pictureIndex & 0x3FF, 10); // temporal_reference
    writer.Write(MpegPictureDecoder.IntraCoded, 3);
    writer.Write(0xFFFF, 16);                    // vbv_delay: unspecified
    writer.WriteBit(0);                          // extra_bit_picture

    // One slice beginning in the first macroblock row. A slice may continue across rows; using one
    // for the whole picture also keeps pictures taller than the 175 start-code row values encodable.
    writer.StartCode(MpegStartCode.FirstSlice);
    writer.Write(_QUANTISER_SCALE, 5);
    writer.WriteBit(0); // extra_bit_slice

    Span<int> block = stackalloc int[64];
    var dcY = 128;
    var dcCb = 128;
    var dcCr = 128;

    for (var macroblockY = 0; macroblockY < this._macroblockHeight; ++macroblockY)
    for (var macroblockX = 0; macroblockX < this._macroblockWidth; ++macroblockX) {
      writer.WriteCode("1"); // macroblock_address_increment = 1, Table B.1
      writer.WriteCode("1"); // I-picture macroblock_type = intra, Table B.2

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
