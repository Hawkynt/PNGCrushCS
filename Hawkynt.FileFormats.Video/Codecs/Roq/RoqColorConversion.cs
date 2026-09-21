using System;
using FileFormat.Core;

namespace FileFormat.Codecs.Roq;

/// <summary>Converts reconstructed RoQ sample planes to and from interleaved colour.</summary>
/// <remarks>
/// RoQ's samples are full range rather than studio-range YCbCr. Chroma is already reconstructed at
/// full pixel resolution before conversion; there is no chroma-siting step here. The optional alpha
/// plane belongs to the older Trilobyte RoQ variant and is copied verbatim rather than transformed.
/// </remarks>
internal static class RoqColorConversion {

  internal static RawImage ToRawImage(RoqFrame frame) {
    var rgb = _ToRgb(frame);
    if (!frame.HasAlpha)
      return new() { Width = frame.Width, Height = frame.Height, Format = PixelFormat.Rgb24, PixelData = rgb };

    var rgba = new byte[frame.Width * frame.Height * 4];
    for (int i = 0, r = 0, o = 0; i < frame.A.Length; ++i, r += 3, o += 4) {
      rgba[o] = rgb[r];
      rgba[o + 1] = rgb[r + 1];
      rgba[o + 2] = rgb[r + 2];
      rgba[o + 3] = frame.A[i];
    }

    return new() { Width = frame.Width, Height = frame.Height, Format = PixelFormat.Rgba32, PixelData = rgba };
  }

  internal static byte[] ToRgb24(RoqFrame frame) => _ToRgb(frame);

  private static byte[] _ToRgb(RoqFrame frame) {
    var pixels = new byte[frame.Width * frame.Height * 3];
    var y = frame.Y;
    var cb = frame.Cb;
    var cr = frame.Cr;

    for (int i = 0, o = 0; i < y.Length; ++i, o += 3) {
      double luma = y[i];
      double blueChroma = cb[i] - 128;
      double redChroma = cr[i] - 128;

      pixels[o] = _Clamp(luma + 1.402 * redChroma);
      pixels[o + 1] = _Clamp(luma - 0.344136 * blueChroma - 0.714136 * redChroma);
      pixels[o + 2] = _Clamp(luma + 1.772 * blueChroma);
    }

    return pixels;
  }

  /// <summary>Fills a frame's colour planes from interleaved RGB.</summary>
  internal static void FromRgb24(ReadOnlySpan<byte> pixels, RoqFrame frame) {
    var expected = checked(frame.Width * frame.Height * 3);
    if (pixels.Length < expected)
      throw new ArgumentException($"RGB input contains {pixels.Length} bytes; {expected} are required.", nameof(pixels));

    var y = frame.Y;
    var cb = frame.Cb;
    var cr = frame.Cr;

    for (int i = 0, o = 0; i < y.Length; ++i, o += 3) {
      double red = pixels[o];
      double green = pixels[o + 1];
      double blue = pixels[o + 2];

      y[i] = _Clamp(0.299 * red + 0.587 * green + 0.114 * blue);
      cb[i] = _Clamp(128 - 0.168736 * red - 0.331264 * green + 0.5 * blue);
      cr[i] = _Clamp(128 + 0.5 * red - 0.418688 * green - 0.081312 * blue);
    }
  }

  private static byte _Clamp(double value) {
    var rounded = Math.Round(value, MidpointRounding.ToEven);
    return rounded switch {
      <= 0 => 0,
      >= 255 => 255,
      _ => (byte)rounded,
    };
  }
}
