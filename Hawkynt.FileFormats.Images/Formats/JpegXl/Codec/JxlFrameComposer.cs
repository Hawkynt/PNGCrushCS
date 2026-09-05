using System;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Putting a file's frames together into the picture (libjxl
/// <c>lib/jxl/blending.cc</c>).
/// </summary>
/// <remarks>
/// A JPEG XL file is not always one frame. A frame may cover only part of the
/// picture, may be drawn over an earlier one rather than replacing it, and may
/// be kept aside for a later frame to draw over. What a viewer shows is the
/// last frame after all of that has happened, so reading the first frame and
/// stopping gives a picture nobody encoded — which is what this reader used to
/// do before it started refusing them.
///
/// <para>Blending is in float, over planes that hold the colour channels
/// followed by the extra ones, because the alpha a frame blends by is one of
/// those extra channels and it has to be read at full precision before the
/// picture is rounded.</para>
/// </remarks>
internal static class JxlFrameComposer {

  /// <summary>libjxl <c>BlendMode</c>.</summary>
  public const uint ModeReplace = 0;
  public const uint ModeAdd = 1;
  public const uint ModeBlend = 2;
  public const uint ModeAlphaWeightedAdd = 3;
  public const uint ModeMultiply = 4;

  /// <summary>
  /// Draw one frame over a background and return the result.
  /// </summary>
  /// <param name="background">The frame being drawn over, at picture size, one
  /// plane per channel. Null means nothing has been drawn yet.</param>
  /// <param name="foreground">The frame being drawn, at its own size.</param>
  /// <param name="planeCount">Colour channels plus extra ones.</param>
  /// <param name="imageWidth">Picture width.</param>
  /// <param name="imageHeight">Picture height.</param>
  /// <param name="frameWidth">The frame's own width.</param>
  /// <param name="frameHeight">The frame's own height.</param>
  /// <param name="originX">Where the frame's left edge sits in the picture.</param>
  /// <param name="originY">Where its top edge sits.</param>
  /// <param name="mode">How the two are combined.</param>
  /// <param name="alphaPlane">Which plane carries the alpha, or -1 for none.</param>
  /// <param name="clamp">Whether that alpha is clamped to 0..1 first.</param>
  /// <param name="premultiplied">Whether the colour channels already carry the
  /// alpha, which changes the formula rather than only scaling it.</param>
  public static float[][] Compose(
    float[][]? background,
    float[][] foreground,
    int planeCount,
    int imageWidth,
    int imageHeight,
    int frameWidth,
    int frameHeight,
    int originX,
    int originY,
    uint mode,
    int alphaPlane,
    bool clamp,
    bool premultiplied
  ) {
    ArgumentNullException.ThrowIfNull(foreground);
    if (planeCount < 3)
      throw new ArgumentOutOfRangeException(nameof(planeCount), "A frame carries at least three planes.");

    var count = checked(imageWidth * imageHeight);
    var result = new float[planeCount][];
    for (var p = 0; p < planeCount; ++p) {
      result[p] = new float[count];
      // Everything outside the frame's own rectangle keeps what was under it.
      if (background != null && p < background.Length && background[p].Length >= count)
        Array.Copy(background[p], result[p], count);
    }

    // Where the frame lands in the picture, clipped to it.
    var x0 = Math.Max(originX, 0);
    var y0 = Math.Max(originY, 0);
    var x1 = Math.Min(originX + frameWidth, imageWidth);
    var y1 = Math.Min(originY + frameHeight, imageHeight);
    if (x1 <= x0 || y1 <= y0)
      return result;

    var hasAlpha = alphaPlane >= 3 && alphaPlane < planeCount;

    for (var y = y0; y < y1; ++y)
    for (var x = x0; x < x1; ++x) {
      var to = y * imageWidth + x;
      var from = (y - originY) * frameWidth + (x - originX);

      switch (mode) {
        case ModeReplace:
          for (var p = 0; p < planeCount; ++p)
            result[p][to] = _At(foreground, p, from);
          break;

        case ModeAdd:
          for (var p = 0; p < planeCount; ++p)
            result[p][to] = _At(background, p, to) + _At(foreground, p, from);
          break;

        case ModeMultiply:
          for (var p = 0; p < planeCount; ++p) {
            var f = _At(foreground, p, from);
            if (clamp)
              f = Math.Clamp(f, 0.0f, 1.0f);
            result[p][to] = _At(background, p, to) * f;
          }

          break;

        case ModeBlend:
          if (!hasAlpha) {
            // With nothing to blend by, libjxl takes the frame as it stands.
            for (var p = 0; p < planeCount; ++p)
              result[p][to] = _At(foreground, p, from);
            break;
          }

          _Blend(background, foreground, result, planeCount, to, from, alphaPlane, clamp, premultiplied);
          break;

        case ModeAlphaWeightedAdd:
          if (!hasAlpha) {
            for (var p = 0; p < planeCount; ++p)
              result[p][to] = _At(background, p, to) + _At(foreground, p, from);
            break;
          }

          {
            var fa = _At(foreground, alphaPlane, from);
            if (clamp)
              fa = Math.Clamp(fa, 0.0f, 1.0f);
            for (var p = 0; p < 3; ++p)
              result[p][to] = _At(background, p, to) + _At(foreground, p, from) * fa;
            for (var p = 3; p < planeCount; ++p)
              result[p][to] = _At(background, p, to) + _At(foreground, p, from) * fa;
          }

          break;

        default:
          throw new InvalidDataException($"A frame states blend mode {mode}, which the format does not define.");
      }
    }

    return result;
  }

  /// <summary>
  /// Alpha compositing, libjxl <c>PerformAlphaBlending</c>. The extra channels
  /// are done with the alpha as it was before blending, which is why the colour
  /// planes are written after them here rather than in one pass.
  /// </summary>
  private static void _Blend(
    float[][]? background, float[][] foreground, float[][] result,
    int planeCount, int to, int from, int alphaPlane, bool clamp, bool premultiplied
  ) {
    var fa = _At(foreground, alphaPlane, from);
    if (clamp)
      fa = Math.Clamp(fa, 0.0f, 1.0f);
    var ba = _At(background, alphaPlane, to);
    var newAlpha = 1.0f - (1.0f - fa) * (1.0f - ba);

    for (var p = 3; p < planeCount; ++p) {
      if (p == alphaPlane)
        continue;

      var f = _At(foreground, p, from);
      var b = _At(background, p, to);
      result[p][to] = premultiplied
        ? f + b * (1.0f - fa)
        : _Unpremultiply(f * fa + b * ba * (1.0f - fa), newAlpha);
    }

    for (var p = 0; p < 3; ++p) {
      var f = _At(foreground, p, from);
      var b = _At(background, p, to);
      result[p][to] = premultiplied
        ? f + b * (1.0f - fa)
        : _Unpremultiply(f * fa + b * ba * (1.0f - fa), newAlpha);
    }

    result[alphaPlane][to] = newAlpha;
  }

  private static float _Unpremultiply(float value, float alpha) => alpha > 0.0f ? value / alpha : 0.0f;

  private static float _At(float[][]? planes, int plane, int index) {
    if (planes == null || plane >= planes.Length)
      return 0.0f;
    var row = planes[plane];
    return index < row.Length ? row[index] : 0.0f;
  }
}
