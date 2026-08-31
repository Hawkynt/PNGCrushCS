using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes H.265 / HEVC video, ITU-T H.265 | ISO/IEC 23008-2.
/// </summary>
/// <remarks>
/// The decoder reconstructs native Main-profile 4:2:0 eight-bit pictures, including intra and inter
/// slices, reference-picture management, weighted prediction, CABAC, scaling lists, deblocking and
/// sample-adaptive offset. Tile and dependent-slice transport structure is handled in the same
/// picture decoder rather than flattened or silently ignored.
/// <para/>
/// Completed pictures are returned as native <see cref="PixelFormat.Yuv420P8"/> samples after both
/// in-loop filters. RGB conversion remains a consumer-side operation through <see cref="RawImageConverter"/>.
/// Unsupported profile extensions still fail explicitly rather than returning plausible partial
/// pictures.
/// </remarks>
public sealed class H265VideoDecoder : IVideoCodecDecoder<H265VideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("hvc1"),
    CodecTag.FromCharacters("hev1"),
    CodecTag.FromCharacters("hvc2"),
    CodecTag.FromCharacters("hev2"),
    CodecTag.FromCharacters("HEVC"),
    CodecTag.FromCharacters("H265"),
    CodecTag.FromCharacters("h265"),
  ];

  private static readonly string[] _CodecIds = [
    "V_MPEGH/ISO/HEVC",
  ];

  private readonly Dictionary<int, H265SequenceParameterSet> _sequenceSets = [];
  private readonly Dictionary<int, H265PictureParameterSet> _pictureSets = [];
  private readonly H265ReferencePictures _references = new();
  private readonly H265DecoderConfiguration? _configuration;
  private readonly Queue<RawImage> _ready = [];

  private H265FrameDecoder? _frame;
  private H265SliceHeader? _pictureHeader;
  private H265SliceHeader? _lastIndependentSliceHeader;
  private H265SequenceParameterSet? _pictureSequence;
  private bool _skippingPicture;

  private H265VideoDecoder(H265DecoderConfiguration? configuration) {
    this._configuration = configuration;

    foreach (var set in configuration?.ParameterSets ?? [])
      this._AcceptParameterSet(H265NalReader.Parse(set));
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName
    => "H.265/HEVC (ITU-T H.265 | ISO/IEC 23008-2), Main profile";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    foreach (var id in _CodecIds)
      if (string.Equals(stream.CodecId, id, StringComparison.OrdinalIgnoreCase))
        return true;

    return false;
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static H265VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(H265DecoderConfiguration.TryParse(stream.CodecPrivateData));
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    foreach (var nal in this._Split(packet.Data)) {
      if (nal.LayerId != 0)
        throw new NotSupportedException(
          $"This H.265 stream carries a NAL unit for layer {nal.LayerId} (nuh_layer_id, clause 7.4.2.2). Only the "
          + "base layer syntax is implemented, and a stream whose enhancement layers were dropped is not the stream "
          + "that was encoded.");

      switch (nal.Type) {
        case H265NalUnitType.VideoParameterSet:
        case H265NalUnitType.SequenceParameterSet:
        case H265NalUnitType.PictureParameterSet:
          this._AcceptParameterSet(nal);
          break;

        case H265NalUnitType.EndOfSequence:
        case H265NalUnitType.EndOfBitstream:
          this._FinishPicture();
          break;

        default:
          if (nal.IsSlice)
            this._DecodeSliceSegment(nal);
          break;
      }
    }

    this._FinishPicture();

    if (this._ready.Count == 0) {
      frame = null!;
      return false;
    }

    frame = this._ready.Dequeue();
    return true;
  }

  /// <summary>Returns any frames still buffered by the decoder.</summary>
  public IEnumerable<RawImage> Flush() {
    this._FinishPicture();

    foreach (var picture in this._references.Flush())
      this._ready.Enqueue(this._ToImage(picture));

    while (this._ready.Count > 0)
      yield return this._ready.Dequeue();
  }

  private IReadOnlyList<H265NalUnit> _Split(ReadOnlyMemory<byte> data) {
    if (this._configuration != null)
      return H265NalReader.SplitLengthPrefixed(data, this._configuration.LengthSize);

    if (data.Length > 0 && !H265NalReader.LooksLikeAnnexB(data.Span))
      throw new InvalidDataException(
        "This H.265 packet begins with neither a start code nor a length prefix this decoder could learn the size "
        + "of: the container stated no HEVCDecoderConfigurationRecord, so the packets were expected to be in the "
        + "Annex B byte stream format, and this one is not. The stream is length-prefixed and its configuration "
        + "record is missing.");

    return H265NalReader.SplitAnnexB(data);
  }

  private void _AcceptParameterSet(H265NalUnit nal) {
    switch (nal.Type) {
      case H265NalUnitType.SequenceParameterSet: {
        var sps = H265SequenceParameterSet.Parse(nal.Payload);
        this._RefuseGeometryChangeMidStream(sps);
        this._sequenceSets[sps.Id] = sps;
        break;
      }

      case H265NalUnitType.PictureParameterSet: {
        var pps = H265PictureParameterSet.Parse(nal.Payload);
        this._pictureSets[pps.Id] = pps;
        break;
      }
    }
  }

  private void _RefuseGeometryChangeMidStream(H265SequenceParameterSet sps) {
    if (this._pictureSequence == null || this._pictureSequence.SameGeometryAs(sps))
      return;

    throw new NotSupportedException(
      $"This H.265 stream changes picture size from {this._pictureSequence.DisplayWidth}x"
      + $"{this._pictureSequence.DisplayHeight} to {sps.DisplayWidth}x{sps.DisplayHeight} part way through, while "
      + "pictures of the old size are still held as references. Decoding a sequence whose size changes is not "
      + "implemented.");
  }

  private void _DecodeSliceSegment(H265NalUnit nal) {
    var header = H265SliceHeader.Parse(
      nal, this._sequenceSets, this._pictureSets, this._lastIndependentSliceHeader);

    if (header.FirstSliceSegmentInPicture)
      this._FinishPicture();

    if (!header.DependentSliceSegment)
      this._lastIndependentSliceHeader = header;

    // A RASL/RADL picture may be intentionally skipped by the reference manager. Its following
    // segments still need to be parsed so dependent-header inheritance remains synchronized, but
    // there is deliberately no frame object to decode them into.
    if (this._skippingPicture && !header.FirstSliceSegmentInPicture)
      return;

    if (this._frame == null && !header.FirstSliceSegmentInPicture)
      throw new InvalidDataException(
        "An H.265 slice segment continues a picture no slice has opened: its first_slice_segment_in_pic_flag is "
        + "zero and no earlier segment of the picture was read. The stream was entered part way through a picture.");

    if (this._frame == null) {
      if (this._references.ShouldSkip(nal)) {
        this._skippingPicture = true;
        return;
      }

      this._BeginPicture(header);
    }

    if (this._skippingPicture)
      return;

    var lists = this._references.BuildLists(header);
    this._frame!.DecodeSliceSegment(header, lists);
    this._pictureLists = lists;
  }

  private IReadOnlyList<H265Picture>[] _pictureLists = [[], []];

  private void _BeginPicture(H265SliceHeader header) {
    this._skippingPicture = false;
    this._pictureHeader = header;
    this._pictureSequence = header.Sps;

    var poc = this._references.ComputePictureOrderCount(header);
    this._references.ApplyReferencePictureSet(header, poc);
    this._references.BumpBeforeDecoding(header.Sps.MaxNumReorderPictures, header.Sps.MaxDecodedPictureBuffering);

    while (this._references.TryTakeOutput(out var released))
      this._ready.Enqueue(this._ToImage(released));

    this._frame = new(header.Sps, header.Pps);
    this._frame.Picture.PictureOrderCount = poc;
    this._frame.Picture.IsOutput = header.PicOutputFlag;
  }

  private void _FinishPicture() {
    this._lastIndependentSliceHeader = null;

    if (this._skippingPicture) {
      this._skippingPicture = false;
      return;
    }

    if (this._frame == null)
      return;

    var frame = this._frame;
    var header = this._pictureHeader!;
    this._frame = null;

    frame.RefuseIfIncomplete();
    H265Deblocking.Filter(frame);
    H265SampleAdaptiveOffset.Filter(frame);

    this._references.Add(frame.Picture, header, this._pictureLists);

    while (this._references.TryTakeOutput(out var picture))
      this._ready.Enqueue(this._ToImage(picture));
  }

  private RawImage _ToImage(H265Picture picture) {
    var sps = this._pictureSequence!;

    return RawImageFactory.FromYuv420P8(
      sps.DisplayWidth,
      sps.DisplayHeight,
      picture.Luma,
      picture.Width,
      picture.Cb,
      picture.Cr,
      picture.ChromaWidth,
      sps.CropOffsetX,
      sps.CropOffsetY,
      RawImageColorInfo.Bt601Limited);
  }
}
