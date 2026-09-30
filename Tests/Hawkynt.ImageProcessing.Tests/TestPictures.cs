using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing.Tests;

/// <summary>Deterministic pictures with the features the operations react to.</summary>
internal static class TestPictures {

  /// <summary>
  /// Something shaped like a photograph: smooth gradients for the tone curves, a hard-edged disc
  /// and a bar pattern for the resamplers and edge filters to ring on, a saturated patch for the hue
  /// and saturation tests, and seeded noise so that no filter meets a region it can pass untouched.
  /// </summary>
  public static RawImage Photo(int width = 64, int height = 48, int low = 0, int high = 255) {
    var pixels = new byte[width * height * 3];
    var random = new Random(20260930);
    var span = high - low;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        double red = (double)x / (width - 1);
        double green = (double)y / (height - 1);
        var blue = 0.5 + 0.5 * Math.Sin(x * 0.3) * Math.Cos(y * 0.2);

        var dx = x - width * 0.35;
        var dy = y - height * 0.5;
        if (dx * dx + dy * dy < height * height * 0.06) {
          red = 0.9;
          green = 0.15;
          blue = 0.2;
        }

        if (x > width * 0.7 && (x / 2) % 2 == 0) {
          red = green = blue = 0.95;
        }

        var at = (y * width + x) * 3;
        pixels[at] = _Level(red, random, low, span);
        pixels[at + 1] = _Level(green, random, low, span);
        pixels[at + 2] = _Level(blue, random, low, span);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte _Level(double value, Random random, int low, int span) {
    var noisy = value + (random.NextDouble() - 0.5) * 0.08;
    noisy = Math.Clamp(noisy, 0.0, 1.0);
    return (byte)Math.Round(low + noisy * span);
  }
}
