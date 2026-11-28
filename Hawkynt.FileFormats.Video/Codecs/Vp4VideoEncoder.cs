using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.Vp4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes On2 VP4 (<c>VP40</c>): a key frame every thirtieth picture and inter frames between them
/// whose blocks are copied or coded as a residual in place.
/// </summary>
/// <remarks>
/// The package's own encoder — FFmpeg has none to convert — writing the syntax of the VP4 decoder
/// beside it at quality index 43, the index the one public VP4 film uses. Lossy by construction: every
/// coded block goes through the transform and VP4's quantiser. FFmpeg's decoder is the oracle that
/// reads what it writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Vp4VideoEncoder : IVideoCodecEncoder<Vp4VideoEncoder> {

  private const int _KEY_FRAME_INTERVAL = 30;
  private const int _QUALITY = 43;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VP40");

  private readonly MediaStreamInfo _requested;
  private readonly Vp4Encoder _encoder;
  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private int _sinceKeyFrame;
  private MediaStreamInfo? _stream;

  private Vp4VideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._codedWidth = (stream.Width + 15) & ~15;
    this._codedHeight = (stream.Height + 15) & ~15;
    this._encoder = new(stream.Width, stream.Height, _QUALITY);
  }

  public static string CodecName => Vp4VideoDecoder.CodecName;

  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static Vp4VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("On2 VP4 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"A VP4 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"This VP4 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    var key = this._sinceKeyFrame == 0;
    var bytes = this._encoder.Encode(this._Planes(frame), key);
    this._sinceKeyFrame = (this._sinceKeyFrame + 1) % _KEY_FRAME_INTERVAL;
    packet = new(this._requested.Index, bytes, PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, IsKeyFrame: key);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize, Width: this._width, Height: this._height, Planes: 1, BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value), ImageSize: 0, XPixelsPerMeter: 0, YPixelsPerMeter: 0, ColorsUsed: 0, ImportantColors: 0);
    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);
    return this._stream = new() {
      Index = this._requested.Index, Kind = MediaStreamKind.Video, Codec = _Tag, Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC", TimeBase = this._requested.TimeBase, FrameRate = this._requested.FrameRate,
      Width = this._width, Height = this._height, BitsPerPixel = 24, CodecPrivateData = format,
    };
  }

  /// <summary>The picture as 4:2:0 planes of the padded size, bottom row first, edges repeated.</summary>
  private byte[][] _Planes(RawImage frame) {
    var planes = new Mpeg4Frame(this._codedWidth / 16, this._codedHeight / 16);
    Mpeg4ColorConversion.FromRgb24(frame.ToRgb24(), planes, this._width, this._height);
    var result = new byte[3][];
    result[0] = new byte[this._codedWidth * this._codedHeight];
    result[1] = new byte[this._codedWidth * this._codedHeight / 4];
    result[2] = new byte[this._codedWidth * this._codedHeight / 4];
    for (var y = 0; y < this._codedHeight; ++y)
      Array.Copy(planes.Luma, planes.LumaOrigin + y * planes.LumaStride, result[0], (this._codedHeight - 1 - y) * this._codedWidth, this._codedWidth);
    for (var y = 0; y < this._codedHeight / 2; ++y) {
      var target = (this._codedHeight / 2 - 1 - y) * (this._codedWidth / 2);
      Array.Copy(planes.Cb, planes.ChromaOrigin + y * planes.ChromaStride, result[1], target, this._codedWidth / 2);
      Array.Copy(planes.Cr, planes.ChromaOrigin + y * planes.ChromaStride, result[2], target, this._codedWidth / 2);
    }

    return result;
  }
}
