using System;
using System.IO;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Intel Indeo 2 (<c>RT21</c>): one Huffman code table, four delta tables, and a picture built
/// two samples at a time either from the line above or from the frame before.
/// </summary>
/// <remarks>
/// The simplest of the Indeo family and nothing like the three that follow it — no blocks, no motion
/// vectors, no transform. A plane is a stream of Huffman codes read least-significant-bit first; a code
/// either names a pair of entries in one of four fixed delta tables or a run of pairs that change
/// nothing. An intra frame's first line takes the table entries as sample values outright and every
/// line after it as differences from the line above; an inter frame takes them as differences from the
/// same plane of the previous frame, at three quarters of their stated strength.
/// <para/>
/// <b>None of the tables are in the file.</b> A frame states which of the four delta tables its
/// luminance uses and which its chrominance uses, in two bit pairs of one header byte, and nothing
/// else; the Huffman table is never named at all. That is why this decoder carries them —
/// see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> in <c>Codecs/Indeo/</c>.
/// <para/>
/// <b>Measured.</b> Six real <c>RT21</c> files from <c>samples.ffmpeg.org/V-codecs/RT21/</c>, all
/// 160x120, 1,984 frames in all, were decoded here and by ffmpeg and compared against ffmpeg's own
/// decoded <c>yuv410p</c> planes, sample for sample, every frame of every file: Y, Cb and Cr are all
/// bit-exact, with no difference at all, on every one. The planes and not RGB are what settle it,
/// because the RGB is a display convention this decoder chose and the planes are the decode.
/// <para/>
/// <b>What it does not read refuses by name.</b> A picture whose width does not divide by eight or
/// whose height does not divide by four, a frame shorter than its own 48-byte header, a chrominance
/// table index outside the four the codec defines, a run that overruns the line it starts on, a plane
/// whose bits run out before its samples do, and any bit pattern that is not one of the 143 codes.
/// There is no fallback returning the previous frame: an inter frame that changes nothing is an
/// ordinary Indeo 2 frame, so handing one back on failure would be indistinguishable from working.
/// </remarks>
public sealed class Indeo2VideoDecoder : IVideoCodecDecoder<Indeo2VideoDecoder> {

  /// <summary>The four-character code Indeo 2 is named by.</summary>
  /// <remarks>
  /// One code and one spelling. Indeo 2 predates the QuickTime side of the family, and every file that
  /// carries it carries it in an AVI stream header as <c>RT21</c> — Intel's own name for the codec
  /// before "Indeo" was one.
  /// </remarks>
  private static readonly CodecTag _TAG = CodecTag.FromCharacters("RT21");

  /// <summary>The chrominance planes are a quarter of the picture in both directions.</summary>
  private const int _CHROMA_SUBSAMPLING = 4;

  /// <summary>
  /// What the picture's width has to be a multiple of.
  /// </summary>
  /// <remarks>
  /// Eight and not four. Every code writes a <i>pair</i> of samples, so a plane's width has to be even
  /// — and a chrominance plane is a quarter of the picture's width, which makes the picture's own width
  /// a multiple of eight. The height only has to divide by four, because nothing about a line's length
  /// constrains how many lines there are.
  /// </remarks>
  private const int _WIDTH_MULTIPLE = 8;

  private readonly Indeo2FrameDecoder _frameDecoder;

  public static string CodecName => "Intel Indeo 2";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_TAG);
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// The picture size comes from the container, because an Indeo 2 frame does not carry one. Its header
  /// says which delta tables the frame uses and how the frame is typed, and the plane decoders count
  /// samples rather than reading a size — so a stream that states no size, or one the chrominance
  /// planes cannot be derived from, cannot be decoded at all and is refused here rather than producing
  /// a picture of the wrong shape.
  /// </remarks>
  public static Indeo2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"An Indeo 2 video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    if (stream.Width % _WIDTH_MULTIPLE != 0 || stream.Height % _CHROMA_SUBSAMPLING != 0)
      throw new NotSupportedException(
        $"An Indeo 2 video stream states a picture of {stream.Width}x{stream.Height}, where the codec codes "
        + $"pairs of samples into chrominance planes a quarter the size — so the width must divide by "
        + $"{_WIDTH_MULTIPLE} and the height by {_CHROMA_SUBSAMPLING}.");

    return new(stream.Width, stream.Height);
  }

  private Indeo2VideoDecoder(int width, int height) => this._frameDecoder = new(width, height);

  /// <summary>The frame last decoded, as the three planes the codec actually codes.</summary>
  /// <remarks>
  /// The picture <see cref="TryDecode"/> hands back is those planes plus a display convention this
  /// decoder chose — a colour matrix and a chrominance upsampling neither the codec nor any file says
  /// anything about. The planes are the decode, so they are what a comparison against another decoder
  /// is made on.
  /// </remarks>
  internal Indeo2FrameDecoder Planes => this._frameDecoder;

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var decoder = this._frameDecoder;
    decoder.Decode(packet.Data.Span);

    frame = new() {
      Width = decoder.Width,
      Height = decoder.Height,
      Format = PixelFormat.Rgb24,
      PixelData = IndeoColorConversion.ToRgb24(
        decoder.Luma, decoder.Cb, decoder.Cr,
        decoder.Width, decoder.Height, decoder.ChromaWidth, decoder.ChromaHeight),
    };

    return true;
  }
}
