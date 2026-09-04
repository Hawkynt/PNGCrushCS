using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes y41p: 4:1:1 YUV with nothing compressed at all, twelve bytes packing eight luma samples
/// and the two chroma pairs that cover them.
/// </summary>
/// <remarks>
/// A plain packing that needs no reference: the layout is the one this package's own
/// <see cref="Y41pVideoDecoder"/> recovered and reads, written back in the same order — one group of
/// twelve bytes is
/// <code>
/// U(0,1,2,3)  Y(0)  V(0,1,2,3)  Y(1)  U(4,5,6,7)  Y(2)  V(4,5,6,7)  Y(3)  Y(4)  Y(5)  Y(6)  Y(7)
/// </code>
/// a row is exactly <c>width</c> times one and a half bytes with no padding, and <b>rows are coded
/// bottom row first</b>, the convention of the Windows bitmaps this format was built around.
/// <para/>
/// <b>Lossless on the planes.</b> The eight-bit samples go into the packet untouched, so a picture's
/// luma and chroma planes come back from the decoder identical; what is lost is only what 4:1:1 has
/// no room for. An eight-bit planar picture is taken as it is, its chroma averaged down to one pair
/// per four columns where the source carried more; anything else — the <see cref="PixelFormat.Rgb24"/>
/// the decoder hands back included — is first converted under the same ITU-R BT.601 studio-swing
/// convention the decoder displays with, so a picture coded here and read back there lands where it
/// started up to the rounding of the matrix and the subsampling.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Packets written here were muxed into an AVI
/// and read back through ffmpeg 9's y41p decoder as <c>yuv411p</c> planes, over pseudo-random pictures
/// at 64x8, 96x40 and 128x33, five frames apiece: every sample of every plane of every frame identical.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, a width that is not a multiple of eight — the decoder
/// refuses the same width, and so does ffmpeg's encoder, so there is no stream to say what a partial
/// group would mean — and a frame whose geometry differs from the stream's.
/// </remarks>
public sealed class Y41pVideoEncoder : IVideoCodecEncoder<Y41pVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("Y41P");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _stride;

  private Y41pVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._chromaWidth = stream.Width / 4;
    this._stride = stream.Width + stream.Width / 2; // width * 1.5, exactly — twelve bytes an eight-pixel group.
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
      BitsPerPixel = 12,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Uncompressed YUV 4:1:1 (y41p)";

  public static CodecTag Codec => _Tag;

  public static Y41pVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("y41p can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from.");

    if (stream.Width % 8 != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a width of {stream.Width}, which y41p's eight-pixel groups do not "
        + "divide evenly — no decoder reads one, and there is no real stream to say what a partial group would "
        + "mean.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"y41p geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
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
  /// Packs one frame from its luma and chroma planes — the form <c>-pix_fmt yuv411p</c> writes, luma
  /// at the full width and each chroma plane at a quarter of it, every plane at the full height.
  /// </summary>
  internal byte[] EncodePlanes(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr) {
    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._height;
    if (luma.Length < lumaSamples || cb.Length < chromaSamples || cr.Length < chromaSamples)
      throw new InvalidDataException(
        $"A {this._width}x{this._height} y41p frame needs {lumaSamples} luma and {chromaSamples} samples a chroma plane; "
        + $"received {luma.Length}, {cb.Length} and {cr.Length}.");

    var data = new byte[this._stride * this._height];

    for (var row = 0; row < this._height; ++row) {
      var line = data.AsSpan(row * this._stride, this._stride);
      // Rows are coded bottom row first, exactly as the decoder reads them back.
      var displayRow = this._height - 1 - row;
      var lumaBase = displayRow * this._width;
      var chromaBase = displayRow * this._chromaWidth;
      var column = 0;
      var chromaColumn = 0;

      for (var offset = 0; offset < this._stride; offset += 12) {
        line[offset] = cb[chromaBase + chromaColumn];
        line[offset + 1] = luma[lumaBase + column];
        line[offset + 2] = cr[chromaBase + chromaColumn];
        line[offset + 3] = luma[lumaBase + column + 1];
        ++chromaColumn;

        line[offset + 4] = cb[chromaBase + chromaColumn];
        line[offset + 5] = luma[lumaBase + column + 2];
        line[offset + 6] = cr[chromaBase + chromaColumn];
        line[offset + 7] = luma[lumaBase + column + 3];
        ++chromaColumn;

        line[offset + 8] = luma[lumaBase + column + 4];
        line[offset + 9] = luma[lumaBase + column + 5];
        line[offset + 10] = luma[lumaBase + column + 6];
        line[offset + 11] = luma[lumaBase + column + 7];

        column += 8;
      }
    }

    return data;
  }
}
