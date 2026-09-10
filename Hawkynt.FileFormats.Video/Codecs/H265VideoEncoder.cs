using System;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes H.265 / HEVC pictures as independent intra PCM access units.
/// </summary>
/// <remarks>
/// This is deliberately the small end of HEVC rather than a second x265. Every frame is an IDR
/// picture made from 32 by 32 PCM coding units, using the same standards-based encoder already used
/// by the HEIF writer. The result is ordinary Main-Still-Picture HEVC: no private escape syntax, no
/// native dependency, and no inter-picture state for a decoder to reconstruct before a packet can be
/// shown.
/// <para/>
/// Samples are carried in the length-prefixed form used by ISO media and Matroska, with the VPS, SPS
/// and PPS in the accompanying <c>HEVCDecoderConfigurationRecord</c>. A raw Annex B writer is a
/// container concern and may translate that representation without changing the coded picture.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H265VideoEncoder : IVideoCodecEncoder<H265VideoEncoder> {

  private static readonly CodecTag _codec = CodecTag.FromCharacters("hvc1");

  private readonly MediaStreamInfo _requested;
  private MediaStreamInfo? _stream;
  private byte[]? _configuration;

  private H265VideoEncoder(MediaStreamInfo stream) => this._requested = stream;

  public static string CodecName => H265VideoDecoder.CodecName;

  public static CodecTag Codec => _codec;

  public static H265VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("H.265 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An H.265 encoder needs the output dimensions before coding begins; {stream.Width}x{stream.Height} was supplied.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._requested.Width || frame.Height != this._requested.Height)
      throw new InvalidDataException(
        $"The H.265 encoder was created for {this._requested.Width}x{this._requested.Height} pictures, but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var encoded = H265PcmStillCodec.Encode(frame);
    if (this._configuration == null)
      this._configuration = encoded.DecoderConfiguration;
    else if (!encoded.DecoderConfiguration.AsSpan().SequenceEqual(this._configuration))
      throw new InvalidDataException(
        "The HEVC parameter sets changed while encoding a fixed-geometry stream. A video stream description can carry only one decoder configuration here.");

    packet = new(
      StreamIndex: this._requested.Index,
      Data: encoded.Sample,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;
    if (this._configuration == null)
      throw new InvalidOperationException(
        "An H.265 stream cannot be described before its parameter sets exist. Encode the first picture first.");

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _codec,
      Handler = _codec,
      CodecId = "V_MPEGH/ISO/HEVC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._requested.Width,
      Height = this._requested.Height,
      CodecPrivateData = this._configuration,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }
}
