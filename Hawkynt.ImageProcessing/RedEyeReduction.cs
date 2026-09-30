using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// Takes the red out of a flash-lit pupil, inside an area the caller marks and nowhere else.
/// </summary>
/// <remarks>
/// <para><b>Why only inside a marked area.</b> The rule below cannot tell a pupil from a red jumper,
/// a lip or a sunset — nothing that looks only at one pixel's colour can — and finding eyes is a
/// face-detection problem this assembly does not pretend to solve. So the caller supplies the area,
/// and outside it not a single component changes. Within it only the pixels are touched that are
/// both inside the ellipse the rectangle bounds (eyes are round; the corners of a box drawn around
/// one are skin) and red enough to pass the test.</para>
/// <para><b>The rule.</b> Each component is weighted — red by 0.5133333, green by 1, blue by
/// 0.1933333 — and a pixel counts as red-eye when its weighted red is at least its weighted green and
/// its weighted blue, each less an allowance the threshold sets. Such a pixel's red is replaced by
/// the mean of its weighted green and blue scaled back into red's terms, which keeps the pupil's
/// lightness and texture and removes only the cast. Green and blue are never changed.</para>
/// <para><b>Where the rule comes from.</b> This is the red-eye operation of GEGL (and so of GIMP),
/// which is licensed under the GPL and could not be taken as code; the rule was restated from it and
/// implemented from that statement, and the three weights are copied exactly, being constants rather
/// than expression. It works on the stored, gamma-encoded components, as the original does.</para>
/// </remarks>
public static class RedEyeReduction {

  private const double _RED_WEIGHT = 0.5133333;
  private const double _GREEN_WEIGHT = 1.0;
  private const double _BLUE_WEIGHT = 0.1933333;

  /// <summary>Reduces red-eye inside the ellipse bounded by a rectangle.</summary>
  /// <param name="image">The picture to retouch.</param>
  /// <param name="left">The rectangle's left edge, in pixels.</param>
  /// <param name="top">The rectangle's top edge, in pixels.</param>
  /// <param name="width">The rectangle's width, in pixels.</param>
  /// <param name="height">The rectangle's height, in pixels.</param>
  /// <param name="threshold">
  /// From 0 to 0.8. The default of 0.4 applies the rule as stated; higher values let pixels that are
  /// less clearly red through, lower values demand more red before anything changes.
  /// </param>
  /// <remarks>
  /// A palette picture has its colours changed pixel by pixel, which a shared palette entry cannot
  /// express, so it comes back as RGB. A grey picture has no red to remove and comes back unchanged.
  /// </remarks>
  public static RawImage Apply(RawImage image, int left, int top, int width, int height, double threshold = 0.4) {
    ArgumentNullException.ThrowIfNull(image);
    if (width < 1 || height < 1)
      throw new ArgumentOutOfRangeException(nameof(width), "The area has to be at least one pixel each way.");
    if (threshold is < 0.0 or > 0.8)
      throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "The threshold runs from 0 to 0.8.");

    var surface = PixelSurfaces.Read(image);
    if (surface.IsGrayscale)
      return image;

    var allowance = (threshold - 0.4) * 2.0;
    var centreX = left + width / 2.0;
    var centreY = top + height / 2.0;
    var radiusX = width / 2.0;
    var radiusY = height / 2.0;
    var samples = surface.Samples;
    var changed = false;

    int fromX = Math.Max(left, 0), toX = Math.Min(left + width, surface.Width);
    int fromY = Math.Max(top, 0), toY = Math.Min(top + height, surface.Height);

    for (var y = fromY; y < toY; ++y)
      for (var x = fromX; x < toX; ++x) {
        // Measured at the pixel's centre, so a one-pixel area selects its one pixel.
        var dx = (x + 0.5 - centreX) / radiusX;
        var dy = (y + 0.5 - centreY) / radiusY;
        if (dx * dx + dy * dy > 1.0)
          continue;

        var at = surface.Offset(x, y);
        var red = samples[at] * _RED_WEIGHT;
        var green = samples[at + 1] * _GREEN_WEIGHT;
        var blue = samples[at + 2] * _BLUE_WEIGHT;
        if (red < green - allowance || red < blue - allowance)
          continue;

        samples[at] = (float)Math.Clamp((green + blue) / (2.0 * _RED_WEIGHT), 0.0, 1.0);
        changed = true;
      }

    return changed ? PixelSurfaces.Write(surface, image) : image;
  }
}
