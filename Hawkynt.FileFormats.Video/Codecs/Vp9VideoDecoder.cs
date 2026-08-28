using System;
using System.Collections.Generic;
using FileFormat.Codecs.Vp9;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes VP9 video, profile 0.
/// </summary>
/// <remarks>
/// The codec every modern <c>V_VP9</c> track in a WebM document is coded with. Everything profile 0
/// has is here: the uncompressed and compressed headers, the four frame contexts and the probability
/// updates they carry, tiles, the recursive superblock partition down to 4x4, the motion vector
/// reference scan, the coefficient tokens with their per-transform scan orders, the cosine transform
/// at four sizes and the sine transform at three, the lossless Walsh-Hadamard transform, all ten intra
/// modes at four block sizes, eight-tap inter prediction with reference scaling and compound
/// prediction, the loop filter, and the backward probability adaptation that requires counting every
/// syntax element the frame contained.
/// <para/>
/// <b>Profile 0 only, and it says so.</b> Profiles 1 and 3 carry chrominance at 4:2:2, 4:4:0 or 4:4:4
/// and profiles 2 and 3 carry ten or twelve bits a sample. Those are not a larger version of this
/// decoder — the transforms, the prediction and the loop filter all change shape — so a stream that
/// states one throws and names the profile. That is a whole boundary rather than a partial one:
/// profile 0 is what WebM overwhelmingly carries, and an eight-bit 4:2:0 stream is decoded completely
/// or not at all.
/// <para/>
/// <b>What it does not do refuses by name.</b> A reserved profile, an sRGB colour space in a profile
/// that cannot hold one, a missing sync code, a frame that predicts from a reference slot nothing has
/// written, a truncated compressed header or tile, a reference too far from the current frame's size
/// to be scaled — each of those throws and says which field was wrong. There is no <c>catch</c>
/// anywhere that hands back a blank frame or repeats the last one. That matters more for VP9 than for
/// most codecs: a repeated frame is exactly what a still passage of a film looks like, so a decoder
/// that produced one on failure would be indistinguishable from one that was working.
/// <para/>
/// <b>Measured.</b> Every frame of every test stream was decoded here and by ffmpeg and compared plane
/// by plane, sample by sample. The planes are identical: not close, not on average, the same bytes.
/// VP9's inverse transforms are specified down to the rounding of every intermediate, so that is the
/// only acceptable result — and it is the measurement that matters, because a mistake in the loop
/// filter, in prediction or in the probability adaptation shows up as a small difference that grows
/// with every frame until the next key frame.
/// <para/>
/// Those exact planes are now what this decoder returns: canonical <see cref="PixelFormat.Yuv420P8"/>
/// with the stream's full/limited-range flag kept in <see cref="RawImage.ColorInfo"/>. RGB conversion
/// is a display or writer concern and can be requested through <see cref="RawImageConverter"/>; it is
/// no longer baked into a successful VP9 decode.
/// </remarks>
public sealed class Vp9VideoDecoder : IVideoCodecDecoder<Vp9VideoDecoder> {

  /// <summary>What Matroska and WebM call this codec, which is a string because Matroska has no code field.</summary>
  private const string _MATROSKA_CODEC_ID = "V_VP9";

  /// <summary>The four-character codes containers with a code field name VP9 with.</summary>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("VP90"),
    CodecTag.FromCharacters("vp09"),
  ];

  private readonly Vp9Decoder _decoder = new();

  /// <summary>
  /// The pictures decoded but not yet handed back.
  /// </summary>
  /// <remarks>
  /// A packet is not a frame in VP9: one chunk may carry several coded frames of which none, one or
  /// several are meant to be shown. The contract here is one picture per packet, so a chunk that
  /// shows more than one leaves the rest here to come out of the following packets and, at the end of
  /// the stream, out of <see cref="Flush"/>. In practice a chunk shows one picture or none.
  /// </remarks>
  private readonly Queue<RawImage> _pending = new();

  public static string CodecName => "VP9 (profile 0)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    if (string.Equals(stream.CodecId, _MATROSKA_CODEC_ID, StringComparison.OrdinalIgnoreCase))
      return true;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// Nothing is read from the stream description, not even the picture size or the profile. VP9
  /// states both in the frame header, and where a container's copy disagrees it is the container's
  /// copy that is wrong.
  /// </remarks>
  public static Vp9VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>
  /// Decodes one packet, which for VP9 is one coded frame or a superframe of several.
  /// </summary>
  /// <returns>
  /// <c>false</c> when the packet holds no picture the stream asked to be shown. VP9 uses frames that
  /// exist only to become references for later ones — an alternate reference built from several source
  /// frames at once, say — and handing one of those back would put a picture on screen that the film
  /// does not contain.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    foreach (var picture in this._decoder.Decode(packet.Data.Span))
      this._pending.Enqueue(
        RawImageFactory.FromYuv420P8(
          picture.Width,
          picture.Height,
          picture.Luma,
          picture.LumaWidth,
          picture.Cb,
          picture.Cr,
          picture.ChromaWidth,
          colorInfo: new() {
            Range = this._decoder.ColorRange != 0 ? RawColorRange.Full : RawColorRange.Limited,
            Matrix = RawMatrixCoefficients.Bt601,
            ChromaLocation = RawChromaLocation.Center,
          }));

    if (this._pending.Count == 0) {
      frame = null!;
      return false;
    }

    frame = this._pending.Dequeue();
    return true;
  }

  public IEnumerable<RawImage> Flush() {
    while (this._pending.Count > 0)
      yield return this._pending.Dequeue();
  }
}
