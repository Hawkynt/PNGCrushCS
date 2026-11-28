using System;
using FileFormat.Codecs.Vp4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes On2 VP4 (<c>VP40</c>): key and inter frames, with VP4's macroblock coding, motion-vector
/// codes, per-coefficient end-of-block runs, DC prediction, quantisation tables and
/// motion-compensation loop filter.
/// </summary>
/// <remarks>
/// Converted from the VP4 paths of FFmpeg's <c>libavcodec/vp3.c</c> and <c>vp3dsp.c</c>
/// (LGPL-2.1-or-later), with the tables of <c>vp4data.h</c>; see
/// <c>Vp4/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. VP4 states no picture size the decoder can use — its key
/// frames carry a macroblock count FFmpeg only checks — so the container's is taken, padded to whole
/// macroblocks for decoding and cropped from the top left on the way out, as FFmpeg crops it. Hands
/// out <see cref="PixelFormat.Yuv420P8"/>.
/// </remarks>
public sealed class Vp4VideoDecoder : IVideoCodecDecoder<Vp4VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VP40");

  private readonly Vp4Decoder _decoder;
  private readonly int _width;
  private readonly int _height;

  private Vp4VideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._decoder = new(width, height);
  }

  public static string CodecName => "On2 VP4";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Vp4VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not On2 VP4.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Stream {stream.Index} states a size of {stream.Width}x{stream.Height}; VP4 needs the container's picture size.");
    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }

    var planes = this._decoder.Decode(packet.Data.Span);
    frame = ToYuv420(planes, this._decoder.CodedWidth, this._decoder.CodedHeight, this._width, this._height);
    return true;
  }

  /// <summary>The visible top-left of bottom-up planes, top row first.</summary>
  internal static RawImage ToYuv420(byte[][] planes, int codedWidth, int codedHeight, int width, int height) {
    var chromaWidth = (width + 1) / 2;
    var chromaHeight = (height + 1) / 2;
    var data = new byte[width * height + 2 * chromaWidth * chromaHeight];
    for (var y = 0; y < height; ++y)
      Array.Copy(planes[0], (codedHeight - 1 - y) * codedWidth, data, y * width, width);

    var at = width * height;
    for (var plane = 1; plane < 3; ++plane)
      for (var y = 0; y < chromaHeight; ++y, at += chromaWidth)
        Array.Copy(planes[plane], (codedHeight / 2 - 1 - y) * (codedWidth / 2), data, at, chromaWidth);

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = data };
  }
}
