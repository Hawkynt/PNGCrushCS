using System;
using FileFormat.Core;

namespace FileFormat.XvThumbnail;

/// <summary>In-memory representation of an XV thumbnail image (P7 332 format).</summary>
[FormatDetectionPriority(90)]
[FormatMagicBytes([0x50, 0x37, 0x20, 0x33, 0x33, 0x32])]
public readonly record struct XvThumbnailFile : IImageFormatReader<XvThumbnailFile>, IImageToRawImage<XvThumbnailFile>, IImageFromRawImage<XvThumbnailFile>, IImageFormatWriter<XvThumbnailFile> {

  static string IImageFormatMetadata<XvThumbnailFile>.PrimaryExtension => ".xv";
  static string[] IImageFormatMetadata<XvThumbnailFile>.FileExtensions => [".xv"];
  static XvThumbnailFile IImageFormatReader<XvThumbnailFile>.FromSpan(ReadOnlySpan<byte> data) => XvThumbnailReader.FromSpan(data);
  static byte[] IImageFormatWriter<XvThumbnailFile>.ToBytes(XvThumbnailFile file) => XvThumbnailWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw 3-3-2 packed pixel data (1 byte per pixel).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts an XV thumbnail to a platform-independent RGB24 image.</summary>
  public static RawImage ToRawImage(XvThumbnailFile file) {

    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var packed = i < file.PixelData.Length ? file.PixelData[i] : (byte)0;
      var r = (packed >> 5) & 0x07;
      var g = (packed >> 2) & 0x07;
      var b = packed & 0x03;
      rgb[i * 3] = (byte)(r * 255 / 7);
      rgb[i * 3 + 1] = (byte)(g * 255 / 7);
      rgb[i * 3 + 2] = (byte)(b * 255 / 3);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates an XV thumbnail from a platform-independent RGB24, Indexed1, or Indexed8 image.</summary>
  public static XvThumbnailFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Indexed8, PixelFormat.Indexed1);

    var pixelCount = image.Width * image.Height;
    var packed = new byte[pixelCount];

    if (image.Format == PixelFormat.Indexed1 || image.Format == PixelFormat.Indexed8) {
      var palette = image.Palette ?? throw new ArgumentException("Indexed input requires a palette.", nameof(image));
      var paletteCount = image.PaletteCount;
      if (image.Format == PixelFormat.Indexed1) {
        var stride = (image.Width + 7) / 8;
        for (var y = 0; y < image.Height; ++y)
          for (var x = 0; x < image.Width; ++x) {
            var b = image.PixelData[y * stride + (x >> 3)];
            var idx = (b >> (7 - (x & 7))) & 1;
            packed[y * image.Width + x] = _PackRgb332(palette, idx, paletteCount);
          }
      } else {
        for (var i = 0; i < pixelCount; ++i)
          packed[i] = _PackRgb332(palette, image.PixelData[i], paletteCount);
      }
    } else if (image.Format == PixelFormat.Rgb24) {
      for (var i = 0; i < pixelCount; ++i) {
        var r = image.PixelData[i * 3];
        var g = image.PixelData[i * 3 + 1];
        var b = image.PixelData[i * 3 + 2];

        // Refuse inputs that won't survive RGB332 quantization with reader's expand (val * 255 / N).
        var r3 = (r * 7 + 127) / 255;
        var g3 = (g * 7 + 127) / 255;
        var b2 = (b * 3 + 127) / 255;
        if ((byte)(r3 * 255 / 7) != r || (byte)(g3 * 255 / 7) != g || (byte)(b2 * 255 / 3) != b)
          throw new ArgumentException($"Rgb24 input at pixel {i} cannot be losslessly encoded as RGB332; use Indexed1/Indexed8 input with a compatible palette.", nameof(image));

        packed[i] = (byte)((r3 << 5) | (g3 << 2) | b2);
      }
    } else {
      throw new ArgumentException($"Expected {PixelFormat.Rgb24}, {PixelFormat.Indexed1}, or {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = packed,
    };
  }

  private static byte _PackRgb332(byte[] palette, int idx, int paletteCount) {
    if (idx >= paletteCount) idx = 0;
    var r = palette[idx * 3];
    var g = palette[idx * 3 + 1];
    var b = palette[idx * 3 + 2];
    var r3 = (r * 7 + 127) / 255;
    var g3 = (g * 7 + 127) / 255;
    var b2 = (b * 3 + 127) / 255;
    return (byte)((r3 << 5) | (g3 << 2) | b2);
  }
}
