using System;

namespace FileFormat.LuraDocument;

/// <summary>
/// What LuraTech's decoder does to a continuous-tone layer after its wavelet decode: the luma/colour-difference
/// mapping to RGB, and the integer bilinear enlargement to the page.
/// </summary>
/// <remarks>
/// A colour layer holds three bytes per pixel: luma, then a blue and a red difference centred on 127. The background
/// layer stores its luma inverted (255 minus luma), the foreground layer stores it as it is. The weights below are the
/// plug-in's own fixed-point constants, in units of 1/1024: 1163 (red from the red difference), 406 and 592 (green),
/// 2090 (blue from the blue difference); see <c>SPEC.md</c>.
/// </remarks>
internal static class LuraDocumentColour {

  /// <summary>Converts a decoded colour layer (luma, blue difference, red difference per pixel) to RGB in place.</summary>
  public static void ToRgb(byte[] samples, bool invertedLuma) {
    for (var i = 0; i + 2 < samples.Length; i += 3) {
      var y = (invertedLuma ? 255 - samples[i] : samples[i]) * 1024;
      var u = samples[i + 1] - 127;
      var v = samples[i + 2] - 127;
      samples[i] = _Clamp(_Shift(y + v * 1163));
      samples[i + 1] = _Clamp(_Shift(y - u * 406 - v * 592));
      samples[i + 2] = _Clamp(_Shift(y + u * 2090));
    }
  }

  /// <summary>Converts a decoded grey layer to grey levels in place.</summary>
  public static void ToGrey(byte[] samples, bool invertedLuma) {
    if (!invertedLuma)
      return;
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = (byte)(255 - samples[i]);
  }

  /// <summary>Repeats each grey level three times.</summary>
  public static byte[] GreyToRgb(byte[] grey) {
    var rgb = new byte[grey.Length * 3];
    for (var i = 0; i < grey.Length; ++i)
      rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = grey[i];
    return rgb;
  }

  /// <summary>
  /// The whole-number factor a layer is enlarged by: the page's height over the layer's when the layer is taller than
  /// wide, its width over the layer's otherwise, rounded up.
  /// </summary>
  public static int Factor(int pageWidth, int pageHeight, int layerWidth, int layerHeight)
    => layerWidth < layerHeight
      ? (pageHeight + layerHeight - 1) / layerHeight
      : (pageWidth + layerWidth - 1) / layerWidth;

  /// <summary>
  /// Enlarges a layer by an integer factor: layer pixel <c>(i, j)</c> lands on page pixel <c>(i·f, j·f)</c>, and the
  /// pixels between are interpolated along the row first and then down the column, each step truncated towards zero.
  /// Past the last layer pixel the last one repeats.
  /// </summary>
  public static byte[] Enlarge(byte[] layer, int width, int height, int channels, int factor, int pageWidth, int pageHeight) {
    var output = new byte[(long)pageWidth * pageHeight * channels];
    if (factor <= 0)
      factor = 1;

    var x0 = new int[pageWidth];
    var x1 = new int[pageWidth];
    var fx = new int[pageWidth];
    for (var x = 0; x < pageWidth; ++x) {
      x0[x] = Math.Min(x / factor, width - 1);
      x1[x] = Math.Min(x0[x] + 1, width - 1);
      fx[x] = x % factor;
    }

    for (var y = 0; y < pageHeight; ++y) {
      var r0 = Math.Min(y / factor, height - 1);
      var r1 = Math.Min(r0 + 1, height - 1);
      var fy = y % factor;
      var row0 = (long)r0 * width * channels;
      var row1 = (long)r1 * width * channels;
      var target = (long)y * pageWidth * channels;
      for (var x = 0; x < pageWidth; ++x) {
        var a = x0[x] * channels;
        var b = x1[x] * channels;
        for (var c = 0; c < channels; ++c) {
          var top = _Lerp(layer[row0 + a + c], layer[row0 + b + c], fx[x], factor);
          var bottom = _Lerp(layer[row1 + a + c], layer[row1 + b + c], fx[x], factor);
          output[target + x * channels + c] = (byte)_Lerp(top, bottom, fy, factor);
        }
      }
    }

    return output;
  }

  /// <summary>
  /// Picks the luma and colour differences whose decode comes closest to an RGB pixel: the plug-in's own forward
  /// transform as a start, then single steps in any of the three while they bring the decode nearer.
  /// </summary>
  public static (byte Y, byte U, byte V) FromRgb(int r, int g, int b) {
    var y = (g * 601 + r * 306 + b * 117) >> 10;
    var u = Math.Clamp(((b - y) * 504 + 127 * 1024) >> 10, 0, 255);
    var v = Math.Clamp(_Shift((r - y) * 898) + 127, 0, 255);
    var best = _Error(y, u, v, r, g, b);
    while (best > 0) {
      int by = y, bu = u, bv = v;
      for (var dy = -1; dy <= 1; ++dy)
      for (var du = -1; du <= 1; ++du)
      for (var dv = -1; dv <= 1; ++dv) {
        int cy = y + dy, cu = u + du, cv = v + dv;
        if ((uint)cy > 255 || (uint)cu > 255 || (uint)cv > 255)
          continue;
        var error = _Error(cy, cu, cv, r, g, b);
        if (error < best) {
          best = error;
          (by, bu, bv) = (cy, cu, cv);
        }
      }

      if (by == y && bu == u && bv == v)
        break;
      (y, u, v) = (by, bu, bv);
    }

    return ((byte)y, (byte)u, (byte)v);
  }

  private static int _Error(int y, int u, int v, int r, int g, int b) {
    var luma = y * 1024;
    u -= 127;
    v -= 127;
    var dr = _Clamp(_Shift(luma + v * 1163)) - r;
    var dg = _Clamp(_Shift(luma - u * 406 - v * 592)) - g;
    var db = _Clamp(_Shift(luma + u * 2090)) - b;
    return dr * dr + dg * dg + db * db;
  }

  private static int _Lerp(int from, int to, int step, int factor) => from + (to - from) * step / factor;

  /// <summary>Divides by 1024, truncating towards zero as the plug-in's signed arithmetic does.</summary>
  private static int _Shift(int value) => value / 1024;

  private static byte _Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
