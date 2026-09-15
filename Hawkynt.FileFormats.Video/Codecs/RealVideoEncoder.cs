using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H263;
using FileFormat.Codecs.RealVideo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes interoperable RealVideo 1 (<c>RV10</c>) intra pictures.</summary>
/// <remarks>
/// RealVideo 1 replaces H.263's picture/group headers and reuses its macroblock and block layers. This
/// encoder deliberately writes the original revision-zero syntax: one explicitly positioned run per
/// picture, no PB frames, and intra pictures only. Intra-only is larger than predictive coding but is
/// a complete, independently decodable write path and avoids inventing a second motion-estimation
/// implementation beside the ones already used by other codecs.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class RealVideoEncoder : IVideoCodecEncoder<RealVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("RV10");
  private static readonly byte[] _PrivateData = [0, 0, 0, 8, 0x10, 0, 0, 0];
  private const int _QUANTISER = 8;
  private const int _MAX_MACROBLOCKS = 4095;

  /// <summary>Pictures per group: one intra picture and eleven predicted ones.</summary>
  /// <remarks>
  /// RealVideo 1 states no group length of its own, so this is a rate decision rather than a syntax
  /// one. Twelve bounds how far a decoder that joined mid-stream has to wait, and how far a lost
  /// packet can poison later pictures, to eleven pictures.
  /// </remarks>
  private const int _GROUP_SIZE = 12;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private MediaStreamInfo? _stream;

  /// <summary>Driven on this encoder's own output, to hand back the picture a predicted one uses.</summary>
  private readonly RealVideoDecoder _reconstruction;

  /// <summary>How far into the current group the next picture is; zero codes an intra picture.</summary>
  private int _groupPosition;

  private RealVideoEncoder(MediaStreamInfo requested) {
    this._requested = requested;
    this._width = requested.Width;
    this._height = requested.Height;
    this._macroblockWidth = this._width / 16;
    this._macroblockHeight = this._height / 16;
    this._reconstruction = RealVideoDecoder.Create(this.DescribeStream());
  }

  /// <summary>
  /// The same name the decoder answers to: the pair name one codec, and which revisions this writer
  /// produces belongs in its documentation rather than in its identity.
  /// </summary>
  public static string CodecName => "RealVideo 1 (RV10/RV13)";

  public static CodecTag Codec => _Tag;

  public static RealVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("RealVideo can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0 || (stream.Width & 15) != 0 || (stream.Height & 15) != 0)
      throw new NotSupportedException(
        $"This RealVideo 1 encoder needs a positive picture size aligned to 16x16 macroblocks; {stream.Width}x{stream.Height} was asked for.");

    var macroblocks = (long)(stream.Width / 16) * (stream.Height / 16);
    if (macroblocks > _MAX_MACROBLOCKS)
      throw new NotSupportedException(
        $"This RealVideo 1 encoder writes one explicitly positioned run, whose 12-bit macroblock-count field can hold at most {_MAX_MACROBLOCKS}; "
        + $"{stream.Width}x{stream.Height} contains {macroblocks} macroblocks.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This RealVideo stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    var source = this._ToPlanes(frame);

    // The first picture of every group is intra, and so is any picture with nothing to predict from.
    var reference = this._reconstruction.CurrentReference;
    var isIntra = this._groupPosition == 0 || reference == null;

    var bytes = new RealVideoPictureEncoder(source, _QUANTISER, isIntra ? null : reference).Encode();
    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: isIntra,
      FragmentOffsets: [0]);

    // Reconstruct by decoding what was just written, so the next predicted picture is built on the
    // samples its decoder will hold rather than on the frame that was handed in.
    this._reconstruction.TryDecode(packet, out _);

    this._groupPosition = (this._groupPosition + 1) % _GROUP_SIZE;
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream() => this._stream ??= new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _Tag,
    Handler = _Tag,
    CodecId = "V_REAL/RV10",
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = 12,
    CodecPrivateData = _PrivateData,
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

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