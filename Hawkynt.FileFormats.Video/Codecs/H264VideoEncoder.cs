using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H264;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes progressive eight-bit 4:2:0 H.264 / AVC as Baseline-profile IDR pictures made from
/// <c>I_PCM</c> macroblocks.
/// </summary>
/// <remarks>
/// This is deliberately the smallest conforming write path rather than a pretend x264. Every picture
/// is independently decodable, every coded sample is carried verbatim by <c>I_PCM</c>, and the only
/// loss a non-YUV source can incur is the conversion to H.264's 4:2:0 sample grid before coding.
/// Pictures already in <see cref="PixelFormat.Yuv420P8"/> round-trip sample for sample.
/// <para/>
/// Samples are emitted in the length-prefixed representation used by MP4, Matroska and FLV, with the
/// SPS/PPS in an <c>AVCDecoderConfigurationRecord</c>. <c>H264VideoWriter</c> converts that same
/// stream description and those packets to Annex B when a raw <c>.264</c> stream is requested.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H264VideoEncoder : IVideoCodecEncoder<H264VideoEncoder> {

  private const int _PROFILE_IDC = 66; // Baseline
  private const int _PROFILE_COMPATIBILITY = 0xC0; // constraint_set0_flag + constraint_set1_flag
  private const int _LEVEL_IDC = 62;
  private const int _MAX_LEVEL_62_MACROBLOCKS = 139_264;
  private const int _MAX_LEVEL_62_DIMENSION_MBS = 1_055;
  private const int _NAL_LENGTH_SIZE = 4;

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

  private MediaStreamInfo? _stream;

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

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This H.264 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    var planes = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, this._width / 2, this._height / 2);
    var rbsp = new H264BitWriter();

    rbsp.WriteUnsignedExpGolomb(0); // first_mb_in_slice
    rbsp.WriteUnsignedExpGolomb(7); // slice_type: I, all slices in the picture are I
    rbsp.WriteUnsignedExpGolomb(0); // pic_parameter_set_id
    rbsp.WriteBits(0, 4); // frame_num
    rbsp.WriteUnsignedExpGolomb(0); // idr_pic_id
    rbsp.WriteBit(false); // no_output_of_prior_pics_flag
    rbsp.WriteBit(false); // long_term_reference_flag
    rbsp.WriteSignedExpGolomb(0); // slice_qp_delta

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX)
        this._WritePcmMacroblock(rbsp, planes, mbX, mbY);

    var slice = _NalUnit(0x65, rbsp.FinishRbsp()); // nal_ref_idc 3, IDR slice
    var sample = new byte[_NAL_LENGTH_SIZE + slice.Length];
    BinaryPrimitives.WriteUInt32BigEndian(sample, checked((uint)slice.Length));
    slice.CopyTo(sample, _NAL_LENGTH_SIZE);

    packet = new(
      this._requested.Index,
      sample,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

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

  private byte[] _SequenceParameterSet() {
    var rbsp = new H264BitWriter();
    rbsp.WriteBits(_PROFILE_IDC, 8);
    rbsp.WriteBits(_PROFILE_COMPATIBILITY, 8);
    rbsp.WriteBits(_LEVEL_IDC, 8);
    rbsp.WriteUnsignedExpGolomb(0); // seq_parameter_set_id
    rbsp.WriteUnsignedExpGolomb(0); // log2_max_frame_num_minus4
    rbsp.WriteUnsignedExpGolomb(2); // pic_order_cnt_type
    rbsp.WriteUnsignedExpGolomb(1); // max_num_ref_frames
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
    rbsp.WriteBits(0, 2); // weighted_bipred_idc
    rbsp.WriteSignedExpGolomb(0); // pic_init_qp_minus26
    rbsp.WriteSignedExpGolomb(0); // pic_init_qs_minus26
    rbsp.WriteSignedExpGolomb(0); // chroma_qp_index_offset
    rbsp.WriteBit(false); // deblocking_filter_control_present_flag
    rbsp.WriteBit(false); // constrained_intra_pred_flag
    rbsp.WriteBit(false); // redundant_pic_cnt_present_flag
    return _NalUnit(0x68, rbsp.FinishRbsp());
  }

  private void _WritePcmMacroblock(H264BitWriter writer, byte[] planes, int mbX, int mbY) {
    writer.WriteUnsignedExpGolomb(25); // I_PCM in an I slice
    writer.AlignWithZeroBits();

    var lumaSamples = this._width * this._height;
    var chromaWidth = this._width / 2;
    var chromaHeight = this._height / 2;
    var chromaSamples = chromaWidth * chromaHeight;

    for (var y = 0; y < 16; ++y) {
      var sourceY = Math.Min(mbY * 16 + y, this._height - 1);
      var row = sourceY * this._width;
      for (var x = 0; x < 16; ++x) {
        var sourceX = Math.Min(mbX * 16 + x, this._width - 1);
        writer.WriteAlignedByte(planes[row + sourceX]);
      }
    }

    var chromaX = mbX * 8;
    var chromaY = mbY * 8;
    _WriteChroma(lumaSamples);
    _WriteChroma(lumaSamples + chromaSamples);

    void _WriteChroma(int planeOffset) {
      for (var y = 0; y < 8; ++y) {
        var sourceY = Math.Min(chromaY + y, chromaHeight - 1);
        var row = planeOffset + sourceY * chromaWidth;
        for (var x = 0; x < 8; ++x) {
          var sourceX = Math.Min(chromaX + x, chromaWidth - 1);
          writer.WriteAlignedByte(planes[row + sourceX]);
        }
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
