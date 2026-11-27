using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Vp6;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes On2 VP6 as <c>VP6F</c> (Flash, top-down), <c>VP60</c>/<c>VP61</c>/<c>VP62</c> (AVI,
/// bottom-up) or <c>VP6A</c> (Flash with an alpha plane coded as a second VP6 bitstream).
/// </summary>
/// <remarks>
/// FFmpeg has no VP6 encoder, so nothing is translated: <see cref="Vp6Encoder"/> is an original
/// inverse of the decoder adapted from FFmpeg, and FFmpeg's decoder is the oracle that reads what it
/// writes. VP6 is a lossy DCT codec and the encoder says so: a picture comes back within the
/// quantiser's step, at a fixed quantiser of 56 on VP6's 0-63 scale, finer towards 63.
/// <para/>
/// A picture whose size is not a whole number of macroblocks is coded at the next size that is, the
/// last row and column repeated, and the shown size is stated the way each container states it:
/// Flash by the per-frame crop byte, which goes out as each packet's container data and as the
/// stream's one-byte codec private data, and AVI by its own picture size.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Vp6VideoEncoder : IVideoCodecEncoder<Vp6VideoEncoder> {

  private const int _KEYFRAME_INTERVAL = 12;
  private const int _QUANTIZER = 56;

  private static readonly CodecTag _Flash = CodecTag.FromCharacters("VP6F");
  private static readonly CodecTag _FlashAlpha = CodecTag.FromCharacters("VP6A");
  private static readonly CodecTag[] _Flipped = [
    CodecTag.FromCharacters("VP60"), CodecTag.FromCharacters("VP61"), CodecTag.FromCharacters("VP62"),
  ];

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly bool _flipped;
  private readonly byte _crop;
  private readonly Vp6Encoder _encoder;
  private readonly Vp6Encoder? _alpha;
  private int _pictureNumber;

  private Vp6VideoEncoder(MediaStreamInfo requested, CodecTag tag) {
    this._width = requested.Width;
    this._height = requested.Height;
    this._codedWidth = (requested.Width + 15) & ~15;
    this._codedHeight = (requested.Height + 15) & ~15;
    this._flipped = Array.Exists(_Flipped, t => tag.EqualsIgnoringCase(t));
    this._crop = (byte)(((this._codedWidth - this._width) << 4) | (this._codedHeight - this._height));
    this._encoder = new(this._codedWidth / 16, this._codedHeight / 16, _QUANTIZER, lumaOnly: false);
    if (tag.EqualsIgnoringCase(_FlashAlpha))
      this._alpha = new(this._codedWidth / 16, this._codedHeight / 16, _QUANTIZER, lumaOnly: true);

    var flash = !this._flipped;
    byte[] codecPrivate;
    string codecId;
    if (flash) {
      codecPrivate = [this._crop];
      codecId = "";
    } else {
      var header = new BitmapInfoHeader(
        HeaderSize: BitmapInfoHeader.StructSize,
        Width: requested.Width,
        Height: requested.Height,
        Planes: 1,
        BitsPerPixel: 24,
        Compression: unchecked((int)tag.Value),
        ImageSize: 0,
        XPixelsPerMeter: 0,
        YPixelsPerMeter: 0,
        ColorsUsed: 0,
        ImportantColors: 0);
      codecPrivate = new byte[BitmapInfoHeader.StructSize];
      header.WriteTo(codecPrivate);
      codecId = "V_MS/VFW/FOURCC";
    }

    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      CodecId = codecId,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = this._alpha != null ? 32 : 24,
      CodecPrivateData = codecPrivate,
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public static string CodecName => "On2 VP6";
  public static CodecTag Codec => _Flash;

  public static bool Accepts(MediaStreamInfo stream)
    => stream.Codec.EqualsIgnoringCase(_Flash) || stream.Codec.EqualsIgnoringCase(_FlashAlpha)
       || Array.Exists(_Flipped, t => stream.Codec.EqualsIgnoringCase(t));

  public static Vp6VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("On2 VP6 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"VP6 needs a positive picture size; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    if (stream.Width > 255 * 16 || stream.Height > 255 * 16)
      throw new NotSupportedException(
        $"VP6 states its size in eight-bit macroblock counts, so 4080x4080 is the largest picture; stream {stream.Index} states {stream.Width}x{stream.Height}.");

    var tag = Accepts(stream) ? stream.Codec : _Flash;
    return new(stream, tag);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"VP6 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var yuv = frame.Format == PixelFormat.Yuv420P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv420P8);
    var luma = this._Pad(yuv.GetPlaneData(0), this._width, this._height, this._codedWidth, this._codedHeight);
    var cb = this._Pad(yuv.GetPlaneData(1), (this._width + 1) / 2, (this._height + 1) / 2, this._codedWidth / 2, this._codedHeight / 2);
    var cr = this._Pad(yuv.GetPlaneData(2), (this._width + 1) / 2, (this._height + 1) / 2, this._codedWidth / 2, this._codedHeight / 2);

    var keyFrame = this._pictureNumber % _KEYFRAME_INTERVAL == 0;
    var main = this._encoder.Encode(luma, cb, cr, keyFrame);
    byte[] data;

    if (this._alpha == null) {
      data = main;
    } else {
      var alphaPlane = new byte[this._width * this._height];
      var rgba = frame.ToRgba32();
      for (var i = 0; i < alphaPlane.Length; ++i)
        alphaPlane[i] = rgba[i * 4 + 3];
      var alphaLuma = this._Pad(alphaPlane, this._width, this._height, this._codedWidth, this._codedHeight);
      var neutral = new byte[this._codedWidth / 2 * (this._codedHeight / 2)];
      neutral.AsSpan().Fill(128);
      var alpha = this._alpha.Encode(alphaLuma, neutral, neutral, keyFrame);

      data = new byte[3 + main.Length + alpha.Length];
      data[0] = (byte)(main.Length >> 16);
      data[1] = (byte)(main.Length >> 8);
      data[2] = (byte)main.Length;
      main.CopyTo(data, 3);
      alpha.CopyTo(data, 3 + main.Length);
    }

    ++this._pictureNumber;
    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: keyFrame,
      ContainerPrivateData: this._flipped ? default : new byte[] { this._crop });
    return true;
  }

  /// <summary>Extends a plane to the coded size by repeating its last column and row, turning it over for the bottom-up form.</summary>
  private byte[] _Pad(ReadOnlySpan<byte> source, int width, int height, int codedWidth, int codedHeight) {
    var result = new byte[codedWidth * codedHeight];
    for (var y = 0; y < codedHeight; ++y) {
      var sourceRow = Math.Min(y, height - 1);
      var targetRow = this._flipped ? codedHeight - 1 - y : y;
      var row = source.Slice(sourceRow * width, width);
      row.CopyTo(result.AsSpan(targetRow * codedWidth));
      result.AsSpan(targetRow * codedWidth + width, codedWidth - width).Fill(row[width - 1]);
    }
    return result;
  }
}
