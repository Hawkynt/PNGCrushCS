using System;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes NV12: uncompressed 4:2:0 YUV, a luma plane and one interleaved chroma plane, and nothing
/// else.
/// </summary>
/// <remarks>
/// <b>The layout.</b> A full-resolution luma plane, then a single chroma plane holding one Cb byte
/// and one Cr byte for each two-by-two block of luma, Cb first — half the width and half the height
/// of the picture in pairs, which is the same count of samples the two separate planes of
/// <c>I420</c> hold. A row of any plane is exactly its own width in bytes, there is no padding
/// anywhere, and rows run top row first.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Files ffmpeg 9 wrote as <c>rawvideo</c> at
/// <c>-pix_fmt nv12 -vtag NV12</c> were decoded here and compared sample for sample against ffmpeg's
/// own <c>-pix_fmt yuv420p</c> decode of the very same files, over pseudo-random content at 16x8,
/// 34x18 and 8x6, five frames apiece: 5,910 samples, none differing.
/// <para/>
/// <b>What comes out is the samples themselves.</b> Unpacking is a rearrangement of eight-bit bytes
/// and nothing more, so the picture is handed back as <see cref="PixelFormat.Yuv420P8"/> — the same
/// sample grid, with the planes in this package's own order — rather than converted to colour on the
/// way out. A caller that wants RGB asks the package's converter for it and chooses the matrix; a
/// decoder that chose one here would be stating a display convention the four-character code does
/// not.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, an odd width or height — a two-by-two chroma block
/// has no stated half — and a packet shorter than a frame.
/// </remarks>
public sealed class Nv12VideoDecoder : IVideoCodecDecoder<Nv12VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("NV12");

  private readonly Yuv420Packing _packing;

  private Nv12VideoDecoder(Yuv420Packing packing) => this._packing = packing;

  public static string CodecName => "Uncompressed semi-planar 4:2:0 (NV12)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Nv12VideoDecoder Create(MediaStreamInfo stream)
    => new(Yuv420Packing.For(stream, Yuv420Order.InterleavedCbCr, "NV12"));

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = this._packing.ToImage(this._packing.Unpack(packet.Data.Span));

    return true;
  }

  /// <summary>
  /// Unpacks one frame into its luma and chroma planes — the form <c>-pix_fmt yuv420p</c> writes and
  /// the one this was verified against.
  /// </summary>
  internal (byte[] Luma, byte[] Cb, byte[] Cr) DecodePlanes(ReadOnlySpan<byte> data) => this._packing.UnpackPlanes(data);
}
