using System;
using System.IO;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Intel Indeo 3 (<c>IV31</c>, <c>IV32</c>): a picture cut into cells by a binary tree, each
/// cell either fetched from the frame before or built from a fixed table of delta pairs.
/// </summary>
/// <remarks>
/// Nothing like Indeo 2 beyond the name and the sample layout. A plane carries a binary tree that cuts
/// it into rectangular cells, and each leaf of that tree says what its cell is: still, moved by a
/// vector, or coded. A coded cell names one of twenty-four quantisation tables and one of six coding
/// modes, and is then a run of bytes each of which either names a pair of deltas — or two pairs packed
/// into one byte — or says that some number of lines, or of whole blocks, carry no change at all.
/// <para/>
/// <b>The tree and the cell data share one buffer and interleave.</b> The tree is a bit stream; the
/// vectors and the coded cells are whole bytes taken from the point the tree has reached, rounded up to
/// a byte boundary. So reading a leaf moves the byte position and the tree has to be pushed past what
/// it took — but only once the tree itself reaches a boundary. That single rule is most of what a
/// decoder of this format has to get right.
/// <para/>
/// <b>None of the tables are in the file.</b> A cell names a table in four bits; the twenty-four tables
/// those four bits address, the requantisation table that keeps a prediction inside range, and the
/// delta pairs themselves are all part of the codec — see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> in
/// <c>Codecs/Indeo/</c> for where they come from.
/// <para/>
/// <b>Measured.</b> Nine real files from <c>samples.ffmpeg.org/V-codecs/IV32/</c> — 152x116 to 320x240,
/// 4,828 pictures out of 4,986 packets, one of the files a QuickTime rather than an AVI — were decoded
/// here and by ffmpeg and compared against ffmpeg's own decoded <c>yuv410p</c> planes, sample for
/// sample, every frame of every file: Y, Cb and Cr are all bit-exact, with no difference at all, on
/// every one. The planes and not RGB are what settle it, because the RGB is a display convention this
/// decoder chose and the planes are the decode.
/// <para/>
/// The 158 packets that made no picture are the same 158 ffmpeg refuses, on the same three damaged
/// files and for the same reasons — one of them, <c>indeo3sux.avi</c>, is refused from its first packet
/// to its last by both. That the refusals line up frame for frame is part of the measurement: a decoder
/// that gave up one packet earlier or later than ffmpeg would have produced a different film out of the
/// same file even with every surviving frame identical.
/// <para/>
/// <c>IV31</c> is taken as well as <c>IV32</c>, and no file coded as <c>IV31</c> could be found to
/// measure. The two codes name one bitstream, and a frame states its own version in its header rather
/// than taking it from the code — so nothing here reads the code beyond deciding that this is the
/// decoder for the stream.
/// <para/>
/// <b>What it does not read refuses by name.</b> Eight-bit samples and half-sample motion vectors —
/// both flagged in a frame's header, neither written by any encoder a corpus holds; the "skip cell"
/// null code, whose effect on the two frame buffers is stated nowhere and which no measured file uses;
/// a coding mode outside the six the format defines; and a picture outside 16x16 to 640x480, which is
/// the range the codec's own buffers are defined over. Malformed input — a header that fails its own
/// checksum, plane offsets outside the frame, a cell reaching outside its plane, a motion vector
/// pointing off the picture, a tree deeper than twenty levels or one that runs out of bits — refuses
/// too, rather than handing back a picture built from part of a frame.
/// </remarks>
public sealed class Indeo3VideoDecoder : IVideoCodecDecoder<Indeo3VideoDecoder> {

  /// <summary>The four-character codes Indeo 3 is named by.</summary>
  /// <remarks>
  /// Two codes and one bitstream. <c>IV31</c> is the original release and <c>IV32</c> the revision, and
  /// a frame states its own bitstream version in its header rather than taking it from the code, so the
  /// same decoder reads both. QuickTime files carry the same four letters in lower case, which the
  /// case-insensitive comparison covers.
  /// </remarks>
  private static readonly CodecTag[] _TAGS = [CodecTag.FromCharacters("IV31"), CodecTag.FromCharacters("IV32")];

  private readonly Indeo3FrameDecoder _frameDecoder;

  public static string CodecName => "Intel Indeo 3";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _TAGS)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// The container's picture size is what the frame buffers are made at, and a frame that states a
  /// different one has them made again — so the container is believed until a frame contradicts it,
  /// which is the order the format itself implies: a frame's header carries a size precisely because
  /// the size may change part way through a stream.
  /// </remarks>
  public static Indeo3VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"An Indeo 3 video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    return new(stream.Width, stream.Height);
  }

  private Indeo3VideoDecoder(int width, int height) => this._frameDecoder = new(width, height);

  /// <summary>The frame last decoded, as the three planes the codec actually codes.</summary>
  /// <remarks>
  /// The picture <see cref="TryDecode"/> hands back is those planes plus a display convention this
  /// decoder chose — a colour matrix and a chrominance upsampling neither the codec nor any file says
  /// anything about. The planes are the decode, so they are what a comparison against another decoder
  /// is made on.
  /// </remarks>
  internal Indeo3FrameDecoder Planes => this._frameDecoder;

  /// <summary>
  /// Decodes one packet, which is one whole frame or a sync frame carrying no picture at all.
  /// </summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = default!;

    var decoder = this._frameDecoder;
    if (!decoder.Decode(packet.Data.Span))
      return false;

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
