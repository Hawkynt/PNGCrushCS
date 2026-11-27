using System;
using System.Buffers.Binary;
using FileFormat.Bmp;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.Wmv2;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Windows Media Video 8 (<c>WMV2</c>): intra, predicted and IntraX8 ("J") pictures, with the
/// quarter-sample motion, adaptive 8x4 and 4x8 transforms, chosen vector predictors, skip maps and
/// deblocking filter the stream's extension header may turn on.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/wmv2dec.c</c>, <c>wmv2dsp.c</c>, <c>intrax8.c</c>,
/// <c>intrax8dsp.c</c> and the H.263 loop filter (LGPL-2.1-or-later) on top of the package's WMV1
/// block layer; see <c>MsMpeg4/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. The picture size comes from the
/// container and the four-byte extension header from the bytes after its <c>BITMAPINFOHEADER</c>:
/// WMV2 carries neither in its pictures. A predicted picture whose skip map skips every macroblock is
/// not a picture, as FFmpeg has it, so no frame comes out for it. Hands out
/// <see cref="PixelFormat.Yuv420P8"/>.
/// </remarks>
public sealed class Wmv2VideoDecoder : IVideoCodecDecoder<Wmv2VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WMV2");

  private readonly int _width;
  private readonly int _height;
  private readonly Wmv2PictureDecoder _decoder;
  private Mpeg4Frame? _reference;

  private Wmv2VideoDecoder(int width, int height, Wmv2Extension extension) {
    this._width = width;
    this._height = height;
    this._decoder = new(extension, width, height);
  }

  public static string CodecName => "Windows Media Video 8 (WMV2)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Wmv2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Windows Media Video 8.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Stream {stream.Index} states a size of {stream.Width}x{stream.Height}. WMV2 carries no picture size in its "
        + "bitstream, so the container's is the only one there is.");
    return new(stream.Width, stream.Height, Wmv2Extension.Parse(ExtensionOf(stream)));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }

    var decoded = this._decoder.Decode(packet.Data.Span, this._reference);
    if (decoded == null) {
      frame = null!;
      return false;
    }

    this._reference = decoded;
    frame = Wmv1VideoDecoder.ToYuv420(decoded, this._width, this._height);
    return true;
  }

  /// <summary>The bytes after the stream's <c>BITMAPINFOHEADER</c>, or the description itself where it is bare extradata.</summary>
  internal static ReadOnlySpan<byte> ExtensionOf(MediaStreamInfo stream) {
    var data = stream.CodecPrivateData.Span;
    if (data.Length >= BitmapInfoHeader.StructSize) {
      var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
      var code = new CodecTag(BinaryPrimitives.ReadUInt32LittleEndian(data[16..]));
      if (headerSize >= BitmapInfoHeader.StructSize && headerSize <= (uint)data.Length && code.EqualsIgnoringCase(_Tag))
        return data[BitmapInfoHeader.StructSize..];
    }

    return data;
  }
}
