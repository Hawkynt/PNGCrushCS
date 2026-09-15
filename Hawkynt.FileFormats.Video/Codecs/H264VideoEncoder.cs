using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H264;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes progressive eight-bit 4:2:0 H.264 / AVC as Main-profile CAVLC I/P/B pictures.
/// </summary>
/// <remarks>
/// The write path is deliberately exact before it is clever. The first picture is an IDR made from
/// <c>I_PCM</c> macroblocks. Reference pictures use <c>P_Skip</c> wherever the macroblock equals the
/// previous reconstructed reference and <c>I_PCM</c> otherwise. One non-reference B picture is placed
/// between reference anchors; a macroblock that is exactly the rounded average of the two zero-motion
/// references is coded as <c>B_Bi_16x16</c>, with <c>I_PCM</c> as its exact fallback. This exercises the
/// real decoded-picture buffer, both reference lists and display/decode reordering without allowing a
/// lossy transform/quantizer to contaminate later reference pictures.
/// <para/>
/// Samples are emitted in the length-prefixed representation used by MP4, Matroska and FLV, with the
/// SPS/PPS in an <c>AVCDecoderConfigurationRecord</c>. <c>H264VideoWriter</c> converts that same stream
/// description and those packets to Annex B when a raw <c>.264</c> stream is requested.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H264VideoEncoder : IVideoCodecEncoder<H264VideoEncoder> {

  private const int _PROFILE_IDC = 77; // Main
  private const int _PROFILE_COMPATIBILITY = 0;
  private const int _LEVEL_IDC = 62;
  private const int _MAX_LEVEL_62_MACROBLOCKS = 139_264;
  private const int _MAX_LEVEL_62_DIMENSION_MBS = 1_055;
  private const int _NAL_LENGTH_SIZE = 4;
  private const int _FRAME_NUM_BITS = 16;
  private const int _POC_BITS = 16;
  private const int _FRAME_NUM_MASK = (1 << _FRAME_NUM_BITS) - 1;
  private const int _POC_MASK = (1 << _POC_BITS) - 1;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("avc1");

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly byte[] _sequenceParameterSet;
  private readonly byte[] _pictureParameterSet;
  private readonly byte[] _configuration;
  private readonly byte[] _sampleEntry;
  private readonly Queue<CodedPacket> _readyPackets = [];

  private MediaStreamInfo? _stream;
  private Frame420? _previousReference;
  private PendingFrame? _pendingB;
  private int _displayIndex;
  private int _lastReferenceFrameNum;

  private H264VideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._codedWidth = (this._width + 15) & ~15;
    this._codedHeight = (this._height + 15) & ~15;
    this._macroblockWidth = this._codedWidth / 16;
    this._macroblockHeight = this._codedHeight / 16;

    this._sequenceParameterSet = this._SequenceParameterSet();
    this._pictureParameterSet = _PictureParameterSet();
    this._configuration = _DecoderConfiguration(this._sequenceParameterSet, this._pictureParameterSet);
    this._sampleEntry = this._AvcSampleEntry();
  }

  public static string CodecName => "H.264/AVC (ITU-T H.264 | ISO/IEC 14496-10)";

  public static CodecTag Codec => _Tag;

  public static H264VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("H.264 can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(stream),
        $"H.264 needs a positive picture size; {stream.Width}x{stream.Height} was requested.");

    // Progressive 4:2:0 has a 2x2 chroma crop unit. Odd display dimensions therefore cannot be
    // represented without changing the sampling grid this encoder promises.
    if ((stream.Width & 1) != 0 || (stream.Height & 1) != 0)
      throw new NotSupportedException(
        $"This H.264 encoder writes progressive 4:2:0, whose chroma and frame-crop grids are 2x2; "
        + $"{stream.Width}x{stream.Height} cannot be represented exactly. Both dimensions must be even.");

    var macroblockWidth = (stream.Width + 15L) / 16;
    var macroblockHeight = (stream.Height + 15L) / 16;
    var macroblocks = macroblockWidth * macroblockHeight;
    if (macroblocks > _MAX_LEVEL_62_MACROBLOCKS
        || macroblockWidth > _MAX_LEVEL_62_DIMENSION_MBS
        || macroblockHeight > _MAX_LEVEL_62_DIMENSION_MBS)
      throw new NotSupportedException(
        $"This H.264 encoder writes level 6.2; {stream.Width}x{stream.Height} needs "
        + $"{macroblockWidth}x{macroblockHeight} macroblocks ({macroblocks} total), outside that level's picture bounds.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    this._ValidateGeometry(frame);

    var current = new PendingFrame(this._To420(frame), presentationTimestamp, this._displayIndex++);
    if (this._previousReference == null) {
      this._readyPackets.Enqueue(this._EncodeIdr(current));
      this._previousReference = current.Samples;
      this._lastReferenceFrameNum = 0;
    } else if (this._pendingB == null) {
      this._pendingB = current;
    } else {
      var b = this._pendingB;
      var referenceFrameNum = (this._lastReferenceFrameNum + 1) & _FRAME_NUM_MASK;
      this._readyPackets.Enqueue(this._EncodeP(current, referenceFrameNum, b.PresentationTimestamp));
      this._readyPackets.Enqueue(this._EncodeB(
        b,
        this._previousReference,
        current.Samples,
        (referenceFrameNum + 1) & _FRAME_NUM_MASK,
        current.PresentationTimestamp));
      this._previousReference = current.Samples;
      this._lastReferenceFrameNum = referenceFrameNum;
      this._pendingB = null;
    }

    if (this._readyPackets.Count == 0) {
      packet = default;
      return false;
    }

    packet = this._readyPackets.Dequeue();
    return true;
  }

  public IEnumerable<CodedPacket> Flush() {
    while (this._readyPackets.Count > 0)
      yield return this._readyPackets.Dequeue();

    if (this._pendingB == null)
      yield break;

    var trailing = this._pendingB;
    this._pendingB = null;
    var frameNum = (this._lastReferenceFrameNum + 1) & _FRAME_NUM_MASK;
    yield return this._EncodeP(trailing, frameNum, trailing.PresentationTimestamp);
    this._previousReference = trailing.Samples;
    this._lastReferenceFrameNum = frameNum;
  }

  public MediaStreamInfo DescribeStream()
    => this._stream ??= new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MPEG4/ISO/AVC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 12,
      Name = this._requested.Name,
      Language = this._requested.Language,
      CodecPrivateData = this._sampleEntry,
    };

  private void _ValidateGeometry(RawImage frame) {
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This H.264 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");
  }

  private CodedPacket _EncodeIdr(PendingFrame frame) {
    var rbsp = new H264BitWriter();
    this._WriteSliceHeader(rbsp, SliceKind.I, frame.DisplayIndex, frameNum: 0, idr: true);

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX)
        this._WritePcmMacroblock(rbsp, frame.Samples, mbX, mbY, 25);

    return this._Packet(0x65, rbsp, frame, frame.PresentationTimestamp, keyFrame: true);
  }

  private CodedPacket _EncodeP(PendingFrame frame, int frameNum, long? decodeTimestamp) {
    var reference = this._previousReference!;
    var rbsp = new H264BitWriter();
    this._WriteSliceHeader(rbsp, SliceKind.P, frame.DisplayIndex, frameNum, idr: false);

    var mbAddr = 0;
    while (mbAddr < this._macroblockWidth * this._macroblockHeight) {
      var skipRun = 0;
      while (mbAddr + skipRun < this._macroblockWidth * this._macroblockHeight) {
        var address = mbAddr + skipRun;
        if (!this._MacroblockEquals(frame.Samples, reference, address))
          break;
        ++skipRun;
      }

      rbsp.WriteUnsignedExpGolomb(skipRun); // mb_skip_run; each skipped P macroblock predicts ref0 at MV (0,0)
      mbAddr += skipRun;
      if (mbAddr >= this._macroblockWidth * this._macroblockHeight)
        break;

      this._WritePcmMacroblock(
        rbsp,
        frame.Samples,
        mbAddr % this._macroblockWidth,
        mbAddr / this._macroblockWidth,
        30); // P slice: I_PCM is I mb_type 25 plus Table 7-13's intra offset 5
      ++mbAddr;
    }

    return this._Packet(0x41, rbsp, frame, decodeTimestamp, keyFrame: false);
  }

  private CodedPacket _EncodeB(
    PendingFrame frame,
    Frame420 previous,
    Frame420 future,
    int frameNum,
    long? decodeTimestamp) {
    var rbsp = new H264BitWriter();
    this._WriteSliceHeader(rbsp, SliceKind.B, frame.DisplayIndex, frameNum, idr: false);

    for (var mbAddr = 0; mbAddr < this._macroblockWidth * this._macroblockHeight; ++mbAddr) {
      rbsp.WriteUnsignedExpGolomb(0); // mb_skip_run: direct mode is deliberately not used here
      var mbX = mbAddr % this._macroblockWidth;
      var mbY = mbAddr / this._macroblockWidth;
      if (this._MacroblockEqualsBiPrediction(frame.Samples, previous, future, mbAddr)) {
        rbsp.WriteUnsignedExpGolomb(3); // B_Bi_16x16, Table 7-14
        rbsp.WriteSignedExpGolomb(0); // mvd_l0[0][0]
        rbsp.WriteSignedExpGolomb(0); // mvd_l0[0][1]
        rbsp.WriteSignedExpGolomb(0); // mvd_l1[0][0]
        rbsp.WriteSignedExpGolomb(0); // mvd_l1[0][1]
        rbsp.WriteUnsignedExpGolomb(0); // coded_block_pattern = 0 in Table 9-4's inter column
      } else
        this._WritePcmMacroblock(rbsp, frame.Samples, mbX, mbY, 48); // B I_PCM = 23 + 25
    }

    return this._Packet(0x01, rbsp, frame, decodeTimestamp, keyFrame: false);
  }

  private void _WriteSliceHeader(H264BitWriter writer, SliceKind kind, int displayIndex, int frameNum, bool idr) {
    writer.WriteUnsignedExpGolomb(0); // first_mb_in_slice
    writer.WriteUnsignedExpGolomb(kind switch {
      SliceKind.P => 5, // all slices of this picture are P
      SliceKind.B => 6, // all slices of this picture are B
      _ => 7, // all slices of this picture are I
    });
    writer.WriteUnsignedExpGolomb(0); // pic_parameter_set_id
    writer.WriteBits(frameNum, _FRAME_NUM_BITS);
    if (idr)
      writer.WriteUnsignedExpGolomb(0); // idr_pic_id
    writer.WriteBits((displayIndex << 1) & _POC_MASK, _POC_BITS); // pic_order_cnt_lsb

    if (kind == SliceKind.B)
      writer.WriteBit(true); // direct_spatial_mv_pred_flag (required syntax, explicit Bi is used below)

    if (kind is SliceKind.P or SliceKind.B) {
      writer.WriteBit(false); // num_ref_idx_active_override_flag: one active entry from each used list
      writer.WriteBit(false); // ref_pic_list_modification_flag_l0
      if (kind == SliceKind.B)
        writer.WriteBit(false); // ref_pic_list_modification_flag_l1
    }

    if (idr) {
      writer.WriteBit(false); // no_output_of_prior_pics_flag
      writer.WriteBit(false); // long_term_reference_flag
    } else if (kind == SliceKind.P)
      writer.WriteBit(false); // adaptive_ref_pic_marking_mode_flag; sliding-window DPB

    writer.WriteSignedExpGolomb(0); // slice_qp_delta
    writer.WriteUnsignedExpGolomb(1); // disable_deblocking_filter_idc: exact reference samples stay exact
  }

  private CodedPacket _Packet(
    byte nalHeader,
    H264BitWriter rbsp,
    PendingFrame frame,
    long? decodeTimestamp,
    bool keyFrame) {
    var slice = _NalUnit(nalHeader, rbsp.FinishRbsp());
    var sample = new byte[_NAL_LENGTH_SIZE + slice.Length];
    BinaryPrimitives.WriteUInt32BigEndian(sample, checked((uint)slice.Length));
    slice.CopyTo(sample, _NAL_LENGTH_SIZE);

    return new(
      this._requested.Index,
      sample,
      PresentationTimestamp: frame.PresentationTimestamp,
      DecodeTimestamp: decodeTimestamp,
      Duration: 1,
      IsKeyFrame: keyFrame);
  }

  private bool _MacroblockEquals(Frame420 current, Frame420 reference, int mbAddr) {
    var mbX = mbAddr % this._macroblockWidth;
    var mbY = mbAddr / this._macroblockWidth;
    return _BlockEquals(current.Y, reference.Y, this._codedWidth, mbX * 16, mbY * 16, 16, 16)
      && _BlockEquals(current.Cb, reference.Cb, this._codedWidth / 2, mbX * 8, mbY * 8, 8, 8)
      && _BlockEquals(current.Cr, reference.Cr, this._codedWidth / 2, mbX * 8, mbY * 8, 8, 8);
  }

  private bool _MacroblockEqualsBiPrediction(Frame420 current, Frame420 previous, Frame420 future, int mbAddr) {
    var mbX = mbAddr % this._macroblockWidth;
    var mbY = mbAddr / this._macroblockWidth;
    return _BlockEqualsAverage(current.Y, previous.Y, future.Y, this._codedWidth, mbX * 16, mbY * 16, 16, 16)
      && _BlockEqualsAverage(current.Cb, previous.Cb, future.Cb, this._codedWidth / 2, mbX * 8, mbY * 8, 8, 8)
      && _BlockEqualsAverage(current.Cr, previous.Cr, future.Cr, this._codedWidth / 2, mbX * 8, mbY * 8, 8, 8);
  }

  private static bool _BlockEquals(
    byte[] first, byte[] second, int stride, int x, int y, int width, int height) {
    for (var row = 0; row < height; ++row) {
      var offset = (y + row) * stride + x;
      if (!first.AsSpan(offset, width).SequenceEqual(second.AsSpan(offset, width)))
        return false;
    }
    return true;
  }

  private static bool _BlockEqualsAverage(
    byte[] current, byte[] first, byte[] second, int stride, int x, int y, int width, int height) {
    for (var row = 0; row < height; ++row) {
      var offset = (y + row) * stride + x;
      for (var column = 0; column < width; ++column) {
        var at = offset + column;
        if (current[at] != (byte)((first[at] + second[at] + 1) >> 1))
          return false;
      }
    }
    return true;
  }

  private Frame420 _To420(RawImage frame) {
    var source = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, this._width / 2, this._height / 2);
    var sourceLumaSamples = this._width * this._height;
    var sourceChromaWidth = this._width / 2;
    var sourceChromaHeight = this._height / 2;
    var sourceChromaSamples = sourceChromaWidth * sourceChromaHeight;
    var luma = new byte[this._codedWidth * this._codedHeight];
    var chromaWidth = this._codedWidth / 2;
    var chromaHeight = this._codedHeight / 2;
    var cb = new byte[chromaWidth * chromaHeight];
    var cr = new byte[chromaWidth * chromaHeight];

    _PadPlane(source.AsSpan(0, sourceLumaSamples), this._width, this._height, luma, this._codedWidth, this._codedHeight);
    _PadPlane(source.AsSpan(sourceLumaSamples, sourceChromaSamples), sourceChromaWidth, sourceChromaHeight, cb, chromaWidth, chromaHeight);
    _PadPlane(source.AsSpan(sourceLumaSamples + sourceChromaSamples, sourceChromaSamples), sourceChromaWidth, sourceChromaHeight, cr, chromaWidth, chromaHeight);
    return new(luma, cb, cr);
  }

  private static void _PadPlane(
    ReadOnlySpan<byte> source,
    int sourceWidth,
    int sourceHeight,
    Span<byte> target,
    int targetWidth,
    int targetHeight) {
    for (var y = 0; y < targetHeight; ++y) {
      var sourceY = Math.Min(y, sourceHeight - 1);
      for (var x = 0; x < targetWidth; ++x)
        target[y * targetWidth + x] = source[sourceY * sourceWidth + Math.Min(x, sourceWidth - 1)];
    }
  }

  private byte[] _SequenceParameterSet() {
    var rbsp = new H264BitWriter();
    rbsp.WriteBits(_PROFILE_IDC, 8);
    rbsp.WriteBits(_PROFILE_COMPATIBILITY, 8);
    rbsp.WriteBits(_LEVEL_IDC, 8);
    rbsp.WriteUnsignedExpGolomb(0); // seq_parameter_set_id
    rbsp.WriteUnsignedExpGolomb(_FRAME_NUM_BITS - 4); // log2_max_frame_num_minus4
    rbsp.WriteUnsignedExpGolomb(0); // pic_order_cnt_type
    rbsp.WriteUnsignedExpGolomb(_POC_BITS - 4); // log2_max_pic_order_cnt_lsb_minus4
    rbsp.WriteUnsignedExpGolomb(2); // max_num_ref_frames: previous and future anchor around a B picture
    rbsp.WriteBit(false); // gaps_in_frame_num_value_allowed_flag
    rbsp.WriteUnsignedExpGolomb(this._macroblockWidth - 1);
    rbsp.WriteUnsignedExpGolomb(this._macroblockHeight - 1);
    rbsp.WriteBit(true); // frame_mbs_only_flag
    rbsp.WriteBit(true); // direct_8x8_inference_flag

    var cropRight = (this._codedWidth - this._width) / 2;
    var cropBottom = (this._codedHeight - this._height) / 2;
    var cropped = cropRight != 0 || cropBottom != 0;
    rbsp.WriteBit(cropped);
    if (cropped) {
      rbsp.WriteUnsignedExpGolomb(0); // frame_crop_left_offset
      rbsp.WriteUnsignedExpGolomb(cropRight);
      rbsp.WriteUnsignedExpGolomb(0); // frame_crop_top_offset
      rbsp.WriteUnsignedExpGolomb(cropBottom);
    }

    rbsp.WriteBit(false); // vui_parameters_present_flag
    return _NalUnit(0x67, rbsp.FinishRbsp());
  }

  private static byte[] _PictureParameterSet() {
    var rbsp = new H264BitWriter();
    rbsp.WriteUnsignedExpGolomb(0); // pic_parameter_set_id
    rbsp.WriteUnsignedExpGolomb(0); // seq_parameter_set_id
    rbsp.WriteBit(false); // entropy_coding_mode_flag: CAVLC
    rbsp.WriteBit(false); // bottom_field_pic_order_in_frame_present_flag
    rbsp.WriteUnsignedExpGolomb(0); // num_slice_groups_minus1
    rbsp.WriteUnsignedExpGolomb(0); // num_ref_idx_l0_default_active_minus1
    rbsp.WriteUnsignedExpGolomb(0); // num_ref_idx_l1_default_active_minus1
    rbsp.WriteBit(false); // weighted_pred_flag
    rbsp.WriteBits(0, 2); // weighted_bipred_idc: ordinary rounded average
    rbsp.WriteSignedExpGolomb(0); // pic_init_qp_minus26
    rbsp.WriteSignedExpGolomb(0); // pic_init_qs_minus26
    rbsp.WriteSignedExpGolomb(0); // chroma_qp_index_offset
    rbsp.WriteBit(true); // deblocking_filter_control_present_flag
    rbsp.WriteBit(false); // constrained_intra_pred_flag
    rbsp.WriteBit(false); // redundant_pic_cnt_present_flag
    return _NalUnit(0x68, rbsp.FinishRbsp());
  }

  private void _WritePcmMacroblock(H264BitWriter writer, Frame420 frame, int mbX, int mbY, int mbType) {
    writer.WriteUnsignedExpGolomb(mbType);
    writer.AlignWithZeroBits();

    for (var y = 0; y < 16; ++y) {
      var row = (mbY * 16 + y) * this._codedWidth + mbX * 16;
      for (var x = 0; x < 16; ++x)
        writer.WriteAlignedByte(frame.Y[row + x]);
    }

    var chromaWidth = this._codedWidth / 2;
    _WriteChroma(frame.Cb);
    _WriteChroma(frame.Cr);

    void _WriteChroma(byte[] plane) {
      for (var y = 0; y < 8; ++y) {
        var row = (mbY * 8 + y) * chromaWidth + mbX * 8;
        for (var x = 0; x < 8; ++x)
          writer.WriteAlignedByte(plane[row + x]);
      }
    }
  }

  /// <summary>
  /// ISO/IEC 14496-12 VisualSampleEntry with the AVCDecoderConfigurationRecord in its <c>avcC</c> box.
  /// MP4 needs the whole entry; the H.264 decoder and raw Annex-B writer deliberately know how to find
  /// the record inside it.
  /// </summary>
  private byte[] _AvcSampleEntry() {
    var result = new byte[86 + 8 + this._configuration.Length];
    BinaryPrimitives.WriteUInt32BigEndian(result, checked((uint)result.Length));
    "avc1"u8.CopyTo(result.AsSpan(4));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(14), 1); // data_reference_index
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(32), checked((ushort)this._width));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(34), checked((ushort)this._height));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(36), 0x00480000); // horizresolution 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(40), 0x00480000); // vertresolution
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(48), 1); // frame_count
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(82), 24); // depth
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(84), 0xFFFF);

    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(86), checked((uint)(8 + this._configuration.Length)));
    "avcC"u8.CopyTo(result.AsSpan(90));
    this._configuration.CopyTo(result, 94);
    return result;
  }

  private static byte[] _DecoderConfiguration(byte[] sps, byte[] pps) {
    if (sps.Length > ushort.MaxValue || pps.Length > ushort.MaxValue)
      throw new InvalidOperationException("H.264 parameter sets exceed AVCDecoderConfigurationRecord length fields.");

    var result = new byte[11 + sps.Length + pps.Length];
    var at = 0;
    result[at++] = 1; // configurationVersion
    result[at++] = _PROFILE_IDC;
    result[at++] = _PROFILE_COMPATIBILITY;
    result[at++] = _LEVEL_IDC;
    result[at++] = 0xFC | (_NAL_LENGTH_SIZE - 1);
    result[at++] = 0xE0 | 1; // one SPS
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(at), checked((ushort)sps.Length));
    at += 2;
    sps.CopyTo(result, at);
    at += sps.Length;
    result[at++] = 1; // one PPS
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(at), checked((ushort)pps.Length));
    at += 2;
    pps.CopyTo(result, at);
    return result;
  }

  private static byte[] _NalUnit(byte header, byte[] rbsp) {
    var escaped = new List<byte>(rbsp.Length + 8) { header };
    var zeroes = 0;

    foreach (var value in rbsp) {
      if (zeroes == 2 && value <= 3) {
        escaped.Add(3);
        zeroes = 0;
      }

      escaped.Add(value);
      zeroes = value == 0 ? zeroes + 1 : 0;
    }

    return [.. escaped];
  }

  private enum SliceKind : byte { P, B, I }

  private sealed record Frame420(byte[] Y, byte[] Cb, byte[] Cr);

  private sealed record PendingFrame(Frame420 Samples, long? PresentationTimestamp, int DisplayIndex);

  /// <summary>MSB-first bit writer for the H.264 syntax elements this encoder emits.</summary>
  private sealed class H264BitWriter {
    private readonly List<byte> _bytes = [];
    private int _partial;
    private int _partialBits;

    internal void WriteBit(bool value) {
      this._partial = (this._partial << 1) | (value ? 1 : 0);
      if (++this._partialBits != 8)
        return;

      this._bytes.Add((byte)this._partial);
      this._partial = 0;
      this._partialBits = 0;
    }

    internal void WriteBits(int value, int count) {
      for (var bit = count - 1; bit >= 0; --bit)
        this.WriteBit(((value >> bit) & 1) != 0);
    }

    internal void WriteUnsignedExpGolomb(int value) {
      if (value < 0)
        throw new ArgumentOutOfRangeException(nameof(value));

      var codeNum = checked(value + 1);
      var leadingZeroBits = 0;
      for (var valueBits = codeNum; valueBits > 1; valueBits >>= 1)
        ++leadingZeroBits;

      for (var i = 0; i < leadingZeroBits; ++i)
        this.WriteBit(false);
      this.WriteBits(codeNum, leadingZeroBits + 1);
    }

    internal void WriteSignedExpGolomb(int value)
      => this.WriteUnsignedExpGolomb(value > 0 ? checked(2 * value - 1) : checked(-2 * value));

    internal void AlignWithZeroBits() {
      while (this._partialBits != 0)
        this.WriteBit(false);
    }

    internal void WriteAlignedByte(byte value) {
      if (this._partialBits != 0)
        throw new InvalidOperationException("H.264 PCM samples must begin on a byte boundary.");
      this._bytes.Add(value);
    }

    internal byte[] FinishRbsp() {
      this.WriteBit(true); // rbsp_stop_one_bit
      while (this._partialBits != 0)
        this.WriteBit(false);
      return [.. this._bytes];
    }
  }
}
