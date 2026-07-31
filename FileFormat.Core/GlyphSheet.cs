using System;

namespace FileFormat.Core;

/// <summary>Sampling a picture down to the set-and-clear grid a character set stores.</summary>
/// <remarks>
/// A character set has one size and no other, so a picture of a different size is sampled to it
/// rather than refused. The sampling is nearest neighbour on purpose: a font is line art, and
/// averaging its edges only blurs the very thing the threshold afterwards has to guess back.
/// </remarks>
public static class GlyphSheet {

  /// <summary>Samples a picture to a fixed grid, one flag a pixel.</summary>
  /// <param name="image">The picture to sample.</param>
  /// <param name="width">Pixels across the sheet.</param>
  /// <param name="height">Rows down the sheet.</param>
  /// <param name="setWhenBright">
  /// Whether a bright pixel is the set one. Character sets differ on which way round they draw:
  /// a set bit is the light foreground on a machine that draws ink over a dark screen, and the
  /// dark ink on one that draws over paper.
  /// </param>
  public static bool[] Sample(RawImage image, int width, int height, bool setWhenBright = true) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var flags = new bool[width * height];

    for (var y = 0; y < height; ++y) {
      var sourceY = image.Height == height ? y : y * image.Height / height;

      for (var x = 0; x < width; ++x) {
        var sourceX = image.Width == width ? x : x * image.Width / width;
        var source = (sourceY * image.Width + sourceX) * 3;

        var luminance = rgb.PixelData[source] * 77
                        + rgb.PixelData[source + 1] * 150
                        + rgb.PixelData[source + 2] * 29;

        flags[y * width + x] = luminance >= 128 * 256 == setWhenBright;
      }
    }

    return flags;
  }
}
