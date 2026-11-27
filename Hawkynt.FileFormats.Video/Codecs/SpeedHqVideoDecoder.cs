using System;
using FileFormat.Codecs.SpeedHq;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes NewTek SpeedHQ (<c>SHQ0</c>-<c>SHQ5</c>, <c>SHQ7</c>, <c>SHQ9</c>): 4:2:0, 4:2:2 and 4:4:4,
/// without alpha, with run-length alpha and with DCT alpha, progressive, interlaced or single-field.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/speedhqdec.c</c>, LGPL-2.1-or-later; see
/// <c>SpeedHq/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Pictures without alpha are handed out in their own
/// planar YUV format. No planar format here carries alpha, so those with it are converted to
/// <see cref="PixelFormat.Rgba32"/>, the alpha plane unchanged.
/// </remarks>
public sealed class SpeedHqVideoDecoder : IVideoCodecDecoder<SpeedHqVideoDecoder> {

  private readonly SpeedHqDecoder _decoder;
  private readonly int _width;
  private readonly int _height;
  private readonly SpeedHqSubsampling _subsampling;
  private readonly bool _alpha;

  private SpeedHqVideoDecoder(MediaStreamInfo stream, SpeedHqSubsampling subsampling, SpeedHqAlpha alpha) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._subsampling = subsampling;
    this._alpha = alpha != SpeedHqAlpha.None;
    this._decoder = new(stream.Width, stream.Height, subsampling, alpha);
  }

  public static string CodecName => "NewTek SpeedHQ";

  /// <summary>Which form a FourCC names, or null for none.</summary>
  internal static (SpeedHqSubsampling Subsampling, SpeedHqAlpha Alpha)? Form(CodecTag tag)
    => tag.ToString() switch {
      "SHQ0" => (SpeedHqSubsampling.Yuv420, SpeedHqAlpha.None),
      "SHQ1" => (SpeedHqSubsampling.Yuv420, SpeedHqAlpha.RunLength),
      "SHQ2" => (SpeedHqSubsampling.Yuv422, SpeedHqAlpha.None),
      "SHQ3" => (SpeedHqSubsampling.Yuv422, SpeedHqAlpha.RunLength),
      "SHQ4" => (SpeedHqSubsampling.Yuv444, SpeedHqAlpha.None),
      "SHQ5" => (SpeedHqSubsampling.Yuv444, SpeedHqAlpha.RunLength),
      "SHQ7" => (SpeedHqSubsampling.Yuv422, SpeedHqAlpha.Dct),
      "SHQ9" => (SpeedHqSubsampling.Yuv444, SpeedHqAlpha.Dct),
      _ => null,
    };

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && Form(stream.Codec) != null;
  }

  public static SpeedHqVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (Form(stream.Codec) is not var (subsampling, alpha))
      throw new NotSupportedException($"Stream {stream.Index} is not NewTek SpeedHQ.");
    if (stream.Width < 8 || stream.Width % 8 != 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"SpeedHQ codes rows in eight-sample columns, so its picture must be a positive multiple of eight wide; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream, subsampling, alpha);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var picture = this._decoder.Decode(packet.Data.ToArray());
    var (shiftX, shiftY) = this._subsampling switch {
      SpeedHqSubsampling.Yuv420 => (1, 1),
      SpeedHqSubsampling.Yuv422 => (1, 0),
      _ => (0, 0),
    };
    var chromaWidth = (this._width + (1 << shiftX) - 1) >> shiftX;
    var chromaHeight = (this._height + (1 << shiftY) - 1) >> shiftY;
    var lumaSize = this._width * this._height;
    var chromaSize = chromaWidth * chromaHeight;

    var yuv = new byte[lumaSize + 2 * chromaSize];
    _Crop(picture.Y, picture.Width, this._width, this._height, yuv.AsSpan(0, lumaSize));
    _Crop(picture.U, picture.ChromaWidth, chromaWidth, chromaHeight, yuv.AsSpan(lumaSize, chromaSize));
    _Crop(picture.V, picture.ChromaWidth, chromaWidth, chromaHeight, yuv.AsSpan(lumaSize + chromaSize, chromaSize));

    var format = this._subsampling switch {
      SpeedHqSubsampling.Yuv420 => PixelFormat.Yuv420P8,
      SpeedHqSubsampling.Yuv422 => PixelFormat.Yuv422P8,
      _ => PixelFormat.Yuv444P8,
    };
    var image = new RawImage { Width = this._width, Height = this._height, Format = format, PixelData = yuv };
    if (!this._alpha) {
      this.LastAlpha = null;
      frame = image;
      return true;
    }

    var alpha = new byte[lumaSize];
    _Crop(picture.A!, picture.Width, this._width, this._height, alpha);
    this.LastAlpha = alpha;
    this.LastYuv = image;
    var rgb = image.ToRgb24();
    var rgba = new byte[lumaSize * 4];
    for (var i = 0; i < lumaSize; ++i) {
      rgba[i * 4] = rgb[i * 3];
      rgba[i * 4 + 1] = rgb[i * 3 + 1];
      rgba[i * 4 + 2] = rgb[i * 3 + 2];
      rgba[i * 4 + 3] = alpha[i];
    }
    frame = new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgba32, PixelData = rgba };
    return true;
  }

  /// <summary>The last picture's YUV planes and alpha plane when it has alpha, for comparing coded samples.</summary>
  internal RawImage? LastYuv { get; private set; }
  internal byte[]? LastAlpha { get; private set; }

  private static void _Crop(byte[] plane, int stride, int width, int height, Span<byte> destination) {
    for (var y = 0; y < height; ++y)
      plane.AsSpan(y * stride, width).CopyTo(destination.Slice(y * width, width));
  }
}
