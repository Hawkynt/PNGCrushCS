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
/// The decoder reconstructs native pictures at eight, ten and twelve bits in every chroma format the
/// standard defines — monochrome, 4:2:0, 4:2:2 and 4:4:4 — including intra and inter slices,
/// reference-picture management, weighted prediction, CABAC, scaling lists, deblocking and
/// sample-adaptive offset. Tile and dependent-slice transport structure is handled in the same
/// picture decoder rather than flattened or silently ignored.
/// <para/>
/// Completed pictures are returned as native planar samples after both in-loop filters, in the
/// layout the sequence's own chroma format and depth name. RGB conversion remains a consumer-side
/// operation through <see cref="RawImageConverter"/>. Unsupported profile extensions still fail
/// explicitly rather than returning plausible partial pictures.
/// <para/>
/// The one deliberately narrow exception is the uniform 8-bit 4:2:0 PCM shape emitted by the managed
/// HEVC writer. That syntax leaves CABAC for each coding unit, carries raw samples and starts the
/// arithmetic registers again without resetting the probability contexts. The still-image HEVC core
/// already implements that standards-defined handoff and is reused here until the streaming frame
/// decoder exposes the same raw-sample handoff generically.
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
  private readonly ReadOnlyMemory<byte> _configurationData;
  private readonly H265DecoderConfiguration? _configuration;
  private readonly Queue<RawImage> _ready = [];

  private H265FrameDecoder? _frame;
  private H265SliceHeader? _pictureHeader;
  private H265SliceHeader? _lastIndependentSliceHeader;
  private H265SequenceParameterSet? _pictureSequence;
  private bool _skippingPicture;

  private H265VideoDecoder(ReadOnlyMemory<byte> configurationData) {
    this._configurationData = configurationData;
    this._configuration = H265DecoderConfiguration.TryParse(configurationData);

    foreach (var set in this._configuration?.ParameterSets ?? [])
      this._AcceptParameterSet(H265NalReader.Parse(set));
  }

  public static string CodecName
    => "H.265/HEVC (ITU-T H.265 | ISO/IEC 23008-2)";

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

  public static H265VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(stream.CodecPrivateData);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    // The managed writer uses the exact uniform PCM subset the HEIF path already decodes. Keep this
    // before the general frame decoder: once that decoder has consumed pcm_flag its arithmetic state
    // has deliberately ended, so discovering afterwards that the raw handoff is unsupported is too
    // late to retry the access unit from a clean state.
    if (!this._configurationData.IsEmpty
        && H265PcmStillCodec.TryDecode(packet.Data, this._configurationData, out var pcm)) {
      this._FinishPicture();
      foreach (var picture in this._references.Flush())
        this._ready.Enqueue(this._ToImage(picture));
      this._ready.Enqueue(pcm);

      frame = this._ready.Dequeue();
      return true;
    }

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
    var width = sps.DisplayWidth;
    var height = sps.DisplayHeight;
    var shiftX = picture.ChromaShiftX;
    var shiftY = picture.ChromaShiftY;
    var chromaWidth = (width + (1 << shiftX) - 1) >> shiftX;
    var chromaHeight = (height + (1 << shiftY) - 1) >> shiftY;

    if (sps.BitDepthLuma == 8 && sps.BitDepthChroma == 8) {
      var luma = _Narrow(picture.Luma);
      var cb = _Narrow(picture.Cb);
      var cr = _Narrow(picture.Cr);
      var stride = picture.ChromaWidth;
      var left = sps.CropOffsetX;
      var top = sps.CropOffsetY;

      return sps.ChromaArrayType switch {
        2 => RawImageFactory.FromYuv422P8(
          width, height, luma, picture.Width, cb, cr, stride, left, top, RawImageColorInfo.Bt601Limited),
        3 => RawImageFactory.FromYuv444P8(
          width, height, luma, picture.Width, cb, cr, stride, left, top, RawImageColorInfo.Bt601Limited),
        _ => RawImageFactory.FromYuv420P8(
          width, height, luma, picture.Width, cb, cr, stride, left, top, RawImageColorInfo.Bt601Limited),
      };
    }

    // A sequence deeper than eight bits keeps its own depth: the samples go out in RawImage's planar
    // sixteen-bit layout rather than being shifted down to a byte, because that shift is a quality
    // decision the caller has not made. The format says which depth they are, so a twelve-bit stream
    // is not handed over labelled as ten.
    var data = new byte[checked((width * height + 2 * chromaWidth * chromaHeight) * 2)];
    var at = _CopyWidePlane(
      picture.Luma, picture.Width, sps.CropOffsetX, sps.CropOffsetY, width, height, data, 0);
    at = _CopyWidePlane(
      picture.Cb, picture.ChromaWidth, sps.CropOffsetX >> shiftX, sps.CropOffsetY >> shiftY,
      chromaWidth, chromaHeight, data, at);
    _CopyWidePlane(
      picture.Cr, picture.ChromaWidth, sps.CropOffsetX >> shiftX, sps.CropOffsetY >> shiftY,
      chromaWidth, chromaHeight, data, at);

    return new() {
      Width = width,
      Height = height,
      Format = _PlanarFormat(sps.ChromaArrayType, Math.Max(sps.BitDepthLuma, sps.BitDepthChroma)),
      PixelData = data,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }

  /// <summary>Which planar layout a sequence's chroma format and sample depth name.</summary>
  private static PixelFormat _PlanarFormat(int chromaArrayType, int bitDepth) => (chromaArrayType, bitDepth) switch {
    (2, 10) => PixelFormat.Yuv422P10,
    (2, 12) => PixelFormat.Yuv422P12,
    (3, 10) => PixelFormat.Yuv444P10,
    (3, 12) => PixelFormat.Yuv444P12,
    (_, 12) => PixelFormat.Yuv420P12,
    _ => PixelFormat.Yuv420P10,
  };

  /// <summary>Copies an eight-bit sequence's samples out of the wider plane they are decoded into.</summary>
  private static byte[] _Narrow(ushort[] plane) {
    var result = new byte[plane.Length];
    for (var i = 0; i < plane.Length; ++i)
      result[i] = (byte)plane[i];
    return result;
  }

  /// <summary>
  /// Crops one plane into RawImage's wide planar layout: a right-justified sample in a little-endian ushort.
  /// </summary>
  private static int _CopyWidePlane(
    ushort[] source, int sourceStride, int left, int top, int width, int height, byte[] target, int at) {
    for (var y = 0; y < height; ++y) {
      var row = (top + y) * sourceStride + left;
      for (var x = 0; x < width; ++x) {
        var sample = source[row + x];
        target[at++] = (byte)sample;
        target[at++] = (byte)(sample >> 8);
      }
    }

    return at;
  }
}
