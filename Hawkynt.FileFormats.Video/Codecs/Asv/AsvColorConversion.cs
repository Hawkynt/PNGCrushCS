using System;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.Asv;

/// <summary>
/// Turns packed 8-bit RGB into the 4:2:0 planes both ASV codecs are coded over.
/// </summary>
/// <remarks>
/// The inverse of <c>H263ColorConversion.ToRgb24</c> as far as an inverse exists: the same ITU-R
/// BT.601 matrix and the same studio swing, and a chrominance plane made by averaging each
/// two-by-two square rather than by inverting that conversion's interpolation, which cannot be
/// inverted — four luminance positions share one chrominance sample and no choice of that sample
/// reproduces all four.
/// <para/>
/// The samples past the picture's own width and height are the edge repeated. A macroblock at the
/// right or bottom edge is coded whole whatever the picture size is — asv1.txt 3.1 has no way to say
/// a macroblock is partly outside — so those samples are transformed and transmitted like any
/// others, and repeating the edge is what makes them cost the fewest bits. Filling them with black
/// would put a hard edge inside the last macroblock of every row and spend coefficients on it.
/// </remarks>
internal static class AsvColorConversion {

  /// <summary>Fills a picture's planes, already sized to whole macroblocks, from packed RGB.</summary>
  /// <param name="rgb">The picture, three bytes a sample, top row first.</param>
  /// <param name="frame">The planes to fill.</param>
  /// <param name="width">The picture's displayed width.</param>
  /// <param name="height">Its displayed height.</param>
  internal static void FromRgb24(byte[] rgb, H263Frame frame, int width, int height) {
    ArgumentNullException.ThrowIfNull(rgb);
    ArgumentNullException.ThrowIfNull(frame);

    for (var y = 0; y < frame.LumaHeight; ++y) {
      var source = Math.Min(y, height - 1) * width * 3;
      var target = y * frame.LumaWidth;

      for (var x = 0; x < frame.LumaWidth; ++x) {
        var at = source + Math.Min(x, width - 1) * 3;
        frame.Luma[target + x] = (byte)_Luminance(rgb[at], rgb[at + 1], rgb[at + 2]);
      }
    }

    for (var y = 0; y < frame.ChromaHeight; ++y) {
      var target = y * frame.ChromaWidth;

      for (var x = 0; x < frame.ChromaWidth; ++x) {
        var blue = 0;
        var red = 0;

        for (var dy = 0; dy < 2; ++dy)
          for (var dx = 0; dx < 2; ++dx) {
            var at = Math.Min(2 * y + dy, height - 1) * width * 3 + Math.Min(2 * x + dx, width - 1) * 3;
            blue += _Blueness(rgb[at], rgb[at + 1], rgb[at + 2]);
            red += _Redness(rgb[at], rgb[at + 1], rgb[at + 2]);
          }

        frame.Cb[target + x] = (byte)((blue + 2) >> 2);
        frame.Cr[target + x] = (byte)((red + 2) >> 2);
      }
    }
  }

  /// <summary>ITU-R BT.601 luminance with studio swing, in 8-bit fixed point.</summary>
  private static int _Luminance(int red, int green, int blue)
    => _Clamp(((66 * red + 129 * green + 25 * blue + 128) >> 8) + 16);

  private static int _Blueness(int red, int green, int blue)
    => _Clamp(((-38 * red - 74 * green + 112 * blue + 128) >> 8) + 128);

  private static int _Redness(int red, int green, int blue)
    => _Clamp(((112 * red - 94 * green - 18 * blue + 128) >> 8) + 128);

  private static int _Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;
}
