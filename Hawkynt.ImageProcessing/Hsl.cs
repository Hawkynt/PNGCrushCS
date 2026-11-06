using System;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// Hue, saturation and lightness, in the exact form the operations that modulate them use.
/// </summary>
/// <remarks>
/// There is more than one HSL in circulation, and the differences are not cosmetic: whether
/// saturation is divided by lightness or by chroma's maximum, whether hue is degrees or a turn,
/// whether the sector is picked by comparing against the maximum or by an ordering. Two of those
/// choices disagree about grey and one disagrees about the primaries, so the conversion is pinned
/// to the one <c>-modulate</c> uses rather than to a general idea of HSL.
/// <para/>
/// Hue is a turn in <c>[0,1)</c>, not degrees, which is what makes a hue shift an addition. It is
/// deliberately not wrapped on the way out of <see cref="FromRgb"/> or into <see cref="ToRgb"/>:
/// <see cref="ToRgb"/> reduces it modulo a full turn itself, so a caller may add any offset,
/// positive or negative, without first working out where it lands.
/// </remarks>
internal static class Hsl {

  /// <summary>How close two component values have to be to count as equal when picking a sector.</summary>
  private const double _EPSILON = 1.0e-15;

  /// <summary>Splits a colour into hue, saturation and lightness.</summary>
  public static (double Hue, double Saturation, double Lightness) FromRgb(double red, double green, double blue) {
    var max = Math.Max(red, Math.Max(green, blue));
    var min = Math.Min(red, Math.Min(green, blue));
    var chroma = max - min;
    var lightness = (max + min) / 2.0;

    if (chroma <= 0.0)
      return (0.0, 0.0, lightness);

    double hue;
    if (Math.Abs(max - red) < _EPSILON) {
      hue = (green - blue) / chroma;
      if (green < blue)
        hue += 6.0;
    } else if (Math.Abs(max - green) < _EPSILON)
      hue = 2.0 + (blue - red) / chroma;
    else
      hue = 4.0 + (red - green) / chroma;

    hue *= 60.0 / 360.0;
    var saturation = lightness <= 0.5 ? chroma / (2.0 * lightness) : chroma / (2.0 - 2.0 * lightness);
    return (hue, saturation, lightness);
  }

  /// <summary>Puts hue, saturation and lightness back together as a colour.</summary>
  /// <remarks>
  /// Saturation and lightness are used as given, so a lightness pushed past one or a saturation
  /// pushed past its own ceiling produces components outside <c>[0,1]</c>. Clamping belongs on the
  /// way out to storage, where one clamp covers every operation, rather than here where it would
  /// silently change the colour an intermediate step asked for.
  /// </remarks>
  public static (double Red, double Green, double Blue) ToRgb(double hue, double saturation, double lightness) {
    var chroma = lightness <= 0.5 ? 2.0 * lightness * saturation : (2.0 - 2.0 * lightness) * saturation;
    var min = lightness - 0.5 * chroma;
    var h = hue * 360.0;
    h -= 360.0 * Math.Floor(h / 360.0);
    h /= 60.0;
    var x = chroma * (1.0 - Math.Abs(h - 2.0 * Math.Floor(h / 2.0) - 1.0));

    return (int)Math.Floor(h) switch {
      1 => (min + x, min + chroma, min),
      2 => (min, min + chroma, min + x),
      3 => (min, min + x, min + chroma),
      4 => (min + x, min, min + chroma),
      5 => (min + chroma, min, min + x),
      _ => (min + chroma, min + x, min),
    };
  }
}
