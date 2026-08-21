using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// Decodes MPEG-1 video (ISO/IEC 11172-2) and MPEG-2 video (ISO/IEC 13818-2): the start-code walk,
/// the headers, and the reordering that puts pictures back into display order.
/// </summary>
/// <remarks>
/// One decoder and not two. 13818-2 does not extend 11172-2 so much as contain it — the picture,
/// slice, macroblock and block layers are the same walk, most of the variable-length tables are the
/// same tables, and 13818-2 requires a decoder of its own to decode 11172-2 streams as well. Two
/// decoders sharing a core would still have had to answer the same question twice at every one of
/// those layers, and the MPEG-1 half of this is already measured against a reference decoder; a fork
/// would leave two places for a correction to have to be made and one of them measured.
/// <para/>
/// Which standard a stream is decides itself, from the presence of a sequence extension after the
/// sequence header, rather than from what a container called the codec. A container's name for a
/// stream is a copy, and copies are wrong — an AVI that says <c>MPEG</c> may hold either, and a
/// program stream that says MPEG-2 systems is allowed to carry MPEG-1 pictures.
/// </remarks>
internal sealed class MpegVideoDecoder {

  private readonly Queue<RawImage> _ready = new();
  private MpegSequenceHeader? _sequence;

  /// <summary>The anchor two anchors back, which a B picture predicts forwards from.</summary>
  private MpegFrame? _previousAnchor;

  /// <summary>The most recent anchor, which a P picture predicts from and a B picture predicts backwards from.</summary>
  private MpegFrame? _currentAnchor;

  private MpegSequenceHeader? _anchorGeometry;

  /// <summary>
  /// Decodes one packet and queues whichever pictures became due for display.
  /// </summary>
  internal void DecodePacket(ReadOnlySpan<byte> data) {
    var reader = new MpegBitReader(data);
    MpegPictureDecoder? picture = null;
    MpegPictureHeader? header = null;
    var sawPictureCodingExtension = false;

    while (_TryReadStartCode(ref reader, out var code))
      switch (code) {
        case MpegStartCode.SequenceHeader:
          this._sequence = MpegSequenceHeader.Parse(ref reader, this._sequence);
          break;

        case MpegStartCode.Extension:
          this._ReadExtension(ref reader, header, ref sawPictureCodingExtension);
          break;

        case MpegStartCode.Group:
          // time_code, closed_gop and broken_link. Nothing in them changes a sample: the reordering
          // this decoder does follows from the picture types alone, and it stays correct across an
          // open group because a B picture's forward reference is the anchor before the group's.
          reader.Skip(27);
          break;

        case MpegStartCode.Picture:
          // A second picture start code in one packet finishes the first. The container here cuts a
          // packet per picture so this never fires for it, but a caller with packets from elsewhere —
          // a program stream, an AVI, its own buffer — may well hand over several at once, and a
          // decoder that kept only the last would drop frames without saying anything.
          if (picture != null)
            this._FinishPicture(picture);

          picture = null;
          sawPictureCodingExtension = false;
          header = MpegPictureHeader.Parse(ref reader);
          break;

        case >= MpegStartCode.FirstSlice and <= MpegStartCode.LastSlice:
          picture ??= this._BeginPicture(
            header ?? throw new InvalidDataException(
              $"An MPEG slice start code (00 00 01 {code:X2}) was reached with no picture header before it in this "
              + "packet. A slice belongs to a picture and cannot be decoded without one."),
            sawPictureCodingExtension);

          picture.DecodeSlice(ref reader, code);
          break;

        case MpegStartCode.SequenceEnd:
        case MpegStartCode.UserData:
        default:
          // The sequence end code carries nothing; user data is bytes the standards give no meaning
          // to and reserved codes are bytes they have not given one to yet. All three are stepped
          // over, which the walk does by simply looking for the next start code.
          break;
      }

    if (picture != null)
      this._FinishPicture(picture);
  }

  /// <summary>The pictures still held when the packets run out: the last anchor, and anything queued behind it.</summary>
  internal IEnumerable<RawImage> Flush() {
    while (this._ready.Count > 0)
      yield return this._ready.Dequeue();

    if (this._currentAnchor == null)
      yield break;

    yield return this._ToImage(this._currentAnchor);
    this._currentAnchor = null;
    this._previousAnchor = null;
  }

  /// <summary>Whether a decoded picture is waiting to be handed out.</summary>
  internal bool TryTakeReady(out RawImage frame) {
    if (this._ready.Count > 0) {
      frame = this._ready.Dequeue();
      return true;
    }

    frame = null!;
    return false;
  }

  // ============================================================================================
  // Extensions — 13818-2, 6.2.2.2
  // ============================================================================================

  /// <summary>
  /// Acts on one extension, positioned just past its start code.
  /// </summary>
  /// <remarks>
  /// The four-bit identifier says which extension this is on its own, so there is no need to know
  /// whether the last start code was a sequence header or a picture header to tell a sequence
  /// extension from a picture coding extension. That is deliberate on the standard's part and it is
  /// what makes this a flat switch rather than a state machine.
  /// <para/>
  /// The three scalable extensions are refused and not skipped. A scalable stream's base layer is
  /// decodable on its own, so skipping them would produce a picture — the wrong one, missing every
  /// enhancement the stream carried, and with no indication that anything was missing.
  /// </remarks>
  private void _ReadExtension(ref MpegBitReader reader, MpegPictureHeader? header, ref bool sawPictureCodingExtension) {
    var identifier = reader.ReadBits(4);
    switch (identifier) {
      case _SEQUENCE_EXTENSION:
        this._Sequence().ApplySequenceExtension(ref reader);
        this._RefuseGeometryChangeMidStream();
        break;

      case _QUANT_MATRIX_EXTENSION:
        this._Sequence().ApplyQuantMatrixExtension(ref reader);
        break;

      case _PICTURE_CODING_EXTENSION:
        if (header == null)
          throw new InvalidDataException(
            "An MPEG-2 picture coding extension was reached with no picture header before it, so there is no picture "
            + "for it to describe.");

        if (!this._Sequence().IsMpeg2)
          throw new InvalidDataException(
            "An MPEG-2 picture coding extension was reached in a stream whose sequence header was not followed by a "
            + "sequence extension, so the stream declares itself MPEG-1 and codes itself MPEG-2. ISO/IEC 13818-2 "
            + "6.2.2.3 requires the sequence extension to follow the first sequence header of every MPEG-2 sequence.");

        header.ApplyPictureCodingExtension(ref reader);
        sawPictureCodingExtension = true;
        break;

      case _SEQUENCE_SCALABLE_EXTENSION:
      case _PICTURE_SPATIAL_SCALABLE_EXTENSION:
      case _PICTURE_TEMPORAL_SCALABLE_EXTENSION:
        throw new NotSupportedException(
          $"This MPEG-2 stream carries extension {identifier}, one of the scalability extensions of ISO/IEC 13818-2 "
          + "6.2.2.5, 6.2.3.5 and 6.2.3.6: it is one layer of a stream coded at several resolutions or quality levels "
          + "at once. Scalable coding is not implemented, and the layer is refused rather than decoded on its own "
          + "because a base layer decoded alone is a picture missing everything the other layers carried.");

      case _SEQUENCE_DISPLAY_EXTENSION:
      case _COPYRIGHT_EXTENSION:
      case _PICTURE_DISPLAY_EXTENSION:
      default:
        // Display geometry, copyright identification and pan-and-scan offsets. None of them changes
        // a sample, and the walk steps over them by looking for the next start code.
        break;
    }
  }

  private const int _SEQUENCE_EXTENSION = 1;
  private const int _SEQUENCE_DISPLAY_EXTENSION = 2;
  private const int _QUANT_MATRIX_EXTENSION = 3;
  private const int _COPYRIGHT_EXTENSION = 4;
  private const int _SEQUENCE_SCALABLE_EXTENSION = 5;
  private const int _PICTURE_DISPLAY_EXTENSION = 7;
  private const int _PICTURE_CODING_EXTENSION = 8;
  private const int _PICTURE_SPATIAL_SCALABLE_EXTENSION = 9;
  private const int _PICTURE_TEMPORAL_SCALABLE_EXTENSION = 10;

  private MpegSequenceHeader _Sequence()
    => this._sequence
       ?? throw new InvalidDataException(
         "An MPEG picture header or extension was reached before any sequence header, so the picture's size and "
         + "quantiser matrices are unknown. Decoding must begin at a sequence header.");

  private MpegPictureDecoder _BeginPicture(MpegPictureHeader header, bool sawPictureCodingExtension) {
    var sequence = this._Sequence();

    if (sequence.IsMpeg2 && !sawPictureCodingExtension)
      throw new InvalidDataException(
        "An MPEG-2 picture header was not followed by a picture coding extension, which ISO/IEC 13818-2 6.2.3 "
        + "requires of every picture. Without it the picture's f_codes, its structure and its scan are unknown.");

    this._RefuseGeometryChangeMidStream();

    return MpegPictureDecoder.BeginPicture(
      sequence,
      new(sequence.MacroblockWidth * 16, sequence.MacroblockHeight * 16, sequence.ChromaFormat),
      this._previousAnchor, this._currentAnchor, header);
  }

  /// <summary>
  /// Files a finished picture: shows it now if it is a B picture, or holds it and shows the anchor it
  /// displaces.
  /// </summary>
  private void _FinishPicture(MpegPictureDecoder picture) {
    picture.RefuseIfIncomplete();

    if (picture.CodingType == MpegPictureDecoder.BidirectionallyCoded) {
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

  private RawImage _ToImage(MpegFrame frame) {
    var sequence = this._sequence!;

    return new() {
      Width = sequence.Width,
      Height = sequence.Height,
      Format = PixelFormat.Rgb24,
      PixelData = MpegColorConversion.ToRgb24(frame, sequence.Width, sequence.Height, sequence.IsMpeg2),
    };
  }

  /// <summary>
  /// Moves to the next start code and consumes it, answering <c>false</c> at the end of the packet.
  /// </summary>
  /// <remarks>
  /// Start codes are byte-aligned and may be preceded by any number of zero bytes, which encoders use
  /// as padding (11172-2, 2.4.2.1; 13818-2, 6.2.1). So the search aligns first and then looks for
  /// <c>00 00 01</c> from there, which finds the code whatever the slice before it left the bit
  /// position at.
  /// </remarks>
  private static bool _TryReadStartCode(ref MpegBitReader reader, out byte code) {
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
