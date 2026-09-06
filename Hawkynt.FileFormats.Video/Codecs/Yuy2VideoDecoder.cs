using System;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes YUY2: uncompressed 4:2:2 YUV, two pixels to four bytes — Y0, Cb, Y1, Cr — and nothing
/// else.
/// </summary>
/// <remarks>
/// <b>The layout.</b> A macropixel is two horizontally adjacent pixels and four bytes: luma, Cb,
/// luma, Cr. Each pair of pixels states its own chroma pair and shares it; a row is exactly
/// <c>width</c> times two bytes with no padding, and rows run top row first. This is the ordering
/// ffmpeg calls <c>yuyv422</c>, and the one <c>UYVY</c> reverses.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Files ffmpeg 9 wrote as <c>rawvideo</c> at
/// <c>-pix_fmt yuyv422 -vtag YUY2</c> were decoded here and compared sample for sample against
/// ffmpeg's own <c>-pix_fmt yuv422p</c> decode of the very same files, over pseudo-random content at
/// 16x8, 34x18 and 8x5 (an odd height, which nothing about 4:2:2 forbids), five frames apiece: 7,800
/// samples, none differing.
/// <para/>
/// <b>What comes out is the samples themselves.</b> Unpacking is a rearrangement of eight-bit bytes
/// and nothing more, so the picture is handed back as <see cref="PixelFormat.Yuv422P8"/> — luma at
/// the full width, each chroma plane at half of it — rather than converted to colour on the way out.
/// A caller that wants RGB asks the package's converter for it and chooses the matrix; a decoder
/// that chose one here would be stating a display convention the four-character code does not.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, an odd width — two pixels to a macropixel leaves
/// no half of one, and ffmpeg's own scaler rounds an odd width up rather than writing one — and a
/// packet shorter than its stride times its height.
/// </remarks>
public sealed class Yuy2VideoDecoder : IVideoCodecDecoder<Yuy2VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("YUY2");

  private readonly PackedYuv422Packing _packing;

  private Yuy2VideoDecoder(PackedYuv422Packing packing) => this._packing = packing;

  public static string CodecName => "Uncompressed packed 4:2:2 (YUY2)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Yuy2VideoDecoder Create(MediaStreamInfo stream)
    => new(PackedYuv422Packing.For(stream, PackedYuv422Order.LumaCbLumaCr, "YUY2"));

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = this._packing.ToImage(this._packing.Unpack(packet.Data.Span));

    return true;
  }

  /// <summary>
  /// Unpacks one frame into its luma and chroma planes — the form <c>-pix_fmt yuv422p</c> writes and
  /// the one this was verified against.
  /// </summary>
  internal (byte[] Luma, byte[] Cb, byte[] Cr) DecodePlanes(ReadOnlySpan<byte> data) => this._packing.UnpackPlanes(data);
}
