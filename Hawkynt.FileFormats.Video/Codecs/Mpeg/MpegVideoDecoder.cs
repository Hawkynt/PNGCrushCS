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

  /// <summary>The most recent complete reference frame.</summary>
  private MpegFrame? _currentAnchor;

  private MpegSequenceHeader? _anchorGeometry;

  /// <summary>
  /// The first field of a field-coded frame while its complementary field has not arrived yet.
  /// </summary>
  /// <remarks>
  /// This is deliberately not filed as <see cref="_currentAnchor"/> yet. H.262 lets the second field
  /// of a P-coded frame use the first field immediately, but a later coded frame may not use the
  /// half-finished frame. Keeping it separately expresses that difference instead of pretending a
  /// half frame is a complete reference picture.
  /// </remarks>
  private MpegFrame? _pendingFieldFrame;
  private int _pendingFieldParity = -1;
  private int _pendingFieldCodingType;

  /// <summary>
  /// The most recently reconstructed complete anchor, which is what an encoder's next P picture
  /// predicts from.
  /// </summary>
  internal MpegFrame? CurrentAnchor => this._currentAnchor;

  /// <summary>Decodes one packet and queues whichever complete frame became due for display.</summary>
  internal void DecodePacket(ReadOnlySpan<byte> data) {
    var reader = new MpegBitReader(data);
    MpegPictureDecoder? picture = null;
    MpegPictureHeader? header = null;
    var sawPictureCodingExtension = false;

    while (_TryReadStartCode(ref reader, out var code))
      switch (code) {
        case MpegStartCode.SequenceHeader:
          if (this._pendingFieldFrame != null)
            throw new InvalidDataException(
              "An MPEG sequence header arrived between the two field pictures of one coded frame. H.262 requires "
              + "the complementary fields of a field-coded frame to be consecutive pictures.");

          this._sequence = MpegSequenceHeader.Parse(ref reader, this._sequence);
          break;

        case MpegStartCode.Extension:
          this._ReadExtension(ref reader, header, ref sawPictureCodingExtension);
          break;

        case MpegStartCode.Group:
          if (this._pendingFieldFrame != null)
            throw new InvalidDataException(
              "An MPEG group header arrived between the two field pictures of one coded frame. H.262 requires "
              + "the complementary fields of a field-coded frame to be consecutive pictures.");

          // time_code, closed_gop and broken_link. Nothing in them changes a sample: the reordering
          // follows from picture types and stays correct across an open group.
          reader.Skip(27);
          break;

        case MpegStartCode.Picture:
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
          break;
      }

    if (picture != null)
      this._FinishPicture(picture);
  }

  /// <summary>The complete frames still held when the packets run out.</summary>
  internal IEnumerable<RawImage> Flush() {
    if (this._pendingFieldFrame != null)
      throw new InvalidDataException(
        $"The MPEG stream ended after a {(this._pendingFieldParity == 0 ? "top" : "bottom")} field picture without "
        + "the complementary field required to complete its coded frame.");

    while (this._ready.Count > 0)
      yield return this._ready.Dequeue();

    if (this._currentAnchor == null)
      yield break;

    yield return this._ToImage(this._currentAnchor);
    this._currentAnchor = null;
    this._previousAnchor = null;
  }

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

    var isFieldPicture = sequence.IsMpeg2 && header.PictureStructure != 3;
    if (isFieldPicture && sequence.ProgressiveSequence)
      throw new InvalidDataException(
        "An MPEG-2 progressive_sequence contains a field picture. ISO/IEC 13818-2 requires every picture of a "
        + "progressive sequence to have picture_structure Frame picture.");

    if (!isFieldPicture) {
      if (this._pendingFieldFrame != null)
        throw new InvalidDataException(
          "An MPEG frame picture arrived while the previous field picture still lacked its complementary field. "
          + "H.262 requires the two fields of a field-coded frame to be consecutive pictures.");

      return MpegPictureDecoder.BeginPicture(
        sequence,
        new(sequence.MacroblockWidth * 16, sequence.MacroblockHeight * 16, sequence.ChromaFormat),
        this._previousAnchor, this._currentAnchor, header);
    }

    var parity = header.PictureStructure - 1;
    if (this._pendingFieldFrame == null) {
      return MpegPictureDecoder.BeginPicture(
        sequence,
        new(sequence.MacroblockWidth * 16, sequence.MacroblockHeight * 16, sequence.ChromaFormat),
        this._previousAnchor, this._currentAnchor, header);
    }

    if (parity == this._pendingFieldParity)
      throw new InvalidDataException(
        $"Two consecutive MPEG-2 field pictures both state {(parity == 0 ? "top" : "bottom")} field. H.262 requires "
        + "the second field of a coded frame to have the opposite parity from the first.");

    if (!_IsValidFieldPair(this._pendingFieldCodingType, header.CodingType))
      throw new InvalidDataException(
        $"An MPEG-2 field-coded frame begins with picture_coding_type {this._pendingFieldCodingType} and ends with "
        + $"type {header.CodingType}. H.262 permits I/I or I/P for a coded I-frame, P/P for a coded P-frame, and "
        + "B/B for a coded B-frame.");

    return MpegPictureDecoder.BeginPicture(
      sequence,
      this._pendingFieldFrame,
      this._previousAnchor, this._currentAnchor, header,
      pairedFieldReference: this._pendingFieldCodingType == MpegPictureDecoder.BidirectionallyCoded
        ? null
        : this._pendingFieldFrame,
      pairedFieldParity: this._pendingFieldParity,
      isSecondField: true,
      firstFieldCodingType: this._pendingFieldCodingType);
  }

  private static bool _IsValidFieldPair(int first, int second) => first switch {
    MpegPictureDecoder.IntraCoded => second is MpegPictureDecoder.IntraCoded or MpegPictureDecoder.PredictiveCoded,
    MpegPictureDecoder.PredictiveCoded => second == MpegPictureDecoder.PredictiveCoded,
    MpegPictureDecoder.BidirectionallyCoded => second == MpegPictureDecoder.BidirectionallyCoded,
    _ => false,
  };

  /// <summary>Files a finished picture, combining field pictures before applying frame reordering.</summary>
  private void _FinishPicture(MpegPictureDecoder picture) {
    picture.RefuseIfIncomplete();

    if (!picture.IsFieldPicture) {
      this._FinishFrame(picture.Target, picture.CodingType == MpegPictureDecoder.BidirectionallyCoded);
      return;
    }

    if (this._pendingFieldFrame == null) {
      this._pendingFieldFrame = picture.Target;
      this._pendingFieldParity = picture.FieldParity;
      this._pendingFieldCodingType = picture.CodingType;
      return;
    }

    if (!ReferenceEquals(this._pendingFieldFrame, picture.Target))
      throw new InvalidDataException(
        "The two field pictures of an MPEG-2 coded frame were reconstructed into different frame buffers. This "
        + "indicates an internal field-pairing error rather than a valid bitstream condition.");

    var codedFrameIsB = this._pendingFieldCodingType == MpegPictureDecoder.BidirectionallyCoded;
    var completed = this._pendingFieldFrame;
    this._pendingFieldFrame = null;
    this._pendingFieldParity = -1;
    this._pendingFieldCodingType = 0;

    this._FinishFrame(completed, codedFrameIsB);
  }

  private void _FinishFrame(MpegFrame frame, bool isBidirectional) {
    if (isBidirectional) {
      this._ready.Enqueue(this._ToImage(frame));
      return;
    }

    if (this._currentAnchor != null)
      this._ready.Enqueue(this._ToImage(this._currentAnchor));

    this._previousAnchor = this._currentAnchor;
    this._currentAnchor = frame;
    this._anchorGeometry = this._sequence;
  }

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
