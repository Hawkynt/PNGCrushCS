using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes MPEG-4 Part 2 rectangular video as independently decodable intra video object planes.
/// </summary>
/// <remarks>
/// ISO/IEC 14496-2 specifies the decoder and the syntax rather than an encoder strategy, so this writer
/// deliberately chooses the smallest useful strategy: every picture is an I-VOP, every macroblock is
/// intra coded, AC prediction is disabled, the H.263 quantisation method is used and every coefficient
/// that needs an escape uses the standard's third escape form. There is no motion search, no picture
/// reordering and no rate-control state for a caller to configure or accidentally make non-deterministic.
/// <para/>
/// <b>Why a video object layer is in every packet.</b> AVI and VFW-style Matroska descriptions name
/// MPEG-4 Part 2 but do not necessarily carry its VOL beside the stream header. Repeating this small
/// header makes every packet a legal random-access point and lets the same packets cross those
/// containers without depending on out-of-band configuration. ISO base media can still obtain the VOL
/// from the first packet when remuxing, but this encoder describes itself in the VFW form because that
/// is the neutral stream description the existing container writers can share.
/// <para/>
/// <b>Lossy.</b> The source is converted to the codec's 8-bit 4:2:0 sample grid and every block passes
/// through an 8x8 transform and a fixed quantiser. The encoder chooses the nearest reconstruction level
/// the decoder's H.263 inverse quantiser can produce rather than merely truncating a coefficient.
/// <para/>
/// The syntax is taken from ISO/IEC 14496-2 clauses 6 and 7 and Annex B. FFmpeg is used only as a
/// conformance oracle for the produced bytes; no FFmpeg implementation code is copied here.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Mpeg4VideoEncoder : IVideoCodecEncoder<Mpeg4VideoEncoder> {

  private static readonly CodecTag _MP4V = CodecTag.FromCharacters("mp4v");

  /// <summary>The thirteen-bit dimensions of a rectangular video object layer.</summary>
  private const int _MAX_DIMENSION = (1 << 13) - 1;

  /// <summary>The five-bit quantiser written in every I-VOP.</summary>
  private const int _QUANTISER = 8;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _timeIncrementResolution;
  private readonly int _timeIncrementStep;

  private MediaStreamInfo? _stream;
  private long _frameIndex;
  private long _previousSeconds;

  private Mpeg4VideoEncoder(MediaStreamInfo stream, int resolution, int step) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._macroblockHeight = (stream.Height + 15) / 16;
    this._timeIncrementResolution = resolution;
    this._timeIncrementStep = step;
  }

  public static string CodecName => "MPEG-4 Part 2 video (ISO/IEC 14496-2)";

  public static CodecTag Codec => _MP4V;

  /// <summary>Builds an all-intra MPEG-4 Part 2 encoder for a fixed rectangular picture size.</summary>
  public static Mpeg4VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("MPEG-4 Part 2 can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An MPEG-4 Part 2 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");

    if (stream.Width > _MAX_DIMENSION || stream.Height > _MAX_DIMENSION)
      throw new NotSupportedException(
        $"MPEG-4 Part 2's rectangular video object layer states each dimension in thirteen bits; "
        + $"{stream.Width}x{stream.Height} exceeds {_MAX_DIMENSION} in at least one direction.");

    var (resolution, step) = _Timing(stream.FrameRate);
    return new(stream, resolution, step);
  }

  /// <summary>Encodes one independently decodable I-VOP.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This MPEG-4 Part 2 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var source = new Mpeg4Frame(this._macroblockWidth, this._macroblockHeight);
    Mpeg4ColorConversion.FromRgb24(frame.ToRgb24(), source, this._width, this._height);

    var absoluteTicks = checked(this._frameIndex * this._timeIncrementStep);
    var seconds = absoluteTicks / this._timeIncrementResolution;
    var increment = (int)(absoluteTicks % this._timeIncrementResolution);
    var moduloSeconds = checked((int)(seconds - this._previousSeconds));

    var bytes = new Mpeg4PictureEncoder(
      source,
      this._width,
      this._height,
      this._macroblockWidth,
      this._macroblockHeight,
      _QUANTISER,
      this._timeIncrementResolution,
      increment,
      moduloSeconds).Encode();

    ++this._frameIndex;
    this._previousSeconds = seconds;

    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  /// <summary>Nothing is delayed because every input picture produces one packet immediately.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>
  /// Describes the stream as the VFW-style <c>mp4v</c> form used by AVI and accepted by Matroska.
  /// </summary>
  /// <remarks>
  /// The VOL itself is repeated in every packet, so the private bytes need only be the container's
  /// conventional BITMAPINFOHEADER. This also keeps codec configuration out of the container writer:
  /// a decoder starting with the first packet learns the same geometry and coding tools from the
  /// bitstream itself.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_MP4V.Value),
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
      Codec = _MP4V,
      Handler = _MP4V,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  /// <summary>
  /// Chooses an exact VOP clock for ordinary rational frame rates, and a close legal one otherwise.
  /// </summary>
  private static (int Resolution, int Step) _Timing(Rational frameRate) {
    if (frameRate.IsKnown && frameRate.Numerator > 0 && frameRate.Denominator > 0) {
      var divisor = _GreatestCommonDivisor(frameRate.Numerator, frameRate.Denominator);
      var resolution = frameRate.Numerator / divisor;
      var step = frameRate.Denominator / divisor;

      if (resolution is > 0 and <= ushort.MaxValue && step > 0 && step <= int.MaxValue)
        return ((int)resolution, (int)step);

      var rate = frameRate.ToDouble();
      if (double.IsFinite(rate) && rate > 0) {
        const int fallbackResolution = 60000;
        var fallbackStep = Math.Max(1, (int)Math.Round(fallbackResolution / rate, MidpointRounding.AwayFromZero));
        return (fallbackResolution, fallbackStep);
      }
    }

    return (25, 1);
  }

  private static long _GreatestCommonDivisor(long left, long right) {
    left = Math.Abs(left);
    right = Math.Abs(right);
    while (right != 0)
      (left, right) = (right, left % right);

    return left == 0 ? 1 : left;
  }
}
