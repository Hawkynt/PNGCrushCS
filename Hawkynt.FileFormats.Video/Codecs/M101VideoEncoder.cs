using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes Matrox Uncompressed SD (<c>M101</c>) as eight- or ten-bit 4:2:2.</summary>
public sealed class M101VideoEncoder : IVideoCodecEncoder<M101VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("M101");

  private readonly MatroxUncompressedVideoEncoderCore _core;

  private M101VideoEncoder(MatroxUncompressedVideoEncoderCore core) => this._core = core;

  public static string CodecName => "Matrox Uncompressed SD";

  public static CodecTag Codec => _Tag;

  public static M101VideoEncoder Create(MediaStreamInfo stream)
    => new(MatroxUncompressedVideoEncoderCore.Create(stream, _Tag, CodecName));

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet)
    => this._core.TryEncode(frame, presentationTimestamp, out packet);

  public MediaStreamInfo DescribeStream() => this._core.DescribeStream();
}
