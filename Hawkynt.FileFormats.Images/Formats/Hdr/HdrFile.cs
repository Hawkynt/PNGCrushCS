using System;
using FileFormat.Core;

namespace FileFormat.Hdr;

/// <summary>In-memory representation of a Radiance HDR image.</summary>
[FormatMagicBytes([0x23, 0x3F])]
[FormatMimeType("image/vnd.radiance", "image/x-hdr")]
public readonly record struct HdrFile : IImageFormatReader<HdrFile>, IImageToRawImage<HdrFile>, IImageFromRawImage<HdrFile>, IImageFormatWriter<HdrFile> {

  static string IImageFormatMetadata<HdrFile>.PrimaryExtension => ".hdr";
  static string[] IImageFormatMetadata<HdrFile>.FileExtensions => [".hdr", ".rgbe", ".xyze", ".rad"];
  static HdrFile IImageFormatReader<HdrFile>.FromSpan(ReadOnlySpan<byte> data) => HdrReader.FromSpan(data);
  static byte[] IImageFormatWriter<HdrFile>.ToBytes(HdrFile file) => HdrWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public float Exposure { get; init; }
  public float[] PixelData { get; init; }

  /// <summary>
  /// Converts this HDR image to a 16-bit <see cref="RawImage"/>, scaling linearly.
  /// </summary>
  /// <remarks>
  /// This used to put every sample through Reinhard tone mapping — v / (1 + v) — which is a creative
  /// curve, not a decode. It halves the middle of the range and has an asymptote at one, so a fully
  /// bright pixel came out at 128 of 255 and pure white could never be white. Neither ImageMagick nor
  /// ffmpeg does anything of the kind: both scale the linear value and clamp, and they agree with
  /// each other to the byte. A viewer that wants a tone curve can apply one; a decoder should hand
  /// over what the file says.
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
