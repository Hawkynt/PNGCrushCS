using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>Encodes Avid Meridien Compressed (<c>AVDJ</c>) video.</summary>
/// <remarks>
/// The coded picture is baseline 4:2:2 JPEG/JFIF, which is what Meridien's compressed representation
/// is. The two standard-definition D1 rasters are written the way Meridien writes them — two complete
/// JPEG field pictures per packet, in temporal order, 720x486 lower field first and 720x576 upper
/// field first — and every other geometry is written as one progressive JPEG. Every packet is
/// independent and is therefore a key frame.
/// <para/>
/// <b>The QuickTime <c>fiel</c> extension is not decoration.</b> Nothing in a pair of JPEGs says
/// which output rows either belongs on, so the sample description has to, and it has to say it in
/// the form that means "stored in this order" rather than only "displayed in this order": Apple's
/// <c>BB</c> (6) for the lower-field-first raster and <c>TT</c> (1) for the upper-field-first one.
/// Writing the display-dominance-only forms <c>BT</c> (14) and <c>TB</c> (9) instead was measured to
/// make ffmpeg weave both rasters the same way round, which is one of them upside down.
/// <para/>
/// <b>Deliberately not done.</b> The public video-encoder contract has no quality or rate-control
/// setting, so one fixed IJG quality factor is used. The undocumented alpha-bearing AVDJ variant is
/// not claimed: a picture carrying non-opaque alpha is refused rather than silently flattened, while
/// an all-opaque alpha channel is accepted because discarding it loses nothing. Interlace follows
/// the geometry and cannot be asked for at another one, since <see cref="MediaStreamInfo"/> has no
/// field-order to read and guessing from a height alone would mis-code progressive material.
/// <para/>
/// A Matroska <c>CodecID</c> is claimed only for the progressive case, where the packet genuinely is
/// one ordinary JPEG and <c>V_MJPEG</c> is the truth. A two-field packet under that CodecID would be
/// read by any Motion JPEG decoder as its first field alone — a picture of half the height with
/// nothing reporting a problem — so the interlaced case declines to name one and a Matroska muxer
/// refuses the stream instead.
/// <para/>
/// JPEG coding is delegated to the image package's managed encoder, selecting baseline 4:2:2 rather
/// than growing a second DCT/Huffman implementation here. FFmpeg has no AVDJ encoder to translate;
/// this writer was built from Apple's QuickTime image-description documentation and Avid's statement
/// of the D1 field order, and is verified by handing what it writes to FFmpeg and comparing every
/// decoded frame.
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
      CodecId = interlace.IsInterlaced ? null : _MATROSKA_CODEC_ID,
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

  /// <summary>What a person calls this codec, and what a refusal message names it by.</summary>
  public static string CodecName => "Avid Meridien Compressed";

  /// <summary>The four-character code a container names this codec by.</summary>
  public static CodecTag Codec => _Tag;

  /// <summary>Builds an encoder, fixing the field layout from the geometry asked for.</summary>
  public static AvidMeridienCompressedVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Avid Meridien Compressed can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        "An Avid Meridien Compressed encoder needs the output dimensions before the muxer is created; "
        + $"{stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} does not fit the sixteen-bit size fields of a QuickTime image description.");

    return new(stream, _LayoutFor(stream.Width, stream.Height));
  }

  /// <summary>Codes one picture, whole and on its own; every packet is a key frame.</summary>
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
      var fieldHeight = frame.Height / 2;
      var firstParity = this._interlace.FirstFieldOnOddRows ? 1 : 0;
      data = [
        .. _EncodeJpeg(_Field(rgb, frame.Width, frame.Height, firstParity), frame.Width, fieldHeight),
        .. _EncodeJpeg(_Field(rgb, frame.Width, frame.Height, 1 - firstParity), frame.Width, fieldHeight),
      ];
    } else
      data = _EncodeJpeg(rgb, frame.Width, frame.Height);

    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  /// <summary>The stream description a muxer writes its headers from, sample entry included.</summary>
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

  /// <summary>Lifts every second row out of a frame, starting at the given parity.</summary>
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

  /// <summary>
  /// The two D1 rasters Meridien switched between are the interlaced ones, with the field order Avid
  /// states for each; everything else is coded progressively.
  /// </summary>
  private static InterlaceLayout _LayoutFor(int width, int height)
    => (width, height) switch {
      (720, 486) => new(true, true, 6),  // 525-line NTSC: lower field stored and shown first (BB).
      (720, 576) => new(true, false, 1), // 625-line PAL: upper field stored and shown first (TT).
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
    BinaryPrimitives.WriteUInt16BigEndian(body[6..], 1);                   // data reference index
    BinaryPrimitives.WriteUInt16BigEndian(body[24..], checked((ushort)width));
    BinaryPrimitives.WriteUInt16BigEndian(body[26..], checked((ushort)height));
    BinaryPrimitives.WriteUInt32BigEndian(body[28..], 0x00480000);         // 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(body[32..], 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(body[40..], 1);                  // frames per sample
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

  /// <param name="Detail">Apple's <c>fiel</c> field-dominance byte, zero when there is one field.</param>
  private readonly record struct InterlaceLayout(bool IsInterlaced, bool FirstFieldOnOddRows, byte Detail);
}
