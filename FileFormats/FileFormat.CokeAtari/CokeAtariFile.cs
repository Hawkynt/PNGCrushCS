using System;
using FileFormat.Core;

namespace FileFormat.CokeAtari;

/// <summary>In-memory representation of a COKE Atari Falcon 16-bit true color (.tg1) image.</summary>
public readonly record struct CokeAtariFile : IImageFormatReader<CokeAtariFile>, IImageToRawImage<CokeAtariFile>, IImageFromRawImage<CokeAtariFile>, IImageFormatWriter<CokeAtariFile> {

  static string IImageFormatMetadata<CokeAtariFile>.PrimaryExtension => ".tg1";
  static string[] IImageFormatMetadata<CokeAtariFile>.FileExtensions => [".tg1"];
  static CokeAtariFile IImageFormatReader<CokeAtariFile>.FromSpan(ReadOnlySpan<byte> data) => CokeAtariReader.FromSpan(data);
  static byte[] IImageFormatWriter<CokeAtariFile>.ToBytes(CokeAtariFile file) => CokeAtariWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(CokeAtariFile file) {

    var rgb565 = file.PixelData;
    var pixelCount = file.Width * file.Height;
    var rgb24 = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 2;
      var hi = srcOffset < rgb565.Length ? rgb565[srcOffset] : (byte)0;
      var lo = srcOffset + 1 < rgb565.Length ? rgb565[srcOffset + 1] : (byte)0;
      var packed = (ushort)((hi << 8) | lo);

      var r5 = (packed >> 11) & 0x1F;
      var g6 = (packed >> 5) & 0x3F;
      var b5 = packed & 0x1F;

      var dstOffset = i * 3;
      rgb24[dstOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[dstOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[dstOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };
  }

  public static CokeAtariFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var pixelCount = image.Width * image.Height;
    var rgb565 = new byte[pixelCount * 2];

    if (image.Format == PixelFormat.Indexed1 || image.Format == PixelFormat.Indexed8) {
      // Indexed inputs: resolve each pixel via the palette, then quantize to RGB565.
      var palette = image.Palette ?? throw new ArgumentException("Indexed input requires a palette.", nameof(image));
      var paletteCount = image.PaletteCount;

      if (image.Format == PixelFormat.Indexed1) {
        var stride = (image.Width + 7) / 8;
        for (var y = 0; y < image.Height; ++y)
          for (var x = 0; x < image.Width; ++x) {
            var b = image.PixelData[y * stride + (x >> 3)];
            var idx = (b >> (7 - (x & 7))) & 1;
            _WriteRgb565(rgb565, y * image.Width + x, palette, idx, paletteCount);
          }
      } else {
        for (var i = 0; i < pixelCount; ++i)
          _WriteRgb565(rgb565, i, palette, image.PixelData[i], paletteCount);
      }
    } else if (image.Format == PixelFormat.Rgb24) {
      var rgb24 = image.PixelData;
      for (var i = 0; i < pixelCount; ++i) {
        var srcOffset = i * 3;
        var r = rgb24[srcOffset];
        var g = rgb24[srcOffset + 1];
        var b = rgb24[srcOffset + 2];

        var r5 = (r >> 3) & 0x1F;
        var g6 = (g >> 2) & 0x3F;
        var b5 = (b >> 3) & 0x1F;
        var packed = (ushort)((r5 << 11) | (g6 << 5) | b5);

        var dstOffset = i * 2;
        rgb565[dstOffset] = (byte)(packed >> 8);
        rgb565[dstOffset + 1] = (byte)(packed & 0xFF);
      }
    } else {
      throw new ArgumentException($"Expected {PixelFormat.Rgb24}, {PixelFormat.Indexed1}, or {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = rgb565,
    };
  }

  private static void _WriteRgb565(byte[] dst, int pixelIndex, byte[] palette, int paletteIdx, int paletteCount) {
    if (paletteIdx >= paletteCount) paletteIdx = 0;
    var r = palette[paletteIdx * 3];
    var g = palette[paletteIdx * 3 + 1];
    var b = palette[paletteIdx * 3 + 2];
    var r5 = (r >> 3) & 0x1F;
    var g6 = (g >> 2) & 0x3F;
    var b5 = (b >> 3) & 0x1F;
    var packed = (ushort)((r5 << 11) | (g6 << 5) | b5);
    var dstOffset = pixelIndex * 2;
    dst[dstOffset] = (byte)(packed >> 8);
    dst[dstOffset + 1] = (byte)(packed & 0xFF);
  }
}
