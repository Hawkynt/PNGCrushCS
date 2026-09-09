using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.ProRes;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Apple ProRes at 4:2:2 — the Proxy, LT, Standard and HQ profiles — one whole picture a
/// packet.
/// </summary>
/// <remarks>
/// Written from SMPTE RDD 36:2022 alongside <see cref="ProResVideoDecoder"/>: every syntax element
/// this writes is one that decoder reads, and the two halves of each — the entropy codes, the block
/// scan, the quantisation, the transform — live in one file apiece so that they cannot drift apart.
/// <para/>
/// <b>What it writes.</b> A progressive 4:2:2 frame at bitstream version 0, with both quantisation
/// weight matrices stated in the frame header, slices of eight macroblocks and no alpha channel.
/// Every packet is a key frame, because every ProRes frame is: the format is intra only and has no
/// other kind.
/// <para/>
/// <b>Which profile.</b> The four-character code the stream asks for chooses it —
/// <c>apco</c>, <c>apcs</c>, <c>apcn</c> or <c>apch</c> — and a stream naming none gets <c>apcn</c>,
/// which is what the registry routes here. The profile decides two things and nothing else: the
/// weight matrices that go in the frame header, and the data rate the per-slice quantisation index is
/// chosen to meet. See <see cref="ProResProfile"/>.
/// <para/>
/// <b>Lossy, and by construction.</b> ProRes quantises transform coefficients; only a picture already
/// on the quantiser's own reconstruction grid comes back exactly, and at 4:2:2 a picture with
/// per-column chroma does not survive the sampling either. What the encoder does guarantee is that
/// its own reconstruction is the one a decoder will build.
/// <para/>
/// <b>What it refuses, by name.</b> The 4:4:4 profiles <c>ap4h</c> and <c>ap4x</c>, whose twelve-bit
/// sampling and alpha channel this encoder does not write; interlaced coding, which it never chooses,
/// so a caller wanting two field pictures is refused rather than given one frame picture; and a
/// ten-bit sample that does not fit in ten bits.
/// <para/>
/// <b>Measured against ffmpeg.</b> Numbers, and what was compared against what, are in
/// <c>codec-notes.md</c>.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class ProResVideoEncoder : IVideoCodecEncoder<ProResVideoEncoder> {

  /// <summary>The four-character codes of the 4:4:4 profiles, which this encoder refuses by name.</summary>
  private static readonly CodecTag[] _FourFourFourTags = [
    CodecTag.FromCharacters("ap4h"),
    CodecTag.FromCharacters("ap4x"),
  ];

  /// <summary>The bytes of <c>frame_size</c> and <c>frame_identifier</c> together, RDD 36:2022, 5.1.</summary>
  private const int _FRAME_PREFIX_SIZE = 8;

  /// <summary>Twenty fixed bytes and the two weight matrices, RDD 36:2022, 5.1.1.</summary>
  private const int _FRAME_HEADER_SIZE = 20 + 64 + 64;

  /// <summary>
  /// The picture header's <c>log2_desired_slice_size_in_mb</c>: eight macroblocks a slice.
  /// </summary>
  /// <remarks>
  /// The largest of the four values 6.2.1 permits, and what every ProRes file examined here is
  /// written with. A slice is the unit of both parallelism and rate control, so the choice trades the
  /// two-byte table entry and the six-byte header of an extra slice against a finer grain for both;
  /// eight is where the format's own encoders put it.
  /// </remarks>
  private const int _LOG2_SLICE_SIZE = 3;

  /// <summary>The height above which an unlabelled picture is taken to be BT.709 rather than BT.601.</summary>
  /// <remarks>
  /// The same rule <see cref="ProResColorConversion"/> applies when a frame states no matrix, so a
  /// picture converted here and displayed there goes through one matrix and its inverse rather than
  /// through two different ones.
  /// </remarks>
  private const int _STANDARD_DEFINITION_LINES = 576;

  private readonly MediaStreamInfo _requested;
  private readonly ProResProfile _profile;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _paddedWidth;
  private readonly int _paddedHeight;
  private readonly RawImageColorInfo _colour;
  private MediaStreamInfo? _stream;

  private ProResVideoEncoder(MediaStreamInfo stream, ProResProfile profile) {
    this._requested = stream;
    this._profile = profile;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._macroblockHeight = (stream.Height + 15) / 16;
    this._paddedWidth = this._macroblockWidth * 16;
    this._paddedHeight = this._macroblockHeight * 16;
    this._colour = stream.Height > _STANDARD_DEFINITION_LINES
      ? RawImageColorInfo.Bt709Limited
      : RawImageColorInfo.Bt601Limited;
  }

  public static string CodecName => "Apple ProRes";

  /// <summary>The code the registry routes here: ProRes 422, the profile of the format's own name.</summary>
  public static CodecTag Codec => ProResProfile.Standard.Tag;

  /// <summary>Builds an encoder for the stream described, taking the profile from the code it names.</summary>
  public static ProResVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Apple ProRes can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Apple ProRes encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");

    // 5.1.1 states horizontal_size and vertical_size in sixteen bits apiece, so a picture larger than
    // that cannot describe itself and is refused rather than written with a wrapped size.
    if (stream.Width > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"A ProRes frame states its size in sixteen bits apiece; {stream.Width}x{stream.Height} does not fit.");

    foreach (var tag in _FourFourFourTags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        throw new NotSupportedException(
          $"Apple ProRes 4444 ('{stream.Codec}') is 4:4:4 at twelve bits with an alpha channel, none of which this "
          + "encoder writes. The 4:2:2 profiles 'apco', 'apcs', 'apcn' and 'apch' are the ones it does.");

    if (stream.Codec == CodecTag.None)
      return new(stream, ProResProfile.Standard);

    var profile = ProResProfile.For(stream.Codec)
      ?? throw new NotSupportedException(
        $"'{stream.Codec}' is not a ProRes profile this encoder writes. The 4:2:2 profiles are 'apco' (422 Proxy), "
        + "'apcs' (422 LT), 'apcn' (422) and 'apch' (422 HQ).");

    return new(stream, profile);
  }

  /// <summary>Codes one picture, which for this codec is always one whole frame.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This ProRes stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived. "
        + "Every ProRes frame states its own size, so a container describing one size cannot carry another.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = new(
      this._requested.Index,
      this.EncodeFrame(frame),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);

    return true;
  }

  /// <summary>Nothing is ever held back — a frame goes in and its packet comes out.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>
  /// The stream as a muxer needs it, including the QuickTime sample entry the picture is described by.
  /// </summary>
  /// <remarks>
  /// A ProRes sample description carries no codec configuration — every frame restates everything a
  /// decoder needs — but an ISO-base-media writer still needs a whole sample entry to put in its
  /// sample table, and only the encoder knows which of the four codes it is writing. Building it here
  /// is what keeps <c>Mp4Writer</c> out of the business of synthesising codec configuration.
  /// </remarks>
  public MediaStreamInfo DescribeStream() => this._stream ??= new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = this._profile.Tag,
    Handler = this._profile.Tag,
    CodecId = "V_MS/VFW/FOURCC",
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    DeclaredFrameCount = this._requested.DeclaredFrameCount,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = 24,
    Language = this._requested.Language,
    Name = this._requested.Name,
    CodecPrivateData = this._SampleEntry(),
  };

  /// <summary>Codes one picture into the bytes of one compressed frame, RDD 36:2022, 5.1.</summary>
  internal byte[] EncodeFrame(RawImage frame) {
    var source = frame.Format == PixelFormat.Yuv422P10
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P10, this._colour);

    if (!source.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {PixelFormat.Yuv422P10} produced too few bytes for {this._width}x{this._height}.");

    var planes = this._ToPlanes(source);
    var picture = ProResPictureEncoder.Encode(
      planes, this._profile, this._macroblockWidth, this._macroblockHeight, _LOG2_SLICE_SIZE);

    var frameSize = _FRAME_PREFIX_SIZE + _FRAME_HEADER_SIZE + picture.Length;
    var bytes = new byte[frameSize];

    BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)frameSize);
    "icpf"u8.CopyTo(bytes.AsSpan(4));
    this._WriteFrameHeader(bytes.AsSpan(_FRAME_PREFIX_SIZE, _FRAME_HEADER_SIZE));
    picture.CopyTo(bytes, _FRAME_PREFIX_SIZE + _FRAME_HEADER_SIZE);

    return bytes;
  }

  /// <summary>
  /// The twenty fixed bytes of RDD 36:2022, 5.1.1 and the two weight matrices behind them.
  /// </summary>
  /// <remarks>
  /// <b>Bitstream version 0.</b> 6.4 fixes <c>chroma_format</c> at 2 and <c>alpha_channel_type</c> at
  /// 0 for version 0, which is exactly what this encoder writes, so there is nothing version 1 would
  /// buy and one fewer thing an older decoder could refuse.
  /// <para/>
  /// <b>The colour description says "unspecified".</b> Table 6's <c>matrix_coefficients</c> value 2,
  /// with the primaries and transfer characteristic to match, which is what leaves a decoder to fall
  /// back on the picture height — the same fall-back the conversion into these planes used. Naming a
  /// matrix here instead would state something about the source that a caller handing over Y′CbCr
  /// planes never told this encoder.
  /// </remarks>
  private void _WriteFrameHeader(Span<byte> header) {
    header.Clear();

    BinaryPrimitives.WriteUInt16BigEndian(header, _FRAME_HEADER_SIZE);
    header[3] = 0;
    "hwky"u8.CopyTo(header[4..]);
    BinaryPrimitives.WriteUInt16BigEndian(header[8..], (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(header[10..], (ushort)this._height);

    // chroma_format 2 (4:2:2) in the top two bits, interlace_mode 0 in bits 3 and 2.
    header[12] = 2 << 6;
    header[14] = 2; // colour_primaries: unknown/unspecified
    header[15] = 2; // transfer_characteristic: unknown/unspecified
    header[16] = 2; // matrix_coefficients: unknown/unspecified
    header[19] = 3; // load_luma_quant_matrix and load_chroma_quant_matrix

    this._profile.LumaMatrix.CopyTo(header[20..]);
    this._profile.ChromaMatrix.CopyTo(header[84..]);
  }

  /// <summary>
  /// Turns the picture's ten-bit planes into the ones the coding works on, padded to whole macroblocks.
  /// </summary>
  /// <remarks>
  /// Padded by repeating the last column and the last row. The samples past the picture are
  /// transformed and transmitted like every other sample — a macroblock is coded whole and 7.5.3 has
  /// the decoder throw the excess away — so repeating the edge is what makes them cost the fewest
  /// bits and, more to the point, what stops an invented value bleeding back into the block it shares
  /// a transform with.
  /// </remarks>
  private ProResPlanes _ToPlanes(RawImage source) {
    var planes = ProResPlanes.Allocate(this._paddedWidth, this._paddedHeight, chromaShift: 1, bitDepth: 10, alphaChannelType: 0);
    var chromaWidth = (this._width + 1) / 2;

    _Fill(source.GetPlaneData(0), this._width, this._height, planes.Luma, this._paddedWidth, this._paddedHeight, "luma");
    _Fill(source.GetPlaneData(1), chromaWidth, this._height, planes.Cb, planes.ChromaWidth, this._paddedHeight, "blue difference");
    _Fill(source.GetPlaneData(2), chromaWidth, this._height, planes.Cr, planes.ChromaWidth, this._paddedHeight, "red difference");

    return planes;
  }

  /// <summary>Widens one plane of little-endian sixteen-bit slots into a padded plane of samples.</summary>
  private static void _Fill(
    ReadOnlySpan<byte> plane, int width, int height, ushort[] target, int paddedWidth, int paddedHeight, string component) {
    for (var y = 0; y < height; ++y) {
      var from = y * width * 2;
      var into = y * paddedWidth;
      var last = (ushort)0;

      for (var x = 0; x < width; ++x) {
        var sample = BinaryPrimitives.ReadUInt16LittleEndian(plane[(from + x * 2)..]);
        if (sample > 0x3FF)
          throw new InvalidDataException(
            $"A ProRes {component} sample is coded at ten bits; {sample} does not fit.");

        target[into + x] = last = sample;
      }

      for (var x = width; x < paddedWidth; ++x)
        target[into + x] = last;
    }

    for (var y = height; y < paddedHeight; ++y)
      Array.Copy(target, (height - 1) * paddedWidth, target, y * paddedWidth, paddedWidth);
  }

  /// <summary>
  /// The QuickTime visual sample entry a picture of this size and profile is described by.
  /// </summary>
  /// <remarks>
  /// The 86 bytes ISO/IEC 14496-12's <c>VisualSampleEntry</c> is, with the four-character code of the
  /// profile, the picture's size, the 72 dpi both resolutions are conventionally written as, an empty
  /// compressor name and a depth of 24. Nothing codec-specific follows it: a ProRes frame carries its
  /// own sampling, interlacing and quantisation, so there is no configuration record to write.
  /// </remarks>
  private byte[] _SampleEntry() {
    var entry = new byte[86];

    BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)entry.Length);
    // A CodecTag holds its four bytes as one little-endian number — the order they sit in a file —
    // so writing it little-endian is what puts the characters back in the order they were read in.
    BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), this._profile.Tag.Value);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(14), 1); // data_reference_index
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(32), (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(34), (ushort)this._height);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(36), 0x00480000); // horizontal resolution, 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(40), 0x00480000); // vertical resolution, 72 dpi
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(48), 1); // frame_count
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(82), 24); // depth
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(84), 0xFFFF); // pre_defined

    return entry;
  }
}
