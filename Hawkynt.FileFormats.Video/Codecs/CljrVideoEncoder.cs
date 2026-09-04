using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes CLJR: Cirrus Logic AccuPak, four pixels of 4:1:1 YUV quantised into one 32-bit big-endian
/// word.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/cljrenc.c</c>, copyright (c) 2003 Alex Beregszaszi,
/// LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// <b>The loss is here.</b> Eight-bit luma is quantised to five bits and eight-bit chroma to six, so
/// that four luma samples and the one chroma pair covering them fit thirty-two bits exactly; the
/// decoder has nothing left to round. The quantiser is the reference's own — luma
/// <c>(249 * (y + d)) &gt;&gt; 11</c>, chroma <c>(253 * (c + d)) &gt;&gt; 10</c> — with the offset
/// <c>d</c> held at the reference's fixed setting of two for every sample rather than at its default
/// pseudo-random dither, so the same picture always codes to the same bytes and a coded word is a plain
/// function of the four pixels it holds. Against the decoder's widening of the same bits back to eight
/// — luma <c>(v &lt;&lt; 3) | (v &gt;&gt; 2)</c>, chroma <c>v &lt;&lt; 2</c> — no luma sample lands
/// more than 6 from where it started and no chroma sample more than 4, measured over every value of
/// each.
/// <para/>
/// <b>The word</b>, written big-endian: bits 27-31 the fourth luma sample, bits 22-26 the third, bits
/// 17-21 the second and bits 12-16 the first — the four columns in <i>reverse</i> order — then bits
/// 6-11 the shared blue difference and bits 0-5 the shared red difference. Rows run top to bottom.
/// <para/>
/// An eight-bit planar picture is taken as it is, its chroma averaged down to one pair per four
/// columns where the source carried more; anything else — the <see cref="PixelFormat.Rgb24"/> the
/// decoder hands back included — is first converted under the same ITU-R BT.601 studio-swing
/// convention the decoder displays with.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Packets written here were muxed into an AVI
/// and read back through ffmpeg 9's CLJR decoder as <c>yuv411p</c> planes, over pseudo-random pictures
/// at 12x5, 64x33 and 100x7, five frames apiece: every sample of every plane of every frame is the
/// quantiser's own rounding of the source computed independently — the formula above, then the
/// widening — and none is more than 6 (luma) or 4 (chroma) from the sample it was coded from.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, a width that is not a multiple of four — the decoder
/// refuses the same width, and so does ffmpeg's encoder — and a frame whose geometry differs from the
/// stream's.
/// </remarks>
public sealed class CljrVideoEncoder : IVideoCodecEncoder<CljrVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("CLJR");

  /// <summary>
  /// The reference's fixed rounding offset (its <c>dither_type</c> 0, the constant 0x492A0000) taken
  /// apart: two for every luma sample and two for every chroma sample.
  /// </summary>
  private const int _LUMA_OFFSET = 2;
  private const int _CHROMA_OFFSET = 2;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _stride;

  private CljrVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._chromaWidth = stream.Width / 4;
    this._stride = stream.Width; // one byte a pixel, on average — four pixels a four-byte group.
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 8,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Cirrus Logic AccuPak (CLJR)";

  public static CodecTag Codec => _Tag;

  public static CljrVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("CLJR can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from.");

    if (stream.Width % 4 != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a width of {stream.Width}, which CLJR's four-pixel groups do not "
        + "divide evenly — no decoder reads one, and ffmpeg's own encoder refuses the same width.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"CLJR geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var (luma, cb, cr) = Yuv411Planes.FromImage(frame);
    packet = new(
      this._stream.Index,
      this.EncodePlanes(luma, cb, cr),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>
  /// Quantises and packs one frame from its luma and chroma planes — the form <c>-pix_fmt yuv411p</c>
  /// writes, luma at the full width and each chroma plane at a quarter of it, every plane at the full
  /// height.
  /// </summary>
  internal byte[] EncodePlanes(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr) {
    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._height;
    if (luma.Length < lumaSamples || cb.Length < chromaSamples || cr.Length < chromaSamples)
      throw new InvalidDataException(
        $"A {this._width}x{this._height} CLJR frame needs {lumaSamples} luma and {chromaSamples} samples a chroma plane; "
        + $"received {luma.Length}, {cb.Length} and {cr.Length}.");

    var data = new byte[this._stride * this._height];

    for (var row = 0; row < this._height; ++row) {
      var line = data.AsSpan(row * this._stride, this._stride);
      var lumaBase = row * this._width;
      var chromaBase = row * this._chromaWidth;
      var column = 0;
      var chromaColumn = 0;

      for (var offset = 0; offset < this._stride; offset += 4) {
        var word = (_QuantiseLuma(luma[lumaBase + column + 3]) << 27)
          | (_QuantiseLuma(luma[lumaBase + column + 2]) << 22)
          | (_QuantiseLuma(luma[lumaBase + column + 1]) << 17)
          | (_QuantiseLuma(luma[lumaBase + column]) << 12)
          | (_QuantiseChroma(cb[chromaBase + chromaColumn]) << 6)
          | _QuantiseChroma(cr[chromaBase + chromaColumn]);
        BinaryPrimitives.WriteUInt32BigEndian(line[offset..], word);

        column += 4;
        ++chromaColumn;
      }
    }

    return data;
  }

  /// <summary>Eight bits to five, the reference's own scale and shift.</summary>
  private static uint _QuantiseLuma(byte value) => (uint)((249 * (value + _LUMA_OFFSET)) >> 11);

  /// <summary>Eight bits to six, the reference's own scale and shift.</summary>
  private static uint _QuantiseChroma(byte value) => (uint)((253 * (value + _CHROMA_OFFSET)) >> 10);
}
