using System;
using System.Collections.Generic;
using FileFormat.Codecs.Vp9;
using FileFormat.Core;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs;

/// <summary>Decodes VP9 video profiles 0 and 1.</summary>
/// <remarks>
/// Profiles 0 and 1 are the complete eight-bit half of VP9. They share the entropy coder, transform,
/// quantisation, prediction and loop-filter machinery; profile 1 additionally carries 4:2:2, 4:4:0,
/// 4:4:4 and sRGB/GBR pictures. Profiles 2 and 3 raise reconstruction precision to ten or twelve bits
/// and remain an explicit high-bit-depth boundary.
/// <para/>
/// YUV pictures are returned in their native planar layout with range and matrix interpretation kept
/// beside the samples. VP9 sRGB is planar GBR internally; it is repacked losslessly to <see cref="PixelFormat.Rgb24"/>
/// because <see cref="RawImage"/> has no planar-GBR layout and calling it YUV444 would be semantically false.
/// </remarks>
public sealed class Vp9VideoDecoder : IVideoCodecDecoder<Vp9VideoDecoder> {

  private const string _MATROSKA_CODEC_ID = "V_VP9";

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("VP90"),
    CodecTag.FromCharacters("vp09"),
  ];

  private readonly Vp9Decoder _decoder = new();
  private readonly Queue<RawImage> _pending = new();

  public static string CodecName => "VP9 (profiles 0/1)";

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

    return (picture.SubsamplingX, picture.SubsamplingY) switch {
      (1, 1) => RawImageFactory.FromYuv420P8(
        picture.Width, picture.Height, picture.Luma, picture.LumaWidth, picture.Cb, picture.Cr,
        picture.ChromaWidth, colorInfo: colorInfo),
      (1, 0) => RawImageFactory.FromYuv422P8(
        picture.Width, picture.Height, picture.Luma, picture.LumaWidth, picture.Cb, picture.Cr,
        picture.ChromaWidth, colorInfo: colorInfo),
      (0, 1) => RawImageFactory.FromYuv440P8(
        picture.Width, picture.Height, picture.Luma, picture.LumaWidth, picture.Cb, picture.Cr,
        picture.ChromaWidth, colorInfo: colorInfo),
      (0, 0) => RawImageFactory.FromYuv444P8(
        picture.Width, picture.Height, picture.Luma, picture.LumaWidth, picture.Cb, picture.Cr,
        picture.ChromaWidth, colorInfo: colorInfo),
      _ => throw new InvalidOperationException(
        $"VP9 produced unsupported chroma subsampling ({picture.SubsamplingX}, {picture.SubsamplingY})."),
    };
  }

  private static RawImage _FromPlanarGbr(Vp9Frame picture) {
    if (picture.SubsamplingX != 0 || picture.SubsamplingY != 0)
      throw new InvalidOperationException("A VP9 sRGB frame must be 4:4:4.");

    var data = new byte[checked(picture.Width * picture.Height * 3)];
    for (var y = 0; y < picture.Height; ++y)
    for (var x = 0; x < picture.Width; ++x) {
      var source = y * picture.LumaWidth + x;
      var target = (y * picture.Width + x) * 3;
      data[target] = picture.Cr[source];
      data[target + 1] = picture.Luma[source];
      data[target + 2] = picture.Cb[source];
    }

    return new() {
      Width = picture.Width,
      Height = picture.Height,
      Format = PixelFormat.Rgb24,
      PixelData = data,
      ColorInfo = new() {
        Range = RawColorRange.Full,
        Primaries = RawColorPrimaries.Bt709,
        Transfer = RawTransferCharacteristic.Srgb,
        Matrix = RawMatrixCoefficients.Identity,
      },
    };
  }

  private static RawMatrixCoefficients _MatrixOf(int colorSpace) => colorSpace switch {
    2 => RawMatrixCoefficients.Bt709,
    3 => RawMatrixCoefficients.Bt601,
    4 => RawMatrixCoefficients.Smpte240M,
    5 => RawMatrixCoefficients.Bt2020NonConstantLuminance,
    CS_BT_601 => RawMatrixCoefficients.Bt601,
    _ => RawMatrixCoefficients.Unspecified,
  };
}
