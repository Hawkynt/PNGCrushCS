using System;

namespace FileFormat.Core;

/// <summary>
/// A bitmap of one bit per pixel with no header, palette or compression — the whole file is the
/// picture.
/// </summary>
/// <remarks>
/// Several scanner and printer formats are nothing more than this. What varies between them is
/// only the page size and which of the two colours a set bit means, so both are parameters here
/// rather than assumptions.
/// </remarks>
public static class MonochromePage {

  /// <summary>Bytes one row of a given width occupies.</summary>
  public static int BytesPerRow(int width) => (width + 7) >> 3;

  /// <summary>Unpacks a bitmap into a two-colour indexed image.</summary>
  /// <param name="inkIsWhite">
  /// Whether a set bit is white on black, as a scanner records it, rather than black on white paper.
  /// </param>
  /// <param name="palette">
  /// The two colours to draw in, or null for plain black and white. A machine whose brightest
  /// level is not white — the Atari's is 0xEE — needs to say so, and there is nowhere else to.
  /// </param>
  public static RawImage Decode(ReadOnlySpan<byte> data, int width, int height, bool inkIsWhite, ReadOnlySpan<byte> palette) {
    var decoded = Decode(data, width, height, inkIsWhite);

    return palette.Length < 6 ? decoded : new() {
      Width = decoded.Width,
      Height = decoded.Height,
      Format = decoded.Format,
      PixelData = decoded.PixelData,
      Palette = palette.ToArray(),
      PaletteCount = 2,
    };
  }

  public static RawImage Decode(ReadOnlySpan<byte> data, int width, int height, bool inkIsWhite) {
    var stride = BytesPerRow(width);
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = y * stride + (x >> 3);
      var b = index < data.Length ? data[index] : 0;
      pixels[y * width + x] = (byte)((b >> (~x & 7)) & 1);
    }

    // Index 0 is always the background; which colour that is depends on the format.
    byte[] palette = inkIsWhite ? [0, 0, 0, 255, 255, 255] : [255, 255, 255, 0, 0, 0];

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 2,
    };
  }

  /// <summary>Packs an image into a bitmap, setting a bit where the pixel is nearer the ink.</summary>
  public static byte[] Encode(RawImage image, int width, int height, bool inkIsWhite) {
    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var stride = BytesPerRow(width);
    var data = new byte[stride * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var pixel = (y * width + x) * 4;
      var brightness = bgra.PixelData[pixel] + bgra.PixelData[pixel + 1] + bgra.PixelData[pixel + 2];
      if (inkIsWhite ? brightness >= 384 : brightness < 384)
        data[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    }

    return data;
  }
}
