using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes progressive MPEG-2 video (ITU-T H.262 / ISO/IEC 13818-2) as Main Profile at Main Level,
/// 8-bit 4:2:0 intra pictures.
/// </summary>
/// <remarks>
/// The writer deliberately starts with the smallest complete H.262 coding mode rather than pretending
/// that motion estimation, interlacing or rate control exist: every frame is an independently decodable
/// I picture, split into one slice per macroblock row, using the default quantisation matrices, linear
/// quantiser scale, zig-zag scan, Table B.14 and eight-bit intra DC precision. Those are all standard
/// choices, so a decoder sees ordinary MPEG-2 rather than a private subset on the wire.
/// <para/>
/// <b>Lossy.</b> H.262 quantises DCT coefficients and has no lossless mode. The encoder begins at
/// quantiser_scale_code 4 and raises it only when necessary to remain inside Main Level's 15 Mbit/s
/// rate bound. A picture that still cannot fit at code 31 is refused rather than labelled Main Level
/// while violating the level it declares.
/// <para/>
/// Every packet carries a repeated sequence header and sequence extension before its picture. H.262
/// explicitly permits sequence headers to repeat, and doing so makes every packet this encoder marks
/// as a key frame independently decodable by the elementary-stream reader as well as by containers.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Mpeg2VideoEncoder : IVideoCodecEncoder<Mpeg2VideoEncoder> {

  private static readonly CodecTag _MPG2 = CodecTag.FromCharacters("MPG2");

  private const int _MAX_WIDTH = 720;
  private const int _MAX_HEIGHT = 576;
  private const long _MAX_LUMA_SAMPLE_RATE = 10_368_000;
  private const long _MAX_BIT_RATE = 15_000_000;
  private const int _VBV_BUFFER_SIZE_VALUE = 112; // 112 * 16,384 = 1,835,008 bits, MP@ML's bound.
  private const int _BIT_RATE_VALUE = (int)(_MAX_BIT_RATE / 400);

  private static readonly int[] _QuantiserScaleCandidates = [4, 6, 8, 12, 16, 20, 24, 28, 31];

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _frameRateCode;
  private readonly Rational _frameRate;

  private long _pictureNumber;
  private MediaStreamInfo? _stream;

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

  /// <summary>Builds an intra-only Main-Profile/Main-Level MPEG-2 encoder.</summary>
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

  /// <summary>Codes one independently decodable I picture.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This MPEG-2 stream is {this._width}x{this._height}; a {frame.Width}x{frame.Height} picture arrived. "
        + "A sequence cannot change dimensions while the encoder is open.");

    var source = this._ToFrame(frame);
    byte[]? bytes = null;
    foreach (var quantiserScaleCode in _QuantiserScaleCandidates) {
      bytes = this._EncodePicture(source, quantiserScaleCode);
      if (_FitsMainLevel(bytes.Length, this._frameRate))
        break;

      bytes = null;
    }

    if (bytes == null)
      throw new InvalidDataException(
        $"The {this._width}x{this._height} MPEG-2 picture exceeds Main Level's {_MAX_BIT_RATE / 1_000_000} Mbit/s "
        + "rate bound even at quantiser_scale_code 31.");

    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);

    ++this._pictureNumber;
    return true;
  }

  /// <summary>Nothing is reordered or held back.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

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

  private byte[] _EncodePicture(MpegFrame source, int quantiserScaleCode) {
    var writer = new MpegBitWriter();
    this._WriteSequenceHeader(writer);
    this._WriteSequenceExtension(writer);
    this._WritePictureHeader(writer);
    this._WritePictureCodingExtension(writer);

    var dcPredictor = new int[3];
    for (var row = 0; row < this._macroblockHeight; ++row) {
      writer.WriteStartCode((byte)(MpegStartCode.FirstSlice + row));
      writer.WriteBits(quantiserScaleCode, 5);
      writer.WriteBit(0); // extra_bit_slice
      dcPredictor[0] = dcPredictor[1] = dcPredictor[2] = 128;

      for (var column = 0; column < this._macroblockWidth; ++column) {
        MpegVlcTables.MacroblockAddressIncrement.Write(writer, 1);
        MpegVlcTables.IntraMacroblockType.Write(writer, MpegVlcTables.TypeIntra);

        for (var block = 0; block < 6; ++block)
          this._WriteIntraBlock(writer, source, column, row, block, quantiserScaleCode, dcPredictor);
      }
    }

    return writer.ToArray();
  }

  private void _WriteSequenceHeader(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.SequenceHeader);
    writer.WriteBits(this._width, 12);
    writer.WriteBits(this._height, 12);
    writer.WriteBits(1, 4);                    // aspect_ratio_information: square samples
    writer.WriteBits(this._frameRateCode, 4);
    writer.WriteBits(_BIT_RATE_VALUE, 18);     // 400 bit/s units
    writer.WriteBit(1);                        // marker_bit
    writer.WriteBits(_VBV_BUFFER_SIZE_VALUE, 10);
    writer.WriteBit(0);                        // constrained_parameters_flag, zero in MPEG-2
    writer.WriteBit(0);                        // load_intra_quantiser_matrix: use Table 7-4
    writer.WriteBit(0);                        // load_non_intra_quantiser_matrix: use Table 7-5
  }

  private static void _WriteSequenceExtension(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.Extension);
    writer.WriteBits(1, 4);                    // extension_start_code_identifier: sequence_extension
    writer.WriteBits(0x48, 8);                 // Main Profile @ Main Level
    writer.WriteBit(1);                        // progressive_sequence
    writer.WriteBits(1, 2);                    // chroma_format: 4:2:0
    writer.WriteBits(0, 2);                    // horizontal_size_extension
    writer.WriteBits(0, 2);                    // vertical_size_extension
    writer.WriteBits(0, 12);                   // bit_rate_extension
    writer.WriteBit(1);                        // marker_bit
    writer.WriteBits(0, 8);                    // vbv_buffer_size_extension
    writer.WriteBit(0);                        // low_delay
    writer.WriteBits(0, 2);                    // frame_rate_extension_n
    writer.WriteBits(0, 5);                    // frame_rate_extension_d
  }

  private void _WritePictureHeader(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.Picture);
    writer.WriteBits((int)(this._pictureNumber & 0x3FF), 10); // temporal_reference
    writer.WriteBits(1, 3);                    // picture_coding_type: I
    writer.WriteBits(0xFFFF, 16);               // vbv_delay: unspecified for this VBR stream
    writer.WriteBit(0);                        // extra_bit_picture
  }

  private static void _WritePictureCodingExtension(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.Extension);
    writer.WriteBits(8, 4);                    // picture_coding_extension
    writer.WriteBits(15, 4);                   // f_code[0][0], unused by an I picture
    writer.WriteBits(15, 4);                   // f_code[0][1]
    writer.WriteBits(15, 4);                   // f_code[1][0]
    writer.WriteBits(15, 4);                   // f_code[1][1]
    writer.WriteBits(0, 2);                    // intra_dc_precision: 8 bits
    writer.WriteBits(3, 2);                    // picture_structure: frame picture
    writer.WriteBit(0);                        // top_field_first
    writer.WriteBit(1);                        // frame_pred_frame_dct
    writer.WriteBit(0);                        // concealment_motion_vectors
    writer.WriteBit(0);                        // q_scale_type: linear
    writer.WriteBit(0);                        // intra_vlc_format: Table B.14
    writer.WriteBit(0);                        // alternate_scan: zig-zag
    writer.WriteBit(0);                        // repeat_first_field
    writer.WriteBit(1);                        // chroma_420_type
    writer.WriteBit(1);                        // progressive_frame
    writer.WriteBit(0);                        // composite_display_flag
  }

  private void _WriteIntraBlock(
    MpegBitWriter writer, MpegFrame source, int macroblockX, int macroblockY, int blockIndex,
    int quantiserScaleCode, int[] dcPredictor) {
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
    ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight,
    Span<byte> target, int targetWidth, int targetHeight) {
    for (var y = 0; y < targetHeight; ++y) {
      var sourceRow = Math.Min(y, sourceHeight - 1);
      var sourceAt = sourceRow * sourceWidth;
      var targetAt = y * targetWidth;
      source.Slice(sourceAt, sourceWidth).CopyTo(target[targetAt..]);
      target.Slice(targetAt + sourceWidth, targetWidth - sourceWidth).Fill(source[sourceAt + sourceWidth - 1]);
    }
  }

  private static (byte[] Plane, int Width, int X, int Y, int Component) _BlockOf(
    MpegFrame frame, int macroblockX, int macroblockY, int blockIndex) {
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
      if ((Int128)requested.Numerator * candidate.Rate.Denominator == (Int128)candidate.Rate.Numerator * requested.Denominator)
        return candidate;

    throw new NotSupportedException(
      $"This Main-Level MPEG-2 encoder writes the base frame rates 24000/1001, 24, 25, 30000/1001 and 30 fps; "
      + $"{requested} was requested.");
  }
}
