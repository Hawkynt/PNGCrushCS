using System;
using FileFormat.Core.Vector;

namespace FileFormat.Svg;

/// <summary>Reads the transform list an element can carry.</summary>
/// <remarks>
/// A list of named transforms applied left to right, so the leftmost is the outermost. Angles are
/// in degrees and the rotate form may name a centre, which is the same as moving to it, turning,
/// and moving back.
/// </remarks>
public static class SvgTransform {

  private const double _DegreesToRadians = Math.PI / 180;

  /// <summary>The whole list as one matrix, or the identity when there is nothing to read.</summary>
  public static Matrix2D Parse(string? text) {
    if (string.IsNullOrWhiteSpace(text))
      return Matrix2D.Identity;

    var result = Matrix2D.Identity;
    var at = 0;

    while (at < text.Length) {
      while (at < text.Length && (char.IsWhiteSpace(text[at]) || text[at] == ','))
        ++at;

      var nameStart = at;
      while (at < text.Length && (char.IsAsciiLetter(text[at])))
        ++at;

      if (at == nameStart)
        break;

      var name = text.AsSpan(nameStart, at - nameStart);
      var open = text.IndexOf('(', at);
      if (open < 0)
        break;

      var close = text.IndexOf(')', open);
      if (close < 0)
        break;

      var values = SvgLength.Numbers(text[(open + 1)..close]);
      at = close + 1;

      var step = _Build(name, values);
      if (step.HasValue)
        result = step.Value.Then(result);
    }

    return result;
  }

  private static Matrix2D? _Build(ReadOnlySpan<char> name, double[] values) {
    if (name.Equals("matrix", StringComparison.OrdinalIgnoreCase))
      return values.Length >= 6 ? new Matrix2D(values[0], values[1], values[2], values[3], values[4], values[5]) : null;

    if (name.Equals("translate", StringComparison.OrdinalIgnoreCase))
      return values.Length >= 1 ? Matrix2D.Translation(values[0], values.Length > 1 ? values[1] : 0) : null;

    if (name.Equals("scale", StringComparison.OrdinalIgnoreCase))
      return values.Length >= 1 ? Matrix2D.Scaling(values[0], values.Length > 1 ? values[1] : values[0]) : null;

    if (name.Equals("rotate", StringComparison.OrdinalIgnoreCase)) {
      if (values.Length < 1)
        return null;

      var turn = Matrix2D.Rotation(values[0] * _DegreesToRadians);
      if (values.Length < 3)
        return turn;

      return Matrix2D.Translation(-values[1], -values[2]).Then(turn).Then(Matrix2D.Translation(values[1], values[2]));
    }

    if (name.Equals("skewX", StringComparison.OrdinalIgnoreCase))
      return values.Length >= 1 ? Matrix2D.SkewX(values[0] * _DegreesToRadians) : null;

    if (name.Equals("skewY", StringComparison.OrdinalIgnoreCase))
      return values.Length >= 1 ? Matrix2D.SkewY(values[0] * _DegreesToRadians) : null;

    return null;
  }
}
