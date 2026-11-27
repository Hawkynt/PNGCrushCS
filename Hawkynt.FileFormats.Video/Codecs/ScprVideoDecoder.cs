using System;
using System.IO;
using FileFormat.Codecs.Scpr;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes all three generations of ScreenPressor and its solid fills: key frames of runs — a new colour
/// coded through adaptive contexts, or a copy from the left, above, a gradient or above-left — and inter
/// frames that recode or move 16x16 blocks of the last picture.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/scpr.c</c>, <c>scpr.h</c> and <c>scpr3.c</c>, LGPL-2.1-or-later;
/// see <c>Scpr/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. The first two generations code through range coders,
/// the third through a byte-wise rANS coder with colour models that change shape as they learn. An inter
/// frame that changes nothing yields no picture, as in FFmpeg.
/// Pictures come out as <see cref="PixelFormat.Bgr24"/>, or for a 16-bit stream <see cref="PixelFormat.Rgb24"/>
/// with each five-bit sample shifted up three as FFmpeg shifts it, the last row coded shown first.
/// </remarks>
public sealed class ScprVideoDecoder : IVideoCodecDecoder<ScprVideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("SCPR");

  private readonly ScprDecoder _decoder;
  private readonly int _width;
  private readonly int _height;
  private readonly bool _sixteen;

  private ScprVideoDecoder(int width, int height, int bits) {
    this._decoder = new(width, height, bits);
    this._width = width;
    this._height = height;
    this._sixteen = bits == 16;
  }

  public static string CodecName => "ScreenPressor";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(Tag);
  }

  public static ScprVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not ScreenPressor.");
    if (stream.BitsPerPixel is not (16 or 24 or 32))
      throw new NotSupportedException($"ScreenPressor is 16, 24 or 32 bits a pixel; stream {stream.Index} states {stream.BitsPerPixel}.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which ScreenPressor needs.");
    return new(stream.Width, stream.Height, stream.BitsPerPixel);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (!this._decoder.Decode(packet.Data.Span)) {
      frame = null!;
      return false;
    }
    var picture = this._decoder.Picture;
    var pixels = new byte[this._width * this._height * 3];
    for (var y = 0; y < this._height; ++y)
      for (var x = 0; x < this._width; ++x) {
        var value = picture[(this._height - 1 - y) * this._width + x];
        var at = (y * this._width + x) * 3;
        var shift = this._sixteen ? 3 : 0;
        pixels[at] = (byte)((value & 0xFF) << shift);
        pixels[at + 1] = (byte)(((value >> 8) & 0xFF) << shift);
        pixels[at + 2] = (byte)(((value >> 16) & 0xFF) << shift);
      }
    frame = new() { Width = this._width, Height = this._height, Format = this._sixteen ? PixelFormat.Rgb24 : PixelFormat.Bgr24, PixelData = pixels };
    return true;
  }
}

/// <summary>
/// Encodes ScreenPressor's second generation losslessly at 24 bits: a key frame of runs, then inter frames
/// that leave unchanged blocks alone and recode the rest as runs.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no ScreenPressor encoder. Each run is the longest a copy from the left,
/// above, the last picture, a gradient or above-left reproduces, or else a run of one new colour. FFmpeg's
/// decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class ScprVideoEncoder : IVideoCodecEncoder<ScprVideoEncoder> {

  private readonly MediaStreamInfo _stream;
  private readonly Func<uint[], byte[]> _encode;
  private bool _started;

  private ScprVideoEncoder(MediaStreamInfo requested, bool thirdGeneration) {
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = ScprVideoDecoder.Tag,
      Handler = ScprVideoDecoder.Tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 24,
      Language = requested.Language,
      Name = requested.Name,
    };
    if (thirdGeneration)
      this._encode = new Scpr3Encoder(requested.Width, requested.Height).Encode;
    else
      this._encode = new ScprEncoder(requested.Width, requested.Height).Encode;
  }

  public static string CodecName => ScprVideoDecoder.CodecName;
  public static CodecTag Codec => ScprVideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(ScprVideoDecoder.Tag);

  public static ScprVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("ScreenPressor can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0 || (long)((stream.Width + 15) / 16) * ((stream.Height + 15) / 16) > 65535)
      throw new NotSupportedException($"ScreenPressor numbers its blocks in sixteen bits; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream, thirdGeneration: false);
  }

  /// <summary>An encoder writing the third generation, <c>scpr3.c</c>'s; the conformance tests read it back through FFmpeg.</summary>
  internal static ScprVideoEncoder CreateThirdGeneration(MediaStreamInfo stream) {
    _ = Create(stream);
    return new(stream, thirdGeneration: true);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    int width = this._stream.Width, height = this._stream.Height;
    if (frame.Width != width || frame.Height != height)
      throw new InvalidDataException($"ScreenPressor geometry is fixed at {width}x{height}; received {frame.Width}x{frame.Height}.");
    var rgb = frame.ToRgb24();
    // Coded rows run bottom up, each pixel blue, green, red from its lowest byte.
    var pixels = new uint[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[(height - 1 - y) * width + x] = (uint)(rgb[at + 2] | rgb[at + 1] << 8 | rgb[at] << 16);
      }
    var key = !this._started;
    this._started = true;
    packet = new(this._stream.Index, this._encode(pixels), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: key);
    return true;
  }
}
