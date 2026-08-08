using System;
using System.Collections.Generic;

namespace FileFormat.Core.Vector;

/// <summary>Which points a fill counts as inside.</summary>
public enum FillRule {

  /// <summary>Inside where the winding number is not zero, so overlapping loops merge.</summary>
  NonZero,

  /// <summary>Inside where a ray crosses the outline an odd number of times, so overlaps punch holes.</summary>
  EvenOdd
}

/// <summary>What a stroke does where two segments meet.</summary>
public enum LineJoin { Miter, Round, Bevel }

/// <summary>What a stroke does at the ends of an open path.</summary>
public enum LineCap { Butt, Round, Square }

/// <summary>
/// A path as a list of subpaths, each a run of points, with curves already flattened.
/// </summary>
/// <remarks>
/// Every vector format in this tree draws with the same handful of moves — go here, draw a line,
/// draw a curve, close the loop — so they share one buffer rather than each carrying its own. The
/// points are whatever coordinates the caller puts in; a reader that has a transform applies it
/// before adding a point, which for an affine transform and a Bézier is exact because the transform
/// of a curve is the curve through the transformed control points.
/// <para/>
/// Curves are flattened as they are added rather than kept, because nothing downstream needs them
/// back and a flattened path is the only thing both the filler and the stroker want.
/// </remarks>
public sealed class VectorPath {

  /// <summary>How far a flattened curve may stray from the true one, in the caller's units.</summary>
  /// <remarks>
  /// A quarter of a pixel when the caller works in device coordinates, which is below what an
  /// eight-bit coverage value can show.
  /// </remarks>
  public const double FlatteningTolerance = 0.25;

  /// <summary>The ceiling on how finely one curve is cut, so a wild control point cannot hang the run.</summary>
  private const int _MaxCurveSegments = 512;

  private readonly List<double> _xs = [];
  private readonly List<double> _ys = [];
  private readonly List<int> _starts = [];
  private readonly List<bool> _closed = [];

  private bool _open;
  private double _startX, _startY;

  /// <summary>How many subpaths have been begun.</summary>
  public int SubPathCount => this._starts.Count;

  /// <summary>Whether nothing has been added that could be drawn.</summary>
  public bool IsEmpty => this._starts.Count == 0;

  /// <summary>Where the last point went, which is where a relative move starts from.</summary>
  public (double X, double Y) CurrentPoint
    => this._xs.Count == 0 ? (0, 0) : (this._xs[^1], this._ys[^1]);

  /// <summary>Where the subpath being built began, which is where a close returns to.</summary>
  public (double X, double Y) SubPathStart => (this._startX, this._startY);

  /// <summary>Begins a subpath at the given point.</summary>
  public void MoveTo(double x, double y) {
    this._starts.Add(this._xs.Count);
    this._closed.Add(false);
    this._xs.Add(x);
    this._ys.Add(y);
    this._startX = x;
    this._startY = y;
    this._open = true;
  }

  /// <summary>Draws a straight line to the given point.</summary>
  public void LineTo(double x, double y) {
    if (!this._open)
      this.MoveTo(x, y);
    else
      this._Add(x, y);
  }

  /// <summary>Draws a cubic Bézier through two control points to an end point.</summary>
  public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3) {
    if (!this._open)
      this.MoveTo(x1, y1);

    var (x0, y0) = this.CurrentPoint;
    var steps = _SegmentsFor(x0, y0, x1, y1, x2, y2, x3, y3);
    for (var i = 1; i <= steps; ++i) {
      var t = (double)i / steps;
      var u = 1 - t;
      var a = u * u * u;
      var b = 3 * u * u * t;
      var c = 3 * u * t * t;
      var d = t * t * t;
      this._Add(a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3);
    }
  }

  /// <summary>Draws a quadratic Bézier, raised to a cubic so there is only one flattener.</summary>
  public void QuadraticTo(double x1, double y1, double x2, double y2) {
    if (!this._open)
      this.MoveTo(x1, y1);

    var (x0, y0) = this.CurrentPoint;
    this.CurveTo(
      x0 + 2.0 / 3.0 * (x1 - x0), y0 + 2.0 / 3.0 * (y1 - y0),
      x2 + 2.0 / 3.0 * (x1 - x2), y2 + 2.0 / 3.0 * (y1 - y2),
      x2, y2
    );
  }

  /// <summary>Closes the current subpath back to where it began.</summary>
  public void Close() {
    if (this._starts.Count == 0)
      return;

    this._closed[^1] = true;
    this._open = false;
  }

  /// <summary>Adds a whole polygon as one closed subpath.</summary>
  public void AddPolygon(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys) {
    if (xs.Length == 0 || xs.Length != ys.Length)
      return;

    this.MoveTo(xs[0], ys[0]);
    for (var i = 1; i < xs.Length; ++i)
      this.LineTo(xs[i], ys[i]);
    this.Close();
  }

  /// <summary>Adds an axis-aligned rectangle as one closed subpath, wound anticlockwise on screen.</summary>
  public void AddRectangle(double x, double y, double width, double height) {
    this.MoveTo(x, y);
    this.LineTo(x + width, y);
    this.LineTo(x + width, y + height);
    this.LineTo(x, y + height);
    this.Close();
  }

  /// <summary>Adds an ellipse arc about a centre, in radians, as lines on the current subpath.</summary>
  /// <remarks>
  /// Angles run anticlockwise in a y-up frame, which is how every one of these formats states an
  /// arc; a reader drawing into a y-down raster gets the flip from its own transform rather than
  /// from here, so the sign of an angle never has to be argued about twice.
  /// </remarks>
  public void ArcTo(double centreX, double centreY, double radiusX, double radiusY, double startAngle, double sweepAngle, bool startsNewSubPath) {
    var steps = _ArcSegmentsFor(Math.Max(Math.Abs(radiusX), Math.Abs(radiusY)), sweepAngle);
    for (var i = 0; i <= steps; ++i) {
      var angle = startAngle + sweepAngle * i / steps;
      var (sin, cos) = Math.SinCos(angle);
      var x = centreX + radiusX * cos;
      var y = centreY + radiusY * sin;
      if (i == 0 && startsNewSubPath)
        this.MoveTo(x, y);
      else
        this.LineTo(x, y);
    }
  }

  /// <summary>Adds a whole ellipse as one closed subpath.</summary>
  public void AddEllipse(double centreX, double centreY, double radiusX, double radiusY) {
    this.ArcTo(centreX, centreY, radiusX, radiusY, 0, 2 * Math.PI, true);
    this.Close();
  }

  /// <summary>Every subpath, as a run of points and whether it is closed.</summary>
  public IEnumerable<(ReadOnlyMemory<double> Xs, ReadOnlyMemory<double> Ys, bool Closed)> SubPaths {
    get {
      var xs = this._xs.ToArray();
      var ys = this._ys.ToArray();
      for (var i = 0; i < this._starts.Count; ++i) {
        var from = this._starts[i];
        var to = i + 1 < this._starts.Count ? this._starts[i + 1] : xs.Length;
        if (to > from)
          yield return (xs.AsMemory(from, to - from), ys.AsMemory(from, to - from), this._closed[i]);
      }
    }
  }

  /// <summary>The smallest box holding every point, or null when there are none.</summary>
  public (double MinX, double MinY, double MaxX, double MaxY)? Bounds {
    get {
      if (this._xs.Count == 0)
        return null;

      double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
      for (var i = 0; i < this._xs.Count; ++i) {
        minX = Math.Min(minX, this._xs[i]);
        maxX = Math.Max(maxX, this._xs[i]);
        minY = Math.Min(minY, this._ys[i]);
        maxY = Math.Max(maxY, this._ys[i]);
      }

      return (minX, minY, maxX, maxY);
    }
  }

  /// <summary>
  /// The same path cut into the on-and-off runs of a dash pattern, as open subpaths.
  /// </summary>
  /// <param name="pattern">
  /// Alternating on and off lengths, in the same units as the points. An empty or all-zero pattern
  /// gives the path back unchanged, which is what a solid line is.
  /// </param>
  /// <param name="offset">How far into the pattern the first subpath starts.</param>
  public VectorPath Dashed(ReadOnlySpan<double> pattern, double offset = 0) {
    var period = 0.0;
    foreach (var length in pattern)
      period += Math.Max(length, 0);

    if (pattern.Length == 0 || period <= 0)
      return this;

    var dashed = new VectorPath();
    foreach (var (xsMemory, ysMemory, closed) in this.SubPaths) {
      var xs = xsMemory.Span;
      var ys = ysMemory.Span;
      var count = closed ? xs.Length : xs.Length - 1;

      var index = 0;
      var remaining = pattern[0];
      var on = true;
      for (var skip = ((offset % period) + period) % period; skip > 0;) {
        if (skip < remaining) {
          remaining -= skip;
          break;
        }

        skip -= remaining;
        index = (index + 1) % pattern.Length;
        remaining = pattern[index];
        on = !on;
      }

      var drawing = false;
      for (var i = 0; i < count; ++i) {
        var j = (i + 1) % xs.Length;
        var dx = xs[j] - xs[i];
        var dy = ys[j] - ys[i];
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0)
          continue;

        var walked = 0.0;
        while (walked < length) {
          var step = Math.Min(remaining, length - walked);
          var from = walked / length;
          var to = (walked + step) / length;

          if (on) {
            if (!drawing) {
              dashed.MoveTo(xs[i] + dx * from, ys[i] + dy * from);
              drawing = true;
            }

            dashed.LineTo(xs[i] + dx * to, ys[i] + dy * to);
          } else
            drawing = false;

          walked += step;
          remaining -= step;
          if (remaining > 0)
            continue;

          index = (index + 1) % pattern.Length;
          remaining = pattern[index];
          on = !on;
          drawing = false;
        }
      }
    }

    return dashed;
  }

  /// <summary>Empties the buffer so one instance can draw shape after shape.</summary>
  public void Clear() {
    this._xs.Clear();
    this._ys.Clear();
    this._starts.Clear();
    this._closed.Clear();
    this._open = false;
  }

  private void _Add(double x, double y) {
    // A repeated point contributes no edge and would only cost the rasteriser work.
    if (this._xs.Count > 0 && this._xs[^1] == x && this._ys[^1] == y)
      return;

    this._xs.Add(x);
    this._ys.Add(y);
  }

  private static int _SegmentsFor(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3) {
    // The control polygon is never shorter than the curve, so cutting to its length is safe and
    // costs at most a few segments more than the curve itself needs.
    var length = _Distance(x0, y0, x1, y1) + _Distance(x1, y1, x2, y2) + _Distance(x2, y2, x3, y3);
    if (!double.IsFinite(length) || length <= FlatteningTolerance)
      return 1;

    return Math.Clamp((int)Math.Ceiling(Math.Sqrt(length / FlatteningTolerance) * 2), 1, _MaxCurveSegments);
  }

  private static int _ArcSegmentsFor(double radius, double sweep) {
    if (!double.IsFinite(radius) || !double.IsFinite(sweep))
      return 1;

    // The sagitta of a chord subtending a is r(1 - cos(a/2)); solving for the tolerance gives the
    // step, and the whole sweep divided by it gives the count.
    var ratio = Math.Clamp(1 - FlatteningTolerance / Math.Max(radius, FlatteningTolerance), -1, 1);
    var step = 2 * Math.Acos(ratio);
    if (step <= 0)
      return _MaxCurveSegments;

    return Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / step), 2, _MaxCurveSegments);
  }

  private static double _Distance(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
}
