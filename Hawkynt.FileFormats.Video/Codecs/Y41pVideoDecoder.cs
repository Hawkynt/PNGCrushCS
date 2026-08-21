using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes y41p: 4:1:1 YUV with nothing compressed at all, twelve bytes packing eight luma samples
/// and the two chroma pairs that cover them.
/// </summary>
/// <remarks>
/// A packing rule and not a codec in the sense most of this package's other entries are. There is no
/// MultimediaWiki page for this one, so the layout below was recovered rather than read: synthetic
/// <c>yuv411p</c> frames of pseudo-random content were carried through ffmpeg's own y41p encoder and
/// swept against every placement of which byte holds which sample, keeping the one that matches
/// every sample of every row. One group of twelve bytes is
/// <code>
/// U(0,1,2,3)  Y(0)  V(0,1,2,3)  Y(1)  U(4,5,6,7)  Y(2)  V(4,5,6,7)  Y(3)  Y(4)  Y(5)  Y(6)  Y(7)
/// </code>
/// — the first chroma pair ahead of the first two luma samples, the second chroma pair ahead of the
/// next two, and the last four luma samples of the group running on with no chroma among them at
/// all. A row is exactly <c>width</c> times one and a half bytes, and there is no padding: ffmpeg's
/// own encoder refuses a width that is not a whole number of eight-pixel groups outright, rather than
/// coding a partial one, so this refuses the same width for the same reason instead of guessing at a
/// layout nothing produces.
/// <para/>
/// <b>Rows are coded bottom row first</b> — the same convention every Windows bitmap this format was
/// built around uses, and the reason the first sweep against real content found no placement that
/// fit: every byte was in a plausible-looking position and none of them were the right sample, because
/// the row being compared against was the wrong row entirely. Comparing each coded row against the
/// picture's rows in reverse is what turned a match rate indistinguishable from noise into an exact
/// one.
/// <para/>
/// <b>Verified on the planes, not on packed colour, and exactly.</b> This is a lossless packing of
/// the eight-bit samples themselves, so <see cref="DecodePlanes"/> was compared against ffmpeg's own
/// <c>-pix_fmt yuv411p</c> raw output of the same content — luma at the full width, chroma at a
/// quarter of it, both planes at the full height — over synthetic pseudo-random pictures at 64x8,
/// 96x40 and 128x33, all a whole number of eight-pixel groups since ffmpeg's encoder accepts no
/// other, and 90 frames across them: every sample of every plane of every frame comes back identical
/// to what ffmpeg wrote before the packing.
/// <para/>
/// <b>The packed colour <see cref="TryDecode"/> hands back is a display convention on top of that</b>
/// — ITU-R BT.601 with studio swing, and each chroma pair repeated across the four luma columns it
/// covers, the same choice this package's HuffYUV decoder made for 4:2:2 and for the same reason: it
/// is what a reference decoder's own conversion does rather than something this coding states.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, a width that is not a multiple of eight, and a
/// packet shorter than its stride times its height.
/// </remarks>
public sealed class Y41pVideoDecoder : IVideoCodecDecoder<Y41pVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("Y41P");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _chromaWidth;
  private readonly int _stride;

  private Y41pVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._chromaWidth = width / 4;
    this._stride = width + width / 2; // width * 1.5, exactly — twelve bytes an eight-pixel group.
  }

  public static string CodecName => "Uncompressed YUV 4:1:1 (y41p)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Y41pVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    if (stream.Width % 8 != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a width of {stream.Width}, which y41p's eight-pixel groups do not "
        + "divide evenly — no encoder writes one, and there is no real stream to say what a partial group would "
        + "mean.");

    return new(stream.Width, stream.Height, stream.Index);
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
  /// Unpacks one frame into its luma and chroma planes — the form <c>-pix_fmt yuv411p</c> writes and
  /// the one this was verified against.
  /// </summary>
  internal (byte[] Luma, byte[] Cb, byte[] Cr) DecodePlanes(ReadOnlySpan<byte> data) {
    var expected = (long)this._stride * this._height;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a y41p packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var luma = new byte[this._width * this._height];
    var cb = new byte[this._chromaWidth * this._height];
    var cr = new byte[this._chromaWidth * this._height];

    for (var row = 0; row < this._height; ++row) {
      var line = data.Slice(row * this._stride, this._stride);
      // Rows are coded bottom row first, the convention every Windows bitmap this format was built
      // around uses — measured on a frame of known content, where reading rows top-down decodes
      // every row of the picture in the wrong place.
      var displayRow = this._height - 1 - row;
      var lumaBase = displayRow * this._width;
      var chromaBase = displayRow * this._chromaWidth;
      var column = 0;
      var chromaColumn = 0;

      for (var offset = 0; offset < this._stride; offset += 12) {
        cb[chromaBase + chromaColumn] = line[offset];
        luma[lumaBase + column] = line[offset + 1];
        cr[chromaBase + chromaColumn] = line[offset + 2];
        luma[lumaBase + column + 1] = line[offset + 3];
        ++chromaColumn;

        cb[chromaBase + chromaColumn] = line[offset + 4];
        luma[lumaBase + column + 2] = line[offset + 5];
        cr[chromaBase + chromaColumn] = line[offset + 6];
        luma[lumaBase + column + 3] = line[offset + 7];
        ++chromaColumn;

        luma[lumaBase + column + 4] = line[offset + 8];
        luma[lumaBase + column + 5] = line[offset + 9];
        luma[lumaBase + column + 6] = line[offset + 10];
        luma[lumaBase + column + 7] = line[offset + 11];

        column += 8;
      }
    }

    return (luma, cb, cr);
  }

  /// <summary>
  /// ITU-R BT.601, studio swing, each chroma pair repeated across the four luma columns it covers.
  /// </summary>
  private byte[] _ToRgb24(byte[] luma, byte[] cb, byte[] cr) {
    var rgb = new byte[this._width * this._height * 3];

    for (var y = 0; y < this._height; ++y) {
      var lumaRow = y * this._width;
      var chromaRow = y * this._chromaWidth;
      var target = y * this._width * 3;

      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = x >> 2;
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
