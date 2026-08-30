using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H264;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes progressive 8-bit 4:2:0 H.264 / AVC pictures.</summary>
/// <remarks>
/// The decoder reconstructs CAVLC and CABAC I/P/B slices, including High-profile 8x8 transforms/scaling lists,
/// long-term references, explicit weighted P/B prediction, implicit weighted B prediction and direct
/// B prediction. Completed pictures stay in native YUV420 and are reordered by picture order count before delivery.
/// </remarks>
public sealed class H264VideoDecoder : IVideoCodecDecoder<H264VideoDecoder> {
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("avc1"),
    CodecTag.FromCharacters("avc3"),
    CodecTag.FromCharacters("H264"),
    CodecTag.FromCharacters("X264"),
    CodecTag.FromCharacters("DAVC"),
    CodecTag.FromCharacters("VSSH"),
  ];

  private static readonly string[] _CodecIds = ["V_MPEG4/ISO/AVC"];

  // Annex A caps MaxDpbFrames at 16. Without a parsed VUI max_num_reorder_frames this is the safe
  // conservative presentation-buffer bound for Main/High streams. Baseline cannot contain B slices
  // and is delivered immediately instead of paying that latency.
  private const int _MAX_PRESENTATION_REORDER = 16;

  private readonly Dictionary<int, H264SequenceParameterSet> _sequenceSets = [];
  private readonly Dictionary<int, H264PictureParameterSet> _pictureSets = [];
  private readonly H264ReferencePictures _references = new();
  private readonly H264PictureOrderCount _pictureOrderCount = new();
  private readonly List<(int Poc, long Serial, RawImage Frame)> _presentation = [];
  private readonly Queue<RawImage> _readyFrames = new();
  private readonly H264DecoderConfiguration? _configuration;

  private H264FrameDecoder? _frame;
  private H264SliceHeader? _pictureHeader;
  private H264SequenceParameterSet? _pictureSequence;
  private H264PictureOrderCount.Result _picturePoc;

  private H264VideoDecoder(H264DecoderConfiguration? configuration) {
    this._configuration = configuration;
    foreach (var set in configuration?.SequenceParameterSets ?? [])
      this._AcceptParameterSet(set);
    foreach (var set in configuration?.PictureParameterSets ?? [])
      this._AcceptParameterSet(set);
  }

  public static string CodecName => "H.264/AVC (ITU-T H.264 | ISO/IEC 14496-10)";

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

  public static H264VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(H264DecoderConfiguration.TryParse(stream.CodecPrivateData));
  }

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
            $"This H.264 stream uses slice data partitioning (NAL unit type {(int)nal.Type}, clause 7.3.2.9), which is not implemented.");

        case H264NalUnitType.PrefixNalUnit:
        case H264NalUnitType.SubsetSequenceParameterSet:
        case H264NalUnitType.SliceExtension:
        case H264NalUnitType.DepthOrThreeDimensionalSliceExtension:
          throw new NotSupportedException(
            $"This H.264 stream carries a scalable or multiview extension (NAL unit type {(int)nal.Type}); only base-layer AVC is reconstructed.");
      }

    this._FinishPicture();
    if (this._readyFrames.Count == 0) {
      frame = null!;
      return false;
    }

    frame = this._readyFrames.Dequeue();
    return true;
  }

  public IEnumerable<RawImage> Flush() {
    this._FinishPicture();
    this._DrainPresentation();
    while (this._readyFrames.Count > 0)
      yield return this._readyFrames.Dequeue();
  }

  private IEnumerable<H264NalUnit> _Split(ReadOnlyMemory<byte> data) {
    if (this._configuration != null)
      return H264NalReader.SplitLengthPrefixed(data, this._configuration.LengthSize);
    if (data.Length > 0 && !H264NalReader.LooksLikeAnnexB(data.Span))
      throw new InvalidDataException(
        "This H.264 packet is not Annex B and no AVCDecoderConfigurationRecord supplied a length-prefix size.");
    return H264NalReader.SplitAnnexB(data);
  }

  private void _AcceptParameterSet(byte[] nalUnit) {
    foreach (var nal in H264NalReader.SplitLengthPrefixed(_WithLengthPrefix(nalUnit), 4))
      this._AcceptParameterSet(nal);
  }

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

  private void _RefuseGeometryChangeMidStream(H264SequenceParameterSet sps) {
    if (this._pictureSequence == null || this._pictureSequence.SameGeometryAs(sps))
      return;
    throw new NotSupportedException(
      $"This H.264 stream changes picture size from {this._pictureSequence.DisplayWidth}x{this._pictureSequence.DisplayHeight} "
      + $"to {sps.DisplayWidth}x{sps.DisplayHeight} while old-size references can still be live.");
  }

  private void _DecodeSlice(H264NalUnit nal) {
    var reader = new H264BitReader(nal.Payload);
    var header = H264SliceHeader.Parse(ref reader, nal, this._sequenceSets, this._pictureSets);

    if (this._frame != null && this._StartsNewPicture(header))
      this._FinishPicture();
    if (this._frame == null)
      this._BeginPicture(header);

    var lists = this._references.BuildLists(header, this._picturePoc.PicOrderCnt);
    if (header.Pps.EntropyCodingModeFlag)
      this._frame!.DecodeCabacSlice(ref reader, header, lists.L0, lists.L1);
    else if (header.IsB)
      this._frame!.DecodeBSlice(ref reader, header, lists.L0, lists.L1);
    else
      this._frame!.DecodeSlice(ref reader, header, lists.L0);
  }

  private bool _StartsNewPicture(H264SliceHeader header) {
    var current = this._pictureHeader!;
    return header.FirstMbInSlice == 0
           || header.FrameNum != current.FrameNum
           || header.Pps.Id != current.Pps.Id
           || header.IdrPicFlag != current.IdrPicFlag
           || (header.IdrPicFlag && header.IdrPicId != current.IdrPicId)
           || header.IsReference != current.IsReference
           || header.PicOrderCntLsb != current.PicOrderCntLsb
           || header.DeltaPicOrderCntBottom != current.DeltaPicOrderCntBottom
           || header.DeltaPicOrderCnt0 != current.DeltaPicOrderCnt0
           || header.DeltaPicOrderCnt1 != current.DeltaPicOrderCnt1;
  }

  private void _BeginPicture(H264SliceHeader header) {
    if (header.IdrPicFlag) {
      this._DrainPresentation();
      this._pictureOrderCount.Reset();
    }

    this._RefuseFrameNumberGap(header);
    this._pictureHeader = header;
    this._pictureSequence = header.Sps;
    this._picturePoc = this._pictureOrderCount.Derive(header);
    this._frame = new(header.Sps, this._references.TakeSerial());
    this._frame.Picture.TopFieldOrderCnt = this._picturePoc.TopFieldOrderCnt;
    this._frame.Picture.BottomFieldOrderCnt = this._picturePoc.BottomFieldOrderCnt;
    this._frame.Picture.PicOrderCnt = this._picturePoc.PicOrderCnt;
  }

  private void _RefuseFrameNumberGap(H264SliceHeader header) {
    if (header.IdrPicFlag || !header.IsReference)
      return;
    if (this._references.PreviousReferenceFrameNum is not { } previous)
      return;
    var expected = (previous + 1) % header.Sps.MaxFrameNum;
    if (header.FrameNum == expected)
      return;
    throw new InvalidDataException(
      $"This H.264 stream's frame_num jumps from {previous} to {header.FrameNum}, where {expected} was due. "
      + (header.Sps.GapsInFrameNumValueAllowedFlag
        ? "Non-existing gap pictures would have to be synthesized, which is not implemented."
        : "A reference picture is missing or decoding did not begin at an IDR picture."));
  }

  private void _FinishPicture() {
    if (this._frame == null)
      return;

    var decoded = this._frame;
    var header = this._pictureHeader!;
    this._frame = null;
    decoded.RefuseIfIncomplete();
    H264Deblocking.Filter(decoded);

    var picture = decoded.Picture;
    this._picturePoc = this._pictureOrderCount.FinishPicture(header, this._picturePoc);
    picture.TopFieldOrderCnt = this._picturePoc.TopFieldOrderCnt;
    picture.BottomFieldOrderCnt = this._picturePoc.BottomFieldOrderCnt;
    picture.PicOrderCnt = this._picturePoc.PicOrderCnt;
    picture.FrameNum = header.FrameNum;
    picture.Motion = decoded.ExportMotionField();
    if (header.IsReference)
      this._references.Add(picture, header);

    var sps = header.Sps;
    var image = RawImageFactory.FromYuv420P8(
      sps.DisplayWidth,
      sps.DisplayHeight,
      picture.Luma,
      picture.LumaWidth,
      picture.Cb,
      picture.Cr,
      picture.ChromaWidth,
      sps.CropOffsetX,
      sps.CropOffsetY,
      RawImageColorInfo.Bt601Limited);
    this._QueueForPresentation(image, picture, header);
  }

  private void _QueueForPresentation(RawImage image, H264Picture picture, H264SliceHeader header) {
    // Baseline/Constrained Baseline has no B pictures, so decode and display order are identical.
    if (header.Sps.ProfileIdc == 66) {
      this._readyFrames.Enqueue(image);
      return;
    }

    var at = this._presentation.BinarySearch(
      (picture.PicOrderCnt, picture.Serial, image),
      Comparer<(int Poc, long Serial, RawImage Frame)>.Create(static (a, b) => {
        var byPoc = a.Poc.CompareTo(b.Poc);
        return byPoc != 0 ? byPoc : a.Serial.CompareTo(b.Serial);
      }));
    if (at < 0)
      at = ~at;
    this._presentation.Insert(at, (picture.PicOrderCnt, picture.Serial, image));

    if (this._presentation.Count > _MAX_PRESENTATION_REORDER) {
      this._readyFrames.Enqueue(this._presentation[0].Frame);
      this._presentation.RemoveAt(0);
    }
  }

  private void _DrainPresentation() {
    foreach (var item in this._presentation)
      this._readyFrames.Enqueue(item.Frame);
    this._presentation.Clear();
  }
}
