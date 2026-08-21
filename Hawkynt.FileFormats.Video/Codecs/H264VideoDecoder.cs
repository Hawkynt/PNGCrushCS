using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H264;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes H.264 / AVC video, ITU-T H.264 | ISO/IEC 14496-10: Baseline profile I and P slices.
/// </summary>
/// <remarks>
/// <b>What it decodes.</b> Coded video sequences whose slices are I or P, entropy coded with the
/// variable-length codes of clause 9.2 (CAVLC), 4:2:0 chroma, 8-bit samples, progressive frames, one
/// slice group, flat quantiser matrices and the 4x4 transform. That is the Baseline and Constrained
/// Baseline profiles less their error-resilience features, and it is also every Main profile stream
/// that happens to be coded without CABAC or B pictures. Multiple reference frames, all four
/// partitionings and both sub-macroblock partitionings, reference list reordering, constrained intra
/// prediction, <c>I_PCM</c> macroblocks and the deblocking filter with per-slice offsets are all
/// implemented.
/// <para/>
/// <b>What it refuses, by name and with the clause.</b> CABAC (<c>entropy_coding_mode_flag</c>). B
/// slices. SP and SI slices. The 8x8 transform and scaling matrices, which is the High profile.
/// 4:2:2, 4:4:4 and monochrome. Sample depths above eight. Interlaced coding, both field pictures and
/// MBAFF. Flexible macroblock ordering. Weighted prediction. Long-term references and the memory
/// management operations that create them. Redundant coded pictures. Every one of those throws a
/// <see cref="NotSupportedException"/> naming the syntax element and what it means, and there is no
/// <c>catch</c> anywhere here that hands back a blank, a copied or a partly decoded picture: a
/// plausible wrong picture is worse than a refusal, because nobody checks a picture that looks like a
/// picture.
/// <para/>
/// <b>Both delivery forms.</b> A transport stream, a program stream and a bare elementary stream
/// carry NAL units separated by start codes (Annex B); MP4, Matroska and FLV carry each unit behind
/// its length, with the parameter sets in an <c>AVCDecoderConfigurationRecord</c> in the header. Which
/// form a stream is in is decided once, from whether that record is present, rather than guessed at
/// each packet — a three-byte length beginning <c>00 00 01</c> is an ordinary 256-byte NAL unit and a
/// guess would read it as a start code.
/// <para/>
/// <b>Frames come out in decoding order, which for these streams is display order.</b> Reordering
/// exists to undo bidirectional prediction, and this decoder refuses the slices that cause it. So
/// there is no reorder buffer here and <see cref="Flush"/> is empty — not as a simplification, but
/// because for every stream this accepts the two orders are the same.
/// </remarks>
public sealed class H264VideoDecoder : IVideoCodecDecoder<H264VideoDecoder> {

  /// <summary>The four-character codes containers name H.264 with.</summary>
  /// <remarks>
  /// Six spellings for one codec, which is what happens to a format carried by every container ever
  /// written. <c>avc1</c> and <c>avc3</c> are the ISO base media sample entry types and differ only in
  /// whether the parameter sets are also in the samples; <c>H264</c> is what the AVI and Flash worlds
  /// settled on; <c>X264</c> and <c>DAVC</c> are what two particular encoders wrote into AVI files and
  /// nothing else has ever produced.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("avc1"),
    CodecTag.FromCharacters("avc3"),
    CodecTag.FromCharacters("H264"),
    CodecTag.FromCharacters("X264"),
    CodecTag.FromCharacters("DAVC"),
    CodecTag.FromCharacters("VSSH"),
  ];

  /// <summary>The names Matroska gives H.264, which names codecs with text rather than with a code.</summary>
  private static readonly string[] _CodecIds = [
    "V_MPEG4/ISO/AVC",
  ];

  private readonly Dictionary<int, H264SequenceParameterSet> _sequenceSets = [];
  private readonly Dictionary<int, H264PictureParameterSet> _pictureSets = [];
  private readonly H264ReferencePictures _references = new();

  private readonly H264DecoderConfiguration? _configuration;

  private H264FrameDecoder? _frame;
  private H264SliceHeader? _pictureHeader;
  private H264SequenceParameterSet? _pictureSequence;
  private RawImage? _ready;

  private H264VideoDecoder(H264DecoderConfiguration? configuration) {
    this._configuration = configuration;

    foreach (var set in configuration?.SequenceParameterSets ?? [])
      this._AcceptParameterSet(set);

    foreach (var set in configuration?.PictureParameterSets ?? [])
      this._AcceptParameterSet(set);
  }

  public static string CodecName => "H.264/AVC (ITU-T H.264 | ISO/IEC 14496-10), Baseline I and P slices";

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
  /// dimensions: every one of them is in the sequence parameter set, and a container's copy is a copy.
  /// An MP4 that states a size its sequence parameter set disagrees with is a file whose pictures are
  /// the size the parameter set says.
  /// </remarks>
  public static H264VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(H264DecoderConfiguration.TryParse(stream.CodecPrivateData));
  }

  /// <summary>Decodes one packet — one access unit — and hands back the picture it completed.</summary>
  /// <returns>
  /// <c>false</c> when the packet held no whole picture, which is the case for a packet carrying only
  /// parameter sets or supplemental information.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    foreach (var nal in this._Split(packet.Data))
      switch (nal.Type) {
        case H264NalUnitType.SequenceParameterSet:
        case H264NalUnitType.PictureParameterSet:
          this._AcceptParameterSet(nal);
          break;

        case H264NalUnitType.NonIdrSlice:
        case H264NalUnitType.IdrSlice:
          this._DecodeSlice(nal);
          break;

        case H264NalUnitType.SlicePartitionA:
        case H264NalUnitType.SlicePartitionB:
        case H264NalUnitType.SlicePartitionC:
          throw new NotSupportedException(
            $"This H.264 stream uses slice data partitioning (NAL unit type {(int)nal.Type}, H.264 clause 7.3.2.9). "
            + "A partitioned slice splits its header, its intra data and its inter data across three NAL units so "
            + "that the important parts survive a loss; reading them is not implemented.");

        case H264NalUnitType.PrefixNalUnit:
        case H264NalUnitType.SubsetSequenceParameterSet:
        case H264NalUnitType.SliceExtension:
        case H264NalUnitType.DepthOrThreeDimensionalSliceExtension:
          throw new NotSupportedException(
            $"This H.264 stream carries a scalable or multiview extension (NAL unit type {(int)nal.Type}, H.264 "
            + "Annexes G, H and I). Only the base layer syntax is implemented, and a stream whose extension units "
            + "were dropped is not the stream that was encoded.");

        default:
          // Supplemental enhancement information, access unit delimiters, filler, the end of a
          // sequence or a stream, and everything the standard has not given a meaning to yet. None
          // of them changes a sample.
          break;
      }

    // The container cuts a packet per access unit, so the picture the packet's slices built is
    // finished here. A picture whose slices arrived across two packets would be finished by the
    // first slice of the next one instead, which _DecodeSlice does.
    this._FinishPicture();

    if (this._ready == null) {
      frame = null!;
      return false;
    }

    frame = this._ready;
    this._ready = null;
    return true;
  }

  /// <summary>
  /// Nothing: this decoder holds no picture between packets, because it refuses the slices that make
  /// display order differ from decoding order.
  /// </summary>
  public IEnumerable<RawImage> Flush() => [];

  private IEnumerable<H264NalUnit> _Split(ReadOnlyMemory<byte> data) {
    if (this._configuration != null)
      return H264NalReader.SplitLengthPrefixed(data, this._configuration.LengthSize);

    if (data.Length > 0 && !H264NalReader.LooksLikeAnnexB(data.Span))
      throw new InvalidDataException(
        "This H.264 packet begins with neither a start code nor a length prefix this decoder could learn the size "
        + "of: the container stated no AVCDecoderConfigurationRecord, so the packets were expected to be in the "
        + "Annex B byte stream format, and this one is not. The stream is length-prefixed and its configuration "
        + "record is missing.");

    return H264NalReader.SplitAnnexB(data);
  }

  private void _AcceptParameterSet(byte[] nalUnit) {
    foreach (var nal in H264NalReader.SplitLengthPrefixed(_WithLengthPrefix(nalUnit), 4))
      this._AcceptParameterSet(nal);
  }

  /// <summary>Wraps a bare NAL unit so that the ordinary splitting path unescapes it.</summary>
  private static byte[] _WithLengthPrefix(byte[] nalUnit) {
    var wrapped = new byte[nalUnit.Length + 4];
    wrapped[0] = (byte)(nalUnit.Length >> 24);
    wrapped[1] = (byte)(nalUnit.Length >> 16);
    wrapped[2] = (byte)(nalUnit.Length >> 8);
    wrapped[3] = (byte)nalUnit.Length;
    nalUnit.CopyTo(wrapped, 4);
    return wrapped;
  }

  private void _AcceptParameterSet(H264NalUnit nal) {
    switch (nal.Type) {
      case H264NalUnitType.SequenceParameterSet: {
        var sps = H264SequenceParameterSet.Parse(nal.Payload);
        this._RefuseGeometryChangeMidStream(sps);
        this._sequenceSets[sps.Id] = sps;
        break;
      }

      case H264NalUnitType.PictureParameterSet: {
        var pps = H264PictureParameterSet.Parse(nal.Payload);
        this._pictureSets[pps.Id] = pps;
        break;
      }
    }
  }

  /// <summary>
  /// Refuses a picture size that changes while pictures of the old size are still references.
  /// </summary>
  /// <remarks>
  /// A repeated sequence parameter set is normal and usually restates the same values. A different
  /// picture size is not: the held references are the old size, and a P picture of the new size
  /// predicting from them has no defined meaning. The standard's answer is an IDR picture, which
  /// empties the buffer first — so a size change that arrives without one is a stream this decoder
  /// cannot follow rather than one it should resample.
  /// </remarks>
  private void _RefuseGeometryChangeMidStream(H264SequenceParameterSet sps) {
    if (this._pictureSequence == null || this._pictureSequence.SameGeometryAs(sps))
      return;

    throw new NotSupportedException(
      $"This H.264 stream changes picture size from {this._pictureSequence.DisplayWidth}x"
      + $"{this._pictureSequence.DisplayHeight} to {sps.DisplayWidth}x{sps.DisplayHeight} part way through, while "
      + "pictures of the old size are still held as references. Decoding a sequence whose size changes is not "
      + "implemented.");
  }

  private void _DecodeSlice(H264NalUnit nal) {
    var reader = new H264BitReader(nal.Payload);
    var header = H264SliceHeader.Parse(ref reader, nal, this._sequenceSets, this._pictureSets);

    if (this._frame != null && this._StartsNewPicture(header))
      this._FinishPicture();

    if (this._frame == null)
      this._BeginPicture(header);

    var referenceList = this._references.BuildList0(header);
    this._frame!.DecodeSlice(ref reader, header, referenceList);
  }

  /// <summary>
  /// Whether this slice belongs to a picture other than the one being built — clause 7.4.1.2.4.
  /// </summary>
  /// <remarks>
  /// The standard lists a dozen fields any of which starting to differ means a new primary coded
  /// picture. Most of them cannot differ here because the syntax that carries them is refused —
  /// <c>field_pic_flag</c>, <c>bottom_field_flag</c>, the picture order count fields of the types this
  /// decoder does not compute. What is left is what is tested.
  /// </remarks>
  private bool _StartsNewPicture(H264SliceHeader header) {
    var current = this._pictureHeader!;

    return header.FirstMbInSlice == 0
           || header.FrameNum != current.FrameNum
           || header.Pps.Id != current.Pps.Id
           || header.IdrPicFlag != current.IdrPicFlag
           || (header.IdrPicFlag && header.IdrPicId != current.IdrPicId)
           || header.IsReference != current.IsReference;
  }

  private void _BeginPicture(H264SliceHeader header) {
    this._RefuseFrameNumberGap(header);

    this._pictureHeader = header;
    this._pictureSequence = header.Sps;
    this._frame = new(header.Sps, this._references.TakeSerial());
  }

  /// <summary>
  /// Refuses a stream whose reference frame numbering skips, which means a picture is missing.
  /// </summary>
  /// <remarks>
  /// <c>frame_num</c> counts reference frames and increments by one for each (clause 7.4.3). A jump
  /// means either that pictures were lost in transmission, or that the encoder used
  /// <c>gaps_in_frame_num_value_allowed_flag</c> to leave deliberate holes for a decoder to invent
  /// "non-existing" frames for (clause 8.2.5.2). Inventing them is not implemented, and decoding on
  /// without them predicts from the wrong pictures — which produces a film that plays and is wrong.
  /// </remarks>
  private void _RefuseFrameNumberGap(H264SliceHeader header) {
    if (header.IdrPicFlag || !header.IsReference)
      return;

    if (this._references.PreviousReferenceFrameNum is not { } previous)
      return;

    var expected = (previous + 1) % header.Sps.MaxFrameNum;
    if (header.FrameNum == expected)
      return;

    // The two things a jump can mean, and they call for different words. With the flag set the
    // encoder left the hole on purpose and a decoder is expected to invent the missing frames
    // (clause 8.2.5.2); without it, the frames were there when the stream was written and are not
    // there now.
    throw new InvalidDataException(
      $"This H.264 stream's frame_num jumps from {previous} to {header.FrameNum}, where {expected} was due. "
      + (header.Sps.GapsInFrameNumValueAllowedFlag
        ? "The sequence sets gaps_in_frame_num_value_allowed_flag, so the gap is deliberate and the missing frames "
          + "are meant to be invented by the decoder (H.264, clause 8.2.5.2). That is not implemented."
        : "A reference picture is missing: either it was lost, or decoding began somewhere other than an IDR "
          + "picture.")
      + " Decoding on without it would predict from the wrong pictures.");
  }

  private void _FinishPicture() {
    if (this._frame == null)
      return;

    var frame = this._frame;
    var header = this._pictureHeader!;
    this._frame = null;

    frame.RefuseIfIncomplete();
    H264Deblocking.Filter(frame);

    var picture = frame.Picture;
    picture.FrameNum = header.FrameNum;

    if (header.IsReference)
      this._references.Add(picture, header);

    var sps = header.Sps;
    this._ready = new() {
      Width = sps.DisplayWidth,
      Height = sps.DisplayHeight,
      Format = PixelFormat.Rgb24,
      PixelData = H264ColorConversion.ToRgb24(
        picture, sps.CropOffsetX, sps.CropOffsetY, sps.DisplayWidth, sps.DisplayHeight),
    };
  }
}
