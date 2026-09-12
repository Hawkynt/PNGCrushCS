using System;
using System.Collections.Generic;
using FileFormat.Codecs.Theora;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Theora I video as interoperable all-intra 4:4:4 pictures.
/// </summary>
/// <remarks>
/// Theora is inherently lossy. This encoder intentionally starts with a narrow, deterministic coding
/// choice rather than a pretend rate controller: every picture is a key frame and every 8x8 block is
/// represented by its direct-current coefficient only. That gives a coarse block-average picture,
/// but the bytes are ordinary Theora I — the three setup headers, quantisation, DC prediction and
/// coefficient token syntax are all the format's own and need no private extension to decode.
/// <para/>
/// The coded frame is padded to whole macroblocks as Theora requires and the picture rectangle keeps
/// the caller's exact size. The encoder writes 4:4:4 so odd picture dimensions do not silently lose a
/// chroma row or column, and it accepts any <see cref="RawImage"/> format the shared colour converter
/// can bring to eight-bit YCbCr.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class TheoraVideoEncoder : IVideoCodecEncoder<TheoraVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("Theo");

  private readonly MediaStreamInfo _requested;
  private readonly TheoraEncoder _encoder;
  private readonly byte[] _codecPrivateData;
  private readonly Rational _frameRate;
  private MediaStreamInfo? _stream;

  private TheoraVideoEncoder(
    MediaStreamInfo requested, TheoraIdentificationHeader header, byte[] codecPrivateData, Rational frameRate) {
    this._requested = requested;
    this._encoder = new(header);
    this._codecPrivateData = codecPrivateData;
    this._frameRate = frameRate;
  }

  public static string CodecName => TheoraVideoDecoder.CodecName;

  public static CodecTag Codec => _Tag;

  /// <summary>Creates an encoder for one fixed-size Theora stream.</summary>
  public static TheoraVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Theora can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Theora needs a positive picture size; {stream.Width}x{stream.Height} was requested.");

    var macroBlocksWide = (stream.Width + 15L) / 16;
    var macroBlocksHigh = (stream.Height + 15L) / 16;
    if (macroBlocksWide > ushort.MaxValue || macroBlocksHigh > ushort.MaxValue)
      throw new NotSupportedException(
        $"Theora stores each coded-frame dimension as a 16-bit macroblock count; {stream.Width}x{stream.Height} needs "
        + $"{macroBlocksWide}x{macroBlocksHigh} macroblocks.");

    var frameWidth = macroBlocksWide * 16;
    var frameHeight = macroBlocksHigh * 16;
    if (frameWidth * frameHeight * 3 > int.MaxValue)
      throw new NotSupportedException(
        $"The padded Theora frame for {stream.Width}x{stream.Height} would need more than 2 GiB of eight-bit 4:4:4 samples.");

    var frameRate = stream.FrameRate.IsKnown ? stream.FrameRate : new Rational(25, 1);
    var (header, privateData) = TheoraEncoderHeaders.Build(stream);
    return new(stream, header, privateData, frameRate);
  }

  /// <summary>Encodes one picture immediately; Theora needs no reordering for this all-intra stream.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var bytes = this._encoder.Encode(frame);
    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>
  /// Describes the stream using Matroska's canonical codec ID and Xiph-laced Theora header packets.
  /// Ogg identifies the same codec from the first header packet rather than requiring a different
  /// encoder-side stream description.
  /// </summary>
  public MediaStreamInfo DescribeStream()
    => this._stream ??= new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_THEORA",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._frameRate,
      Width = this._requested.Width,
      Height = this._requested.Height,
      BitsPerPixel = 24,
      CodecPrivateData = this._codecPrivateData,
    };
}
