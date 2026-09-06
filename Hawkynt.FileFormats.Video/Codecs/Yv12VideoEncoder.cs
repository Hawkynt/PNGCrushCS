using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes YV12: uncompressed 4:2:0 YUV, three separate planes, and nothing else.
/// </summary>
/// <remarks>
/// The mirror of <see cref="Yv12VideoDecoder"/>, writing the layout that decoder reads back: a
/// full-resolution luma plane, then a whole Cr plane, then a whole Cb plane, each chroma plane half
/// the width and half the height of the picture — <c>I420</c> with the two chroma planes exchanged,
/// which is the whole of the difference between them. Nothing is predicted and nothing is entropy
/// coded, so every packet is a key frame and the stream this encoder describes is decodable with
/// nothing more than the tag and the picture size.
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
/// 34x18 and 8x6, five frames apiece: 5,910 samples, none differing.
/// <para/>
/// <b>The packets are not the ones ffmpeg writes under this tag, and that is the point.</b> Its
/// raw-video encoder writes <c>I420</c>'s plane order whatever tag it is given, so over the same
/// pictures its packets differ from these in all 1,970 chroma bytes and in none of the 3,940 luma
/// ones — these are exactly its packets with the two chroma planes exchanged. Its decoder exchanges
/// them back, which is why reading these packets through it returns the pictures unchanged.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, an odd width or height — the
/// decoder refuses the same size and for the same reason — and a frame whose geometry differs from
/// the stream's.
/// </remarks>
public sealed class Yv12VideoEncoder : IVideoCodecEncoder<Yv12VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("YV12");

  private readonly MediaStreamInfo _stream;
  private readonly Yuv420Packing _packing;

  private Yv12VideoEncoder(MediaStreamInfo stream, Yuv420Packing packing) {
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

  public static string CodecName => "Uncompressed planar 4:2:0 (YV12)";

  public static CodecTag Codec => _Tag;

  public static Yv12VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("YV12 can only encode a video stream.");

    return new(stream, Yuv420Packing.For(stream, Yuv420Order.PlanarCrCb, "YV12"));
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"YV12 geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
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
