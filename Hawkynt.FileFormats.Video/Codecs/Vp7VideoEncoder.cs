using System;
using System.IO;
using FileFormat.Codecs.Vp7;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes On2 VP7 (<c>VP70</c>, or <c>VP71</c> when that is asked for) in profile 0: a key frame
/// every twelfth picture, intra and inter macroblocks between them, the golden frame refreshed every
/// fourth interframe, and a change of overall brightness coded as a fade.
/// </summary>
/// <remarks>
/// See <see cref="Vp7Encoder"/>. VP7 is lossy by construction — every block goes through the
/// transform and a quantiser, here index 24 of 0-127 with the normal loop filter at level 12. FFmpeg
/// has no encoder; its decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Vp7VideoEncoder : IVideoCodecEncoder<Vp7VideoEncoder> {

  private const int _KEYFRAME_INTERVAL = 12;

  private static readonly CodecTag _Default = CodecTag.FromCharacters("VP70");
  private static readonly CodecTag _Alternative = CodecTag.FromCharacters("VP71");

  private readonly MediaStreamInfo _stream;
  private readonly Vp7Encoder _encoder;
  private readonly int _width;
  private readonly int _height;
  private int _pictureNumber;

  private Vp7VideoEncoder(MediaStreamInfo requested, CodecTag tag, Vp7EncoderSettings settings) {
    this._width = requested.Width;
    this._height = requested.Height;
    this._encoder = new(requested.Width, requested.Height, settings);
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 24,
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public static string CodecName => "On2 VP7";
  public static CodecTag Codec => _Default;

  public static bool Accepts(MediaStreamInfo stream)
    => stream.Codec.EqualsIgnoringCase(_Default) || stream.Codec.EqualsIgnoringCase(_Alternative);

  public static Vp7VideoEncoder Create(MediaStreamInfo stream) => Create(stream, new());

  internal static Vp7VideoEncoder Create(MediaStreamInfo stream, Vp7EncoderSettings settings) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("On2 VP7 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0 || stream.Width > 4095 || stream.Height > 4095)
      throw new NotSupportedException(
        $"VP7 states its picture size in twelve bits, so 1x1 to 4095x4095; stream {stream.Index} states {stream.Width}x{stream.Height}.");

    return new(stream, Accepts(stream) ? stream.Codec : _Default, settings);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>The encoder's own reference, for comparing what a decoder reads back.</summary>
  internal Vp7Encoder Encoder => this._encoder;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"VP7 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var yuv = frame.Format == PixelFormat.Yuv420P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv420P8);
    var codedWidth = (this._width + 15) & ~15;
    var codedHeight = (this._height + 15) & ~15;
    var chromaWidth = (this._width + 1) / 2;
    var chromaHeight = (this._height + 1) / 2;
    var luma = _Pad(yuv.GetPlaneData(0), this._width, this._height, codedWidth, codedHeight);
    var cb = _Pad(yuv.GetPlaneData(1), chromaWidth, chromaHeight, codedWidth / 2, codedHeight / 2);
    var cr = _Pad(yuv.GetPlaneData(2), chromaWidth, chromaHeight, codedWidth / 2, codedHeight / 2);

    var keyFrame = this._pictureNumber % _KEYFRAME_INTERVAL == 0;
    var data = this._encoder.Encode(luma, cb, cr, keyFrame);
    packet = new(this._stream.Index, data, PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: keyFrame);
    ++this._pictureNumber;
    return true;
  }

  /// <summary>Copies a plane into a larger one, repeating its last column and row into the margin.</summary>
  private static byte[] _Pad(ReadOnlySpan<byte> plane, int width, int height, int paddedWidth, int paddedHeight) {
    var padded = new byte[paddedWidth * paddedHeight];
    for (var y = 0; y < paddedHeight; ++y) {
      var from = Math.Min(y, height - 1) * width;
      var row = padded.AsSpan(y * paddedWidth, paddedWidth);
      plane.Slice(from, width).CopyTo(row);
      row[width..].Fill(plane[from + width - 1]);
    }
    return padded;
  }
}
