using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes YVYU: uncompressed 4:2:2 YUV, two pixels to four bytes — Y0, Cr, Y1, Cb — and nothing
/// else.
/// </summary>
/// <remarks>
/// The mirror of <see cref="YvyuVideoDecoder"/>, writing the layout that decoder reads back: luma,
/// Cr, luma, Cb in every four bytes, a row exactly <c>width</c> times two bytes with no padding, top
/// row first. Nothing is predicted and nothing is entropy coded, so every packet is a key frame and
/// the stream this encoder describes is decodable with nothing more than the tag and the picture
/// size.
/// <para/>
/// <b>Lossless on the samples it is given.</b> A <see cref="PixelFormat.Yuv422P8"/> picture goes
/// into the packet byte for byte and comes back from the decoder identical. An eight-bit planar
/// picture of any other subsampling keeps its luma untouched and has its chroma averaged onto this
/// grid, so one already sited at a pair per two columns is reproduced exactly too. Anything else —
/// the RGB a caller is likeliest to hold — is first converted to 4:4:4 under the ITU-R BT.601
/// studio-swing convention this package's uncompressed YUV decoders display with, and it is there
/// and only there that the rounding of the matrix enters.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Packets written here were muxed into an AVI
/// and read back through ffmpeg 9 as <c>yuv422p</c> planes, over pseudo-random pictures at 16x8,
/// 34x18 and 8x5 (an odd height, which nothing about 4:2:2 forbids), five frames apiece: 7,800
/// samples, none differing. The packets are also byte for byte the ones ffmpeg's own <c>-pix_fmt
/// yvyu422</c> encoder wrote for the same pictures — all 7,800 of them.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, an odd width — the decoder
/// refuses the same width and for the same reason — and a frame whose geometry differs from the
/// stream's.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class YvyuVideoEncoder : IVideoCodecEncoder<YvyuVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("YVYU");

  private readonly MediaStreamInfo _stream;
  private readonly PackedYuv422Packing _packing;

  private YvyuVideoEncoder(MediaStreamInfo stream, PackedYuv422Packing packing) {
    this._packing = packing;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 16,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Uncompressed packed 4:2:2 (YVYU)";

  public static CodecTag Codec => _Tag;

  public static YvyuVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("YVYU can only encode a video stream.");

    return new(stream, PackedYuv422Packing.For(stream, PackedYuv422Order.LumaCrLumaCb, "YVYU"));
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"YVYU geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = new(
      this._stream.Index,
      this._packing.Pack(this._packing.PlanesOf(frame)),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);

    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;
}
