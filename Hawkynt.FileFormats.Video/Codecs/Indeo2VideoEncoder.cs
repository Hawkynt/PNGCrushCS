using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Intel Indeo 2 (<c>RT21</c>) as independently decodable intra frames.
/// </summary>
/// <remarks>
/// Indeo 2 codes YUV 4:1:0: luminance at full size and Cb/Cr at one quarter of the width and one
/// quarter of the height. Each plane is a stream of Huffman-coded pairs against one of four fixed
/// sample/delta tables; the writer tries every table and keeps the least-error result. Runs are emitted
/// wherever the predictor is already at least as good as every pair the selected table can state.
/// <para/>
/// <b>Every packet is a key frame.</b> The format also has inter frames, but they are only a way to
/// compress differences against the previous picture. Emitting the complete intra form is valid and
/// makes packet loss or seeking unable to poison a later picture. No undocumented rate-control or
/// inter-frame heuristic is invented merely to save bytes in a codec from the early 1990s.
/// <para/>
/// <b>Input.</b> Eight-bit planar YUV keeps its luminance samples and has chrominance averaged onto the
/// codec's 4:1:0 grid. Other picture layouts pass through the package's shared ITU-R BT.601
/// studio-swing conversion before the same averaging, so this codec does not grow a second YUV path
/// with subtly different rounding from the rest of the video package.
/// <para/>
/// The 48-byte packet header is only partly understood publicly: the reference decoder hard-codes its
/// length and reads the intra flag at byte 18 plus the two table selectors at byte 0x22. The writer
/// therefore zeroes the uninterpreted fields instead of fabricating semantics for them. FFmpeg's
/// decoder consumes this form, and the AVI stream description follows the 24-bit <c>RT21</c>
/// <c>BITMAPINFOHEADER</c> used by the real-file fixtures in this repository.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Indeo2VideoEncoder : IVideoCodecEncoder<Indeo2VideoEncoder> {

  private static readonly CodecTag _TAG = CodecTag.FromCharacters("RT21");
  private const int _CHROMA_DIVISOR = 4;
  private const int _WIDTH_MULTIPLE = 8;
  private const int _AVI_BIT_DEPTH = 24;

  private readonly MediaStreamInfo _stream;
  private readonly Indeo2FrameEncoder _frameEncoder;
  private readonly int _width;
  private readonly int _height;
  private readonly int _lumaSamples;
  private readonly int _chromaSamples;

  private Indeo2VideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._lumaSamples = checked(stream.Width * stream.Height);
    this._chromaSamples = checked((stream.Width / _CHROMA_DIVISOR) * (stream.Height / _CHROMA_DIVISOR));
    this._frameEncoder = new(stream.Width, stream.Height);

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: stream.Width,
      Height: stream.Height,
      Planes: 1,
      BitsPerPixel: _AVI_BIT_DEPTH,
      Compression: unchecked((int)_TAG.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);
    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _TAG,
      Handler = _TAG,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = _AVI_BIT_DEPTH,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Intel Indeo 2";

  public static CodecTag Codec => _TAG;

  public static Indeo2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Intel Indeo 2 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Indeo 2 encoder needs a positive picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width % _WIDTH_MULTIPLE != 0 || stream.Height % _CHROMA_DIVISOR != 0)
      throw new NotSupportedException(
        $"Indeo 2 codes sample pairs into chrominance planes a quarter of the picture size, so the width "
        + $"must divide by {_WIDTH_MULTIPLE} and the height by {_CHROMA_DIVISOR}; "
        + $"{stream.Width}x{stream.Height} does not.");
    if ((long)stream.Width * stream.Height * 3 > int.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is larger than the managed buffers an Indeo 2 frame can hold.");
    if (stream.BitsPerPixel is not (0 or _AVI_BIT_DEPTH))
      throw new NotSupportedException(
        $"The RT21 AVI representation is described as {_AVI_BIT_DEPTH} bits per pixel; stream {stream.Index} asks for "
        + $"{stream.BitsPerPixel}.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Indeo 2 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");

    var planes = RawYuvPlanes.Subsampled(frame, this._width / _CHROMA_DIVISOR, this._height / _CHROMA_DIVISOR);
    var luma = planes.AsSpan(0, this._lumaSamples);
    var cb = planes.AsSpan(this._lumaSamples, this._chromaSamples);
    var cr = planes.AsSpan(this._lumaSamples + this._chromaSamples, this._chromaSamples);
    var data = this._frameEncoder.EncodeIntra(luma, cb, cr);

    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;
}
