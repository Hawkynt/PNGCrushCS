using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Windows Media Video 7 (<c>WMV1</c>): intra and predicted pictures, the three run-level
/// tables chosen per picture or per macroblock, WMV1's own scans and DC steps, its extension header
/// with flip-flop rounding, its variable-length escapes, and the inter-intra prediction small
/// low-rate pictures use.
/// </summary>
/// <remarks>
/// WMV1 is Microsoft MPEG-4 version 3 with those differences, so this is the package's version 3
/// decoder taught them, converted from the WMV1 paths of FFmpeg's <c>libavcodec/msmpeg4dec.c</c> and
/// <c>msmpeg4.c</c> (LGPL-2.1-or-later); see <c>MsMpeg4/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. It is held
/// to FFmpeg's integer <c>simple_idct</c>, which is what FFmpeg decodes WMV1 with, and hands out
/// <see cref="PixelFormat.Yuv420P8"/>. The picture size comes from the container: WMV1 states none.
/// </remarks>
public sealed class Wmv1VideoDecoder : IVideoCodecDecoder<Wmv1VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WMV1");

  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private Mpeg4Frame? _reference;
  private MsMpeg4PictureHeader? _previous;
  private int _sliceHeight;
  private int _rounding;

  private Wmv1VideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
    this._sliceHeight = this._macroblockHeight;
  }

  public static string CodecName => "Windows Media Video 7 (WMV1)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Wmv1VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Windows Media Video 7.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Stream {stream.Index} states a size of {stream.Width}x{stream.Height}. WMV1 carries no picture size in its "
        + "bitstream, so the container's is the only one there is.");
    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }

    var reader = new Mpeg4BitReader(packet.Data.Span);
    var header = MsMpeg4PictureHeader.Parse(
      ref reader, MsMpeg4Version.Wmv1, this._macroblockHeight, this._sliceHeight, this._previous, this._width * this._height);
    if (header.CodingType != MsMpeg4PictureHeader.IntraCoded && this._reference == null)
      throw new InvalidDataException("This WMV1 stream begins with a predicted picture; decoding has to start at an intra picture.");

    this._sliceHeight = header.SliceHeight;
    this._rounding = header.CodingType == MsMpeg4PictureHeader.IntraCoded ? 1
      : header.FlipFlopRounding ? this._rounding ^ 1 : 0;
    this._previous = header;

    var picture = MsMpeg4PictureDecoder.BeginPicture(
      MsMpeg4Version.Wmv1, header, new(this._macroblockWidth, this._macroblockHeight), this._reference,
      this._macroblockWidth, this._macroblockHeight, this._rounding);
    picture.DecodePicture(ref reader);
    if (reader.BitsRemaining < 0)
      throw new InvalidDataException(
        $"This WMV1 picture's macroblocks run {-reader.BitsRemaining} bit(s) past the end of its {packet.Data.Length}-byte packet. "
        + "The packet is truncated or damaged; FFmpeg conceals such a picture, and this decoder does not guess.");
    picture.Target.PadBorders();
    this._reference = picture.Target;

    frame = ToYuv420(picture.Target, this._width, this._height);
    return true;
  }

  /// <summary>Copies the visible part of a decoded frame out as planar 4:2:0.</summary>
  internal static RawImage ToYuv420(Mpeg4Frame decoded, int width, int height) {
    var chromaWidth = (width + 1) / 2;
    var chromaHeight = (height + 1) / 2;
    var data = new byte[width * height + 2 * chromaWidth * chromaHeight];
    for (var y = 0; y < height; ++y)
      decoded.Luma.AsSpan(decoded.LumaOrigin + y * decoded.LumaStride, width).CopyTo(data.AsSpan(y * width));

    var u = width * height;
    var v = u + chromaWidth * chromaHeight;
    for (var y = 0; y < chromaHeight; ++y) {
      decoded.Cb.AsSpan(decoded.ChromaOrigin + y * decoded.ChromaStride, chromaWidth).CopyTo(data.AsSpan(u + y * chromaWidth));
      decoded.Cr.AsSpan(decoded.ChromaOrigin + y * decoded.ChromaStride, chromaWidth).CopyTo(data.AsSpan(v + y * chromaWidth));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = data };
  }
}
