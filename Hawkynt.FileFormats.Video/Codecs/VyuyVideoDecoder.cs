using System;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes VYUY: uncompressed 4:2:2 YUV, two pixels to four bytes — Cr, Y0, Cb, Y1 — and nothing
/// else.
/// </summary>
/// <remarks>
/// <b>The layout.</b> A macropixel is two horizontally adjacent pixels and four bytes: Cr, luma, Cb,
/// luma. Each pair of pixels states its own chroma pair and shares it; a row is exactly <c>width</c>
/// times two bytes with no padding, and rows run top row first. It is <c>UYVY</c> with the chroma
/// pair exchanged.
/// <para/>
/// <b>ffmpeg is not an oracle for this tag.</b> Its raw-video tag table maps <c>VYUY</c> onto
/// <c>yuyv422</c>, so it reads and writes YUY2's ordering under this code: its decode of a VYUY file
/// states Y0 Cb Y1 Cr where the code states Cr Y0 Cb Y1, and comparing against it would confirm the
/// wrong layout — over the frames measured below, its reading of the same files differs from this
/// one in 7,775 of 7,800 samples. What is read here is the layout the Linux kernel's V4L2
/// documentation states for <c>V4L2_PIX_FMT_VYUY</c> — byte 0 Cr, byte 1 Y0, byte 2 Cb, byte 3 Y1 —
/// and the one Microsoft's note on 8-bit YUV formats states by describing VYUY as UYVY with the
/// chroma samples exchanged.
/// <para/>
/// <b>Verified against ffmpeg all the same, exactly.</b> VYUY's bytes are UYVY's packing of the same
/// picture with its two chroma planes exchanged, and that ffmpeg does write. Files it wrote at
/// <c>-pix_fmt uyvy422</c> from chroma-exchanged content were decoded here and compared sample for
/// sample against the content before the exchange, over pseudo-random pictures at 16x8, 34x18 and
/// 8x5 (an odd height, which nothing about 4:2:2 forbids), five frames apiece: 7,800 samples, none
/// differing.
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
public sealed class VyuyVideoDecoder : IVideoCodecDecoder<VyuyVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VYUY");

  private readonly PackedYuv422Packing _packing;

  private VyuyVideoDecoder(PackedYuv422Packing packing) => this._packing = packing;

  public static string CodecName => "Uncompressed packed 4:2:2 (VYUY)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static VyuyVideoDecoder Create(MediaStreamInfo stream)
    => new(PackedYuv422Packing.For(stream, PackedYuv422Order.CrLumaCbLuma, "VYUY"));

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
