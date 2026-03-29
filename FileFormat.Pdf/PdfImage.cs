using System;
using FileFormat.Core;

namespace FileFormat.Pdf;

/// <summary>A single raster image extracted from a PDF file.</summary>
public sealed class PdfImage {

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per color component (typically 8).</summary>
  public int BitsPerComponent { get; init; } = 8;

  /// <summary>The color space used by this image.</summary>
  public PdfColorSpace ColorSpace { get; init; } = PdfColorSpace.DeviceRGB;

  /// <summary>Decoded pixel data in the color space's native layout.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this PDF image to a platform-independent <see cref="RawImage"/>.</summary>
  internal RawImage ToRawImage() {
    var format = ColorSpace switch {
      PdfColorSpace.DeviceGray => PixelFormat.Gray8,
      PdfColorSpace.DeviceCMYK => PixelFormat.Rgb24, // CMYK is converted during extraction
      _ => PixelFormat.Rgb24,
    };

    var bpp = format == PixelFormat.Gray8 ? 1 : 3;
    var expectedSize = Width * Height * bpp;
    var pixels = new byte[expectedSize];

    if (ColorSpace == PdfColorSpace.DeviceCMYK) {
      // Convert CMYK to RGB
      var cmykSize = Width * Height * 4;
      var srcLen = Math.Min(PixelData.Length, cmykSize);
      for (int s = 0, d = 0; s + 3 < srcLen && d + 2 < expectedSize; s += 4, d += 3) {
        var c = PixelData[s];
        var m = PixelData[s + 1];
        var y = PixelData[s + 2];
        var k = PixelData[s + 3];
        pixels[d] = (byte)(255 - Math.Min(255, c + k));
        pixels[d + 1] = (byte)(255 - Math.Min(255, m + k));
        pixels[d + 2] = (byte)(255 - Math.Min(255, y + k));
      }
    } else
      PixelData.AsSpan(0, Math.Min(PixelData.Length, expectedSize)).CopyTo(pixels);

    return new RawImage { Width = Width, Height = Height, Format = format, PixelData = pixels };
  }
}
