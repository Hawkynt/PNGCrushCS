using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes baseline H.263, the H.263+ PLUSPTYPE subset used for custom formats and Annex O temporal
/// B-pictures, and Sorenson Spark.
/// </summary>
/// <remarks>
/// Ordinary I/P pictures use the clause-6 predictor and macroblock syntax. Annex O B-pictures are
/// decoded against both temporal anchors and support direct, forward, backward, bidirectional and
/// intra macroblocks, including their independent forward/backward vector predictor fields.
/// Optional H.263+ coding tools not implemented here are rejected where signalled instead of being
/// silently decoded as baseline syntax.
/// <para/>
/// When a container supplies distinct PTS/DTS values, references decoded ahead of B-pictures are held
/// until their display turn. A bare elementary stream carries temporal reference numbers but gives this
/// packet-at-a-time interface no advance notice that B-pictures follow an already-decoded anchor, so in
/// the absence of timestamps pictures are returned in packet/coding order while their prediction is
/// still decoded correctly.
/// </remarks>
public sealed class H263VideoDecoder : IVideoCodecDecoder<H263VideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("H263"),
    CodecTag.FromCharacters("s263"),
    CodecTag.FromCharacters("U263"),
    CodecTag.FromCharacters("L263"),
    CodecTag.FromCharacters("M263"),
    CodecTag.FromCharacters("X263"),
  ];

  private static readonly CodecTag[] _SorensonTags = [CodecTag.FromCharacters("FLV1")];

  private readonly bool _isSorenson;
  private H263Frame? _previousReference;
  private H263Frame? _reference;
  private H263PictureHeader? _geometry;
  private RawImage? _pendingDisplayReference;
  private readonly Queue<RawImage> _ready = new();

  internal H263Frame? CurrentReference => this._reference;

  private H263VideoDecoder(bool isSorenson) => this._isSorenson = isSorenson;

  public static string CodecName => "H.263 / H.263+ (Annex O temporal B-pictures, Sorenson Spark)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && (_Matches(stream, _Tags) || _Matches(stream, _SorensonTags));
  }

  public static H263VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(_Matches(stream, _SorensonTags));
  }

  private static bool _Matches(MediaStreamInfo stream, CodecTag[] tags) {
    foreach (var tag in tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;
    return false;
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._DecodePacket(packet);
    if (this._ready.TryDequeue(out frame!))
      return true;

    frame = null!;
    return false;
  }

  /// <summary>Returns the final reference held behind reordered Annex O B-pictures.</summary>
  public IEnumerable<RawImage> Flush() {
    if (this._pendingDisplayReference != null) {
      this._ready.Enqueue(this._pendingDisplayReference);
      this._pendingDisplayReference = null;
    }

    while (this._ready.TryDequeue(out var frame))
      yield return frame;
  }

  private void _DecodePacket(CodedPacket packet) {
    var data = packet.Data.Span;
    var offset = 0;

    while (offset + 3 <= data.Length) {
      if (!this._IsPictureStart(data, offset)) {
        ++offset;
        continue;
      }

      var reader = new H263BitReader(data[offset..]);
      reader.Skip(this._isSorenson ? 17 : 22);

      var header = this._isSorenson
        ? H263PictureHeader.ParseSorenson(ref reader)
        : H263PictureHeader.Parse(ref reader);

      this._RefuseGeometryChangeMidStream(header);
      var target = new H263Frame(header.MacroblockWidth, header.MacroblockHeight);

      if (header.IsBidirectional) {
        var past = this._previousReference
          ?? throw new InvalidDataException(
            "An Annex O B-picture arrived before its temporally previous reference picture was decoded.");
        var future = this._reference
          ?? throw new InvalidDataException(
            "An Annex O B-picture arrived before its temporally subsequent reference picture was decoded. Annex O transmits the future anchor before the B-picture.");

        var picture = new H263BidirectionalPictureDecoder(header, target, past, future);
        picture.DecodePicture(ref reader);
        this._ready.Enqueue(_ToImage(target, header));
      } else {
        // A new reference closes the display interval of the reference decoded before it. In a
        // reordered stream that older anchor follows all B-pictures between the two anchors.
        if (header.IsReference && this._pendingDisplayReference != null) {
          this._ready.Enqueue(this._pendingDisplayReference);
          this._pendingDisplayReference = null;
        }

        var picture = H263PictureDecoder.BeginPicture(header, target, this._reference);
        picture.DecodePicture(ref reader);

        if (header.IsReference) {
          this._previousReference = this._reference;
          this._reference = picture.Target;
        }

        var image = _ToImage(picture.Target, header);
        var isReorderedReference = header.IsReference
                                   && packet.PresentationTimestamp.HasValue
                                   && packet.DecodeTimestamp.HasValue
                                   && packet.PresentationTimestamp.Value != packet.DecodeTimestamp.Value;
        if (isReorderedReference)
          this._pendingDisplayReference = image;
        else
          this._ready.Enqueue(image);
      }

      this._geometry = header;
      offset += Math.Max(1, reader.BitPosition >> 3);
    }
  }

  private bool _IsPictureStart(ReadOnlySpan<byte> data, int offset) {
    if (data[offset] != 0 || data[offset + 1] != 0)
      return false;

    var third = data[offset + 2];
    return this._isSorenson ? (third & 0x80) != 0 : (third & 0xFC) == 0x80;
  }

  private void _RefuseGeometryChangeMidStream(H263PictureHeader header) {
    if (this._geometry == null || this._reference == null || this._geometry.SameGeometryAs(header))
      return;

    throw new NotSupportedException(
      $"This H.263 stream changes picture size from {this._geometry.Width}x{this._geometry.Height} to {header.Width}x{header.Height} while a reference of the old size is still active.");
  }

  private static RawImage _ToImage(H263Frame frame, H263PictureHeader header) => new() {
    Width = header.Width,
    Height = header.Height,
    Format = PixelFormat.Rgb24,
    PixelData = H263ColorConversion.ToRgb24(frame, header.Width, header.Height),
  };
}
