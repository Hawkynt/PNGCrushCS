using System;
using System.IO;
using FileFormat.Codecs.Dxtory;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Dxtory: uncompressed BGR, RGB565, RGB555 and 4:2:0, 4:1:0 and 4:4:4 YUV frames, and the same
/// layouts coded as move-to-front symbols in slices, any of them stored bottom up.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/dxtory.c</c>, LGPL-2.1-or-later; see <c>Dxtory/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// The odd-size paths are FFmpeg's, the samples they read for positions past the edge included, with one
/// exception: for an uncompressed 4:2:0 frame of odd width FFmpeg stores each pair of rows' last luma
/// sample one place too far right and leaves the last column unset, and this decoder stores it in the last
/// column. Pictures come out in the frame's own layout — <see cref="PixelFormat.Bgr24"/>,
/// <see cref="PixelFormat.Rgb565"/>, a planar YUV format, or <see cref="PixelFormat.Rgb24"/> for the
/// coded 5- and 6-bit layouts and for uncompressed RGB555, widened as FFmpeg widens the coded ones.
/// </remarks>
public sealed class DxtoryVideoDecoder : IVideoCodecDecoder<DxtoryVideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("xtor");

  private readonly int _width;
  private readonly int _height;

  private DxtoryVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "Dxtory";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(Tag);
  }

  public static DxtoryVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Dxtory.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which Dxtory needs.");
    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = DxtoryFrame.Decode(packet.Data.Span, this._width, this._height);
    return true;
  }
}

/// <summary>
/// Encodes Dxtory losslessly: every frame one slice of move-to-front symbols, in the layout of the first
/// picture given.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no Dxtory encoder. A <see cref="PixelFormat.Yuv420P8"/>,
/// <see cref="PixelFormat.Yuv444P8"/> or <see cref="PixelFormat.Yuv410P8"/> picture is written in that
/// layout and comes back exactly; any other is written as BGR and comes back exactly as 24-bit colour.
/// A 4:2:0 picture of odd height is written as 4:4:4, each chroma sample repeated over its two-by-two
/// block, because the last row of a coded 4:2:0 frame carries only every other luma sample: it comes back
/// exactly, as 4:4:4. Later pictures are taken to the first one's layout. FFmpeg's decoder is the oracle that reads what this
/// writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class DxtoryVideoEncoder : IVideoCodecEncoder<DxtoryVideoEncoder> {

  private readonly MediaStreamInfo _requested;
  private DxtoryLayout? _layout;

  private DxtoryVideoEncoder(MediaStreamInfo requested) => this._requested = requested;

  public static string CodecName => DxtoryVideoDecoder.CodecName;
  public static CodecTag Codec => DxtoryVideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(DxtoryVideoDecoder.Tag);

  public static DxtoryVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Dxtory can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which Dxtory needs.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() {
    var requested = this._requested;
    return new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = DxtoryVideoDecoder.Tag,
      Handler = DxtoryVideoDecoder.Tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = this._layout switch { DxtoryLayout.Yuv420 => 12, DxtoryLayout.Yuv410 => 9, _ => 24 },
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._requested.Width || frame.Height != this._requested.Height)
      throw new InvalidDataException($"Dxtory geometry is fixed at {this._requested.Width}x{this._requested.Height}; received {frame.Width}x{frame.Height}.");

    var layout = this._layout ??= frame.Format switch {
      PixelFormat.Yuv420P8 when frame.Height % 2 == 1 => DxtoryLayout.Yuv444,
      PixelFormat.Yuv420P8 => DxtoryLayout.Yuv420,
      PixelFormat.Yuv444P8 => DxtoryLayout.Yuv444,
      PixelFormat.Yuv410P8 => DxtoryLayout.Yuv410,
      _ => DxtoryLayout.Bgr24,
    };
    byte[] data;
    if (layout == DxtoryLayout.Bgr24) {
      var bgr = frame.Format == PixelFormat.Bgr24 ? frame.PixelData : _Bgr(frame.ToRgb24());
      data = DxtoryEncoder.Encode(frame.Width, frame.Height, layout, bgr, default, default);
    } else {
      var format = layout switch { DxtoryLayout.Yuv420 => PixelFormat.Yuv420P8, DxtoryLayout.Yuv444 => PixelFormat.Yuv444P8, _ => PixelFormat.Yuv410P8 };
      var planar = frame.Format == format ? frame
        : frame.Format == PixelFormat.Yuv420P8 && format == PixelFormat.Yuv444P8 ? RepeatChroma(frame)
        : FastRawImageConverter.Convert(frame, format, RawImageColorInfo.Bt601Limited);
      data = DxtoryEncoder.Encode(frame.Width, frame.Height, layout, planar.GetPlaneData(0), planar.GetPlaneData(1), planar.GetPlaneData(2));
    }
    packet = new(this._requested.Index, data, PresentationTimestamp: presentationTimestamp, DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: true);
    return true;
  }

  /// <summary>A 4:2:0 picture as 4:4:4, each chroma sample repeated over the two-by-two block it stands for.</summary>
  internal static RawImage RepeatChroma(RawImage frame) {
    int width = frame.Width, height = frame.Height, chromaWidth = (width + 1) / 2;
    var pixels = new byte[width * height * 3];
    frame.GetPlaneData(0).CopyTo(pixels);
    for (var plane = 1; plane < 3; ++plane) {
      var chroma = frame.GetPlaneData(plane);
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          pixels[plane * width * height + y * width + x] = chroma[(y / 2) * chromaWidth + x / 2];
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = pixels, ColorInfo = frame.ColorInfo };
  }

  /// <summary>A copy in blue-green-red order; the picture given is left as it was.</summary>
  private static byte[] _Bgr(byte[] rgb) {
    var bgr = new byte[rgb.Length];
    for (var i = 0; i < rgb.Length; i += 3) {
      bgr[i] = rgb[i + 2];
      bgr[i + 1] = rgb[i + 1];
      bgr[i + 2] = rgb[i];
    }
    return bgr;
  }
}
