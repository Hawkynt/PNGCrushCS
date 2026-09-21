using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.ProRes;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes all six Apple ProRes profiles, including 4:4:4 colour, lossless alpha and field pictures.
/// </summary>
/// <remarks>
/// Written from SMPTE RDD 36:2022 alongside <see cref="ProResVideoDecoder"/>. ProRes is strictly
/// intra-frame: there are no P or B pictures and no forward or backward references to maintain.
/// Interlace is represented by two independently coded field pictures in one frame, in display order.
/// <para/>
/// The four-character code selects Proxy, LT, 422, 422 HQ, 4444 or 4444 XQ. The first four are
/// written as ten-bit 4:2:2; the latter two as twelve-bit 4:4:4. A 4444 stream whose
/// <see cref="MediaStreamInfo.BitsPerPixel"/> is 32 preserves alpha, using eight- or sixteen-bit
/// lossless alpha according to the source representation; 24 or an unspecified zero writes colour
/// only. This makes the stream description deterministic before its first frame arrives.
/// <para/>
/// <see cref="MediaStreamInfo"/> has no field-order property. An existing QuickTime visual sample
/// entry may request field coding through its <c>fiel</c> child: 0x0201 writes top field first and
/// 0x0206 bottom field first. The two QuickTime orders whose coded and displayed orders disagree are
/// refused because RDD 36 defines the two ProRes pictures in temporal/display order and has no syntax
/// with which to preserve that distinction.
/// <para/>
/// <b>Lossy in colour, exact in alpha, and both by construction.</b> ProRes quantises transform
/// coefficients, so only a picture already on the quantiser's own reconstruction grid comes back
/// unchanged, and at 4:2:2 a picture with per-column chroma does not survive the sampling either.
/// The alpha channel goes through neither: RDD 36 codes it as runs of differences over the samples
/// themselves, so a matte is reproduced sample for sample or the writer is wrong.
/// <para/>
/// <b>Each half sits beside the half that undoes it.</b> The entropy codes, the block scan, the
/// quantisation and the transform live in one file apiece with their decoding counterparts, so the
/// two cannot drift apart unnoticed. What that arrangement cannot catch — a pair that is wrong in
/// the same way and agrees perfectly — is what the FFmpeg oracles are for, in both directions.
/// Numbers, and what was compared against what, are in <c>codec-notes.md</c>.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class ProResVideoEncoder : IVideoCodecEncoder<ProResVideoEncoder> {

  /// <summary>The bytes of <c>frame_size</c> and <c>frame_identifier</c> together, RDD 36:2022, 5.1.</summary>
  private const int _FRAME_PREFIX_SIZE = 8;

  /// <summary>Twenty fixed bytes and the two weight matrices, RDD 36:2022, 5.1.1.</summary>
  private const int _FRAME_HEADER_SIZE = 20 + 64 + 64;

  /// <summary>The fixed part of ISO/IEC 14496-12's <c>VisualSampleEntry</c>, before any child atom.</summary>
  private const int _VISUAL_SAMPLE_ENTRY_SIZE = 86;

  /// <summary>A QuickTime <c>fiel</c> atom: its header and the two bytes of field ordering.</summary>
  private const int _FIEL_ATOM_SIZE = 10;

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
  private readonly int _paddedWidth;
  private readonly int _interlaceMode;
  private readonly bool _alphaEnabled;
  private readonly RawImageColorInfo _colour;
  private MediaStreamInfo? _stream;

  private ProResVideoEncoder(MediaStreamInfo stream, ProResProfile profile, int interlaceMode, bool alphaEnabled) {
    this._requested = stream;
    this._profile = profile;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._paddedWidth = this._macroblockWidth * 16;
    this._interlaceMode = interlaceMode;
    this._alphaEnabled = alphaEnabled;
    this._colour = stream.Height > _STANDARD_DEFINITION_LINES
      ? RawImageColorInfo.Bt709Limited
      : RawImageColorInfo.Bt601Limited;
  }

  public static string CodecName => "Apple ProRes";

  /// <summary>The code the registry routes here: ProRes 422, the profile of the format's own name.</summary>
  public static CodecTag Codec => ProResProfile.Standard.Tag;

  /// <summary>
  /// Whether this writer will take the stream, which it does for any of the six profile codes.
  /// </summary>
  /// <remarks>
  /// <see cref="Codec"/> can name only one of them, and a registry that matched on it alone would
  /// route a stream asking for 4444 to whatever else claimed the tag, or to nothing. All six are
  /// written here, so all six are accepted here.
  /// </remarks>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var profile in ProResProfile.All)
      if (stream.Codec.EqualsIgnoringCase(profile.Tag))
        return true;

    return false;
  }

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

    var profile = stream.Codec == CodecTag.None
      ? ProResProfile.Standard
      : ProResProfile.For(stream.Codec)
        ?? throw new NotSupportedException(
          $"'{stream.Codec}' is not an Apple ProRes profile. Expected 'apco', 'apcs', 'apcn', 'apch', 'ap4h' or 'ap4x'.");

    if (profile.IsFourFourFour && stream.BitsPerPixel is not (0 or 24 or 32))
      throw new NotSupportedException(
        $"ProRes 4444 uses a 24-bit visual sample description without alpha or 32-bit with alpha; "
        + $"this stream asks for {stream.BitsPerPixel} bits per pixel.");

    var interlaceMode = _InterlaceMode(stream.CodecPrivateData.Span);
    if (interlaceMode != 0 && stream.Height < 2)
      throw new NotSupportedException("An interlaced ProRes frame needs at least one row in each of its two fields.");

    var alphaEnabled = profile.IsFourFourFour && stream.BitsPerPixel == 32;
    return new(stream, profile, interlaceMode, alphaEnabled);
  }

  /// <summary>Codes one picture, which for this codec is always one frame and always a key frame.</summary>
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
  /// sample table, and only the encoder knows which of the six codes it is writing, whether the
  /// stream has a matte, and which field order it was asked for. Building it here is what keeps
  /// <c>Mp4Writer</c> out of the business of synthesising codec configuration.
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
    BitsPerPixel = this._alphaEnabled ? 32 : 24,
    Language = this._requested.Language,
    Name = this._requested.Name,
    CodecPrivateData = this._SampleEntry(),
  };

  /// <summary>Codes one picture into the bytes of one compressed frame, RDD 36:2022, 5.1.</summary>
  /// <remarks>
  /// One picture for a progressive frame and two for an interlaced one, in the temporal order the
  /// fields are displayed in, which is the order 5.1 puts them in and the only order it can express.
  /// The frame header is written once and describes both.
  /// </remarks>
  internal byte[] EncodeFrame(RawImage frame) {
    var targetFormat = this._profile.IsFourFourFour ? PixelFormat.Yuv444P12 : PixelFormat.Yuv422P10;
    var source = frame.Format == targetFormat
      ? frame
      : FastRawImageConverter.Convert(frame, targetFormat, this._colour);

    if (!source.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {targetFormat} produced too few bytes for {this._width}x{this._height}.");

    var alphaChannelType = this._AlphaChannelType(frame);
    var alpha = alphaChannelType == 0 ? null : this._AlphaPlane(frame, alphaChannelType);

    byte[] firstPicture;
    byte[]? secondPicture = null;

    if (this._interlaceMode == 0) {
      firstPicture = this._EncodePicture(source, alpha, alphaChannelType, 0, 1, this._height, interlaced: false);
    } else {
      var topHeight = (this._height + 1) / 2;
      var bottomHeight = this._height / 2;
      var firstIsTop = this._interlaceMode == 1;

      firstPicture = this._EncodePicture(
        source,
        alpha,
        alphaChannelType,
        firstIsTop ? 0 : 1,
        2,
        firstIsTop ? topHeight : bottomHeight,
        interlaced: true);
      secondPicture = this._EncodePicture(
        source,
        alpha,
        alphaChannelType,
        firstIsTop ? 1 : 0,
        2,
        firstIsTop ? bottomHeight : topHeight,
        interlaced: true);
    }

    var frameSize = checked(
      _FRAME_PREFIX_SIZE + _FRAME_HEADER_SIZE + firstPicture.Length + (secondPicture?.Length ?? 0));
    var bytes = new byte[frameSize];

    BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)frameSize);
    "icpf"u8.CopyTo(bytes.AsSpan(4));
    this._WriteFrameHeader(bytes.AsSpan(_FRAME_PREFIX_SIZE, _FRAME_HEADER_SIZE), alphaChannelType);

    var at = _FRAME_PREFIX_SIZE + _FRAME_HEADER_SIZE;
    firstPicture.CopyTo(bytes, at);
    at += firstPicture.Length;
    if (secondPicture is not null)
      secondPicture.CopyTo(bytes, at);

    return bytes;
  }

  /// <summary>
  /// Codes one whole picture: a progressive frame, or one field of an interlaced one.
  /// </summary>
  /// <remarks>
  /// A field picture is an ordinary picture over every other row of the frame, which is what
  /// <paramref name="fieldOffset"/> and <paramref name="fieldStep"/> say — 0 and 1 for a frame
  /// picture, the field's parity and 2 for a field one. Its height is the frame's halved, rounded up
  /// for the field that carries the extra row, and 6.2 lets the two fields differ by that row.
  /// </remarks>
  /// <param name="source">The converted colour planes of the whole frame.</param>
  /// <param name="alpha">The matte of the whole frame, or <c>null</c> where the stream has none.</param>
  /// <param name="alphaChannelType">RDD 36 Table 7: 0 none, 1 eight-bit, 2 sixteen-bit.</param>
  /// <param name="fieldOffset">The frame row this picture's first row is.</param>
  /// <param name="fieldStep">1 for a frame picture, 2 for a field picture.</param>
  /// <param name="pictureHeight">The picture's own height in rows.</param>
  /// <param name="interlaced">Whether this is a field picture, which selects the coefficient scan.</param>
  private byte[] _EncodePicture(
    RawImage source,
    ushort[]? alpha,
    int alphaChannelType,
    int fieldOffset,
    int fieldStep,
    int pictureHeight,
    bool interlaced) {
    var macroblockHeight = (pictureHeight + 15) / 16;
    var paddedHeight = macroblockHeight * 16;
    var chromaShift = this._profile.IsFourFourFour ? 0 : 1;
    var planes = ProResPlanes.Allocate(
      this._paddedWidth,
      paddedHeight,
      chromaShift,
      this._profile.BitDepth,
      alphaChannelType);

    var (sourceChromaWidth, _) = source.GetPlaneDimensions(1);
    var maximumSample = (1 << this._profile.BitDepth) - 1;

    _FillPicturePlane(
      source.GetPlaneData(0), this._width, this._height,
      planes.Luma, planes.Width,
      fieldOffset, fieldStep, pictureHeight, maximumSample, "luma");
    _FillPicturePlane(
      source.GetPlaneData(1), sourceChromaWidth, this._height,
      planes.Cb, planes.ChromaWidth,
      fieldOffset, fieldStep, pictureHeight, maximumSample, "blue difference");
    _FillPicturePlane(
      source.GetPlaneData(2), sourceChromaWidth, this._height,
      planes.Cr, planes.ChromaWidth,
      fieldOffset, fieldStep, pictureHeight, maximumSample, "red difference");

    if (planes.Alpha is not null) {
      if (alpha is null)
        throw new InvalidDataException("A ProRes frame announced alpha but no source alpha plane was prepared.");
      _FillPictureAlpha(alpha, this._width, this._height, planes.Alpha, planes.Width, fieldOffset, fieldStep, pictureHeight);
    }

    return ProResPictureEncoder.Encode(
      planes,
      this._profile,
      this._macroblockWidth,
      macroblockHeight,
      pictureHeight,
      _LOG2_SLICE_SIZE,
      interlaced);
  }

  /// <summary>
  /// The twenty fixed bytes of RDD 36:2022, 5.1.1 and the two weight matrices behind them.
  /// </summary>
  /// <remarks>
  /// <b>The bitstream version follows the syntax used.</b> 6.4 fixes <c>chroma_format</c> at 2 and
  /// <c>alpha_channel_type</c> at 0 for version 0, so a 4:2:2 frame with no matte stays at version 0
  /// where an older decoder can read it, and 4:4:4 or alpha moves to version 1 because version 0 has
  /// no way to state either.
  /// <para/>
  /// <b>The colour description says "unspecified".</b> Table 6's <c>matrix_coefficients</c> value 2,
  /// with the primaries and transfer characteristic to match, which is what leaves a decoder to fall
  /// back on the picture height — the same fall-back the conversion into these planes used. Naming a
  /// matrix here instead would state something about the source that a caller handing over Y′CbCr
  /// planes never told this encoder.
  /// </remarks>
  private void _WriteFrameHeader(Span<byte> header, int alphaChannelType) {
    header.Clear();

    BinaryPrimitives.WriteUInt16BigEndian(header, _FRAME_HEADER_SIZE);
    header[3] = (byte)(this._profile.IsFourFourFour || alphaChannelType != 0 ? 1 : 0);
    "hwky"u8.CopyTo(header[4..]);
    BinaryPrimitives.WriteUInt16BigEndian(header[8..], (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(header[10..], (ushort)this._height);

    // chroma_format in the top two bits, interlace_mode in bits 3 and 2.
    header[12] = (byte)((this._profile.ChromaFormat << 6) | (this._interlaceMode << 2));
    header[14] = 2; // colour_primaries: unknown/unspecified
    header[15] = 2; // transfer_characteristic: unknown/unspecified
    header[16] = 2; // matrix_coefficients: unknown/unspecified
    header[17] = (byte)alphaChannelType;
    header[19] = 3; // load_luma_quant_matrix and load_chroma_quant_matrix

    this._profile.LumaMatrix.CopyTo(header[20..]);
    this._profile.ChromaMatrix.CopyTo(header[84..]);
  }

  /// <summary>
  /// Widens one plane of little-endian sixteen-bit slots into this picture's padded plane of samples.
  /// </summary>
  /// <remarks>
  /// Padded by repeating the last column and the last row. The samples past the picture are
  /// transformed and transmitted like every other sample — a macroblock is coded whole and 7.5.3 has
  /// the decoder throw the excess away — so repeating the edge is what makes them cost the fewest
  /// bits and, more to the point, what stops an invented value bleeding back into the block it shares
  /// a transform with. For a field picture the repeated row is that field's own last row, not the
  /// frame's, which is why the padding is done through the same field mapping as the picture.
  /// </remarks>
  private static void _FillPicturePlane(
    ReadOnlySpan<byte> source,
    int sourceWidth,
    int sourceHeight,
    ushort[] target,
    int targetWidth,
    int fieldOffset,
    int fieldStep,
    int pictureHeight,
    int maximumSample,
    string component) {
    var targetHeight = target.Length / targetWidth;
    var bitDepthName = maximumSample == 0x3FF ? "ten" : "twelve";

    for (var y = 0; y < targetHeight; ++y) {
      var pictureRow = Math.Min(y, pictureHeight - 1);
      var sourceY = checked(fieldOffset + pictureRow * fieldStep);
      if ((uint)sourceY >= (uint)sourceHeight)
        throw new InvalidDataException("A ProRes field mapping reached a row outside its source picture.");

      var from = checked(sourceY * sourceWidth * 2);
      var into = y * targetWidth;
      var last = (ushort)0;

      for (var x = 0; x < sourceWidth; ++x) {
        var sample = BinaryPrimitives.ReadUInt16LittleEndian(source[(from + x * 2)..]);
        if (sample > maximumSample)
          throw new InvalidDataException(
            $"A ProRes {component} sample is coded at {bitDepthName} bits; {sample} does not fit.");

        target[into + x] = last = sample;
      }

      for (var x = sourceWidth; x < targetWidth; ++x)
        target[into + x] = last;
    }
  }

  /// <summary>The same mapping and the same edge padding, over the matte.</summary>
  private static void _FillPictureAlpha(
    ushort[] source,
    int sourceWidth,
    int sourceHeight,
    ushort[] target,
    int targetWidth,
    int fieldOffset,
    int fieldStep,
    int pictureHeight) {
    var targetHeight = target.Length / targetWidth;

    for (var y = 0; y < targetHeight; ++y) {
      var pictureRow = Math.Min(y, pictureHeight - 1);
      var sourceY = checked(fieldOffset + pictureRow * fieldStep);
      if ((uint)sourceY >= (uint)sourceHeight)
        throw new InvalidDataException("A ProRes alpha field mapping reached a row outside its source picture.");

      var from = sourceY * sourceWidth;
      var into = y * targetWidth;
      Array.Copy(source, from, target, into, sourceWidth);
      var last = target[into + sourceWidth - 1];

      for (var x = sourceWidth; x < targetWidth; ++x)
        target[into + x] = last;
    }
  }

  /// <summary>
  /// Which of RDD 36 Table 7's alpha codings this frame's matte is written with.
  /// </summary>
  /// <remarks>
  /// The depth follows the source rather than the profile, because the coding is lossless either way
  /// and the only thing the choice decides is how many bits a sample costs. An eight-bit source
  /// written at sixteen would cost twice as much to say the same thing; a sixteen-bit source written
  /// at eight would stop being lossless, which is the one outcome this must not produce.
  /// </remarks>
  private int _AlphaChannelType(RawImage frame) {
    if (!this._alphaEnabled)
      return 0;

    if (!frame.HasAlpha)
      return 2;

    var traits = RawPixelFormats.Get(frame.Format);
    return traits.IsIndexed || traits.ComponentBitDepth is > 0 and <= 8 ? 1 : 2;
  }

  /// <summary>
  /// The frame's matte at its coded depth, or full opacity where the frame carries none.
  /// </summary>
  /// <remarks>
  /// A stream whose sample description states alpha keeps stating it, frame by frame, whatever any
  /// one frame happens to carry: the alternative is a stream whose <c>alpha_channel_type</c> changes
  /// under a decoder that was told the depth once. A frame with no alpha of its own is therefore
  /// opaque rather than absent.
  /// </remarks>
  private ushort[] _AlphaPlane(RawImage frame, int alphaChannelType) {
    var count = checked(this._width * this._height);
    var result = new ushort[count];

    if (!frame.HasAlpha) {
      Array.Fill(result, alphaChannelType == 1 ? (ushort)0x00FF : ushort.MaxValue);
      return result;
    }

    if (alphaChannelType == 1) {
      var rgba = frame.Format == PixelFormat.Rgba32
        ? frame
        : FastRawImageConverter.Convert(frame, PixelFormat.Rgba32);
      for (var i = 0; i < count; ++i)
        result[i] = rgba.PixelData[i * 4 + 3];
      return result;
    }

    var rgba64 = frame.Format == PixelFormat.Rgba64
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Rgba64);
    for (var i = 0; i < count; ++i)
      result[i] = BinaryPrimitives.ReadUInt16BigEndian(rgba64.PixelData.AsSpan(i * 8 + 6));

    return result;
  }

  /// <summary>
  /// The QuickTime visual sample entry a picture of this size and profile is described by.
  /// </summary>
  /// <remarks>
  /// The 86 bytes ISO/IEC 14496-12's <c>VisualSampleEntry</c> is, with the four-character code of the
  /// profile, the picture's size, the 72 dpi both resolutions are conventionally written as, the
  /// compressor name Apple's own files carry, and a depth of 24 or, for a stream with a matte, 32.
  /// There is no codec configuration record to write — a ProRes frame carries its own sampling,
  /// interlacing and quantisation — but a field-coded stream gets a <c>fiel</c> child, because the
  /// field order is the one thing a container states that the frame header cannot fully express.
  /// </remarks>
  private byte[] _SampleEntry() {
    var size = _VISUAL_SAMPLE_ENTRY_SIZE + (this._interlaceMode == 0 ? 0 : _FIEL_ATOM_SIZE);
    var entry = new byte[size];

    BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)size);
    // A CodecTag holds its four bytes as one little-endian number — the order they sit in a file —
    // so writing it little-endian is what puts the characters back in the order they were read in.
    BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), this._profile.Tag.Value);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(14), 1); // data_reference_index
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(32), (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(34), (ushort)this._height);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(36), 0x00480000); // horizontal resolution, 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(40), 0x00480000); // vertical resolution, 72 dpi
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(48), 1); // frame_count

    ReadOnlySpan<byte> compressor = this._profile.IsFourFourFour ? "Apple ProRes 4444"u8 : "Apple ProRes 422"u8;
    entry[50] = (byte)compressor.Length;
    compressor.CopyTo(entry.AsSpan(51));
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(82), (ushort)(this._alphaEnabled ? 32 : 24));
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(84), ushort.MaxValue);

    if (this._interlaceMode != 0) {
      var at = _VISUAL_SAMPLE_ENTRY_SIZE;
      BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(at), _FIEL_ATOM_SIZE);
      "fiel"u8.CopyTo(entry.AsSpan(at + 4));
      BinaryPrimitives.WriteUInt16BigEndian(
        entry.AsSpan(at + 8),
        this._interlaceMode == 1 ? (ushort)0x0201 : (ushort)0x0206);
    }

    return entry;
  }

  /// <summary>
  /// The RDD 36 interlace mode a caller's existing sample entry asks for, by way of its <c>fiel</c>.
  /// </summary>
  /// <remarks>
  /// <see cref="MediaStreamInfo"/> has nowhere to say "code this as fields", so the request has to
  /// come from the sample entry a caller already holds, and finding it means walking that entry's
  /// child atoms. Every size in that walk is checked against the entry it is inside before it is
  /// used as an offset: a sample entry is caller data like any other, and an atom claiming to be
  /// larger than its parent is how a walk like this reads past its buffer.
  /// </remarks>
  private static int _InterlaceMode(ReadOnlySpan<byte> sampleEntry) {
    if (sampleEntry.Length < _VISUAL_SAMPLE_ENTRY_SIZE)
      return 0;

    var statedSize = BinaryPrimitives.ReadUInt32BigEndian(sampleEntry);
    int limit;
    if (statedSize == 0) {
      limit = sampleEntry.Length;
    } else {
      if (statedSize < _VISUAL_SAMPLE_ENTRY_SIZE || statedSize > (uint)sampleEntry.Length)
        throw new InvalidDataException(
          $"A ProRes visual sample entry states {statedSize} bytes but carries {sampleEntry.Length}; its child atoms cannot be located safely.");
      limit = (int)statedSize;
    }

    for (var at = _VISUAL_SAMPLE_ENTRY_SIZE; at + 8 <= limit;) {
      var size32 = BinaryPrimitives.ReadUInt32BigEndian(sampleEntry[at..]);
      int atomSize;
      var headerSize = 8;

      if (size32 == 0) {
        atomSize = limit - at;
      } else if (size32 == 1) {
        if (at + 16 > limit)
          throw new InvalidDataException("A ProRes visual sample entry ends inside an extended QuickTime child header.");
        var extended = BinaryPrimitives.ReadUInt64BigEndian(sampleEntry[(at + 8)..]);
        if (extended > int.MaxValue)
          throw new InvalidDataException($"A QuickTime child atom of {extended} bytes is too large to inspect in memory.");
        atomSize = (int)extended;
        headerSize = 16;
      } else {
        if (size32 > int.MaxValue)
          throw new InvalidDataException($"A QuickTime child atom of {size32} bytes is too large to inspect in memory.");
        atomSize = (int)size32;
      }

      if (atomSize < headerSize || atomSize > limit - at)
        throw new InvalidDataException(
          $"A ProRes visual sample entry contains a child atom of {atomSize} bytes outside its {limit}-byte entry.");

      if (sampleEntry.Slice(at + 4, 4).SequenceEqual("fiel"u8)) {
        if (atomSize < 10)
          throw new InvalidDataException("A QuickTime 'fiel' atom is shorter than its two field-order bytes.");

        return BinaryPrimitives.ReadUInt16BigEndian(sampleEntry[(at + 8)..]) switch {
          0x0100 => 0,
          0x0201 => 1,
          0x0206 => 2,
          0x0209 => throw new NotSupportedException(
            "QuickTime field order 0x0209 codes the top field first but displays the bottom first; ProRes has no separate coded/displayed-order flag with which to preserve it."),
          0x020E => throw new NotSupportedException(
            "QuickTime field order 0x020E codes the bottom field first but displays the top first; ProRes has no separate coded/displayed-order flag with which to preserve it."),
          var code => throw new NotSupportedException($"QuickTime field order 0x{code:X4} is not a ProRes field order this encoder can preserve."),
        };
      }

      at += atomSize;
    }

    return 0;
  }
}
