using FileFormat.Core;

namespace FileFormat.BilevelFax;

/// <summary>
/// The page a fax machine puts on paper: one bit per pixel, each row starting on a fresh byte, the
/// leftmost pixel in the top bit, and a set bit meaning ink.
/// </summary>
/// <remarks>
/// Two dozen of the formats here are a header and a compressed run in front of exactly this. What
/// differs between them is what the header says and how the run is packed; once it is expanded the
/// picture is the same picture, drawn the same way, so it is drawn in one place. Twenty-three copies
/// of one loop are twenty-three places for a correction to reach twenty-two of.
/// </remarks>
internal static class FaxPage {

  /// <summary>Draws the page as an <see cref="RawImage"/> in Rgb24, black ink on white paper.</summary>
  /// <param name="width">Pixels across, as the format's own header states it.</param>
  /// <param name="height">Rows down.</param>
  /// <param name="pixelData">The expanded bitmap, rows padded to a byte boundary.</param>
  public static RawImage ToRawImage(int width, int height, byte[] pixelData) {

    var bytesPerRow = (width + 7) / 8;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * bytesPerRow + x / 8;
        var bitIndex = 7 - (x % 8);
        var bit = (pixelData[byteIndex] >> bitIndex) & 1;
        var offset = (y * width + x) * 3;
        var color = bit == 1 ? (byte)0 : (byte)255;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }
}
