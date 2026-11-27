using System;
using System.IO;
using FileFormat.Codecs.Vp5;
using FileFormat.Codecs.Vp6;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes On2 VP6 in all three of its forms: <c>VP60</c>/<c>VP61</c>/<c>VP62</c> as AVI carries it,
/// coded bottom-up; <c>VP6F</c> as Flash carries it, top-down; and <c>VP6A</c>, Flash's form with a
/// second VP6 bitstream coding an alpha plane.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/vp6.c</c> and <c>vp56.c</c>, LGPL-2.1-or-later; see
/// <c>Vp6/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. This is the codec <c>codec-investigations.md</c> recorded
/// as desynchronising on a magnitude bit when built from tables alone; converted from FFmpeg it
/// follows the bitstream through every coding mode.
/// <para/>
/// <b>Picture size.</b> The coded size is a whole number of macroblocks. What is shown is smaller in
/// two cases, both FFmpeg's: Flash states a per-frame crop byte — its high nibble columns and its low
/// nibble rows removed from the right and the bottom — which the FLV reader hands over as the packet's
/// container data; and a container that states a picture size rounding up to the coded size is taken
/// at its word. Otherwise the coded size is the picture size.
/// <para/>
/// Plain VP6 is handed out as <see cref="PixelFormat.Yuv420P8"/>, the coded samples themselves. VP6A has
/// no planar format with alpha to go into, so it is converted to <see cref="PixelFormat.Rgba32"/>, the
/// alpha plane unchanged.
/// </remarks>
public sealed class Vp6VideoDecoder : IVideoCodecDecoder<Vp6VideoDecoder> {

  private static readonly CodecTag[] _Flipped = [
    CodecTag.FromCharacters("VP60"), CodecTag.FromCharacters("VP61"), CodecTag.FromCharacters("VP62"),
  ];

  private static readonly CodecTag _Flash = CodecTag.FromCharacters("VP6F");
  private static readonly CodecTag _FlashAlpha = CodecTag.FromCharacters("VP6A");

  private readonly Vp6Decoder _decoder = new(lumaOnly: false);
  private readonly Vp6Decoder? _alpha;
  private readonly bool _flipped;
  private readonly int _streamIndex;
  private readonly int _containerWidth;
  private readonly int _containerHeight;
  private readonly bool _containerHasExtraData;
  private int _crop = -1;
  private int _width;
  private int _height;

  private Vp6VideoDecoder(MediaStreamInfo stream) {
    this._streamIndex = stream.Index;
    this._flipped = Array.Exists(_Flipped, tag => stream.Codec.EqualsIgnoringCase(tag));
    if (stream.Codec.EqualsIgnoringCase(_FlashAlpha))
      this._alpha = new(lumaOnly: true);

    this._containerWidth = stream.Width;
    this._containerHeight = stream.Height;

    // FFmpeg's extradata is what follows a BITMAPINFOHEADER, and a one-byte CodecPrivate is Flash's crop.
    var privateData = stream.CodecPrivateData;
    if (privateData.Length == 1)
      this._crop = privateData.Span[0];
    this._containerHasExtraData = privateData.Length is 1 or > 40;
  }

  public static string CodecName => "On2 VP6";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video
      && (Array.Exists(_Flipped, tag => stream.Codec.EqualsIgnoringCase(tag))
          || stream.Codec.EqualsIgnoringCase(_Flash)
          || stream.Codec.EqualsIgnoringCase(_FlashAlpha));
  }

  public static Vp6VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not On2 VP6.");
    return new(stream);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.ContainerPrivateData.Length == 1)
      this._crop = packet.ContainerPrivateData.Span[0];

    var data = packet.Data;
    var alphaOffset = data.Length;
    if (this._alpha != null) {
      if (data.Length < 3)
        throw new InvalidDataException($"VP6A stream {this._streamIndex} has a packet too short for its alpha offset.");
      var span = data.Span;
      alphaOffset = (span[0] << 16) | (span[1] << 8) | span[2];
      data = data[3..];
      if (alphaOffset > data.Length)
        throw new InvalidDataException($"VP6A stream {this._streamIndex} states an alpha bitstream past the end of its packet.");
    }

    var picture = this._decoder.Decode(data[..alphaOffset]);
    if (this._decoder.SizeChanged || this._width == 0)
      this._SetSize();

    Vp5Frame? alpha = null;
    if (this._alpha != null) {
      alpha = this._alpha.Decode(data[alphaOffset..]);
      if (this._alpha.MacroblockWidth != this._decoder.MacroblockWidth || this._alpha.MacroblockHeight != this._decoder.MacroblockHeight)
        throw new InvalidDataException($"VP6A stream {this._streamIndex} codes its alpha plane at a different size from its picture.");
    }

    this.LastPlanes = this._Crop(picture, alpha);
    frame = this._ToRawImage(this.LastPlanes);
    return true;
  }

  /// <summary>The last picture's planes as decoded, cropped and the right way up: Y, U, V and, for VP6A, A.</summary>
  internal (byte[] Y, byte[] U, byte[] V, byte[]? A) LastPlanes { get; private set; }

  internal int Width => this._width;
  internal int Height => this._height;

  private void _SetSize() {
    var codedWidth = this._decoder.MacroblockWidth * 16;
    var codedHeight = this._decoder.MacroblockHeight * 16;

    if (!this._containerHasExtraData && this._crop < 0
        && ((this._containerWidth + 15) & ~15) == codedWidth && ((this._containerHeight + 15) & ~15) == codedHeight
        && this._containerWidth > 0 && this._containerHeight > 0) {
      this._width = this._containerWidth;
      this._height = this._containerHeight;
      return;
    }

    this._width = codedWidth;
    this._height = codedHeight;
    if (this._crop >= 0) {
      this._width -= this._crop >> 4;
      this._height -= this._crop & 0x0F;
    }
  }

  private (byte[] Y, byte[] U, byte[] V, byte[]? A) _Crop(Vp5Frame picture, Vp5Frame? alpha) {
    var chromaWidth = (this._width + 1) >> 1;
    var chromaHeight = (this._height + 1) >> 1;
    return (
      this._CropPlane(picture.Luma, picture.LumaWidth, picture.LumaHeight, this._width, this._height),
      this._CropPlane(picture.Cb, picture.ChromaWidth, picture.ChromaHeight, chromaWidth, chromaHeight),
      this._CropPlane(picture.Cr, picture.ChromaWidth, picture.ChromaHeight, chromaWidth, chromaHeight),
      alpha == null ? null : this._CropPlane(alpha.Luma, alpha.LumaWidth, alpha.LumaHeight, this._width, this._height));
  }

  /// <summary>
  /// Takes the top-left corner of a plane, after turning a bottom-up plane over: the bottom-up form's
  /// first coded row is the picture's last.
  /// </summary>
  private byte[] _CropPlane(byte[] samples, int planeWidth, int planeHeight, int width, int height) {
    var result = new byte[width * height];
    for (var y = 0; y < height; ++y) {
      var sourceRow = this._flipped ? planeHeight - 1 - y : y;
      samples.AsSpan(sourceRow * planeWidth, width).CopyTo(result.AsSpan(y * width));
    }
    return result;
  }

  private RawImage _ToRawImage((byte[] Y, byte[] U, byte[] V, byte[]? A) planes) {
    var yuv = new byte[planes.Y.Length + planes.U.Length + planes.V.Length];
    planes.Y.CopyTo(yuv, 0);
    planes.U.CopyTo(yuv, planes.Y.Length);
    planes.V.CopyTo(yuv, planes.Y.Length + planes.U.Length);
    var image = new RawImage { Width = this._width, Height = this._height, Format = PixelFormat.Yuv420P8, PixelData = yuv };
    if (planes.A == null)
      return image;

    var rgb = image.ToRgb24();
    var rgba = new byte[this._width * this._height * 4];
    for (var i = 0; i < this._width * this._height; ++i) {
      rgba[i * 4] = rgb[i * 3];
      rgba[i * 4 + 1] = rgb[i * 3 + 1];
      rgba[i * 4 + 2] = rgb[i * 3 + 2];
      rgba[i * 4 + 3] = planes.A[i];
    }
    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgba32, PixelData = rgba };
  }
}
