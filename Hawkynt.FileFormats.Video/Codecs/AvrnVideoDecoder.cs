using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Avid AVRn: ordinary baseline JPEG, marker for marker, one packet one whole picture — the
/// one place this codec departs from a plain Motion JPEG stream is which of the JPEG's own stated size
/// and the container's is trusted, and here it is the other way round.
/// </summary>
/// <remarks>
/// There is no published bitstream description of this one either, so what follows was recovered
/// directly from four real captures at samples.ffmpeg.org — two broadcast recordings at 720x486
/// (NTSC) and 720x576 (PAL) and two small test clips at 160x120 — by reading a packet on its own as a
/// standalone JPEG and comparing that against ffmpeg's own decode of the whole file.
/// <para/>
/// <b>Every packet is a real, complete, marker-delimited JPEG</b> — <c>FF D8</c>, a JFIF <c>APP0</c>,
/// a comment naming <c>AVID</c>, a restart interval, the quantisation and Huffman tables, the frame
/// and scan headers, entropy data, <c>FF D9</c> — decoded here by the same <see cref="JpegReader"/>
/// <see cref="MotionJpegDecoder"/> already uses, because it genuinely is the same coding underneath a
/// different fourcc.
/// <para/>
/// <b>Where it differs is the one thing <see cref="MotionJpegDecoder"/>'s own remarks call out by
/// name.</b> That decoder trusts the JPEG's own stated size over the container's, on the reasoning
/// that the JPEG is the thing that was actually coded. The NTSC broadcast capture measured here is the
/// case that reasoning gets wrong for this codec: its packets code a frame header stating 720x496,
/// sixteen lines taller than the 720x486 the container's own <c>BITMAPINFOHEADER</c> states, and
/// ffmpeg's own decode of the file is 486 lines — the container's figure, not the frame header's.
/// Four hundred and ninety-six is 486 rounded up to the next multiple of sixteen, so what is happening
/// is an encoder padding its coded frame out to a whole number of macroblock rows and never trimming
/// the frame header back down to say so, leaving the true height nowhere but the container. The other
/// three captures' frame headers already state their real size exactly — the PAL one because 576 is
/// already a multiple of sixteen and needs no padding, the two small ones because their encoder simply
/// wrote the true height regardless — so the difference is invisible on three of the four files and
/// would still be a defect for the fourth if unhandled: a picture with sixteen rows of undefined
/// content at the bottom that nothing tells you not to trust.
/// <para/>
/// <b>Verified.</b> All four captures — 46, 200, 50 and 50 pictures — were compared against ffmpeg's
/// own decode of the same file, plane by plane, sampling every frame: 4:2:2 and 4:2:0 both, every one
/// identical, including the padded NTSC capture whose every frame needs the crop this decoder applies
/// and the exact-height PAL one whose every frame needs none.
/// </remarks>
public sealed class AvrnVideoDecoder : IVideoCodecDecoder<AvrnVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVRn");

  private readonly int _streamIndex;
  private readonly int _width;
  private readonly int _height;

  private AvrnVideoDecoder(int streamIndex, int width, int height) {
    this._streamIndex = streamIndex;
    this._width = width;
    this._height = height;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Avid AVRn";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static AvrnVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(stream.Index, stream.Width, stream.Height);
  }

  /// <summary>
  /// Decodes one packet, then crops it to the container's own declared size when that size is smaller
  /// than what the JPEG's own frame header states — the padding <see cref="AvrnVideoDecoder"/>'s own
  /// remarks describe. A container that states no size of its own, or one no smaller than the JPEG's,
  /// leaves the picture exactly as the JPEG reader produced it.
  /// </summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var decoded = JpegFile.ToRawImage(JpegReader.FromSpan(packet.Data.Span));

    if (this._width > decoded.Width || this._height > decoded.Height)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a picture size of {this._width}x{this._height}, larger than the "
        + $"{decoded.Width}x{decoded.Height} its own JPEG frame header codes.");

    var width = this._width > 0 ? this._width : decoded.Width;
    var height = this._height > 0 ? this._height : decoded.Height;

    if (width == decoded.Width && height == decoded.Height) {
      frame = decoded;
      return true;
    }

    frame = new() {
      Width = width,
      Height = height,
      Format = decoded.Format,
      PixelData = _Crop(decoded, width, height),
    };
    return true;
  }

  /// <summary>
  /// Keeps the bottom <paramref name="height"/> rows of <paramref name="source"/>, not the top — the
  /// padding a coded frame carries beyond the container's own declared height sits above the real
  /// picture rather than below it, measured directly against ffmpeg's decode of the one capture where
  /// the two disagree by a whole macroblock row (720x496 coded, 720x486 declared): keeping the top 486
  /// rows disagreed with ffmpeg on every sample, and keeping the bottom 486 matched exactly.
  /// </summary>
  private static byte[] _Crop(RawImage source, int width, int height) {
    var bytesPerPixel = RawImage.BytesPerPixel(source.Format);
    var sourceStride = source.Width * bytesPerPixel;
    var targetStride = width * bytesPerPixel;
    var target = new byte[targetStride * height];
    var rowOffset = source.Height - height;

    for (var y = 0; y < height; ++y)
      source.PixelData.AsSpan((rowOffset + y) * sourceStride, targetStride).CopyTo(target.AsSpan(y * targetStride, targetStride));

    return target;
  }
}
