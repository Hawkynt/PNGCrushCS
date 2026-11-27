using System;
using FileFormat.Codecs.Vp7;
using FileFormat.Codecs.Vp8;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes On2 VP7 (<c>VP70</c>, <c>VP71</c>) in both profiles: key and inter frames, the previous and
/// golden references, fading, the normal and simple loop filters and a restated scan order.
/// </summary>
/// <remarks>
/// Converted from the VP7 paths of FFmpeg's <c>libavcodec/vp8.c</c> and <c>vp8dsp.c</c>,
/// LGPL-2.1-or-later; see <c>Vp7/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Pictures are handed out as
/// <see cref="PixelFormat.Yuv420P8"/> at the size the key frame states. An empty packet is a frame
/// the encoder dropped and gives no picture, as FFmpeg gives none.
/// </remarks>
public sealed class Vp7VideoDecoder : IVideoCodecDecoder<Vp7VideoDecoder> {

  private static readonly CodecTag[] _Tags = [CodecTag.FromCharacters("VP70"), CodecTag.FromCharacters("VP71")];

  private readonly Vp7Decoder _decoder = new();

  private Vp7VideoDecoder() { }

  public static string CodecName => "On2 VP7";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && Array.Exists(_Tags, tag => stream.Codec.EqualsIgnoringCase(tag));
  }

  public static Vp7VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not On2 VP7.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }

    var picture = this._decoder.Decode(packet.Data.Span);
    frame = ToYuv420(picture, this._decoder.Width, this._decoder.Height);
    return true;
  }

  /// <summary>Whether the last packet faded the previous picture's reference in place; see <see cref="Vp7Decoder.FadedInPlace"/>.</summary>
  internal bool FadedInPlace => this._decoder.FadedInPlace;

  internal (int Alpha, int Beta) Fade => this._decoder.Fade;

  /// <summary>Crops a macroblock-aligned picture to its stated size as planar 4:2:0.</summary>
  internal static RawImage ToYuv420(Vp8Frame picture, int width, int height) {
    var chromaWidth = (width + 1) >> 1;
    var chromaHeight = (height + 1) >> 1;
    var data = new byte[width * height + 2 * chromaWidth * chromaHeight];
    for (var y = 0; y < height; ++y)
      picture.Luma.AsSpan(y * picture.LumaWidth, width).CopyTo(data.AsSpan(y * width));

    var u = width * height;
    var v = u + chromaWidth * chromaHeight;
    for (var y = 0; y < chromaHeight; ++y) {
      picture.Cb.AsSpan(y * picture.ChromaWidth, chromaWidth).CopyTo(data.AsSpan(u + y * chromaWidth));
      picture.Cr.AsSpan(y * picture.ChromaWidth, chromaWidth).CopyTo(data.AsSpan(v + y * chromaWidth));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = data };
  }
}
