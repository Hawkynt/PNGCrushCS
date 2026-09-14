using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>Encodes Avid Meridien Compressed (<c>AVDJ</c>) video.</summary>
/// <remarks>
/// The encoded picture data is baseline 4:2:2 JPEG/JFIF, matching Meridien's intra-only compressed
/// representation. Standard 720x486 NTSC and 720x576 PAL pictures are written as two complete JPEG
/// fields per packet in their documented temporal order; other geometries are written as one
/// progressive JPEG. Every packet is independent and is therefore a key frame.
/// <para/>
/// The public video-encoder contract has no quality/rate-control setting, so this implementation uses
/// one fixed IJG quality factor. It does not claim the undocumented alpha-bearing AVDJ variant: a
/// source containing non-opaque alpha is refused rather than silently flattened. Opaque alpha-bearing
/// source formats are accepted because discarding an all-255 alpha plane loses no information.
/// <para/>
/// JPEG coding is delegated to the image package's existing managed encoder, explicitly selecting
/// baseline 4:2:2 rather than growing a second DCT/Huffman implementation here. The QuickTime sample
/// description carries a <c>fiel</c> extension for the standard interlaced geometries.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class AvidMeridienCompressedVideoEncoder : IVideoCodecEncoder<AvidMeridienCompressedVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVDJ");
  private const string _MATROSKA_CODEC_ID = "V_MJPEG";
  private const int _QUALITY = 90;

  private readonly MediaStreamInfo _stream;
  private readonly InterlaceLayout _interlace;

  private AvidMeridienCompressedVideoEncoder(MediaStreamInfo stream, InterlaceLayout interlace) {
    this._interlace = interlace;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = _MATROSKA_CODEC_ID,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 24,
      CodecPrivateData = _SampleEntry(stream.Width, stream.Height, interlace),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Avid Meridien Compressed";

  public static CodecTag Codec => _Tag;

  public static AvidMeridienCompressedVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Avid Meridien Compressed can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Avid Meridien Compressed encoder needs the output dimensions before the muxer is created; "
        + $"{stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} does not fit the sixteen-bit size fields of a QuickTime image description.");

    return new(stream, _LayoutFor(stream.Width, stream.Height));
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The encoder was created for {this._stream.Width}x{this._stream.Height} pictures, but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    _RefuseTransparency(frame);
    var rgb = frame.Format == PixelFormat.Rgb24 ? frame.PixelData : frame.ToRgb24();

    byte[] data;
    if (this._interlace.IsInterlaced) {
      var first = _Field(rgb, frame.Width, frame.Height, this._interlace.FirstFieldOnOddRows ? 1 : 0);
      var second = _Field(rgb, frame.Width, frame.Height, this._interlace.FirstFieldOnOddRows ? 0 : 1);
      data = [
        .. _EncodeJpeg(first, frame.Width, frame.Height / 2),
        .. _EncodeJpeg(second, frame.Width, frame.Height / 2),
      ];
    } else {
      data = _EncodeJpeg(rgb, frame.Width, frame.Height);
    }

    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private static byte[] _EncodeJpeg(byte[] rgb, int width, int height)
    => JpegManagedEncoder.Encode(
      rgb,
      width,
      height,
      quality: _QUALITY,
      JpegMode.Baseline,
      JpegSubsampling.Chroma422,
      optimizeHuffman: false,
      isGrayscale: false);

  private static byte[] _Field(byte[] rgb, int width, int height, int parity) {
    var stride = checked(width * 3);
    var fieldHeight = height / 2;
    var result = new byte[checked(stride * fieldHeight)];

    for (var row = 0; row < fieldHeight; ++row)
      rgb.AsSpan((row * 2 + parity) * stride, stride)
        .CopyTo(result.AsSpan(row * stride, stride));

    return result;
  }

  private static void _RefuseTransparency(RawImage frame) {
    if (!frame.HasAlpha)
      return;

    var rgba = frame.Format == PixelFormat.Rgba32 ? frame.PixelData : frame.ToRgba32();
    for (var i = 3; i < rgba.Length; i += 4)
      if (rgba[i] != byte.MaxValue)
        throw new NotSupportedException(
          "Avid Meridien Compressed alpha has no public bitstream description; this encoder refuses non-opaque alpha rather than flattening it.");
  }

  private static InterlaceLayout _LayoutFor(int width, int height)
    => (width, height) switch {
      (720, 486) => new(true, true, 14),  // 525-line/NTSC: lower field first.
      (720, 576) => new(true, false, 9),  // 625-line/PAL: upper field first.
      _ => new(false, false, 0),
    };

  /// <summary>Builds a QuickTime visual sample entry with explicit field/frame information.</summary>
  private static byte[] _SampleEntry(int width, int height, InterlaceLayout interlace) {
    const int _BODY_SIZE = 78;
    const int _FIEL_SIZE = 10;
    const ushort _NO_COLOUR_TABLE = 0xFFFF;

    var entry = new byte[8 + _BODY_SIZE + _FIEL_SIZE];
    var span = entry.AsSpan();
    BinaryPrimitives.WriteInt32BigEndian(span, entry.Length);
    "AVDJ"u8.CopyTo(span[4..8]);

    var body = span.Slice(8, _BODY_SIZE);
    BinaryPrimitives.WriteUInt16BigEndian(body[6..], 1); // data reference index
    BinaryPrimitives.WriteUInt16BigEndian(body[24..], checked((ushort)width));
    BinaryPrimitives.WriteUInt16BigEndian(body[26..], checked((ushort)height));
    BinaryPrimitives.WriteUInt32BigEndian(body[28..], 0x00480000); // 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(body[32..], 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(body[40..], 1); // frames per sample
    var name = "Avid Meridien"u8;
    body[42] = (byte)name.Length;
    name.CopyTo(body[43..]);
    BinaryPrimitives.WriteUInt16BigEndian(body[74..], 24);
    BinaryPrimitives.WriteUInt16BigEndian(body[76..], _NO_COLOUR_TABLE);

    var fiel = span[(8 + _BODY_SIZE)..];
    BinaryPrimitives.WriteInt32BigEndian(fiel, _FIEL_SIZE);
    "fiel"u8.CopyTo(fiel[4..8]);
    fiel[8] = interlace.IsInterlaced ? (byte)2 : (byte)1;
    fiel[9] = interlace.Detail;
    return entry;
  }

  private readonly record struct InterlaceLayout(bool IsInterlaced, bool FirstFieldOnOddRows, byte Detail);
}
