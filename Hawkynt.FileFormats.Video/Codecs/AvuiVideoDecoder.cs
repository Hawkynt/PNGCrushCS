using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes avui: Avid Meridien's uncompressed 4:2:2 packing, a fixed block of unused lines ahead of
/// an ordinary UYVY row of the picture.
/// </summary>
/// <remarks>
/// There is no MultimediaWiki page for this one and no published layout, so what follows was
/// recovered rather than read: pseudo-random <c>uyvy422</c> content was carried through ffmpeg's own
/// avui encoder and swept against every placement of a header ahead of, inside and behind the picture
/// data, keeping the one that reproduces every sample. Ffmpeg's encoder itself only accepts two
/// geometries — 720x486 and 720x576 — refusing every other width and height outright, which settles
/// what a decoder needs to accept as firmly as any documentation could.
/// <para/>
/// <b>The packet.</b> A run of zero bytes the width of ten UYVY rows at 720x486 or sixteen at
/// 720x576 — 14,400 and 23,040 bytes respectively, always a whole number of the format's own 1,440-byte
/// row stride — comes first, and the picture itself follows as plain <c>uyvy422</c>: one byte of Cb,
/// one of luma, one of Cr, one more of luma, repeating across each row with no padding of its own.
/// Nothing about the header scales with width the way the picture rows do; it is a fixed count of
/// blank lines tied to which of the two standards a stream is, not a formula computed from either
/// dimension.
/// <para/>
/// <b>Verified on the planes, not on packed colour, and exactly.</b> This is a lossless packing of the
/// eight-bit samples themselves, so <see cref="DecodePlanes"/> was compared against ffmpeg's own
/// <c>-pix_fmt uyvy422</c> raw output of the same content — luma at the full width, chroma at half of
/// it, both planes at the full height — over 720x486 and 720x576 pseudo-random pictures, fifty frames
/// each: every sample of every plane of every frame comes back identical to what ffmpeg wrote before
/// the packing, and the header bytes ahead of it are all zero in every one of the hundred frames
/// measured, which is what a decoder that only ever skips them rather than interpreting them needs to
/// be true.
/// <para/>
/// <b>The packed colour <see cref="TryDecode"/> hands back is a display convention on top of that</b>
/// — ITU-R BT.601 with studio swing, and each chroma pair repeated across the two luma columns it
/// covers, the same choice this package's HuffYUV and v210 decoders made and for the same reason: it
/// is what a reference decoder's own conversion does rather than something this coding states.
/// <para/>
/// <b>What refuses.</b> A picture whose width and height are not one of the two geometries ffmpeg's
/// own encoder ever writes, and a packet shorter than its header plus its picture's stride times its
/// height.
/// </remarks>
public sealed class AvuiVideoDecoder : IVideoCodecDecoder<AvuiVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVUI");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _chromaWidth;
  private readonly int _rowStride;
  private readonly int _headerSize;

  private AvuiVideoDecoder(int width, int height, int streamIndex, int headerLines) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._chromaWidth = width / 2;
    this._rowStride = width * 2;
    this._headerSize = this._rowStride * headerLines;
  }

  public static string CodecName => "Avid Meridien Uncompressed (avui)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static AvuiVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    var headerLines = (stream.Width, stream.Height) switch {
      (720, 486) => 10,
      (720, 576) => 16,
      _ => -1,
    };

    if (headerLines < 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which is neither of "
        + "the two geometries avui's own encoder ever writes — 720x486 or 720x576 — and there is no real stream to "
        + "say what a header of any other size would mean.");

    return new(stream.Width, stream.Height, stream.Index, headerLines);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var (luma, cb, cr) = this.DecodePlanes(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ToRgb24(luma, cb, cr),
    };

    return true;
  }

  /// <summary>
  /// Unpacks one frame into its luma and chroma planes, skipping the fixed run of blank lines ahead
  /// of the picture — the form <c>-pix_fmt uyvy422</c> writes and the one this was verified against.
  /// </summary>
  internal (byte[] Luma, byte[] Cb, byte[] Cr) DecodePlanes(ReadOnlySpan<byte> data) {
    var expected = (long)this._headerSize + (long)this._rowStride * this._height;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an avui packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame needs {expected} — {this._headerSize} of header plus its picture.");

    var picture = data[this._headerSize..];
    var luma = new byte[this._width * this._height];
    var cb = new byte[this._chromaWidth * this._height];
    var cr = new byte[this._chromaWidth * this._height];

    for (var row = 0; row < this._height; ++row) {
      var line = picture.Slice(row * this._rowStride, this._rowStride);
      var lumaBase = row * this._width;
      var chromaBase = row * this._chromaWidth;
      var column = 0;
      var chromaColumn = 0;

      for (var offset = 0; offset < this._rowStride; offset += 4) {
        cb[chromaBase + chromaColumn] = line[offset];
        luma[lumaBase + column] = line[offset + 1];
        cr[chromaBase + chromaColumn] = line[offset + 2];
        luma[lumaBase + column + 1] = line[offset + 3];
        ++chromaColumn;
        column += 2;
      }
    }

    return (luma, cb, cr);
  }

  /// <summary>
  /// ITU-R BT.601, studio swing, each chroma pair repeated across the two luma columns it covers.
  /// </summary>
  private byte[] _ToRgb24(byte[] luma, byte[] cb, byte[] cr) {
    var rgb = new byte[this._width * this._height * 3];

    for (var y = 0; y < this._height; ++y) {
      var lumaRow = y * this._width;
      var chromaRow = y * this._chromaWidth;
      var target = y * this._width * 3;

      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = x >> 1;
        var scaledLuma = 298 * (luma[lumaRow + x] - 16);
        var blueDifference = cb[chromaRow + chromaColumn] - 128;
        var redDifference = cr[chromaRow + chromaColumn] - 128;

        rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        target += 3;
      }
    }

    return rgb;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;

    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
