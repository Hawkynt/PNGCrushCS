using System;
using FileFormat.Core;

namespace FileFormat.Hdr;

/// <summary>In-memory representation of a Radiance HDR image.</summary>
[FormatMagicBytes([0x23, 0x3F])]
[FormatMimeType("image/vnd.radiance", "image/x-hdr")]
public readonly record struct HdrFile : IImageFormatReader<HdrFile>, IImageToRawImage<HdrFile>, IImageFromRawImage<HdrFile>, IImageFormatWriter<HdrFile> {

  static string IImageFormatMetadata<HdrFile>.PrimaryExtension => ".hdr";

  /// <summary><c>.hdri</c> is the same Radiance file under a longer name.</summary>
  /// <remarks>
  /// XnView lists <c>hdri</c> and <c>rad</c> as two names, and the second reads <c>.rad</c>, which
  /// is claimed here already. Both are Radiance RGBE, and the header decides rather than the name,
  /// so a foreign file under either is refused rather than read.
  /// <para/>
  /// The header is not always the <c>#?</c> line: nconvert's <c>.rad</c> opens with
  /// <c>FORMAT=32-bit_rle_rgbe</c> and never writes one, which is why
  /// <see cref="HdrHeaderParser.HasRadianceHeader"/> accepts either opening.
  /// </remarks>
  static string[] IImageFormatMetadata<HdrFile>.FileExtensions => [".hdr", ".hdri", ".rgbe", ".xyze", ".rad"];
  static HdrFile IImageFormatReader<HdrFile>.FromSpan(ReadOnlySpan<byte> data) => HdrReader.FromSpan(data);
  static byte[] IImageFormatWriter<HdrFile>.ToBytes(HdrFile file) => HdrWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public float Exposure { get; init; }
  public float[] PixelData { get; init; }

  /// <summary>Converts this HDR image to a 16-bit <see cref="RawImage"/>, scaled by the file's exposure.</summary>
  /// <remarks>
  /// This used to put a Reinhard curve over the result, which halves everything at full scale and
  /// darkens the rest by varying amounts — a picture that should have come out fully blue came out
  /// half blue. Tone mapping is a decision about how to show a high range on a low one, and it is
  /// not the decoder's to make: what the file holds is radiance, and anything above the range is
  /// clipped rather than rolled off. A caller that wants a curve can apply one to the result.
  /// </remarks>
  public static RawImage ToRawImage(HdrFile file) {
    var width = file.Width;
    var height = file.Height;
    var exposure = file.Exposure;
    var src = file.PixelData;
    var pixelCount = width * height;
    var result = new byte[pixelCount * 6];
    for (var i = 0; i < pixelCount; ++i) {
      var si = i * 3;
      var di = i * 6;
      for (var c = 0; c < 3; ++c) {
        var v = Math.Max(src[si + c] * exposure, 0f);
        var u16 = (ushort)Math.Clamp(v * 65535f, 0, 65535);
        result[di + c * 2] = (byte)(u16 >> 8);
        result[di + c * 2 + 1] = (byte)u16;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb48,
      PixelData = result,
    };
  }

  /// <summary>Creates an <see cref="HdrFile"/> from a <see cref="RawImage"/>. Accepts Rgb48 (lossless) or any format convertible to Rgb48.</summary>
  public static HdrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb48 = PixelConverter.Convert(image, PixelFormat.Rgb48);
    var width = rgb48.Width;
    var height = rgb48.Height;
    var src = rgb48.PixelData;
    var pixelCount = width * height;
    var floats = new float[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var si = i * 6;
      var di = i * 3;
      for (var c = 0; c < 3; ++c) {
        var u16 = (src[si + c * 2] << 8) | src[si + c * 2 + 1];
        floats[di + c] = u16 / 65535.0f;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Exposure = 1.0f,
      PixelData = floats,
    };
  }
}
