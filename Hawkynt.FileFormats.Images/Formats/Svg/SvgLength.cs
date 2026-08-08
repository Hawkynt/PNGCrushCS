using System;
using System.Globalization;
using FileFormat.Core.Vector;

namespace FileFormat.Svg;

/// <summary>Reads the numbers and lengths an SVG attribute is written with.</summary>
/// <remarks>
/// A length is a number and optionally a unit. The units that name a physical size — inches,
/// millimetres, points and the rest — are fixed multiples of the pixel by the specification, which
/// defines the pixel as a ninety-sixth of an inch, so every one of them reduces to a count of
/// pixels without anything being decided here.
/// </remarks>
public static class SvgLength {

  /// <summary>The specification's own conversions, all of them relative to the inch.</summary>
  private const double _PixelsPerInch = VectorViewport.DefaultDotsPerInch;
  private const double _PixelsPerPoint = _PixelsPerInch / VectorViewport.PointsPerInch;
  private const double _PixelsPerPica = _PixelsPerPoint * 12;
  private const double _PixelsPerMillimetre = _PixelsPerInch / VectorViewport.MillimetresPerInch;
  private const double _PixelsPerCentimetre = _PixelsPerMillimetre * 10;

  /// <summary>Reads a plain number, ignoring anything after it.</summary>
  public static bool TryNumber(string? text, out double value) {
    value = 0;
    if (string.IsNullOrWhiteSpace(text))
      return false;

    var span = text.AsSpan().Trim();
    var end = 0;
    while (end < span.Length && (char.IsAsciiDigit(span[end]) || span[end] is '.' or '-' or '+' or 'e' or 'E'))
      ++end;

    return end > 0 && double.TryParse(span[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
  }

  /// <summary>
  /// Reads a length in pixels, or nothing when the attribute is absent or is a percentage.
  /// </summary>
  /// <param name="percentageOf">
  /// What a percentage is a percentage of, for the cases where that is known. Pass zero where it is
  /// not and a percentage will be refused rather than measured against a made-up whole.
  /// </param>
  public static bool TryPixels(string? text, double percentageOf, out double pixels) {
    pixels = 0;
    if (!TryNumber(text, out var number))
      return false;

    var unit = text!.AsSpan().Trim();
    var digits = 0;
    while (digits < unit.Length && (char.IsAsciiDigit(unit[digits]) || unit[digits] is '.' or '-' or '+' or 'e' or 'E'))
      ++digits;

    var suffix = unit[digits..].Trim();
    if (suffix.IsEmpty || suffix.Equals("px", StringComparison.OrdinalIgnoreCase)) {
      pixels = number;
      return true;
    }

    if (suffix.Equals("%", StringComparison.Ordinal)) {
      if (percentageOf <= 0)
        return false;

      pixels = number / 100 * percentageOf;
      return true;
    }

    var factor = suffix switch {
      _ when suffix.Equals("in", StringComparison.OrdinalIgnoreCase) => _PixelsPerInch,
      _ when suffix.Equals("pt", StringComparison.OrdinalIgnoreCase) => _PixelsPerPoint,
      _ when suffix.Equals("pc", StringComparison.OrdinalIgnoreCase) => _PixelsPerPica,
      _ when suffix.Equals("mm", StringComparison.OrdinalIgnoreCase) => _PixelsPerMillimetre,
      _ when suffix.Equals("cm", StringComparison.OrdinalIgnoreCase) => _PixelsPerCentimetre,
      _ => double.NaN
    };

    if (double.IsNaN(factor))
      return false;

    pixels = number * factor;
    return true;
  }

  /// <summary>Reads a list of numbers separated by commas, spaces or both.</summary>
  public static double[] Numbers(string? text) {
    if (string.IsNullOrWhiteSpace(text))
      return [];

    var values = new System.Collections.Generic.List<double>();
    var span = text.AsSpan();
    var at = 0;

    while (at < span.Length) {
      while (at < span.Length && (char.IsWhiteSpace(span[at]) || span[at] == ','))
        ++at;

      if (at >= span.Length)
        break;

      var start = at;
      if (span[at] is '-' or '+')
        ++at;

      while (at < span.Length && (char.IsAsciiDigit(span[at]) || span[at] == '.'))
        ++at;

      if (at < span.Length && span[at] is 'e' or 'E') {
        ++at;
        if (at < span.Length && span[at] is '-' or '+')
          ++at;
        while (at < span.Length && char.IsAsciiDigit(span[at]))
          ++at;
      }

      if (at == start) {
        ++at;
        continue;
      }

      if (double.TryParse(span[start..at], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        values.Add(value);
    }

    return values.ToArray();
  }
}
