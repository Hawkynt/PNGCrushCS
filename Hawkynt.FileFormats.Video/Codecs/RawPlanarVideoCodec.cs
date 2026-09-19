using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes canonical planar raw-video packets described by a YUV4MPEG2 chroma token.</summary>
/// <remarks>
/// These packets have no inter-picture syntax at all: every packet is a complete picture, so there
/// are no P/B pictures, forward or backward references, reorder delay, motion vectors or decoder
/// state to reconstruct. The only semantics outside the bytes themselves are the plane geometry,
/// sample precision and, for the three eight-bit 4:2:0 spellings, chroma sample location.
/// </remarks>
public sealed class RawPlanarVideoDecoder : IVideoCodecDecoder<RawPlanarVideoDecoder> {

  private static readonly CodecTag _TAG = CodecTag.FromCharacters("YUV ");

  private readonly int _streamIndex;
  private readonly int _width;
  private readonly int _height;
  private readonly Layout _layout;
  private readonly int _frameBytes;

  private RawPlanarVideoDecoder(MediaStreamInfo stream, Layout layout) {
    this._streamIndex = stream.Index;
    this._width = stream.Width;
    this._height = stream.Height;
    this._layout = layout;
    this._frameBytes = _FrameBytes(stream.Width, stream.Height, layout.Format);
  }

  public static string CodecName => "Planar raw YUV";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;

    if (stream.Codec.EqualsIgnoringCase(_TAG))
      return true;

    if (!string.Equals(stream.CodecId, "rawvideo", StringComparison.OrdinalIgnoreCase) || stream.CodecPrivateData.IsEmpty)
      return false;

    return _TryLayout(Encoding.ASCII.GetString(stream.CodecPrivateData.Span), out _);
  }

  public static RawPlanarVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException("The stream is not planar raw video.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException("Planar raw video requires positive dimensions.");

    return new(stream, _Layout(_Chroma(stream)));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.StreamIndex != this._streamIndex)
      throw new InvalidDataException($"Raw-video decoder for stream {this._streamIndex} cannot decode stream {packet.StreamIndex}.");
    if (packet.Data.Length != this._frameBytes)
      throw new InvalidDataException($"A {this._layout.Format} frame at {this._width}x{this._height} must contain exactly {this._frameBytes} bytes, not {packet.Data.Length}.");

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = this._layout.Format,
      PixelData = packet.Data.ToArray(),
      ColorInfo = _ColorInfo(this._layout.ChromaLocation),
    };
    return true;
  }

  internal static PixelFormat PixelFormatFor(string chroma) => _Layout(chroma).Format;

  private static string _Chroma(MediaStreamInfo stream)
    => stream.CodecPrivateData.IsEmpty ? "420jpeg" : Encoding.ASCII.GetString(stream.CodecPrivateData.Span);

  private static Layout _Layout(string chroma)
    => _TryLayout(chroma, out var layout)
      ? layout
      : throw new NotSupportedException($"Raw planar chroma mode '{chroma}' is not represented by a RawImage pixel format.");

  private static bool _TryLayout(string chroma, out Layout layout) {
    layout = chroma switch {
      "mono" => new(PixelFormat.Gray8),
      "411" => new(PixelFormat.Yuv411P8),
      "420" or "420jpeg" => new(PixelFormat.Yuv420P8, RawChromaLocation.Center),
      "420mpeg2" => new(PixelFormat.Yuv420P8, RawChromaLocation.Left),
      "420paldv" => new(PixelFormat.Yuv420P8, RawChromaLocation.TopLeft),
      "422" => new(PixelFormat.Yuv422P8),
      "444" => new(PixelFormat.Yuv444P8),
      "420p9" => new(PixelFormat.Yuv420P9),
      "422p9" => new(PixelFormat.Yuv422P9),
      "444p9" => new(PixelFormat.Yuv444P9),
      "420p10" => new(PixelFormat.Yuv420P10),
      "422p10" => new(PixelFormat.Yuv422P10),
      "444p10" => new(PixelFormat.Yuv444P10),
      "420p12" => new(PixelFormat.Yuv420P12),
      "422p12" => new(PixelFormat.Yuv422P12),
      "444p12" => new(PixelFormat.Yuv444P12),
      "420p14" => new(PixelFormat.Yuv420P14),
      "422p14" => new(PixelFormat.Yuv422P14),
      "444p14" => new(PixelFormat.Yuv444P14),
      "420p16" => new(PixelFormat.Yuv420P16),
      "422p16" => new(PixelFormat.Yuv422P16),
      "444p16" => new(PixelFormat.Yuv444P16),
      _ => default,
    };

    return layout.Format != default || chroma == "mono";
  }

  private static RawImageColorInfo? _ColorInfo(RawChromaLocation location)
    => location == RawChromaLocation.Unspecified ? null : new() { ChromaLocation = location };

  private static int _FrameBytes(int width, int height, PixelFormat format) {
    var image = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    return checked((int)image.MinimumPixelDataLength);
  }

  private readonly record struct Layout(
    PixelFormat Format,
    RawChromaLocation ChromaLocation = RawChromaLocation.Unspecified
  );
}

/// <summary>Encodes RawImage frames as canonical tightly packed planar raw video.</summary>
/// <remarks>
/// Raw planar video is all-intra by definition. Each call produces one self-contained packet, copies
/// the source samples into packet-owned storage, marks it as a key frame and gives it identical PTS
/// and DTS; there is therefore no reference lifetime or decode-order state for a caller to manage.
/// </remarks>
public sealed class RawPlanarVideoEncoder : IVideoCodecEncoder<RawPlanarVideoEncoder> {

  private static readonly CodecTag _TAG = CodecTag.FromCharacters("YUV ");

  private readonly MediaStreamInfo _stream;
  private readonly PixelFormat _format;
  private readonly RawChromaLocation _chromaLocation;
  private readonly int _frameBytes;

  private RawPlanarVideoEncoder(MediaStreamInfo stream, PixelFormat format, string chroma, RawChromaLocation chromaLocation) {
    this._format = format;
    this._chromaLocation = chromaLocation;
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

  public static string CodecName => "Planar raw YUV";
  public static CodecTag Codec => _TAG;

  public static RawPlanarVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video || stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException("Planar raw-video encoding requires a video stream with positive dimensions.");

    var chroma = stream.CodecPrivateData.IsEmpty ? "420jpeg" : Encoding.ASCII.GetString(stream.CodecPrivateData.Span);
    var format = RawPlanarVideoDecoder.PixelFormatFor(chroma);
    var chromaLocation = chroma switch {
      "420" or "420jpeg" => RawChromaLocation.Center,
      "420mpeg2" => RawChromaLocation.Left,
      "420paldv" => RawChromaLocation.TopLeft,
      _ => RawChromaLocation.Unspecified,
    };
    return new(stream, format, chroma, chromaLocation);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"Raw-video geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var converted = this._Prepare(frame);
    if (converted.PixelData.Length < this._frameBytes)
      throw new InvalidDataException($"Conversion to {this._format} produced {converted.PixelData.Length} bytes, expected {this._frameBytes}.");

    packet = new(
      this._stream.Index,
      converted.PixelData.AsSpan(0, this._frameBytes).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private RawImage _Prepare(RawImage frame) {
    if (frame.Format == this._format) {
      var actual = frame.ColorInfo?.ChromaLocation ?? RawChromaLocation.Unspecified;
      if (this._chromaLocation != RawChromaLocation.Unspecified
          && actual != RawChromaLocation.Unspecified
          && actual != this._chromaLocation)
        throw new InvalidDataException(
          $"The stream declares {this._chromaLocation} chroma samples but the source frame declares {actual}; raw packing cannot relocate samples without changing them.");

      return frame;
    }

    // The converter's 4:2:0 downsampling averages a 2x2 cell, which naturally produces centered
    // chroma. MPEG-2's left siting and PAL-DV's top-left siting need a resampler, not a relabel, so
    // accept those only when the caller already supplies the requested 4:2:0 sample grid.
    if (this._chromaLocation is RawChromaLocation.Left or RawChromaLocation.TopLeft)
      throw new InvalidDataException(
        $"The requested raw-video mode uses {this._chromaLocation} chroma siting; supply an already-sited {this._format} frame rather than converting from {frame.Format}.");

    return this._chromaLocation == RawChromaLocation.Unspecified
      ? FastRawImageConverter.Convert(frame, this._format)
      : FastRawImageConverter.Convert(frame, this._format, new RawImageColorInfo { ChromaLocation = this._chromaLocation });
  }

  private static int _FrameBytes(int width, int height, PixelFormat format) {
    var image = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    return checked((int)image.MinimumPixelDataLength);
  }
}
