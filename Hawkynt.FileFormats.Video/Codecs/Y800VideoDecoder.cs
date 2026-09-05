using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Y800: uncompressed luma and nothing else, one byte a pixel.
/// </summary>
/// <remarks>
/// <b>The layout.</b> One byte a pixel, a row exactly <c>width</c> bytes with no padding, top row
/// first, and no chroma anywhere in the packet. There is nothing to subsample and nothing to
/// interleave, which is why this is the one of the ten raw pixel layouts that takes any picture size
/// at all — odd width, odd height or both.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Files ffmpeg 9 wrote as <c>rawvideo</c> at
/// <c>-pix_fmt gray -vtag Y800</c> were decoded here and compared sample for sample against ffmpeg's
/// own <c>-pix_fmt gray</c> decode of the very same files, over pseudo-random content at 16x8, 17x9
/// and 7x5 (both dimensions odd, which only this one of the ten layouts allows), five frames apiece:
/// 1,580 samples, none differing.
/// <para/>
/// <b>What comes out is the bytes, unchanged.</b> The picture is handed back as <see
/// cref="PixelFormat.Gray8"/> holding the packet's own samples, with no range expansion applied —
/// the same thing ffmpeg's own decode of this tag does, and the same thing this package's canonical
/// planar raw decoder does for a <c>mono</c> stream. A Y800 stream's luma is nominally ITU-R BT.601
/// studio swing, so a viewer that treats it as full-range grey renders it slightly flat; rescaling
/// it here would be a display convention the four-character code does not state, and would stop the
/// packet from coming back the way it went in.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, and a packet shorter than its width times its
/// height.
/// </remarks>
public sealed class Y800VideoDecoder : IVideoCodecDecoder<Y800VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("Y800");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;

  private Y800VideoDecoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._streamIndex = stream.Index;
  }

  public static string CodecName => "Uncompressed grey (Y800)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Y800VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    return new(stream);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Gray8,
      PixelData = this.DecodeLuma(packet.Data.Span),
    };

    return true;
  }

  /// <summary>
  /// Unpacks one frame into its luma plane — the form <c>-pix_fmt gray</c> writes and the one this was
  /// verified against.
  /// </summary>
  internal byte[] DecodeLuma(ReadOnlySpan<byte> data) {
    var expected = (long)this._width * this._height;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Y800 packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame needs {expected}.");

    return data[..(int)expected].ToArray();
  }
}
