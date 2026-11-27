using System;
using FileFormat.Codecs.Ylc;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes the YUY2 Lossless Codec (<c>YLC0</c>): four Huffman tables built from the packet's own counts,
/// table-coded and literal YUYV groups, runs of unchanged groups, and gradient prediction.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/ylc.c</c>, LGPL-2.1-or-later; see
/// <c>Ylc/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Pictures come out as <see cref="PixelFormat.Yuv422P8"/>,
/// the planar form of the packed YUYV the codec codes. YUYV pairs samples, so the width must be even.
/// </remarks>
public sealed class YlcVideoDecoder : IVideoCodecDecoder<YlcVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("YLC0");

  private readonly int _width;
  private readonly int _height;

  private YlcVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "YUY2 Lossless Codec (YLC)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static YlcVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not YLC.");
    if (stream.Width <= 0 || stream.Height <= 0 || (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"YLC codes YUYV pairs, so its picture is a positive even number of samples wide; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }

    var packed = YlcCodec.Decode(packet.Data.Span, this._width, this._height);
    frame = ToPlanar(packed, this._width, this._height);
    return true;
  }

  internal static RawImage ToPlanar(ReadOnlySpan<byte> packed, int width, int height) {
    var luma = width * height;
    var chroma = width / 2 * height;
    var data = new byte[luma + 2 * chroma];
    for (var i = 0; i < width * height / 2; ++i) {
      data[2 * i] = packed[4 * i];
      data[2 * i + 1] = packed[4 * i + 2];
      data[luma + i] = packed[4 * i + 1];
      data[luma + chroma + i] = packed[4 * i + 3];
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv422P8, PixelData = data };
  }
}
