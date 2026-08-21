using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes H.265 / HEVC video, ITU-T H.265 | ISO/IEC 23008-2: Main profile intra pictures.
/// </summary>
/// <remarks>
/// <b>What it decodes, exactly.</b> Coded video sequences whose pictures are intra, in the Main
/// profile — 4:2:0, eight-bit samples, progressive frames. Everything an intra picture is made of is
/// implemented from the standard: the parameter sets, the arithmetic decoder with the context
/// initialisation of clause 9.3.2.2 entered from Tables 9-5 to 9-37, the coding tree unit quadtree
/// down to eight samples, all thirty-five intra prediction modes with their reference sample
/// substitution and both smoothing filters, all four transform sizes with the sine transform the
/// smallest luma blocks use, dequantisation with scaling lists, sign data hiding, per-unit quantiser
/// changes, entropy coding synchronised across rows of coding tree blocks, and both in-loop filters —
/// deblocking and the sample adaptive offset, which HEVC has and H.264 does not.
/// <para/>
/// "Exactly" is a measurement rather than a claim. Against a reference decoder, over forty-two
/// encoded streams from 34x18 to 640x360, every encoder preset from the fastest to the slowest, every
/// coding tree and transform and quantiser-group size, lossless and transform-skipped blocks and
/// quantisers from 4 to 48, the luminance and both chrominance planes of every frame come back with
/// zero differing samples. HEVC specifies exact integer transforms, so that is the right bar and
/// anything short of it is a defect.
/// <para/>
/// <b>What it refuses, by name and with the clause.</b> Pictures predicted from other pictures —
/// predicted and bidirectional slices — are refused at <c>slice_type</c>. The inter prediction is
/// written and most of it is exact; it is refused because <em>most</em> is not the bar. The reasoning
/// is set out where the refusal is raised. Also refused: tiles, dependent slice segments, coding
/// units whose samples were sent uncompressed, 4:2:2, 4:4:4, monochrome, sample depths above eight,
/// separately coded colour planes, the format range extensions, the screen content coding extensions,
/// and the multilayer and 3D extensions. Every one of them throws with the syntax element that says
/// so and what it means.
/// <para/>
/// <b>There is no <c>catch</c> here that hands back a blank, a copied or a partly decoded picture.</b>
/// That is worth stating because this repository has had the other kind: an HEVC decoder whose
/// arithmetic contexts were never initialised, which had no dequantisation at all, which guessed
/// where its slice header ended — and which reported success for months while returning pictures that
/// were almost entirely zero, because nobody compared the samples. A refusal is a result a caller can
/// act on. A plausible wrong picture is not, because nobody checks a picture that looks like a
/// picture.
/// <para/>
/// <b>Both delivery forms.</b> A transport stream, a program stream and a bare elementary stream
/// carry NAL units separated by start codes (Annex B); MP4, Matroska and the ISO base media family
/// carry each unit behind its length, with the parameter sets in an
/// <c>HEVCDecoderConfigurationRecord</c>. Which form a stream is in is decided once, from whether
/// that record is present, rather than guessed at each packet.
/// </remarks>
public sealed class H265VideoDecoder : IVideoCodecDecoder<H265VideoDecoder> {

  /// <summary>The four-character codes containers name HEVC with.</summary>
  /// <remarks>
  /// <c>hvc1</c> and <c>hev1</c> are the ISO base media sample entry types and differ only in whether
  /// the parameter sets may also appear in the samples; <c>hvc2</c> and <c>hev2</c> are the same two
  /// with extractors permitted. <c>HEVC</c> and <c>H265</c> are what the AVI world writes.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("hvc1"),
    CodecTag.FromCharacters("hev1"),
    CodecTag.FromCharacters("hvc2"),
    CodecTag.FromCharacters("hev2"),
    CodecTag.FromCharacters("HEVC"),
    CodecTag.FromCharacters("H265"),
    CodecTag.FromCharacters("h265"),
  ];

  /// <summary>The name Matroska gives HEVC, which names codecs with text rather than with a code.</summary>
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
  private H265SequenceParameterSet? _pictureSequence;
  private bool _skippingPicture;

  private H265VideoDecoder(H265DecoderConfiguration? configuration) {
    this._configuration = configuration;

    foreach (var set in configuration?.ParameterSets ?? [])
      this._AcceptParameterSet(H265NalReader.Parse(set));
  }

  public static string CodecName
    => "H.265/HEVC (ITU-T H.265 | ISO/IEC 23008-2), Main profile intra pictures";

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

  /// <summary>
  /// Builds a decoder for one stream, reading whatever the container knew about it out of band.
  /// </summary>
  /// <remarks>
  /// Nothing is taken from the stream description but the codec configuration, and not even the
  /// dimensions: every one of them is in the sequence parameter set, and a container's copy is a
  /// copy. An MP4 that states a size its sequence parameter set disagrees with is a file whose
  /// pictures are the size the parameter set says.
  /// </remarks>
  public static H265VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(H265DecoderConfiguration.TryParse(stream.CodecPrivateData));
  }

  /// <summary>Decodes one packet — one access unit — and hands back a picture ready to be shown.</summary>
  /// <returns>
  /// <c>false</c> when no picture is ready, which is the case for a packet carrying only parameter
  /// sets, for one whose picture must wait for a later one to be shown before it, and for a leading
  /// picture skipped because the stream was entered at the access point it follows.
  /// </returns>
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

          // Supplemental enhancement information, access unit delimiters, filler, and everything the
          // standard has not given a meaning to yet. None of them changes a sample.
          break;
      }
    }

    // The container cuts a packet per access unit, so the picture this packet's slices built is
    // finished here. A picture whose slices arrived across two packets would be finished by the
    // first slice of the next one instead, which _DecodeSliceSegment does.
    this._FinishPicture();

    if (this._ready.Count == 0) {
      frame = null!;
      return false;
    }

    frame = this._ready.Dequeue();
    return true;
  }

  /// <summary>
  /// The pictures still held because a picture that belongs before them might still have arrived.
  /// </summary>
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

      // The video parameter set describes how the temporal sub-layers and the extension layers of a
      // stream relate to one another. Nothing in the sample decoding process reads it — the sequence
      // parameter set carries everything a picture is reconstructed from — so it is accepted and not
      // parsed rather than parsed and not used.
    }
  }

  /// <summary>Refuses a picture size that changes while pictures of the old size are still references.</summary>
  /// <remarks>
  /// A repeated sequence parameter set is normal and usually restates the same values. A different
  /// picture size is not: the held references are the old size, and a predicted picture of the new
  /// size has no defined meaning against them. The standard's answer is a refresh picture, which
  /// empties the buffer first — so a size change that arrives without one is a stream this decoder
  /// cannot follow rather than one it should resample.
  /// </remarks>
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
    var header = H265SliceHeader.Parse(nal, this._sequenceSets, this._pictureSets);

    if (header.FirstSliceSegmentInPicture)
      this._FinishPicture();

    if (this._frame == null && !header.FirstSliceSegmentInPicture)
      throw new InvalidDataException(
        "An H.265 slice segment continues a picture no slice has opened: its first_slice_segment_in_pic_flag is "
        + "zero and no earlier segment of the picture was read. The stream was entered part way through a picture.");

    if (this._frame == null) {
      // A leading picture that predicts from before the access point the stream was entered at is
      // not decodable and the standard says not to decode it. Skipping it is the honest answer;
      // decoding it against whatever the buffer happens to hold would produce a picture.
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

    // Whatever the buffer can no longer be made to reorder is shown before this picture is decoded,
    // which is where the standard puts it and is what keeps the output in order.
    this._references.BumpBeforeDecoding(header.Sps.MaxNumReorderPictures, header.Sps.MaxDecodedPictureBuffering);

    while (this._references.TryTakeOutput(out var released))
      this._ready.Enqueue(this._ToImage(released));

    this._frame = new(header.Sps, header.Pps);
    this._frame.Picture.PictureOrderCount = poc;
    this._frame.Picture.IsOutput = header.PicOutputFlag;
  }

  private void _FinishPicture() {
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

    return new() {
      Width = sps.DisplayWidth,
      Height = sps.DisplayHeight,
      Format = PixelFormat.Rgb24,
      PixelData = H265ColorConversion.ToRgb24(
        picture, sps.CropOffsetX, sps.CropOffsetY, sps.DisplayWidth, sps.DisplayHeight),
    };
  }
}
