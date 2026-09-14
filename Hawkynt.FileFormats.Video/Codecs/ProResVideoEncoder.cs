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
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class ProResVideoEncoder : IVideoCodecEncoder<ProResVideoEncoder> {

  private const int _FRAME_PREFIX_SIZE = 8;
  private const int _FRAME_HEADER_SIZE = 20 + 64 + 64;
  private const int _VISUAL_SAMPLE_ENTRY_SIZE = 86;
  private const int _FIEL_ATOM_SIZE = 10;
  private const int _LOG2_SLICE_SIZE = 3;
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

  public static CodecTag Codec => ProResProfile.Standard.Tag;

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var profile in ProResProfile.All)
      if (stream.Codec.EqualsIgnoringCase(profile.Tag))
        return true;

    return false;
  }

  public static ProResVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Apple ProRes can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Apple ProRes encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");

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

  public IEnumerable<CodedPacket> Flush() => [];

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

  private void _WriteFrameHeader(Span<byte> header, int alphaChannelType) {
    header.Clear();

    BinaryPrimitives.WriteUInt16BigEndian(header, _FRAME_HEADER_SIZE);
    header[3] = (byte)(this._profile.IsFourFourFour || alphaChannelType != 0 ? 1 : 0);
    "hwky"u8.CopyTo(header[4..]);
    BinaryPrimitives.WriteUInt16BigEndian(header[8..], (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(header[10..], (ushort)this._height);

    header[12] = (byte)((this._profile.ChromaFormat << 6) | (this._interlaceMode << 2));
    header[14] = 2;
    header[15] = 2;
    header[16] = 2;
    header[17] = (byte)alphaChannelType;
    header[19] = 3;

    this._profile.LumaMatrix.CopyTo(header[20..]);
    this._profile.ChromaMatrix.CopyTo(header[84..]);
  }

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

  private int _AlphaChannelType(RawImage frame) {
    if (!this._alphaEnabled)
      return 0;

    if (!frame.HasAlpha)
      return 2;

    var traits = RawPixelFormats.Get(frame.Format);
    return traits.IsIndexed || traits.ComponentBitDepth is > 0 and <= 8 ? 1 : 2;
  }

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

  private byte[] _SampleEntry() {
    var size = _VISUAL_SAMPLE_ENTRY_SIZE + (this._interlaceMode == 0 ? 0 : _FIEL_ATOM_SIZE);
    var entry = new byte[size];

    BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)size);
    BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), this._profile.Tag.Value);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(14), 1);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(32), (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(34), (ushort)this._height);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(36), 0x00480000);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(40), 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(48), 1);

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

  private static int _InterlaceMode(ReadOnlySpan<byte> sampleEntry) {
    if (sampleEntry.Length < _VISUAL_SAMPLE_ENTRY_SIZE)
      return 0;

    var statedSize = BinaryPrimitives.ReadUInt32BigEndian(sampleEntry);
    var limit = statedSize >= _VISUAL_SAMPLE_ENTRY_SIZE && statedSize <= (uint)sampleEntry.Length
      ? (int)statedSize
      : sampleEntry.Length;

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
