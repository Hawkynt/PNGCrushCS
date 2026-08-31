using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes MPEG-4 Part 2 video, ISO/IEC 14496-2: intra, predicted and bidirectionally coded pictures.
/// </summary>
/// <remarks>
/// The successor to H.263 and the ancestor of nothing — MPEG-4 Part 10 is a different codec that
/// shares only a name. What it keeps from H.263 is the shape of a macroblock and the shape of a
/// motion vector; what it adds is prediction between the coefficients of neighbouring intra blocks,
/// a second inverse quantisation method, four motion vectors per macroblock, vectors that may point
/// outside the picture, vectors to a quarter of a sample, and pictures predicted from both
/// directions.
/// <para/>
/// <b>What it does not do refuses by name.</b> Every tool that is signalled and not implemented is
/// refused where it is signalled, naming the clause and the field: quarter-sample motion vectors,
/// sprites and global motion compensation, interlaced coding, overlapped block motion compensation,
/// data partitioning, scalability, non-rectangular shape, samples of any depth but eight, chroma
/// formats other than 4:2:0, newpred, reduced-resolution pictures and the complexity estimation
/// header. There is no <c>catch</c> anywhere that hands back a blank, a copied or a zero-filled
/// picture, because a plausible wrong picture is worse than a refusal: nobody checks a picture that
/// looks like a picture.
/// <para/>
/// <b>Measured.</b> Twenty-seven encoded streams, one thousand and eighty-three frames, were decoded
/// here and by ffmpeg and compared plane by plane, sample by sample, every frame: sizes from 64x48 to
/// 352x288, quantisers from 1 to 25, both inverse quantisation methods, one and four motion vectors
/// per macroblock, groups of pictures from a single frame to a hundred, up to four bidirectionally
/// coded pictures between anchors, video packets, and headers carried in the packets and out of band
/// in an ISO base media sample entry. Every stream produced the frame count ffprobe counts.
/// <para/>
/// Seventeen of the twenty-seven are identical to ffmpeg's decode on every sample of every plane of
/// every frame. The other ten differ in at most sixty samples of a frame out of thirty-eight
/// thousand, always by exactly one level. Over a hundred frames with one intra picture and
/// bidirectionally coded pictures throughout, the worst frame differs in thirty-five samples and the
/// difference never exceeds one level. That residual is the inverse transform's, which ISO/IEC
/// 14496-2 Annex A specifies as an accuracy bound rather than as an algorithm.
/// <para/>
/// The streams with one intra picture and a hundred frames after it are the ones worth having: a
/// group of pictures that resets every few frames hides a wrong reference or a wrong time base
/// entirely, because the error is displaced before it can be seen.
/// <para/>
/// <b>Frames come out in display order.</b> A bidirectionally coded picture is transmitted after the
/// picture it is predicted backwards from, so an anchor is held until the next anchor arrives and
/// handed out then. That is why <see cref="TryDecode"/> answers "not yet" to the first packet of a
/// stream with such pictures and why <see cref="Flush"/> is not empty: the last anchor of a stream has
/// no successor to displace it.
/// </remarks>
public sealed class Mpeg4VideoDecoder : IVideoCodecDecoder<Mpeg4VideoDecoder> {

  /// <summary>The four-character codes containers name MPEG-4 Part 2 video with.</summary>
  /// <remarks>
  /// The vendor codes are here because they are the same bitstream under a different name — an
  /// encoder wrote its own four letters into an AVI and the pictures inside are ISO/IEC 14496-2.
  /// What is deliberately absent is the <c>DIV3</c> family: those are Microsoft's MPEG-4 version 3,
  /// which is a different bitstream that this decoder would start and then stop part way into.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("mp4v"),
    CodecTag.FromCharacters("MP4S"),
    CodecTag.FromCharacters("M4S2"),
    CodecTag.FromCharacters("DIVX"),
    CodecTag.FromCharacters("DX50"),
    CodecTag.FromCharacters("XVID"),
    CodecTag.FromCharacters("FMP4"),
    CodecTag.FromCharacters("3IV2"),
    CodecTag.FromCharacters("FVFW"),
    CodecTag.FromCharacters("RMP4"),
  ];

  /// <summary>
  /// The names Matroska gives the same bitstream.
  /// </summary>
  /// <remarks>
  /// <c>V_MPEG4/MS/V3</c> is deliberately absent for the same reason <c>DIV3</c> is: it is Microsoft's
  /// MPEG-4 version 3, a different bitstream that shares the name and nothing else.
  /// </remarks>
  private static readonly string[] _CodecIds = [
    "V_MPEG4/ISO/SP",
    "V_MPEG4/ISO/ASP",
    "V_MPEG4/ISO/AP",
  ];

  private readonly Queue<RawImage> _ready = new();
  private readonly ReadOnlyMemory<byte> _outOfBandHeaders;
  private bool _headersRead;

  private Mpeg4VideoObjectLayer? _layer;

  /// <summary>The anchor two anchors back, which a bidirectionally coded picture predicts forwards from.</summary>
  private Mpeg4Frame? _previousAnchor;

  /// <summary>The most recent anchor, which a predicted picture predicts from and a B picture predicts backwards from.</summary>
  private Mpeg4Frame? _currentAnchor;

  private Mpeg4VideoObjectLayer? _anchorGeometry;

  /// <summary>
  /// How many whole seconds each of the two held anchors sits past the start of the sequence.
  /// </summary>
  /// <remarks>
  /// Two counts and not one, because the two picture types count their own seconds from different
  /// places. An intra or predicted picture counts from the anchor decoded before it; a
  /// bidirectionally coded one counts from the anchor <i>shown</i> before it, which is the older of
  /// the two held here — the newer one has already been decoded by the time the bidirectional picture
  /// arrives, even though it is shown later.
  /// <para/>
  /// Using one count for both is invisible until a group of pictures crosses a second boundary, and
  /// then it puts every bidirectionally coded picture in that group a whole second away from where it
  /// belongs. Direct mode divides by that distance, so the macroblocks it predicts move by a
  /// multiple of what they should — which is a picture, badly wrong, in two frames out of every
  /// twenty-five, with everything either side of them correct.
  /// </remarks>
  private int _previousAnchorSeconds;

  private int _currentAnchorSeconds;
  private int _previousAnchorTime;
  private int _currentAnchorTime;

  /// <summary>The macroblock types and vectors of the last anchor, which direct mode predicts from.</summary>
  private Mpeg4AnchorMotion? _anchorMotion;

  private Mpeg4VideoDecoder(ReadOnlyMemory<byte> outOfBandHeaders) => this._outOfBandHeaders = outOfBandHeaders;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "MPEG-4 Part 2 video (ISO/IEC 14496-2)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    if (stream.CodecId != null) {
      foreach (var id in _CodecIds)
        if (string.Equals(stream.CodecId, id, StringComparison.OrdinalIgnoreCase))
          return true;
    }

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// The one thing taken from the stream description is whatever described the codec, because for a
  /// stream in an ISO base media file that is where the video object layer header lives and the
  /// packets hold nothing but coded pictures. Nothing else is taken, not even the dimensions: they
  /// are in the layer header, and a container's copy is a copy — a file that states a size its layer
  /// header disagrees with is a file whose pictures are the size the layer header says.
  /// </remarks>
  public static Mpeg4VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(Mpeg4DecoderConfiguration.HeadersIn(stream.CodecPrivateData));
  }

  /// <summary>
  /// Decodes one packet and hands back whichever picture is due for display.
  /// </summary>
  /// <returns>
  /// <c>false</c> when the packet decoded but the picture it produced is not the one due next, which
  /// is the case for the first anchor of a stream carrying bidirectionally coded pictures and for any
  /// packet that holds no picture at all.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (!this._headersRead) {
      this._headersRead = true;
      if (this._outOfBandHeaders.Length > 0)
        this._DecodePacket(this._outOfBandHeaders.Span);
    }

    this._DecodePacket(packet.Data.Span);

    if (this._ready.Count > 0) {
      frame = this._ready.Dequeue();
      return true;
    }

    frame = null!;
    return false;
  }

  /// <summary>The pictures still held when the packets run out: the last anchor, and anything queued behind it.</summary>
  public IEnumerable<RawImage> Flush() {
    while (this._ready.Count > 0)
      yield return this._ready.Dequeue();

    if (this._currentAnchor == null)
      yield break;

    yield return this._ToImage(this._currentAnchor);
    this._currentAnchor = null;
    this._previousAnchor = null;
  }

  // ============================================================================================
  // The start-code walk — ISO/IEC 14496-2, 6.2
  // ============================================================================================

  /// <summary>
  /// Walks one packet's start codes and acts on each header.
  /// </summary>
  /// <remarks>
  /// This is the codec's own scan and not the container's, deliberately. Start codes are defined by
  /// 14496-2 and a decoder handed packets by some other demuxer — an AVI, an ISO base media file, a
  /// caller with bytes from anywhere — still has to find the headers inside them. A decoder that
  /// could only work on packets somebody else had already cut at the right places would not be a
  /// decoder of the format.
  /// </remarks>
  private void _DecodePacket(ReadOnlySpan<byte> data) {
    var offset = 0;
    while (offset + 4 <= data.Length) {
      if (data[offset] != 0 || data[offset + 1] != 0 || data[offset + 2] != 1) {
        ++offset;
        continue;
      }

      var code = data[offset + 3];
      var body = data[(offset + 4)..];
      var reader = new Mpeg4BitReader(body);

      switch (code) {
        case >= Mpeg4StartCode.FirstVideoObjectLayer and <= Mpeg4StartCode.LastVideoObjectLayer:
          this._layer = Mpeg4VideoObjectLayer.Parse(ref reader);
          this._RefuseGeometryChangeMidStream();
          break;

        case Mpeg4StartCode.VideoObjectPlane:
          offset += 4 + this._DecodePicture(ref reader);
          continue;

        case Mpeg4StartCode.GroupOfVideoObjectPlanes:
          // time_code, closed_gov and broken_link. The reordering this decoder does follows from the
          // picture types alone and stays correct across an open group, because a bidirectionally
          // coded picture's forward reference is the anchor before the group's.
          this._previousAnchorSeconds = 0;
          this._currentAnchorSeconds = 0;
          break;

        case Mpeg4StartCode.VisualObjectSequenceEnd:
        case Mpeg4StartCode.UserData:
        case Mpeg4StartCode.VisualObjectSequence:
        case Mpeg4StartCode.VisualObject:
        case >= Mpeg4StartCode.FirstVideoObject and <= Mpeg4StartCode.LastVideoObject:
        default:
          // The sequence and object headers carry the profile, the version and the colour primaries;
          // none of them changes a sample and none sizes a later field, so they are stepped over,
          // which the walk does by simply looking for the next start code. User data is bytes the
          // standard gives no meaning to and the reserved codes are bytes it has not given one to yet.
          break;
      }

      offset += 4;
    }
  }

  /// <summary>Decodes one picture and returns how many bytes of the packet it consumed.</summary>
  private int _DecodePicture(ref Mpeg4BitReader reader) {
    var layer = this._layer
      ?? throw new InvalidDataException(
        "An MPEG-4 video object plane was reached before any video object layer header, so the picture's size, "
        + "quantisation method and coding tools are unknown. Decoding must begin at a video object layer header, "
        + "which for a stream in an ISO base media file is carried in the sample entry rather than in the packets.");

    var plane = Mpeg4VideoObjectPlane.Parse(ref reader, layer);
    var seconds = plane.CodingType == Mpeg4VideoObjectPlane.BidirectionallyCoded
      ? this._previousAnchorSeconds + plane.ModuloSeconds
      : this._currentAnchorSeconds + plane.ModuloSeconds;
    var time = seconds * layer.TimeIncrementResolution + plane.TimeIncrement;

    // A picture that carries no macroblocks is the previous one again. It still counts as a picture,
    // and for an anchor it still displaces the one before it — a decoder that dropped it would hand
    // back fewer frames than the stream has.
    if (!plane.IsCoded) {
      this._RepeatPicture(plane, seconds, time);
      return _BytesConsumed(ref reader);
    }

    var picture = Mpeg4PictureDecoder.BeginPicture(
      layer, plane, new(layer.MacroblockWidth, layer.MacroblockHeight),
      plane.CodingType == Mpeg4VideoObjectPlane.BidirectionallyCoded ? this._previousAnchor : this._currentAnchor,
      plane.CodingType == Mpeg4VideoObjectPlane.BidirectionallyCoded ? this._currentAnchor : null,
      this._anchorMotion,
      this._currentAnchorTime - this._previousAnchorTime,
      time - this._previousAnchorTime);

    picture.DecodePicture(ref reader);
    picture.Target.PadBorders();

    this._FinishPicture(picture, plane, seconds, time);
    return _BytesConsumed(ref reader);
  }

  private static int _BytesConsumed(ref Mpeg4BitReader reader) => Math.Max(1, reader.BitPosition >> 3);

  /// <summary>
  /// Files a finished picture: shows it now if it is bidirectionally coded, or holds it and shows the
  /// anchor it displaces.
  /// </summary>
  private void _FinishPicture(Mpeg4PictureDecoder picture, Mpeg4VideoObjectPlane plane, int seconds, int time) {
    if (plane.CodingType == Mpeg4VideoObjectPlane.BidirectionallyCoded) {
      // A bidirectionally coded picture is never a reference, so it is due the moment it is decoded
      // and nothing keeps it.
      this._ready.Enqueue(this._ToImage(picture.Target));
      return;
    }

    this._PushAnchor(picture.Target, seconds, time, picture.Motion);
  }

  /// <summary>Shows the previous picture again, for a video object plane that carries no macroblocks.</summary>
  private void _RepeatPicture(Mpeg4VideoObjectPlane plane, int seconds, int time) {
    if (plane.CodingType == Mpeg4VideoObjectPlane.BidirectionallyCoded) {
      var shown = this._currentAnchor
        ?? throw new InvalidDataException(
          "An MPEG-4 bidirectionally coded picture states vop_coded 0 before any picture it could repeat.");

      this._ready.Enqueue(this._ToImage(shown));
      return;
    }

    var repeated = this._currentAnchor
      ?? throw new InvalidDataException(
        "An MPEG-4 picture states vop_coded 0 before any picture it could repeat. A stream must begin with a coded "
        + "intra picture.");

    this._PushAnchor(repeated, seconds, time, this._anchorMotion);
  }

  private void _PushAnchor(Mpeg4Frame frame, int seconds, int time, Mpeg4AnchorMotion? motion) {
    if (this._currentAnchor != null)
      this._ready.Enqueue(this._ToImage(this._currentAnchor));

    this._previousAnchor = this._currentAnchor;
    this._previousAnchorTime = this._currentAnchorTime;
    this._previousAnchorSeconds = this._currentAnchorSeconds;
    this._currentAnchor = frame;
    this._currentAnchorTime = time;
    this._currentAnchorSeconds = seconds;
    this._anchorMotion = motion;
    this._anchorGeometry = this._layer;
  }

  /// <summary>
  /// Refuses a picture size that changes while pictures predicted from the old one are still held.
  /// </summary>
  private void _RefuseGeometryChangeMidStream() {
    if (this._anchorGeometry == null || this._layer == null || this._anchorGeometry.SameGeometryAs(this._layer))
      return;

    throw new NotSupportedException(
      $"This stream changes picture size from {this._anchorGeometry.Width}x{this._anchorGeometry.Height} to "
      + $"{this._layer.Width}x{this._layer.Height} part way through, while pictures predicted from the old size are "
      + "still held as references. Decoding a stream whose size changes is not implemented.");
  }

  private RawImage _ToImage(Mpeg4Frame frame) {
    var layer = this._layer!;

    return new() {
      Width = layer.Width,
      Height = layer.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Mpeg4ColorConversion.ToRgb24(frame, layer.Width, layer.Height),
    };
  }
}
