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
/// <b>Input.</b> <see cref="PixelFormat.Yuv444P8"/> is subsampled by averaging each 4x4 chrominance
/// block with round-to-nearest integer arithmetic. <see cref="PixelFormat.Rgb24"/> first passes through
/// the package's ITU-R BT.601 studio-swing converter and is then subsampled the same way. Other formats
/// are refused rather than silently dropping alpha or narrowing deeper samples.
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
  private const int _CHROMA_SUBSAMPLING = 4;
  private const int _WIDTH_MULTIPLE = 8;
  private const int _AVI_BIT_DEPTH = 24;

  private readonly MediaStreamInfo _stream;
  private readonly Indeo2FrameEncoder _frameEncoder;
  private readonly int _width;
  private readonly int _height;

  private Indeo2VideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
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
    if (stream.Width % _WIDTH_MULTIPLE != 0 || stream.Height % _CHROMA_SUBSAMPLING != 0)
      throw new NotSupportedException(
        $"Indeo 2 codes sample pairs into chrominance planes a quarter of the picture size, so the width "
        + $"must divide by {_WIDTH_MULTIPLE} and the height by {_CHROMA_SUBSAMPLING}; "
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
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var yuv = _ToYuv444(frame);
    var luma = yuv.GetPlaneData(0);
    var cb = _DownsampleChroma(yuv.GetPlaneData(1), this._width, this._height);
    var cr = _DownsampleChroma(yuv.GetPlaneData(2), this._width, this._height);
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

  private static RawImage _ToYuv444(RawImage frame) => frame.Format switch {
    PixelFormat.Yuv444P8 => frame,
    PixelFormat.Rgb24 => FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited),
    _ => throw new NotSupportedException(
      $"Indeo 2 takes {PixelFormat.Yuv444P8} for explicit 4x4 chroma subsampling or {PixelFormat.Rgb24} through "
      + $"the decoder's BT.601 studio-swing display convention; {frame.Format} is refused rather than reduced silently."),
  };

  private static byte[] _DownsampleChroma(ReadOnlySpan<byte> source, int width, int height) {
    var chromaWidth = width >> 2;
    var chromaHeight = height >> 2;
    var result = new byte[chromaWidth * chromaHeight];

    for (var y = 0; y < chromaHeight; ++y) {
      var sourceY = y << 2;
      var destinationRow = y * chromaWidth;
      for (var x = 0; x < chromaWidth; ++x) {
        var sourceX = x << 2;
        var sum = 0;
        for (var dy = 0; dy < _CHROMA_SUBSAMPLING; ++dy) {
          var at = (sourceY + dy) * width + sourceX;
          sum += source[at] + source[at + 1] + source[at + 2] + source[at + 3];
        }

        result[destinationRow + x] = (byte)((sum + 8) >> 4);
      }
    }

    return result;
  }
}
