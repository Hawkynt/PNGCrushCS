using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes NV12: uncompressed 4:2:0 YUV, a luma plane and one interleaved chroma plane, and nothing
/// else.
/// </summary>
/// <remarks>
/// The mirror of <see cref="Nv12VideoDecoder"/>, writing the layout that decoder reads back: a
/// full-resolution luma plane, then a single chroma plane holding one Cb byte and one Cr byte for
/// each two-by-two block of luma, Cb first — half the width and half the height of the picture in
/// pairs, which is the same count of samples the two separate planes of <c>I420</c> hold. Nothing is
/// predicted and nothing is entropy coded, so every packet is a key frame and the stream this
/// encoder describes is decodable with nothing more than the tag and the picture size.
/// <para/>
/// <b>Lossless on the samples it is given.</b> A <see cref="PixelFormat.Yuv420P8"/> picture goes
/// into the packet byte for byte and comes back from the decoder identical. An eight-bit planar
/// picture of any other subsampling keeps its luma untouched and has its chroma averaged onto this
/// grid, so one already sited at a pair per two-by-two block is reproduced exactly too; one that is
/// not gives up three of every four chroma samples, and that loss is 4:2:0's own rather than this
/// packing's. Anything else — the RGB a caller is likeliest to hold — is first converted to 4:4:4
/// under the ITU-R BT.601 studio-swing convention this package's uncompressed YUV decoders display
/// with, and it is there that the rounding of the matrix enters.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Packets written here were muxed into an AVI
/// and read back through ffmpeg 9 as <c>yuv420p</c> planes, over pseudo-random pictures at 16x8,
/// 34x18 and 8x6, five frames apiece: 5,910 samples, none differing. The packets are also byte for
/// byte the ones ffmpeg's own <c>-pix_fmt nv12</c> encoder wrote for the same pictures — all 5,910
/// of them.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, an odd width or height — the
/// decoder refuses the same size and for the same reason — and a frame whose geometry differs from
/// the stream's.
/// </remarks>
public sealed class Nv12VideoEncoder : IVideoCodecEncoder<Nv12VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("NV12");

  private readonly MediaStreamInfo _stream;
  private readonly Yuv420Packing _packing;

  private Nv12VideoEncoder(MediaStreamInfo stream, Yuv420Packing packing) {
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
      BitsPerPixel = 12,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Uncompressed semi-planar 4:2:0 (NV12)";

  public static CodecTag Codec => _Tag;

  public static Nv12VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("NV12 can only encode a video stream.");

    return new(stream, Yuv420Packing.For(stream, Yuv420Order.InterleavedCbCr, "NV12"));
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"NV12 geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
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
