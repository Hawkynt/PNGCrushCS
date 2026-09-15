using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Avid AVRn Resolution 1:1 as lossless packed UYVY 4:2:2.
/// </summary>
/// <remarks>
/// <c>AVRn</c> is historically overloaded: without Avid's <c>1:1</c> codec marker it names Motion
/// JPEG, while the marker selects the uncompressed Resolution 1:1 path. This encoder deliberately
/// writes the latter. Each packet is one progressive picture in UYVY order — Cb, Y0, Cr, Y1 — with
/// no prediction, transform or entropy coding, so every packet is a key frame and a
/// <see cref="PixelFormat.Yuv422P8"/> picture round-trips sample-exactly.
/// <para/>
/// The stream description is a standard forty-byte <c>BITMAPINFOHEADER</c> followed by the minimum
/// thirty-one bytes of Avid codec data needed to carry <c>1:1</c> at byte 28. That is the discriminator
/// used by FFmpeg's AVI demuxer before its AVRn decoder is selected. The remaining bytes are zero,
/// which does not match the separate <c>1:1(</c> interlace descriptor and therefore states the
/// progressive layout this encoder writes.
/// <para/>
/// FFmpeg provides no AVRn encoder to translate. This writer was implemented independently from the
/// public behavior of its LGPL-2.1-or-later AVI demuxer and AVRn decoder and is verified by handing a
/// muxed AVI to FFmpeg through the repository's encoder oracle test.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class AvrnVideoEncoder : IVideoCodecEncoder<AvrnVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVRn");

  private const string _VFW_CODEC_ID = "V_MS/VFW/FOURCC";
  private const int _EXTRA_DATA_SIZE = 31;
  private const int _ONE_TO_ONE_MARKER_OFFSET = 28;

  private readonly MediaStreamInfo _stream;
  private readonly PackedYuv422Packing _packing;

  private AvrnVideoEncoder(MediaStreamInfo stream, PackedYuv422Packing packing, byte[] format) {
    this._packing = packing;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = _VFW_CODEC_ID,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 16,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Avid AVRn";

  public static CodecTag Codec => _Tag;

  public static AvrnVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Avid AVRn can only encode a video stream.");

    var packing = PackedYuv422Packing.For(stream, PackedYuv422Order.CbLumaCrLuma, "AVRn Resolution 1:1");
    return new(stream, packing, _BuildFormat(stream));
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"AVRn Resolution 1:1 geometry is fixed at {this._stream.Width}x{this._stream.Height}; "
        + $"received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = new(
      this._stream.Index,
      this._packing.Pack(this._packing.PlanesOf(frame)),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);

    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private static byte[] _BuildFormat(MediaStreamInfo stream) {
    int imageBytes;
    try {
      imageBytes = checked(stream.Width * stream.Height * 2);
    } catch (OverflowException exception) {
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, whose AVRn frame size does not fit in memory.",
        exception);
    }

    var format = new byte[BitmapInfoHeader.StructSize + _EXTRA_DATA_SIZE];
    new BitmapInfoHeader(
      BitmapInfoHeader.StructSize,
      stream.Width,
      stream.Height,
      1,
      16,
      unchecked((int)_Tag.Value),
      imageBytes,
      0,
      0,
      0,
      0).WriteTo(format);

    "1:1"u8.CopyTo(format.AsSpan(BitmapInfoHeader.StructSize + _ONE_TO_ONE_MARKER_OFFSET));
    return format;
  }
}
