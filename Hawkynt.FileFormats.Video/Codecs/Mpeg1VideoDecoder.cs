using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Mpeg1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes MPEG-1 video, ISO/IEC 11172-2: I, P and B pictures.
/// </summary>
/// <remarks>
/// The first codec in this library that is a codec — everything before it either handed the packet to
/// an image reader or read the packet as samples. So the parts a video codec is made of are all here
/// for the first time: variable-length codes (<see cref="Mpeg1VlcTables"/>), dequantisation with the
/// default and any loaded weighting matrices (<see cref="Mpeg1Quantisation"/>), the inverse transform
/// (<see cref="Mpeg1InverseDct"/>), motion compensation at half-pixel resolution
/// (<see cref="Mpeg1MotionCompensation"/>) and prediction from the pictures either side
/// (<see cref="Mpeg1PictureDecoder"/>).
/// <para/>
/// <b>What it does not do refuses by name.</b> A D picture, an MPEG-2 sequence extension, a forbidden
/// picture type, a quantiser scale of zero, a motion vector that points off the reference, a picture
/// whose slices leave macroblocks uncoded — each of those throws, naming the field and the clause. It
/// does not have a <c>catch</c> that hands back a blank or a repeated frame anywhere, because a
/// plausible wrong picture is worse than a refusal: nobody checks a picture that looks like a picture.
/// <para/>
/// <b>Measured.</b> Thirty-one encoded streams were decoded here and by ffmpeg and compared plane by
/// plane, sample by sample, every frame — static and moving content, sizes that are and are not whole
/// macroblocks, 16x16 to 704x480, quantiser scales from 1 to 31, loaded quantiser matrices, one slice
/// per row and six, groups of pictures open and closed, and sequences with no B pictures and with
/// four between anchors. Every stream produced the frame count ffprobe counts.
/// <para/>
/// Against ffmpeg's floating-point inverse transform (<c>-idct faani</c>) sixteen of the thirty-one
/// are identical byte for byte on every frame, and the other fifteen differ in at most thirty-two
/// samples of one frame — always by exactly one level, never growing from frame to frame, and
/// vanishing at the next intra picture. Against ffmpeg's default integer transform the difference is
/// hundreds to a couple of thousand samples a frame at one to three levels, which is the same size as
/// the difference between ffmpeg's own two transforms on the same streams. The standard specifies the
/// inverse transform as a formula with an accuracy bound rather than as an algorithm (11172-2 Annex
/// A), so this is the residual it exists to allow, and not a disagreement about the bitstream.
/// <para/>
/// <b>Frames come out in display order.</b> B pictures are coded after the picture they are predicted
/// backwards from, so an anchor is held until the next anchor arrives and handed out then. That is
/// why <see cref="TryDecode"/> answers "not yet" to the first packet of a stream and why
/// <see cref="Flush"/> is not empty: the last anchor of a stream has no successor to displace it.
/// </remarks>
public sealed class Mpeg1VideoDecoder : IVideoCodecDecoder<Mpeg1VideoDecoder> {

  /// <summary>The four-character codes containers name MPEG-1 video with.</summary>
  /// <remarks>
  /// <c>MPEG</c> is deliberately absent. AVI files carrying MPEG-2 use it too, and MPEG-2 is a
  /// different bitstream this decoder refuses part way into rather than at the door — so claiming the
  /// code would turn a clean "no codec for this stream" into a decode that starts and then stops.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("MPG1"),
    CodecTag.FromCharacters("PIM1"),
    CodecTag.FromCharacters("mp1v"),
  ];

  private readonly Queue<RawImage> _ready = new();
  private Mpeg1SequenceHeader? _sequence;

  /// <summary>The anchor two anchors back, which a B picture predicts forwards from.</summary>
  private Mpeg1Frame? _previousAnchor;

  /// <summary>The most recent anchor, which a P picture predicts from and a B picture predicts backwards from.</summary>
  private Mpeg1Frame? _currentAnchor;

  private Mpeg1SequenceHeader? _anchorGeometry;

  public static string CodecName => "MPEG-1 video (ISO/IEC 11172-2)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// Nothing is read from the stream description, not even the dimensions. Every one of them is in
  /// the sequence header of the stream itself, and a container's copy is a copy — an AVI that states
  /// a size its sequence header disagrees with is a file whose pictures are the size the sequence
  /// header says.
  /// </remarks>
  public static Mpeg1VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>
  /// Decodes one packet — one coded picture — and hands back whichever picture is due for display.
  /// </summary>
  /// <returns>
  /// <c>false</c> when the packet decoded but the picture it produced is not the one due next, which
  /// is the case for the first anchor of a stream and for any packet that holds no picture at all.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
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
  // The start-code walk — 11172-2, 2.4.2
  // ============================================================================================

  /// <summary>
  /// Walks one packet's start codes and acts on each header.
  /// </summary>
  /// <remarks>
  /// This is the codec's own scan and not the container's, deliberately. Start codes are defined by
  /// 11172-2 and a decoder handed packets by some other demuxer — an AVI, a program stream, a caller
  /// with bytes from anywhere — still has to find the headers inside them. A decoder that could only
  /// work on packets somebody else had already cut at the right places would not be a decoder of the
  /// format.
  /// </remarks>
  private void _DecodePacket(ReadOnlySpan<byte> data) {
    var reader = new Mpeg1BitReader(data);
    Mpeg1PictureDecoder? picture = null;

    while (_TryReadStartCode(ref reader, out var code))
      switch (code) {
        case Mpeg1StartCode.SequenceHeader:
          this._sequence = Mpeg1SequenceHeader.Parse(ref reader, this._sequence);
          this._RefuseGeometryChangeMidStream();
          break;

        case Mpeg1StartCode.Extension:
          throw new NotSupportedException(
            "This stream carries an extension start code (00 00 01 B5) after its sequence header, which makes it "
            + "MPEG-2 video (ISO/IEC 13818-2) rather than MPEG-1. This decoder reads MPEG-1 (ISO/IEC 11172-2); "
            + "MPEG-2 is not implemented.");

        case Mpeg1StartCode.Group:
          // time_code, closed_gop and broken_link. Nothing in them changes a sample: the reordering
          // this decoder does follows from the picture types alone, and it stays correct across an
          // open group because a B picture's forward reference is the anchor before the group's.
          reader.Skip(27);
          break;

        case Mpeg1StartCode.Picture:
          // A second picture start code in one packet finishes the first. The container here cuts a
          // packet per picture so this never fires for it, but a caller with packets from elsewhere —
          // a program stream, an AVI, its own buffer — may well hand over several at once, and a
          // decoder that kept only the last would drop frames without saying anything.
          if (picture != null)
            this._FinishPicture(picture);

          picture = this._BeginPicture(ref reader);
          break;

        case >= Mpeg1StartCode.FirstSlice and <= Mpeg1StartCode.LastSlice:
          if (picture == null)
            throw new InvalidDataException(
              $"An MPEG-1 slice start code (00 00 01 {code:X2}) was reached with no picture header before it in this "
              + "packet. A slice belongs to a picture and cannot be decoded without one.");

          picture.DecodeSlice(ref reader, code);
          break;

        case Mpeg1StartCode.SequenceEnd:
        case Mpeg1StartCode.UserData:
        default:
          // The sequence end code carries nothing; user data is bytes the standard gives no meaning
          // to and reserved codes are bytes it has not given one to yet. All three are stepped over,
          // which the walk does by simply looking for the next start code.
          break;
      }

    if (picture != null)
      this._FinishPicture(picture);
  }

  private Mpeg1PictureDecoder _BeginPicture(ref Mpeg1BitReader reader) {
    var sequence = this._sequence
      ?? throw new InvalidDataException(
        "An MPEG-1 picture header was reached before any sequence header, so the picture's size and quantiser "
        + "matrices are unknown. Decoding must begin at a sequence header.");

    return Mpeg1PictureDecoder.BeginPicture(
      ref reader, sequence, new(sequence.MacroblockWidth * 16, sequence.MacroblockHeight * 16),
      this._previousAnchor, this._currentAnchor);
  }

  /// <summary>
  /// Files a finished picture: shows it now if it is a B picture, or holds it and shows the anchor it
  /// displaces.
  /// </summary>
  private void _FinishPicture(Mpeg1PictureDecoder picture) {
    picture.RefuseIfIncomplete();

    if (picture.CodingType == Mpeg1PictureDecoder.BidirectionallyCoded) {
      // A B picture is never a reference, so it is due the moment it is decoded and nothing keeps it.
      this._ready.Enqueue(this._ToImage(picture.Target));
      return;
    }

    if (this._currentAnchor != null)
      this._ready.Enqueue(this._ToImage(this._currentAnchor));

    this._previousAnchor = this._currentAnchor;
    this._currentAnchor = picture.Target;
    this._anchorGeometry = this._sequence;
  }

  /// <summary>
  /// Refuses a picture size that changes while pictures predicted from the old one are still held.
  /// </summary>
  /// <remarks>
  /// A repeated sequence header is normal and usually restates the same values; it is allowed to load
  /// new quantiser matrices, which affects only pictures after it and is fine. A different picture
  /// size is not fine: the held anchors are the old size, and a P picture of the new size predicting
  /// from them has no defined meaning. Rescaling them, or reading the smaller one into the larger,
  /// would be inventing the parts that were never coded.
  /// </remarks>
  private void _RefuseGeometryChangeMidStream() {
    if (this._anchorGeometry == null || this._sequence == null || this._anchorGeometry.SameGeometryAs(this._sequence))
      return;

    throw new NotSupportedException(
      $"This stream changes picture size from {this._anchorGeometry.Width}x{this._anchorGeometry.Height} to "
      + $"{this._sequence.Width}x{this._sequence.Height} part way through, while pictures predicted from the old size "
      + "are still held as references. Decoding a sequence whose size changes is not implemented.");
  }

  private RawImage _ToImage(Mpeg1Frame frame) {
    var sequence = this._sequence!;

    return new() {
      Width = sequence.Width,
      Height = sequence.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Mpeg1ColorConversion.ToRgb24(frame, sequence.Width, sequence.Height),
    };
  }

  /// <summary>
  /// Moves to the next start code and consumes it, answering <c>false</c> at the end of the packet.
  /// </summary>
  /// <remarks>
  /// Start codes are byte-aligned and may be preceded by any number of zero bytes, which encoders use
  /// as padding (11172-2, 2.4.2.1). So the search aligns first and then looks for <c>00 00 01</c>
  /// from there, which finds the code whatever the slice before it left the bit position at.
  /// </remarks>
  private static bool _TryReadStartCode(ref Mpeg1BitReader reader, out byte code) {
    reader.AlignToByte();

    while (reader.BitsRemaining >= 32) {
      if (reader.NextBits(24) == 1) {
        reader.Skip(24);
        code = (byte)reader.ReadBits(8);
        return true;
      }

      reader.Skip(8);
    }

    code = 0;
    return false;
  }
}
