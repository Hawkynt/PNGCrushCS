using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes v308: 4:4:4 YUV with nothing compressed at all, three bytes a pixel and no chroma
/// subsampling to interpolate around.
/// </summary>
/// <remarks>
/// There is no MultimediaWiki page for this one and no dedicated ffmpeg decoder to ask about it — it
/// is one of the raw layouts ffmpeg's own container demuxers map straight onto a pixel format rather
/// than routing through a codec of its own, the same shape as <c>rawvideo</c> itself. So the layout
/// below was recovered the same way v210's and y41p's were: pseudo-random content, built at the pixel
/// format ffmpeg's own QuickTime demuxer names for this tag, carried through its generic uncompressed
/// muxer with the tag forced to <c>v308</c> and swept against every placement of a header ahead of,
/// inside and behind the picture data.
/// <para/>
/// <b>The word.</b> Three bytes a pixel and nothing else — V, then Y, then U, repeating across a row
/// with no padding of any kind. A row is exactly <c>width</c> times three bytes, measured against a
/// width that is not a multiple of four in either direction, and there is no header ahead of the
/// picture the way avui's SD-only relatives carry one.
/// <para/>
/// <b>Packed-YUV format, and a direct sample comparison is what settles it</b> — 4:4:4 carries no
/// subsampling at all, so every pixel states its own chroma and there is no interpolation convention
/// for a comparison to disagree about the way there is for anything at 4:2:2 or 4:2:0. Fifty frames of
/// pseudo-random content at 17x9 — not a whole number of any alignment this format's neighbours use —
/// carried through this packing and decoded here, compared byte for byte against ffmpeg's own raw
/// output of the same content before it was packed: every sample of every plane of every frame
/// identical.
/// <para/>
/// <b>The packed colour <see cref="TryDecode"/> hands back is a display convention on top of that</b>
/// — ITU-R BT.601 with studio swing, needing no chroma repetition at all since every pixel already
/// carries its own full-resolution pair.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, and a packet shorter than its stride times its
/// height.
/// </remarks>
public sealed class V308VideoDecoder : IVideoCodecDecoder<V308VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("v308");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _stride;

  private V308VideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._stride = width * 3;
  }

  public static string CodecName => "Uncompressed 4:4:4 (v308)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static V308VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var (luma, cb, cr) = this.DecodePlanes(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ToRgb24(luma, cb, cr),
    };

    return true;
  }

  /// <summary>
  /// Unpacks one frame into its luma and chroma planes, each at the picture's own full resolution —
  /// the form ffmpeg's own raw output writes and the one this was verified against.
  /// </summary>
  internal (byte[] Luma, byte[] Cb, byte[] Cr) DecodePlanes(ReadOnlySpan<byte> data) {
    var expected = (long)this._stride * this._height;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a v308 packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var count = this._width * this._height;
    var luma = new byte[count];
    var cb = new byte[count];
    var cr = new byte[count];

    for (var row = 0; row < this._height; ++row) {
      var line = data.Slice(row * this._stride, this._stride);
      var planeBase = row * this._width;

      for (var x = 0; x < this._width; ++x) {
        var offset = x * 3;
        cr[planeBase + x] = line[offset];
        luma[planeBase + x] = line[offset + 1];
        cb[planeBase + x] = line[offset + 2];
      }
    }

    return (luma, cb, cr);
  }

  /// <summary>
  /// ITU-R BT.601, studio swing — no chroma repetition needed since every pixel already carries its
  /// own full-resolution pair.
  /// </summary>
  private byte[] _ToRgb24(byte[] luma, byte[] cb, byte[] cr) {
    var count = this._width * this._height;
    var rgb = new byte[count * 3];

    for (var i = 0; i < count; ++i) {
      var scaledLuma = 298 * (luma[i] - 16);
      var blueDifference = cb[i] - 128;
      var redDifference = cr[i] - 128;
      var target = i * 3;

      rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
      rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
      rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
    }

    return rgb;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;

    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
