using System;
using FileFormat.Codecs.Ea;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Electronic Arts TQI video (<c>pIQT</c> pictures in <c>.WVE</c> and <c>.UV2</c> films): MPEG-1
/// intra macroblocks under a quality-derived AAN matrix and EA's own inverse DCT.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/eatqi.c</c>, <c>eaidct.c</c> and <c>ff_mpeg1_decode_block_intra</c>,
/// LGPL-2.1-or-later; see <c>Ea/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Every picture states its own size.
/// Pictures come out as <see cref="PixelFormat.Yuv420P8"/>.
/// </remarks>
public sealed class EaTqiVideoDecoder : IVideoCodecDecoder<EaTqiVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("tqi ");

  public static string CodecName => "Electronic Arts TQI Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static EaTqiVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Electronic Arts TQI.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }
    var picture = TqiCodec.Decode(packet.Data.Span);
    frame = new() { Width = picture.Width, Height = picture.Height, Format = PixelFormat.Yuv420P8, PixelData = picture.ToYuv420() };
    return true;
  }
}

/// <summary>
/// Decodes Electronic Arts TGQ video (<c>TGQs</c> and <c>pQGT</c> pictures): macroblocks coded with TGQ's
/// own three-bit coefficient code, as DC values alone, or copied from the last picture.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/eatgq.c</c> and <c>eaidct.c</c>, LGPL-2.1-or-later; see
/// <c>Ea/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Both byte orders are read. Pictures come out as
/// <see cref="PixelFormat.Yuv420P8"/>.
/// </remarks>
public sealed class EaTgqVideoDecoder : IVideoCodecDecoder<EaTgqVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("tgq ");
  private EaPicture? _last;

  public static string CodecName => "Electronic Arts TGQ Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static EaTgqVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Electronic Arts TGQ.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }
    var picture = TgqCodec.Decode(packet.Data.Span, this._last);
    this._last = picture;
    frame = new() { Width = picture.Width, Height = picture.Height, Format = PixelFormat.Yuv420P8, PixelData = picture.ToYuv420() };
    return true;
  }
}

/// <summary>
/// Decodes Electronic Arts Madcow video (MAD: <c>MADk</c>, <c>MADm</c>, <c>MADe</c> pictures): intra
/// blocks under MPEG-1's run-level code, and predicted macroblocks copied from the last reference at a
/// whole-sample vector with a DC offset; lower-quality pictures are shown and not kept.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/eamad.c</c> and <c>eaidct.c</c>, LGPL-2.1-or-later; see
/// <c>Ea/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Pictures come out as <see cref="PixelFormat.Yuv420P8"/>.
/// </remarks>
public sealed class EaMadVideoDecoder : IVideoCodecDecoder<EaMadVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("mad ");
  private EaPicture? _reference;

  public static string CodecName => "Electronic Arts Madcow Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static EaMadVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Electronic Arts Madcow.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }
    var picture = MadCodec.Decode(packet.Data.Span, this._reference, out var isReference);
    if (isReference)
      this._reference = picture;
    frame = new() { Width = picture.Width, Height = picture.Height, Format = PixelFormat.Yuv420P8, PixelData = picture.ToYuv420() };
    return true;
  }
}

/// <summary>
/// Decodes Electronic Arts TGV video (<c>kVGT</c> and <c>fVGT</c> pictures): palettised key frames packed
/// by an LZ77 variant, and predicted frames built from four-by-four cells copied at codebook vectors or
/// drawn from codebooks of raw and two-bit-packed cells.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/eatgv.c</c>, LGPL-2.1-or-later; see <c>Ea/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// Pictures come out as <see cref="PixelFormat.Indexed8"/> with the palette the last key frame stated. A
/// predicted frame before any key frame gives no picture, as FFmpeg gives none.
/// </remarks>
public sealed class EaTgvVideoDecoder : IVideoCodecDecoder<EaTgvVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("tgv ");
  private readonly TgvCodec _codec = new();

  public static string CodecName => "Electronic Arts TGV Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static EaTgvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Electronic Arts TGV.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var pixels = packet.Data.Length == 0 ? null : this._codec.Decode(packet.Data.Span);
    if (pixels == null) {
      frame = null!;
      return false;
    }
    frame = new() {
      Width = this._codec.Width,
      Height = this._codec.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = (byte[])this._codec.Palette.Clone(),
      PaletteCount = 256,
    };
    return true;
  }
}
