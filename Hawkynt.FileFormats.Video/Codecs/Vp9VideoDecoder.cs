using System;
using System.Collections.Generic;
using FileFormat.Codecs.Vp9;
using FileFormat.Core;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs;

/// <summary>Decodes VP9 video profiles 0 through 3.</summary>
/// <remarks>
/// Profiles 0/1 reconstruct eight-bit samples; profiles 2/3 reconstruct ten- or twelve-bit samples.
/// Profiles 0/2 use 4:2:0 chroma, while profiles 1/3 carry the non-4:2:0 layouts and the special
/// full-range sRGB/GBR representation. Entropy coding, prediction, transforms, filtering and reference
/// management are shared where VP9 shares them, with arithmetic widened to the coded sample depth.
/// <para/>
/// YUV pictures leave the decoder without loss in their native planar P8/P10/P12 layout. VP9 sRGB is
/// planar GBR internally. Eight-bit GBR is repacked to RGB24; high-bit-depth GBR is repacked to RGB48,
/// scaling the 10/12-bit code values onto the full 16-bit range. That mapping is injective and therefore
/// retains every source code value while using the canonical high-precision RGB layout already exposed
/// by <see cref="RawImage"/>.
/// </remarks>
public sealed class Vp9VideoDecoder : IVideoCodecDecoder<Vp9VideoDecoder> {

  private const string _MATROSKA_CODEC_ID = "V_VP9";

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("VP90"),
    CodecTag.FromCharacters("vp09"),
  ];

  private readonly Vp9Decoder _decoder = new();
  private readonly Queue<RawImage> _pending = new();

  public static string CodecName => "VP9 (profiles 0-3)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    if (string.Equals(stream.CodecId, _MATROSKA_CODEC_ID, StringComparison.OrdinalIgnoreCase))
      return true;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  public static Vp9VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    foreach (var picture in this._decoder.Decode(packet.Data.Span))
      this._pending.Enqueue(_ToRawImage(picture));

    if (this._pending.Count == 0) {
      frame = null!;
      return false;
    }

    frame = this._pending.Dequeue();
    return true;
  }

  public IEnumerable<RawImage> Flush() {
    while (this._pending.Count > 0)
      yield return this._pending.Dequeue();
  }

  private static RawImage _ToRawImage(Vp9Frame picture) {
    if (picture.ColorSpace == CS_RGB)
      return _FromPlanarGbr(picture);

    var colorInfo = new RawImageColorInfo {
      Range = picture.ColorRange != 0 ? RawColorRange.Full : RawColorRange.Limited,
      Matrix = _MatrixOf(picture.ColorSpace),
      ChromaLocation = RawChromaLocation.Center,
    };

    var format = _YuvFormat(picture.BitDepth, picture.SubsamplingX, picture.SubsamplingY);
    var bytesPerSample = picture.BitDepth == 8 ? 1 : 2;
    var chromaWidth = (picture.Width + (1 << picture.SubsamplingX) - 1) >> picture.SubsamplingX;
    var chromaHeight = (picture.Height + (1 << picture.SubsamplingY) - 1) >> picture.SubsamplingY;
    var ySamples = checked(picture.Width * picture.Height);
    var cSamples = checked(chromaWidth * chromaHeight);
    var data = new byte[checked((ySamples + 2 * cSamples) * bytesPerSample)];

    var at = 0;
    at = _CopyPlane(
      picture.Luma, picture.LumaWidth, picture.Width, picture.Height,
      data, at, bytesPerSample);
    at = _CopyPlane(
      picture.Cb, picture.ChromaWidth, chromaWidth, chromaHeight,
      data, at, bytesPerSample);
    _CopyPlane(
      picture.Cr, picture.ChromaWidth, chromaWidth, chromaHeight,
      data, at, bytesPerSample);

    return new() {
      Width = picture.Width,
      Height = picture.Height,
      Format = format,
      PixelData = data,
      ColorInfo = colorInfo,
    };
  }

  private static int _CopyPlane(
    ushort[] source, int sourceStride, int width, int height,
    byte[] target, int targetOffset, int bytesPerSample) {
    if (bytesPerSample == 1) {
      for (var y = 0; y < height; ++y) {
        var sourceAt = y * sourceStride;
        for (var x = 0; x < width; ++x)
          target[targetOffset++] = checked((byte)source[sourceAt + x]);
      }
      return targetOffset;
    }

    // RawImage's P10/P12 convention is a right-justified numeric sample in a little-endian ushort.
    for (var y = 0; y < height; ++y) {
      var sourceAt = y * sourceStride;
      for (var x = 0; x < width; ++x) {
        var sample = source[sourceAt + x];
        target[targetOffset++] = (byte)sample;
        target[targetOffset++] = (byte)(sample >> 8);
      }
    }

    return targetOffset;
  }

  private static PixelFormat _YuvFormat(int bitDepth, int subX, int subY) => (bitDepth, subX, subY) switch {
    (8, 1, 1) => PixelFormat.Yuv420P8,
    (8, 1, 0) => PixelFormat.Yuv422P8,
    (8, 0, 1) => PixelFormat.Yuv440P8,
    (8, 0, 0) => PixelFormat.Yuv444P8,
    (10, 1, 1) => PixelFormat.Yuv420P10,
    (10, 1, 0) => PixelFormat.Yuv422P10,
    (10, 0, 1) => PixelFormat.Yuv440P10,
    (10, 0, 0) => PixelFormat.Yuv444P10,
    (12, 1, 1) => PixelFormat.Yuv420P12,
    (12, 1, 0) => PixelFormat.Yuv422P12,
    (12, 0, 1) => PixelFormat.Yuv440P12,
    (12, 0, 0) => PixelFormat.Yuv444P12,
    _ => throw new InvalidOperationException(
      $"VP9 produced unsupported {bitDepth}-bit chroma subsampling ({subX}, {subY})."),
  };

  private static RawImage _FromPlanarGbr(Vp9Frame picture) {
    if (picture.SubsamplingX != 0 || picture.SubsamplingY != 0)
      throw new InvalidOperationException("A VP9 sRGB frame must be 4:4:4.");

    if (picture.BitDepth == 8) {
      var data = new byte[checked(picture.Width * picture.Height * 3)];
      for (var y = 0; y < picture.Height; ++y)
      for (var x = 0; x < picture.Width; ++x) {
        var source = y * picture.LumaWidth + x;
        var target = (y * picture.Width + x) * 3;
        data[target] = checked((byte)picture.Cr[source]);
        data[target + 1] = checked((byte)picture.Luma[source]);
        data[target + 2] = checked((byte)picture.Cb[source]);
      }

      return new() {
        Width = picture.Width,
        Height = picture.Height,
        Format = PixelFormat.Rgb24,
        PixelData = data,
        ColorInfo = _SrgbColorInfo(),
      };
    }

    var max = (1 << picture.BitDepth) - 1;
    var rgb48 = new byte[checked(picture.Width * picture.Height * 6)];
    for (var y = 0; y < picture.Height; ++y)
    for (var x = 0; x < picture.Width; ++x) {
      var source = y * picture.LumaWidth + x;
      var target = (y * picture.Width + x) * 6;
      _WriteBigEndian16(rgb48, target, _ExpandTo16(picture.Cr[source], max));
      _WriteBigEndian16(rgb48, target + 2, _ExpandTo16(picture.Luma[source], max));
      _WriteBigEndian16(rgb48, target + 4, _ExpandTo16(picture.Cb[source], max));
    }

    return new() {
      Width = picture.Width,
      Height = picture.Height,
      Format = PixelFormat.Rgb48,
      PixelData = rgb48,
      ColorInfo = _SrgbColorInfo(),
    };
  }

  private static ushort _ExpandTo16(int sample, int max)
    => (ushort)((sample * 65535L + max / 2) / max);

  private static void _WriteBigEndian16(byte[] target, int at, ushort value) {
    target[at] = (byte)(value >> 8);
    target[at + 1] = (byte)value;
  }

  private static RawImageColorInfo _SrgbColorInfo() => new() {
    Range = RawColorRange.Full,
    Primaries = RawColorPrimaries.Bt709,
    Transfer = RawTransferCharacteristic.Srgb,
    Matrix = RawMatrixCoefficients.Identity,
  };

  private static RawMatrixCoefficients _MatrixOf(int colorSpace) => colorSpace switch {
    2 => RawMatrixCoefficients.Bt709,
    3 => RawMatrixCoefficients.Bt601,
    4 => RawMatrixCoefficients.Smpte240M,
    5 => RawMatrixCoefficients.Bt2020NonConstantLuminance,
    CS_BT_601 => RawMatrixCoefficients.Bt601,
    _ => RawMatrixCoefficients.Unspecified,
  };
}
