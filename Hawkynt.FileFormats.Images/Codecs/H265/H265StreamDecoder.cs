using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Decodes a sequence of H.265 NAL units into finished pictures, in output order.
/// </summary>
/// <remarks>
/// This is everything between a NAL unit and a displayable picture: parameter sets, slice headers,
/// picture order counts, the reference picture set and the decoded picture buffer, then both in-loop
/// filters. It is shared by the video decoder, which feeds it one access unit at a time, and by the
/// still-image formats that carry HEVC — BPG and HEIF — which feed it whole streams.
/// </remarks>
internal sealed class H265StreamDecoder {

  private readonly Dictionary<int, H265SequenceParameterSet> _sequenceSets = [];
  private readonly Dictionary<int, H265PictureParameterSet> _pictureSets = [];
  private readonly H265ReferencePictures _references = new();
  private readonly Queue<(H265Picture Picture, H265SequenceParameterSet Sps)> _ready = [];

  private H265FrameDecoder? _frame;
  private H265SliceHeader? _pictureHeader;
  private H265SliceHeader? _lastIndependentSliceHeader;
  private H265SequenceParameterSet? _pictureSequence;
  private bool _skippingPicture;
  private IReadOnlyList<H265Picture>[] _pictureLists = [[], []];

  /// <summary>The sequence parameter set of the picture being decoded or last decoded.</summary>
  internal H265SequenceParameterSet? Sequence => this._pictureSequence;

  /// <summary>
  /// The picture most recently finished, in decoding order, with both in-loop filters applied.
  /// </summary>
  /// <remarks>
  /// A decoded picture hash SEI message describes the picture it follows in decoding order, not the
  /// one that leaves the buffer next, so whoever checks one needs the picture before bumping has
  /// reordered it.
  /// </remarks>
  internal H265Picture? LastFinishedPicture { get; private set; }

  /// <summary>Reads NAL units; a picture completes when the next one begins or at <see cref="EndOfAccessUnit"/>.</summary>
  internal void Decode(IEnumerable<H265NalUnit> nals) {
    foreach (var nal in nals) {
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
          this.EndOfAccessUnit();
          break;

        default:
          if (nal.IsSlice)
            this._DecodeSliceSegment(nal);
          break;
      }
    }
  }

  /// <summary>Finishes the picture being decoded, if any.</summary>
  internal void EndOfAccessUnit() => this._FinishPicture();

  /// <summary>Finishes the current picture and releases every picture still held for output.</summary>
  internal void Flush() {
    this._FinishPicture();
    foreach (var picture in this._references.Flush())
      this._ready.Enqueue((picture, this._pictureSequence!));
  }

  /// <summary>The next picture in output order, with the sequence it belongs to.</summary>
  internal bool TryTakePicture(out H265Picture picture, out H265SequenceParameterSet sps) {
    if (this._ready.Count == 0) {
      picture = null!;
      sps = null!;
      return false;
    }

    (picture, sps) = this._ready.Dequeue();
    return true;
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

    var lists = this._references.BuildLists(header, this._frame!.Picture);
    this._frame!.DecodeSliceSegment(header, lists);
    this._pictureLists = lists;
  }

  private void _BeginPicture(H265SliceHeader header) {
    this._skippingPicture = false;
    this._pictureHeader = header;
    this._pictureSequence = header.Sps;

    var poc = this._references.ComputePictureOrderCount(header);
    this._references.ApplyReferencePictureSet(header, poc);
    // A picture kept in two versions while it is decoded holds a second buffer of its own, which
    // clause C.5.2.2 counts against the buffering the sequence declares.
    var twoVersions = header.Pps.TwoVersionsOfCurrentPicture(header.Sps) ? 1 : 0;
    this._references.BumpBeforeDecoding(
      header.Sps.MaxNumReorderPictures, header.Sps.MaxDecodedPictureBuffering - twoVersions);

    while (this._references.TryTakeOutput(out var released))
      this._ready.Enqueue((released, this._pictureSequence));

    this._frame = new(header.Sps, header.Pps);
    this._frame.Picture.PictureOrderCount = poc;
    this._frame.Picture.IsOutput = header.PicOutputFlag;

    // A picture that may predict from itself is marked as a long-term reference while it is decoded
    // (clause 8.1.3), which is what keeps motion vector prediction from scaling a block vector by a
    // distance in time it does not have. Storing it remarks it as a short-term one.
    if (header.Pps.CurrentPictureReferenceEnabled)
      this._frame.Picture.Marking = H265ReferenceMarking.LongTerm;
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
    this.LastFinishedPicture = frame.Picture;

    this._references.Add(frame.Picture, header, this._pictureLists);

    while (this._references.TryTakeOutput(out var picture))
      this._ready.Enqueue((picture, this._pictureSequence!));
  }

  /// <summary>
  /// A decoded picture as native planar samples, cropped to the conformance window: eight-bit planes
  /// for an eight-bit sequence, RawImage's sixteen-bit planar layout for a deeper one.
  /// </summary>
  /// <remarks>
  /// A monochrome sequence comes out as 4:2:0 with neutral chrominance, which is what a reference
  /// decoder writes when asked for a 4:2:0 file and keeps the luminance exactly as decoded.
  /// <para/>
  /// The planar formats name one depth for all three planes, and H.265 lets luma and chroma differ.
  /// The shallower planes are shifted up to the deeper one's depth, which loses nothing. Depths the
  /// formats have no name for — eleven, thirteen and fifteen bits — are shifted up one bit into the
  /// next one that does.
  /// </remarks>
  internal static RawImage ToPlanarImage(H265Picture picture, H265SequenceParameterSet sps) {
    var width = sps.DisplayWidth;
    var height = sps.DisplayHeight;
    var monochrome = picture.IsMonochrome;
    var shiftX = monochrome ? 1 : picture.ChromaShiftX;
    var shiftY = monochrome ? 1 : picture.ChromaShiftY;
    var chromaWidth = (width + (1 << shiftX) - 1) >> shiftX;
    var chromaHeight = (height + (1 << shiftY) - 1) >> shiftY;
    var chromaArrayType = monochrome ? 1 : sps.ChromaArrayType;

    if (sps.BitDepthLuma == 8 && (monochrome || sps.BitDepthChroma == 8)) {
      var luma = _Narrow(picture.Luma);
      var stride = monochrome ? (picture.Width + 1) >> 1 : picture.ChromaWidth;
      var cb = monochrome ? _Neutral(stride * ((picture.Height + 1) >> 1), 128) : _Narrow(picture.Cb);
      var cr = monochrome ? cb : _Narrow(picture.Cr);
      var left = sps.CropOffsetX;
      var top = sps.CropOffsetY;

      return chromaArrayType switch {
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
    var depth = monochrome ? sps.BitDepthLuma : Math.Max(sps.BitDepthLuma, sps.BitDepthChroma);
    var named = depth switch { 11 or 13 or 15 => depth + 1, _ => depth };
    var lumaShift = named - sps.BitDepthLuma;
    var chromaShift = named - (monochrome ? named : sps.BitDepthChroma);

    var data = new byte[checked((width * height + 2 * chromaWidth * chromaHeight) * 2)];
    var at = _CopyWidePlane(
      picture.Luma, picture.Width, sps.CropOffsetX, sps.CropOffsetY, width, height, lumaShift, data, 0);

    if (monochrome) {
      var neutral = 1 << (named - 1);
      for (var i = 0; i < 2 * chromaWidth * chromaHeight; ++i) {
        data[at++] = (byte)neutral;
        data[at++] = (byte)(neutral >> 8);
      }
    } else {
      at = _CopyWidePlane(
        picture.Cb, picture.ChromaWidth, sps.CropOffsetX >> shiftX, sps.CropOffsetY >> shiftY,
        chromaWidth, chromaHeight, chromaShift, data, at);
      _CopyWidePlane(
        picture.Cr, picture.ChromaWidth, sps.CropOffsetX >> shiftX, sps.CropOffsetY >> shiftY,
        chromaWidth, chromaHeight, chromaShift, data, at);
    }

    return new() {
      Width = width,
      Height = height,
      Format = _PlanarFormat(chromaArrayType, named),
      PixelData = data,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }

  /// <summary>Which planar layout a sequence's chroma format and sample depth name.</summary>
  private static PixelFormat _PlanarFormat(int chromaArrayType, int bitDepth) => (chromaArrayType, bitDepth) switch {
    (2, 9) => PixelFormat.Yuv422P9,
    (2, 10) => PixelFormat.Yuv422P10,
    (2, 12) => PixelFormat.Yuv422P12,
    (2, 14) => PixelFormat.Yuv422P14,
    (2, 16) => PixelFormat.Yuv422P16,
    (3, 9) => PixelFormat.Yuv444P9,
    (3, 10) => PixelFormat.Yuv444P10,
    (3, 12) => PixelFormat.Yuv444P12,
    (3, 14) => PixelFormat.Yuv444P14,
    (3, 16) => PixelFormat.Yuv444P16,
    (_, 9) => PixelFormat.Yuv420P9,
    (_, 12) => PixelFormat.Yuv420P12,
    (_, 14) => PixelFormat.Yuv420P14,
    (_, 16) => PixelFormat.Yuv420P16,
    _ => PixelFormat.Yuv420P10,
  };

  /// <summary>Copies an eight-bit sequence's samples out of the wider plane they are decoded into.</summary>
  private static byte[] _Narrow(ushort[] plane) {
    var result = new byte[plane.Length];
    for (var i = 0; i < plane.Length; ++i)
      result[i] = (byte)plane[i];
    return result;
  }

  private static byte[] _Neutral(int count, byte value) {
    var result = new byte[count];
    Array.Fill(result, value);
    return result;
  }

  /// <summary>Crops one plane into RawImage's wide planar layout: a right-justified sample in a little-endian ushort.</summary>
  private static int _CopyWidePlane(
    ushort[] source, int sourceStride, int left, int top, int width, int height, int shift, byte[] target, int at) {
    for (var y = 0; y < height; ++y) {
      var row = (top + y) * sourceStride + left;
      for (var x = 0; x < width; ++x) {
        var sample = source[row + x] << shift;
        target[at++] = (byte)sample;
        target[at++] = (byte)(sample >> 8);
      }
    }

    return at;
  }
}
