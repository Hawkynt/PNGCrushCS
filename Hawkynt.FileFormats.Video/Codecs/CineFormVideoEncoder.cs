using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.CineForm;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>The progressive CineForm layouts this encoder can write.</summary>
public enum CineFormEncodingFormat {
  /// <summary>Ten-bit YUV 4:2:2, encoded as Y, V, U.</summary>
  Yuv422,
  /// <summary>Twelve-bit RGB 4:4:4, encoded as G, R, B.</summary>
  Rgb444,
  /// <summary>Twelve-bit RGBA 4:4:4:4, encoded as G, R, B, A with CineForm alpha companding.</summary>
  Rgba4444,
}

/// <summary>Encodes progressive GoPro CineForm I-frames.</summary>
/// <remarks>
/// CineForm is spatial-wavelet and intra-frame in this path: every input picture becomes one complete
/// key-frame packet and there are no P/B pictures or temporal forward/backward references to maintain.
/// The default <see cref="Create(MediaStreamInfo)"/> remains the historical ten-bit 4:2:2 writer;
/// <see cref="Create(MediaStreamInfo,CineFormEncodingFormat)"/> additionally exposes the twelve-bit
/// RGB and RGBA layouts emitted by contemporary CFHD encoders.
/// <para/>
/// Source pictures are converted through the repository's raw-image converter to canonical planar
/// ten-bit 4:2:2 or packed sixteen-bit RGB[A], then narrowed to CineForm's coded precision. RGB[A]
/// therefore preserves high-bit-depth sources instead of needlessly routing them through eight-bit
/// display colour. No native SDK, P/Invoke or third-party package is involved.
/// <para/>
/// Packet framing and the alpha transfer were cross-checked against GoPro's MIT/Apache-2.0 reference
/// SDK and FFmpeg's LGPL encoder. The transforms, companding codebook and prescale schedules remain the
/// same managed implementation shared with the decoder.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class CineFormVideoEncoder : IVideoCodecEncoder<CineFormVideoEncoder> {
  private static readonly CodecTag _codec = CodecTag.FromCharacters("CFHD");

  private readonly MediaStreamInfo _stream;
  private readonly RawImageColorInfo _colour;
  private readonly CineFormEncodingFormat _encodingFormat;
  private readonly int _encodedHeight;
  private uint _frameNumber;

  private CineFormVideoEncoder(MediaStreamInfo stream, CineFormEncodingFormat encodingFormat) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("GoPro CineForm can only encode a video stream.");

    if (!Enum.IsDefined(encodingFormat))
      throw new ArgumentOutOfRangeException(nameof(encodingFormat));

    if (stream.Width < 48 || (stream.Width & 15) != 0)
      throw new NotSupportedException(
        $"This CineForm encoder needs a width of at least 48 pixels and a multiple of 16 for its three 2/6 wavelet levels; {stream.Width} was supplied.");

    if (stream.Height <= 0)
      throw new NotSupportedException($"A CineForm encoder needs a positive picture height; {stream.Height} was supplied.");

    if (stream.Width > 65_520 || stream.Height > 65_528)
      throw new NotSupportedException(
        $"CineForm's picture dimensions are sixteen-bit values after padding; {stream.Width}x{stream.Height} does not fit this writer's progressive frame header.");

    this._encodedHeight = Math.Max(32, (stream.Height + 7) & ~7);
    if ((long)stream.Width * this._encodedHeight > Array.MaxLength)
      throw new NotSupportedException(
        $"A padded CineForm frame of {stream.Width}x{this._encodedHeight} samples is too large for a managed plane.");

    this._encodingFormat = encodingFormat;
    this._colour = stream.Height > 576 ? RawImageColorInfo.Bt709Limited : RawImageColorInfo.Bt601Limited;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _codec,
      Handler = _codec,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = encodingFormat switch {
        CineFormEncodingFormat.Yuv422 => 20,
        CineFormEncodingFormat.Rgb444 => 36,
        CineFormEncodingFormat.Rgba4444 => 48,
        _ => throw new ArgumentOutOfRangeException(nameof(encodingFormat)),
      },
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "GoPro CineForm";

  public static CodecTag Codec => _codec;

  /// <summary>Creates the interoperable ten-bit 4:2:2 writer retained as the default.</summary>
  public static CineFormVideoEncoder Create(MediaStreamInfo stream) => new(stream, CineFormEncodingFormat.Yuv422);

  /// <summary>Creates a CineForm writer for one of its progressive YUV, RGB or RGBA layouts.</summary>
  public static CineFormVideoEncoder Create(MediaStreamInfo stream, CineFormEncodingFormat encodingFormat)
    => new(stream, encodingFormat);

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"This CineForm stream is {this._stream.Width}x{this._stream.Height}; a {frame.Width}x{frame.Height} picture arrived.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var frameNumber = presentationTimestamp.HasValue
      ? unchecked((ushort)presentationTimestamp.Value)
      : unchecked((ushort)this._frameNumber);

    var bytes = this.EncodeFrame(frame, frameNumber);
    ++this._frameNumber;

    packet = new(
      StreamIndex: this._stream.Index,
      Data: bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream() => this._stream;

  internal byte[] EncodeFrame(RawImage frame, ushort frameNumber = 0)
    => this._encodingFormat switch {
      CineFormEncodingFormat.Yuv422 => this._EncodeYuv422(frame, frameNumber),
      CineFormEncodingFormat.Rgb444 => this._EncodeRgb(frame, withAlpha: false, frameNumber),
      CineFormEncodingFormat.Rgba4444 => this._EncodeRgb(frame, withAlpha: true, frameNumber),
      _ => throw new InvalidOperationException($"Unsupported CineForm encoding format {this._encodingFormat}."),
    };

  private byte[] _EncodeYuv422(RawImage frame, ushort frameNumber) {
    var source = frame.Format == PixelFormat.Yuv422P10
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P10, this._colour);

    if (!source.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {PixelFormat.Yuv422P10} produced too few bytes for {source.Width}x{source.Height}.");

    var width = this._stream.Width;
    var height = this._stream.Height;
    var chromaWidth = width >> 1;

    var y = new int[width * this._encodedHeight];
    var u = new int[chromaWidth * this._encodedHeight];
    var v = new int[chromaWidth * this._encodedHeight];

    _FillTenBitPlane(source.GetPlaneData(0), width, height, y, width, this._encodedHeight, "luma");
    _FillTenBitPlane(source.GetPlaneData(1), chromaWidth, height, u, chromaWidth, this._encodedHeight, "blue difference");
    _FillTenBitPlane(source.GetPlaneData(2), chromaWidth, height, v, chromaWidth, this._encodedHeight, "red difference");

    // CineForm's measured 4:2:2 channel order is Y, V, U rather than conventional planar Y,U,V.
    return CineFormPictureEncoder.Encode(y, v, u, width, chromaWidth, this._encodedHeight, height, frameNumber);
  }

  private byte[] _EncodeRgb(RawImage frame, bool withAlpha, ushort frameNumber) {
    var targetFormat = withAlpha ? PixelFormat.Rgba64 : PixelFormat.Rgb48;
    var source = frame.Format == targetFormat ? frame : FastRawImageConverter.Convert(frame, targetFormat);
    if (!source.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {targetFormat} produced too few bytes for {source.Width}x{source.Height}.");

    var width = this._stream.Width;
    var height = this._stream.Height;
    var channelCount = withAlpha ? 4 : 3;
    var planes = new int[channelCount][];
    for (var i = 0; i < planes.Length; ++i)
      planes[i] = new int[width * this._encodedHeight];

    _FillRgbPlanes(source.PixelData, width, height, planes, this._encodedHeight, withAlpha);

    // CineForm's encoded RGB order is G,R,B[,A], not the packed source's R,G,B[,A].
    var encodedPlanes = withAlpha
      ? new[] { planes[1], planes[0], planes[2], planes[3] }
      : new[] { planes[1], planes[0], planes[2] };
    var widths = new int[encodedPlanes.Length];
    Array.Fill(widths, width);

    if (withAlpha)
      _CompactAlpha(encodedPlanes[3]);

    return CineFormPictureEncoder.Encode(
      encodedPlanes,
      widths,
      this._encodedHeight,
      height,
      withAlpha ? CineFormEncodedFormat.Rgba4444 : CineFormEncodedFormat.Rgb444,
      precision: 12,
      prescaleTable: 0x2800,
      CineFormPrescale.TwelveBit,
      frameNumber);
  }

  private static void _FillTenBitPlane(
    ReadOnlySpan<byte> source, int width, int height,
    int[] target, int targetWidth, int targetHeight, string component) {

    for (var y = 0; y < height; ++y) {
      var sourceRow = y * width * 2;
      var targetRow = y * targetWidth;
      for (var x = 0; x < width; ++x) {
        var sample = BinaryPrimitives.ReadUInt16LittleEndian(source[(sourceRow + x * 2)..]);
        if (sample > 0x3FF)
          throw new InvalidDataException($"A CineForm {component} sample is ten-bit; {sample} does not fit.");
        target[targetRow + x] = sample;
      }
    }

    _PadRows(target, targetWidth, height, targetHeight);
  }

  private static void _FillRgbPlanes(
    ReadOnlySpan<byte> source,
    int width,
    int height,
    int[][] planes,
    int targetHeight,
    bool withAlpha) {

    var components = withAlpha ? 4 : 3;
    var bytesPerPixel = components * 2;
    for (var y = 0; y < height; ++y) {
      var sourceRow = y * width * bytesPerPixel;
      var targetRow = y * width;
      for (var x = 0; x < width; ++x) {
        var sourcePixel = sourceRow + x * bytesPerPixel;
        for (var component = 0; component < components; ++component) {
          var value = BinaryPrimitives.ReadUInt16BigEndian(source[(sourcePixel + component * 2)..]);
          planes[component][targetRow + x] = _Scale16To12(value);
        }
      }
    }

    foreach (var plane in planes)
      _PadRows(plane, width, height, targetHeight);
  }

  private static int _Scale16To12(ushort value)
    => (value * 4095 + 32767) / 65535;

  private static void _CompactAlpha(int[] alpha) {
    for (var i = 0; i < alpha.Length; ++i) {
      var value = alpha[i];
      if (value is > 0 and < 4080)
        value = ((value * 223 + 128) >> 8) + 256;
      alpha[i] = value > 4095 ? 4095 : value;
    }
  }

  private static void _PadRows(int[] target, int width, int height, int targetHeight) {
    for (var y = height; y < targetHeight; ++y)
      Array.Copy(target, (height - 1) * width, target, y * width, width);
  }
}
