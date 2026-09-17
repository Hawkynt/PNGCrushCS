using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.CineForm;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>The CineForm component layouts this encoder can write.</summary>
public enum CineFormEncodingFormat {
  /// <summary>Ten-bit YUV 4:2:2, encoded as Y, V, U.</summary>
  Yuv422,
  /// <summary>Twelve-bit RGB 4:4:4, encoded as G, R, B.</summary>
  Rgb444,
  /// <summary>Twelve-bit RGBA 4:4:4:4, encoded as G, R, B, A with CineForm alpha companding.</summary>
  Rgba4444,
}

/// <summary>How scan lines are organized in a CineForm sample.</summary>
public enum CineFormScanMode {
  /// <summary>Adjacent rows are adjacent in time and space.</summary>
  Progressive,
  /// <summary>Interlaced YUV, with the upper/even field displayed first.</summary>
  InterlacedUpperFieldFirst,
  /// <summary>Interlaced YUV, with the lower/odd field displayed first.</summary>
  InterlacedLowerFieldFirst,
}

/// <summary>Encodes GoPro CineForm I-frames.</summary>
/// <remarks>
/// CineForm remains intra-frame here: every input picture becomes one complete key-frame packet and
/// there are no MPEG-style P/B pictures or forward/backward motion references. Progressive YUV/RGB
/// uses the ordinary three-level spatial transform. Legacy interlaced YUV uses CineForm's documented
/// non-progressive first level, which combines adjacent field rows before the two coarser spatial
/// levels; it still has ten subbands and one independently decodable picture per packet.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class CineFormVideoEncoder : IVideoCodecEncoder<CineFormVideoEncoder> {
  private static readonly CodecTag _codec = CodecTag.FromCharacters("CFHD");

  private readonly MediaStreamInfo _stream;
  private readonly RawImageColorInfo _colour;
  private readonly CineFormEncodingFormat _encodingFormat;
  private readonly CineFormScanMode _scanMode;
  private readonly int _encodedHeight;
  private uint _frameNumber;

  private CineFormVideoEncoder(MediaStreamInfo stream, CineFormEncodingFormat encodingFormat, CineFormScanMode scanMode) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("GoPro CineForm can only encode a video stream.");
    if (!Enum.IsDefined(encodingFormat))
      throw new ArgumentOutOfRangeException(nameof(encodingFormat));
    if (!Enum.IsDefined(scanMode))
      throw new ArgumentOutOfRangeException(nameof(scanMode));
    if (scanMode != CineFormScanMode.Progressive && encodingFormat != CineFormEncodingFormat.Yuv422)
      throw new NotSupportedException("CineForm's legacy interlaced transform is supported only for YUV 4:2:2.");

    if (stream.Width < 48 || (stream.Width & 15) != 0)
      throw new NotSupportedException(
        $"This CineForm encoder needs a width of at least 48 pixels and a multiple of 16 for its three 2/6 wavelet levels; {stream.Width} was supplied.");
    if (stream.Height <= 0)
      throw new NotSupportedException($"A CineForm encoder needs a positive picture height; {stream.Height} was supplied.");
    if (scanMode != CineFormScanMode.Progressive && (stream.Height & 1) != 0)
      throw new NotSupportedException($"An interlaced CineForm picture needs an even height; {stream.Height} was supplied.");
    if (stream.Width > 65_520 || stream.Height > 65_528)
      throw new NotSupportedException(
        $"CineForm's picture dimensions are sixteen-bit values after padding; {stream.Width}x{stream.Height} does not fit this writer's frame header.");

    this._encodedHeight = Math.Max(32, (stream.Height + 7) & ~7);
    if ((long)stream.Width * this._encodedHeight > Array.MaxLength)
      throw new NotSupportedException(
        $"A padded CineForm frame of {stream.Width}x{this._encodedHeight} samples is too large for a managed plane.");

    this._encodingFormat = encodingFormat;
    this._scanMode = scanMode;
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

  /// <summary>Creates the interoperable progressive ten-bit 4:2:2 writer retained as the default.</summary>
  public static CineFormVideoEncoder Create(MediaStreamInfo stream)
    => new(stream, CineFormEncodingFormat.Yuv422, CineFormScanMode.Progressive);

  /// <summary>Creates a progressive CineForm writer for the requested component layout.</summary>
  public static CineFormVideoEncoder Create(MediaStreamInfo stream, CineFormEncodingFormat encodingFormat)
    => new(stream, encodingFormat, CineFormScanMode.Progressive);

  /// <summary>Creates a CineForm writer with an explicit scan mode.</summary>
  public static CineFormVideoEncoder Create(
    MediaStreamInfo stream,
    CineFormEncodingFormat encodingFormat,
    CineFormScanMode scanMode)
    => new(stream, encodingFormat, scanMode);

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
    var interlaced = this._scanMode != CineFormScanMode.Progressive;

    var y = new int[width * this._encodedHeight];
    var u = new int[chromaWidth * this._encodedHeight];
    var v = new int[chromaWidth * this._encodedHeight];

    _FillTenBitPlane(source.GetPlaneData(0), width, height, y, width, this._encodedHeight, "luma", interlaced);
    _FillTenBitPlane(source.GetPlaneData(1), chromaWidth, height, u, chromaWidth, this._encodedHeight, "blue difference", interlaced);
    _FillTenBitPlane(source.GetPlaneData(2), chromaWidth, height, v, chromaWidth, this._encodedHeight, "red difference", interlaced);

    return CineFormPictureEncoder.Encode(
      y, v, u, width, chromaWidth, this._encodedHeight, height, frameNumber,
      interlaced,
      upperFieldFirst: this._scanMode != CineFormScanMode.InterlacedLowerFieldFirst);
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
    int[] target, int targetWidth, int targetHeight, string component, bool interlaced) {

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

    _PadRows(target, targetWidth, height, targetHeight, interlaced);
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
      _PadRows(plane, width, height, targetHeight, interlaced: false);
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

  private static void _PadRows(int[] target, int width, int height, int targetHeight, bool interlaced) {
    if (!interlaced) {
      for (var y = height; y < targetHeight; ++y)
        Array.Copy(target, (height - 1) * width, target, y * width, width);
      return;
    }

    for (var y = height; y < targetHeight; ++y) {
      var sourceRow = height - 2 + (y & 1);
      Array.Copy(target, sourceRow * width, target, y * width, width);
    }
  }
}
