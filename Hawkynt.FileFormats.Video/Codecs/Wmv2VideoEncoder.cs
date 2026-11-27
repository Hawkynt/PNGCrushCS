using System;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Windows Media Video 8 (<c>WMV2</c>): intra pictures every twelfth and predicted ones between
/// them, one half-sample motion vector a macroblock, a skip map, and WMV2's own transform.
/// </summary>
/// <remarks>
/// The package's WMV1 encoder writing WMV2's picture headers, skip map, macroblock tables and vector
/// predictor, and reconstructing with WMV2's transform and its alternating rounding as WMV2's decoders
/// do. The extension header it states after the <c>BITMAPINFOHEADER</c> leaves every optional tool off
/// — quarter-sample motion, adaptive transforms, IntraX8 pictures, chosen vector predictors,
/// per-macroblock tables and the deblocking filter — which is also what FFmpeg's own WMV2 encoder
/// leaves off but for the bits it states and then never uses. Lossy by construction; FFmpeg's decoder
/// is the oracle that reads what it writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Wmv2VideoEncoder : IVideoCodecEncoder<Wmv2VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WMV2");

  private readonly MsMpeg4VideoEncoder _encoder;

  private Wmv2VideoEncoder(MsMpeg4VideoEncoder encoder) => this._encoder = encoder;

  public static string CodecName => Wmv2VideoDecoder.CodecName;

  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static Wmv2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Windows Media Video 8 can only encode a video stream.");
    return new(MsMpeg4VideoEncoder.CreateWmv2(stream));
  }

  public MediaStreamInfo DescribeStream() => this._encoder.DescribeStream();

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet)
    => this._encoder.TryEncode(frame, presentationTimestamp, out packet);
}
