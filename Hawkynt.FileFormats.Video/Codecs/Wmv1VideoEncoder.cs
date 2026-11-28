using System;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Windows Media Video 7 (<c>WMV1</c>): intra pictures every twelfth and predicted ones between
/// them, one motion vector a macroblock, the middle run-level tables, and no alternating rounding.
/// </summary>
/// <remarks>
/// The package's Microsoft MPEG-4 version 3 encoder writing WMV1's headers, scans, DC steps and
/// escapes, and reconstructing with FFmpeg's integer <c>simple_idct</c> as WMV1's decoders do. It states
/// a bit rate of 255 kbit/s in the extension header, which turns off the inter-intra prediction small
/// low-rate pictures would otherwise use. Lossy by construction: every block goes through the transform
/// and a quantiser. FFmpeg's decoder is the oracle that reads what it writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Wmv1VideoEncoder : IVideoCodecEncoder<Wmv1VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WMV1");

  private readonly MsMpeg4VideoEncoder _encoder;

  private Wmv1VideoEncoder(MsMpeg4VideoEncoder encoder) => this._encoder = encoder;

  public static string CodecName => Wmv1VideoDecoder.CodecName;

  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static Wmv1VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Windows Media Video 7 can only encode a video stream.");
    return new(MsMpeg4VideoEncoder.CreateWmv1(stream));
  }

  public MediaStreamInfo DescribeStream() => this._encoder.DescribeStream();

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet)
    => this._encoder.TryEncode(frame, presentationTimestamp, out packet);
}
