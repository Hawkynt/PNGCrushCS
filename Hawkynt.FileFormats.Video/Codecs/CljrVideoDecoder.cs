using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes CLJR: Cirrus Logic AccuPak, four pixels of 4:1:1 YUV quantised into one 32-bit big-endian
/// word.
/// </summary>
/// <remarks>
/// The loss is the encoder's — quantising eight-bit luma down to five bits and eight-bit chroma down
/// to six, so that four luma samples and the one chroma pair covering them fit thirty-two bits exactly
/// — and a decoder reading the same bits back has nothing left to round; the quantisation itself, and
/// which four-pixel group's word holds which sample, is what this file recovers rather than a table
/// this format documents anywhere this project found.
/// <para/>
/// <b>The word</b>, read big-endian: bits 27-31 are the fourth luma sample, bits 22-26 the third, bits
/// 17-21 the second and bits 12-16 the first — the four columns in <i>reverse</i> order — then bits
/// 6-11 are the shared chroma blue difference and bits 0-5 the shared red difference, both for the
/// same four columns. Found by generating four-pixel groups of pseudo-random content, quantising them
/// through ffmpeg's own encoder with dithering held to one fixed algorithm, and sweeping every
/// placement of five- and six-bit fields within the word against ffmpeg's own decode of what it wrote,
/// which is the oracle that has to be used here: dithering carries a sample's rounding error into the
/// columns after it, so a coded word does not equal a plain quantisation of the source picture, and
/// only another decoder's reading of what the word itself states is a fact this format's encoder could
/// be checked against.
/// <para/>
/// <b>Two different ways of turning five and six bits back into eight</b>, and they are not the same
/// rule at two widths. Luma replicates its own top three bits into the three bits it does not carry —
/// value <c>v</c> becomes <c>(v &lt;&lt; 3) | (v &gt;&gt; 2)</c>, the usual way of filling in a
/// narrower channel without ever landing short of white. Chroma does not: a coded value of 41 decodes
/// to 164, which is <c>41 &lt;&lt; 2</c> exactly and not the 166 the same replication would give,
/// measured across every mismatch a first attempt at replicating both left — the low two bits of a
/// decoded chroma sample are always zero.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Three geometries and sixty frames of
/// pseudo-random <c>yuv411p</c> content, quantised through ffmpeg's encoder and decoded both here and
/// by ffmpeg: every sample of every plane of every frame identical. Rows run top to bottom, unlike
/// y41p's — checked the same way that format's row order was found, by comparing against the reverse
/// order first and finding it wrong on nineteen twentieths of the picture.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, a width that is not a multiple of four — no encoder
/// writes one, per ffmpeg's own refusal of the same width — and a packet shorter than its stride times
/// its height.
/// </remarks>
public sealed class CljrVideoDecoder : IVideoCodecDecoder<CljrVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("CLJR");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _chromaWidth;
  private readonly int _stride;

  private CljrVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._chromaWidth = width / 4;
    this._stride = width; // one byte a pixel, on average — four pixels a four-byte group.
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Cirrus Logic AccuPak (CLJR)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static CljrVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    if (stream.Width % 4 != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a width of {stream.Width}, which CLJR's four-pixel groups do not "
        + "divide evenly — no encoder writes one, and ffmpeg's own refuses the same width.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
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
        $"Video stream {this._streamIndex} carries a CLJR packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var luma = new byte[this._width * this._height];
    var cb = new byte[this._chromaWidth * this._height];
    var cr = new byte[this._chromaWidth * this._height];

    for (var row = 0; row < this._height; ++row) {
      var line = data.Slice(row * this._stride, this._stride);
      var lumaBase = row * this._width;
      var chromaBase = row * this._chromaWidth;
      var column = 0;
      var chromaColumn = 0;

      for (var offset = 0; offset < this._stride; offset += 4) {
        var word = BinaryPrimitives.ReadUInt32BigEndian(line[offset..]);

        var y3 = (word >> 27) & 0x1F;
        var y2 = (word >> 22) & 0x1F;
        var y1 = (word >> 17) & 0x1F;
        var y0 = (word >> 12) & 0x1F;
        var blue = (word >> 6) & 0x3F;
        var red = word & 0x3F;

        luma[lumaBase + column] = _WidenLuma(y0);
        luma[lumaBase + column + 1] = _WidenLuma(y1);
        luma[lumaBase + column + 2] = _WidenLuma(y2);
        luma[lumaBase + column + 3] = _WidenLuma(y3);
        cb[chromaBase + chromaColumn] = _WidenChroma(blue);
        cr[chromaBase + chromaColumn] = _WidenChroma(red);

        column += 4;
        ++chromaColumn;
      }
    }

    return (luma, cb, cr);
  }

  /// <summary>Five bits to eight, filling the low three by replicating the top three.</summary>
  private static byte _WidenLuma(uint value) => (byte)((value << 3) | (value >> 2));

  /// <summary>Six bits to eight, the low two left at zero rather than replicated.</summary>
  private static byte _WidenChroma(uint value) => (byte)(value << 2);

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
