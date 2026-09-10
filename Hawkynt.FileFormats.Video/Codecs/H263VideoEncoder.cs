using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes baseline H.263 video as intra pictures in the five standard source formats of Table 5.
/// </summary>
/// <remarks>
/// <b>Scope.</b> The decoder beside this reads baseline intra and predicted pictures plus Sorenson
/// Spark. The encoder writes the deliberately smaller common denominator: ITU-T H.263 baseline
/// I-pictures, no annexes, no optional GOB headers, and only sub-QCIF, QCIF, CIF, 4CIF and 16CIF.
/// Custom sizes require the extended PTYPE of clause 5.1.4, which the decoder itself does not yet read,
/// so accepting one here would create a stream this package could not read back.
/// <para/>
/// <b>Lossy by definition.</b> Every block is transformed and quantised. A fixed quantiser is used for
/// the whole picture because there is no rate-control contract in the codec interface; changing it per
/// macroblock would add syntax without serving a caller-visible decision. Every packet is an I-picture,
/// which makes the first writer complete without importing a motion estimator into the same change and
/// makes every packet independently decodable.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H263VideoEncoder : IVideoCodecEncoder<H263VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("H263");

  /// <summary>A middle-of-the-range baseline quantiser, stated once in every picture header.</summary>
  private const int _QUANTISER = 8;

  /// <summary>TR is eight bits wide in the baseline picture header.</summary>
  private const int _TEMPORAL_REFERENCE_PERIOD = 256;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _sourceFormat;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;

  private int _pictureIndex;
  private MediaStreamInfo? _stream;

  private H263VideoEncoder(MediaStreamInfo stream, int sourceFormat) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._sourceFormat = sourceFormat;
    this._macroblockWidth = this._width / 16;
    this._macroblockHeight = this._height / 16;
  }

  public static string CodecName => "H.263 (ITU-T H.263 baseline, and Sorenson Spark)";

  public static CodecTag Codec => _Tag;

  /// <summary>Creates an encoder for one of H.263 Table 5's five standard picture formats.</summary>
  public static H263VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("H.263 can only encode a video stream.");

    var sourceFormat = (stream.Width, stream.Height) switch {
      (128, 96) => 1,
      (176, 144) => 2,
      (352, 288) => 3,
      (704, 576) => 4,
      (1408, 1152) => 5,
      _ => 0,
    };

    if (sourceFormat != 0)
      return new(stream, sourceFormat);

    throw new NotSupportedException(
      $"Baseline H.263 codes the five standard source formats 128x96 (sub-QCIF), 176x144 (QCIF), 352x288 (CIF), "
      + $"704x576 (4CIF) and 1408x1152 (16CIF); {stream.Width}x{stream.Height} was asked for. Other sizes require "
      + "the extended PTYPE of ITU-T H.263 clause 5.1.4, which this codec does not implement.");
  }

  /// <summary>Encodes one complete intra picture.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This H.263 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived. "
        + "The source format is stated in every baseline picture header, and this stream does not change it.");

    var source = this._ToPlanes(frame);
    var bytes = new H263PictureEncoder(
      this._sourceFormat,
      this._pictureIndex % _TEMPORAL_REFERENCE_PERIOD,
      _QUANTISER,
      source).Encode();

    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);

    ++this._pictureIndex;
    return true;
  }

  /// <summary>Nothing is reordered or delayed: every input picture is one output packet.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>Describes the VFW-style H.263 stream that AVI and Matroska use for the <c>H263</c> tag.</summary>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
    };
  }

  private H263Frame _ToPlanes(RawImage frame) {
    var planes = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, this._width / 2, this._height / 2);
    var source = new H263Frame(this._macroblockWidth, this._macroblockHeight);
    var lumaSamples = this._width * this._height;
    var chromaSamples = lumaSamples / 4;

    Array.Copy(planes, 0, source.Luma, 0, lumaSamples);
    Array.Copy(planes, lumaSamples, source.Cb, 0, chromaSamples);
    Array.Copy(planes, lumaSamples + chromaSamples, source.Cr, 0, chromaSamples);

    return source;
  }
}
