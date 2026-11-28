using System;
using System.IO;
using FileFormat.Codecs.Fmvc;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes FM Screen Capture: a bottom-up picture that key frames replace and other frames change by XOR in
/// blocks, both packed by one of two LZ77 schemes.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/fmvc.c</c>, LGPL-2.1-or-later; see <c>Fmvc/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// Type 2 is LZO1X; type 1 is FM Screen Capture's own. The stream's bits a pixel choose the picture:
/// 24 is <see cref="PixelFormat.Bgr24"/>, 32 <see cref="PixelFormat.Bgra32"/>, and 16, RGB555, comes out
/// widened to <see cref="PixelFormat.Rgb24"/>.
/// </remarks>
public sealed class FmvcVideoDecoder : IVideoCodecDecoder<FmvcVideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("FMVC");

  private readonly FmvcFrame _frame;

  private FmvcVideoDecoder(FmvcFrame frame) => this._frame = frame;

  public static string CodecName => "FM Screen Capture";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(Tag);
  }

  public static FmvcVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not FM Screen Capture.");
    if (stream.BitsPerPixel is not (16 or 24 or 32))
      throw new NotSupportedException($"FM Screen Capture is 16, 24 or 32 bits a pixel; stream {stream.Index} states {stream.BitsPerPixel}.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which FM Screen Capture needs.");
    return new(new(stream.Width, stream.Height, stream.BitsPerPixel));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._frame.Decode(packet.Data.Span);
    var pixels = this._frame.Picture();
    int width = this._frame.Width, height = this._frame.Height;
    frame = this._frame.BytesPerPixel switch {
      3 => new() { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = pixels },
      4 => new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels },
      _ => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = _Widen555(pixels, width * height) },
    };
    return true;
  }

  private static byte[] _Widen555(byte[] packed, int count) {
    var rgb = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      var v = packed[i * 2] | packed[i * 2 + 1] << 8;
      int r = (v >> 10) & 0x1F, g = (v >> 5) & 0x1F, b = v & 0x1F;
      rgb[i * 3] = (byte)((r << 3) | (r >> 2));
      rgb[i * 3 + 1] = (byte)((g << 3) | (g >> 2));
      rgb[i * 3 + 2] = (byte)((b << 3) | (b >> 2));
    }
    return rgb;
  }
}

/// <summary>
/// Encodes FM Screen Capture losslessly at 24 bits a pixel: a key frame, then only the blocks that changed,
/// as XOR, packed by FM Screen Capture's own LZ77 scheme.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no FM Screen Capture encoder. The picture comes back exactly as 24-bit
/// colour. A picture under 49 rows, or under 49 pixels wide, has no block in FM Screen Capture's layout
/// and FFmpeg refuses it, so it is refused here. FFmpeg's decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class FmvcVideoEncoder : IVideoCodecEncoder<FmvcVideoEncoder> {

  private readonly MediaStreamInfo _stream;
  private readonly FmvcEncoder _encoder;
  private bool _started;

  private FmvcVideoEncoder(MediaStreamInfo requested) {
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = FmvcVideoDecoder.Tag,
      Handler = FmvcVideoDecoder.Tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 24,
      Language = requested.Language,
      Name = requested.Name,
    };
    this._encoder = new(requested.Width, requested.Height);
  }

  public static string CodecName => FmvcVideoDecoder.CodecName;
  public static CodecTag Codec => FmvcVideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(FmvcVideoDecoder.Tag);

  public static FmvcVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("FM Screen Capture can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which FM Screen Capture needs.");
    _ = new FmvcFrame(stream.Width, stream.Height, 24); // refuses a picture too small for one block
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"FM Screen Capture geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    byte[] bgr;
    if (frame.Format == PixelFormat.Bgr24)
      bgr = frame.PixelData;
    else {
      var rgb = frame.ToRgb24();
      bgr = new byte[rgb.Length];
      for (var i = 0; i < rgb.Length; i += 3) {
        bgr[i] = rgb[i + 2];
        bgr[i + 1] = rgb[i + 1];
        bgr[i + 2] = rgb[i];
      }
    }
    var key = !this._started;
    this._started = true;
    packet = new(this._stream.Index, this._encoder.Encode(bgr), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: key);
    return true;
  }
}
