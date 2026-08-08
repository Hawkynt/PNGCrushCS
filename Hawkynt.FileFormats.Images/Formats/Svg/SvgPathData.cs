using System;
using System.Globalization;
using FileFormat.Core.Vector;

namespace FileFormat.Svg;

/// <summary>Reads the <c>d</c> attribute into a path.</summary>
/// <remarks>
/// Ten commands, each in an uppercase absolute form and a lowercase relative one, and each may be
/// followed by more than one set of operands — a run of them repeats the command, except that a
/// repeated <c>moveto</c> means <c>lineto</c>, which is the rule that keeps a polygon written as
/// one <c>M</c> and a list of points from becoming a row of disconnected points.
/// <para/>
/// The smooth forms <c>S</c> and <c>T</c> take their missing control point from the previous
/// command, reflected about the current point, and only when the previous command was of the same
/// kind; otherwise the reflection is the current point itself. The elliptical arc is converted from
/// the endpoint form the attribute states to the centre form a renderer needs, by the procedure the
/// specification sets out in its implementation notes.
/// <para/>
/// Points are transformed as they are read rather than after, which for an affine transform and a
/// Bézier is exact.
/// </remarks>
public static class SvgPathData {

  private const double _DegreesToRadians = Math.PI / 180;

  /// <summary>Builds the path the attribute describes, in the coordinates the transform maps to.</summary>
  public static VectorPath Parse(string? data, Matrix2D transform) {
    var path = new VectorPath();
    if (string.IsNullOrWhiteSpace(data))
      return path;

    var cursor = new Cursor(data);
    double x = 0, y = 0, startX = 0, startY = 0;
    double lastControlX = 0, lastControlY = 0;
    var lastWasCubic = false;
    var lastWasQuadratic = false;
    var command = '\0';

    while (true) {
      cursor.SkipSeparators();
      if (cursor.AtEnd)
        break;

      if (char.IsAsciiLetter(cursor.Peek)) {
        command = cursor.Take();
      } else if (command == '\0') {
        break;
      } else if (command is 'M') {
        command = 'L';
      } else if (command is 'm') {
        command = 'l';
      }

      var relative = char.IsLower(command);
      var originX = relative ? x : 0;
      var originY = relative ? y : 0;

      switch (char.ToUpperInvariant(command)) {
        case 'M': {
          if (!cursor.TryPair(out var px, out var py))
            return path;

          x = originX + px;
          y = originY + py;
          startX = x;
          startY = y;
          _Move(path, transform, x, y);
          lastWasCubic = lastWasQuadratic = false;
          break;
        }

        case 'L': {
          if (!cursor.TryPair(out var px, out var py))
            return path;

          x = originX + px;
          y = originY + py;
          _Line(path, transform, x, y);
          lastWasCubic = lastWasQuadratic = false;
          break;
        }

        case 'H': {
          if (!cursor.TryNumber(out var px))
            return path;

          x = originX + px;
          _Line(path, transform, x, y);
          lastWasCubic = lastWasQuadratic = false;
          break;
        }

        case 'V': {
          if (!cursor.TryNumber(out var py))
            return path;

          y = originY + py;
          _Line(path, transform, x, y);
          lastWasCubic = lastWasQuadratic = false;
          break;
        }

        case 'C': {
          if (!cursor.TryPair(out var c1x, out var c1y) || !cursor.TryPair(out var c2x, out var c2y) || !cursor.TryPair(out var ex, out var ey))
            return path;

          _Curve(path, transform, originX + c1x, originY + c1y, originX + c2x, originY + c2y, originX + ex, originY + ey);
          lastControlX = originX + c2x;
          lastControlY = originY + c2y;
          x = originX + ex;
          y = originY + ey;
          lastWasCubic = true;
          lastWasQuadratic = false;
          break;
        }

        case 'S': {
          if (!cursor.TryPair(out var c2x, out var c2y) || !cursor.TryPair(out var ex, out var ey))
            return path;

          var c1x = lastWasCubic ? 2 * x - lastControlX : x;
          var c1y = lastWasCubic ? 2 * y - lastControlY : y;
          _Curve(path, transform, c1x, c1y, originX + c2x, originY + c2y, originX + ex, originY + ey);
          lastControlX = originX + c2x;
          lastControlY = originY + c2y;
          x = originX + ex;
          y = originY + ey;
          lastWasCubic = true;
          lastWasQuadratic = false;
          break;
        }

        case 'Q': {
          if (!cursor.TryPair(out var cx, out var cy) || !cursor.TryPair(out var ex, out var ey))
            return path;

          _Quadratic(path, transform, originX + cx, originY + cy, originX + ex, originY + ey);
          lastControlX = originX + cx;
          lastControlY = originY + cy;
          x = originX + ex;
          y = originY + ey;
          lastWasQuadratic = true;
          lastWasCubic = false;
          break;
        }

        case 'T': {
          if (!cursor.TryPair(out var ex, out var ey))
            return path;

          var cx = lastWasQuadratic ? 2 * x - lastControlX : x;
          var cy = lastWasQuadratic ? 2 * y - lastControlY : y;
          _Quadratic(path, transform, cx, cy, originX + ex, originY + ey);
          lastControlX = cx;
          lastControlY = cy;
          x = originX + ex;
          y = originY + ey;
          lastWasQuadratic = true;
          lastWasCubic = false;
          break;
        }

        case 'A': {
          if (!cursor.TryNumber(out var rx) || !cursor.TryNumber(out var ry) || !cursor.TryNumber(out var rotation)
              || !cursor.TryFlag(out var largeArc) || !cursor.TryFlag(out var sweep) || !cursor.TryPair(out var ex, out var ey))
            return path;

          var toX = originX + ex;
          var toY = originY + ey;
          _Arc(path, transform, x, y, rx, ry, rotation, largeArc, sweep, toX, toY);
          x = toX;
          y = toY;
          lastWasCubic = lastWasQuadratic = false;
          break;
        }

        case 'Z': {
          path.Close();
          x = startX;
          y = startY;
          lastWasCubic = lastWasQuadratic = false;
          break;
        }

        default:
          return path;
      }
    }

    return path;
  }

  private static void _Move(VectorPath path, Matrix2D transform, double x, double y) {
    var (dx, dy) = transform.Apply(x, y);
    path.MoveTo(dx, dy);
  }

  private static void _Line(VectorPath path, Matrix2D transform, double x, double y) {
    var (dx, dy) = transform.Apply(x, y);
    path.LineTo(dx, dy);
  }

  private static void _Curve(VectorPath path, Matrix2D transform, double c1x, double c1y, double c2x, double c2y, double ex, double ey) {
    var (a1, b1) = transform.Apply(c1x, c1y);
    var (a2, b2) = transform.Apply(c2x, c2y);
    var (a3, b3) = transform.Apply(ex, ey);
    path.CurveTo(a1, b1, a2, b2, a3, b3);
  }

  private static void _Quadratic(VectorPath path, Matrix2D transform, double cx, double cy, double ex, double ey) {
    var (a1, b1) = transform.Apply(cx, cy);
    var (a2, b2) = transform.Apply(ex, ey);
    path.QuadraticTo(a1, b1, a2, b2);
  }

  /// <summary>
  /// Turns the endpoint form of an elliptical arc into the centre form and adds it.
  /// </summary>
  /// <remarks>
  /// The specification's own conversion: correct out-of-range radii, rotate into the ellipse's
  /// frame, solve for the centre, and take the two angles from there. A radius of zero, or two
  /// endpoints that are the same point, mean a straight line, which the specification says outright.
  /// </remarks>
  private static void _Arc(VectorPath path, Matrix2D transform, double x0, double y0, double rx, double ry, double degrees, bool largeArc, bool sweep, double x1, double y1) {
    rx = Math.Abs(rx);
    ry = Math.Abs(ry);
    if (rx == 0 || ry == 0 || (x0 == x1 && y0 == y1)) {
      _Line(path, transform, x1, y1);
      return;
    }

    var (sin, cos) = Math.SinCos(degrees * _DegreesToRadians);
    var dx = (x0 - x1) / 2;
    var dy = (y0 - y1) / 2;
    var ux = cos * dx + sin * dy;
    var uy = -sin * dx + cos * dy;

    // Radii too small to reach both ends are scaled up until they just do, which the specification
    // requires rather than leaves to the renderer.
    var overshoot = ux * ux / (rx * rx) + uy * uy / (ry * ry);
    if (overshoot > 1) {
      var grow = Math.Sqrt(overshoot);
      rx *= grow;
      ry *= grow;
    }

    var numerator = rx * rx * ry * ry - rx * rx * uy * uy - ry * ry * ux * ux;
    var denominator = rx * rx * uy * uy + ry * ry * ux * ux;
    var factor = denominator <= 0 ? 0 : Math.Sqrt(Math.Max(numerator / denominator, 0));
    if (largeArc == sweep)
      factor = -factor;

    var cxPrime = factor * rx * uy / ry;
    var cyPrime = -factor * ry * ux / rx;
    var centreX = cos * cxPrime - sin * cyPrime + (x0 + x1) / 2;
    var centreY = sin * cxPrime + cos * cyPrime + (y0 + y1) / 2;

    var startAngle = Math.Atan2((uy - cyPrime) / ry, (ux - cxPrime) / rx);
    var endAngle = Math.Atan2((-uy - cyPrime) / ry, (-ux - cxPrime) / rx);
    var span = endAngle - startAngle;

    if (!sweep && span > 0)
      span -= 2 * Math.PI;
    else if (sweep && span < 0)
      span += 2 * Math.PI;

    // Walked in the ellipse's own frame and mapped out through the rotation and the caller's
    // transform, so a rotated arc under a scaled transform comes out as the ellipse it is.
    var toUser = Matrix2D.Scaling(rx, ry)
      .Then(new Matrix2D(cos, sin, -sin, cos, 0, 0))
      .Then(Matrix2D.Translation(centreX, centreY))
      .Then(transform);

    var steps = Math.Clamp((int)Math.Ceiling(Math.Abs(span) / (Math.PI / 32)), 2, 512);
    for (var i = 1; i <= steps; ++i) {
      var angle = startAngle + span * i / steps;
      var (s, c) = Math.SinCos(angle);
      var (px, py) = toUser.Apply(c, s);
      path.LineTo(px, py);
    }
  }

  private ref struct Cursor(ReadOnlySpan<char> text) {

    private readonly ReadOnlySpan<char> _text = text;
    private int _at = 0;

    public readonly bool AtEnd => this._at >= this._text.Length;

    public readonly char Peek => this._text[this._at];

    public char Take() => this._text[this._at++];

    public void SkipSeparators() {
      while (this._at < this._text.Length && (char.IsWhiteSpace(this._text[this._at]) || this._text[this._at] == ','))
        ++this._at;
    }

    public bool TryNumber(out double value) {
      this.SkipSeparators();
      value = 0;
      var start = this._at;

      if (this._at < this._text.Length && (this._text[this._at] is '-' or '+'))
        ++this._at;

      while (this._at < this._text.Length && char.IsAsciiDigit(this._text[this._at]))
        ++this._at;

      if (this._at < this._text.Length && this._text[this._at] == '.') {
        ++this._at;
        while (this._at < this._text.Length && char.IsAsciiDigit(this._text[this._at]))
          ++this._at;
      }

      if (this._at < this._text.Length && this._text[this._at] is 'e' or 'E') {
        var mark = this._at;
        ++this._at;
        if (this._at < this._text.Length && (this._text[this._at] is '-' or '+'))
          ++this._at;

        if (this._at < this._text.Length && char.IsAsciiDigit(this._text[this._at]))
          while (this._at < this._text.Length && char.IsAsciiDigit(this._text[this._at]))
            ++this._at;
        else
          this._at = mark;
      }

      return this._at > start && double.TryParse(this._text[start..this._at], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public bool TryPair(out double x, out double y) {
      y = 0;
      return this.TryNumber(out x) && this.TryNumber(out y);
    }

    /// <summary>
    /// Reads an arc flag, which is a single digit and may be run together with what follows it.
    /// </summary>
    /// <remarks>
    /// The specification allows <c>a1 1 0 1 1 10 10</c> to be written <c>a1 1 0 1110 10</c>, so a
    /// flag has to be taken one character at a time rather than as a number.
    /// </remarks>
    public bool TryFlag(out bool flag) {
      this.SkipSeparators();
      flag = false;
      if (this._at >= this._text.Length || this._text[this._at] is not ('0' or '1'))
        return false;

      flag = this._text[this._at++] == '1';
      return true;
    }
  }
}
