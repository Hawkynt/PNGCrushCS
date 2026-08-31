using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes canonical planar raw-video packets described by a YUV4MPEG2 chroma token.</summary>
public sealed class RawPlanarVideoDecoder : IVideoCodecDecoder<RawPlanarVideoDecoder> {

  private static readonly CodecTag _TAG = CodecTag.FromCharacters("YUV ");

  private readonly int _streamIndex;
  private readonly int _width;
  private readonly int _height;
  private readonly PixelFormat _format;
  private readonly int _frameBytes;

  private RawPlanarVideoDecoder(MediaStreamInfo stream, PixelFormat format) {
    this._streamIndex = stream.Index;
    this._width = stream.Width;
    this._height = stream.Height;
    this._format = format;
    this._frameBytes = _FrameBytes(stream.Width, stream.Height, format);
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Planar raw YUV";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video
      && (stream.Codec.EqualsIgnoringCase(_TAG) || string.Equals(stream.CodecId, "rawvideo", StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static RawPlanarVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException("The stream is not planar raw video.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException("Planar raw video requires positive dimensions.");

    var chroma = _Chroma(stream);
    return new(stream, _PixelFormat(chroma));
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.StreamIndex != this._streamIndex)
      throw new InvalidDataException($"Raw-video decoder for stream {this._streamIndex} cannot decode stream {packet.StreamIndex}.");
    if (packet.Data.Length != this._frameBytes)
      throw new InvalidDataException($"A {this._format} frame at {this._width}x{this._height} must contain exactly {this._frameBytes} bytes, not {packet.Data.Length}.");

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = this._format,
      PixelData = packet.Data.ToArray(),
    };
    return true;
  }

  internal static PixelFormat PixelFormatFor(string chroma) => _PixelFormat(chroma);

  private static string _Chroma(MediaStreamInfo stream)
    => stream.CodecPrivateData.IsEmpty ? "420jpeg" : Encoding.ASCII.GetString(stream.CodecPrivateData.Span);

  private static PixelFormat _PixelFormat(string chroma) => chroma switch {
    "mono" => PixelFormat.Gray8,
    "420" or "420jpeg" => PixelFormat.Yuv420P8,
    "422" => PixelFormat.Yuv422P8,
    "444" => PixelFormat.Yuv444P8,
    "420p10" => PixelFormat.Yuv420P10,
    "422p10" => PixelFormat.Yuv422P10,
    "444p10" => PixelFormat.Yuv444P10,
    "420p12" => PixelFormat.Yuv420P12,
    "422p12" => PixelFormat.Yuv422P12,
    "444p12" => PixelFormat.Yuv444P12,
    "420p16" => PixelFormat.Yuv420P16,
    "422p16" => PixelFormat.Yuv422P16,
    "444p16" => PixelFormat.Yuv444P16,
    "420mpeg2" or "420paldv" => throw new NotSupportedException(
      $"Raw planar chroma mode '{chroma}' has chroma siting semantics that PixelFormat.Yuv420P8 alone cannot represent faithfully."),
    _ => throw new NotSupportedException($"Raw planar chroma mode '{chroma}' is not represented by a RawImage pixel format."),
  };

  private static int _FrameBytes(int width, int height, PixelFormat format) {
    var image = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    return checked((int)image.MinimumPixelDataLength);
  }
}

/// <summary>Encodes RawImage frames as canonical tightly packed planar raw video.</summary>
public sealed class RawPlanarVideoEncoder : IVideoCodecEncoder<RawPlanarVideoEncoder> {

  private static readonly CodecTag _TAG = CodecTag.FromCharacters("YUV ");

  private readonly MediaStreamInfo _stream;
  private readonly PixelFormat _format;
  private readonly int _frameBytes;

  private RawPlanarVideoEncoder(MediaStreamInfo stream, PixelFormat format, string chroma) {
    this._format = format;
    this._frameBytes = _FrameBytes(stream.Width, stream.Height, format);
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _TAG,
      Handler = stream.Handler,
      CodecId = "rawvideo",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = RawImage.BitsPerPixel(format),
      CodecPrivateData = Encoding.ASCII.GetBytes(chroma),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Planar raw YUV";
  /// <summary>Gets the codec.</summary>
  public static CodecTag Codec => _TAG;

  /// <summary>Creates an encoder for the specified media stream.</summary>
  public static RawPlanarVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video || stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException("Planar raw-video encoding requires a video stream with positive dimensions.");

    var chroma = stream.CodecPrivateData.IsEmpty ? "420jpeg" : Encoding.ASCII.GetString(stream.CodecPrivateData.Span);
    var format = RawPlanarVideoDecoder.PixelFormatFor(chroma);
    return new(stream, format, chroma);
  }

  /// <summary>Performs the try Encode operation.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"Raw-video geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var converted = frame.Format == this._format ? frame : FastRawImageConverter.Convert(frame, this._format);
    if (converted.PixelData.Length < this._frameBytes)
      throw new InvalidDataException($"Conversion to {this._format} produced {converted.PixelData.Length} bytes, expected {this._frameBytes}.");

    var data = converted.PixelData.Length == this._frameBytes
      ? converted.PixelData
      : converted.PixelData.AsSpan(0, this._frameBytes).ToArray();
    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  /// <summary>Performs the describe Stream operation.</summary>
  public MediaStreamInfo DescribeStream() => this._stream;

  private static int _FrameBytes(int width, int height, PixelFormat format) {
    var image = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    return checked((int)image.MinimumPixelDataLength);
  }
}
