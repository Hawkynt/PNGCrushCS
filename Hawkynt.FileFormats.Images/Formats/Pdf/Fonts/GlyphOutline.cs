using System.Collections.Generic;

namespace FileFormat.Pdf.Fonts;

/// <summary>What one step of a glyph outline does.</summary>
internal enum GlyphVerb : byte { MoveTo, LineTo, CurveTo, Close }

/// <summary>
/// A glyph's shape as moves, lines and cubic curves, kept unflattened so it can be placed at any
/// size before it is cut into straight segments.
/// </summary>
/// <remarks>
/// Every outline font format comes down to these four verbs: TrueType's quadratic splines raise to
/// cubics exactly, and Type 1 and CFF charstrings are cubic to begin with. One shape for all of them
/// means the renderer has a single glyph path to place, whatever program the glyph came out of.
/// </remarks>
internal sealed class GlyphOutline {

  private bool _open;
  private double _lastX, _lastY;

  /// <summary>The steps, in order.</summary>
  public List<GlyphVerb> Verbs { get; } = [];

  /// <summary>The coordinates of the steps: two for a move or line, six for a curve, none for a close.</summary>
  public List<double> Points { get; } = [];

  /// <summary>Whether nothing that could be drawn has been added.</summary>
  public bool IsEmpty => this.Verbs.Count == 0;

  /// <summary>Begins a contour.</summary>
  public void MoveTo(double x, double y) {
    this.Verbs.Add(GlyphVerb.MoveTo);
    this.Points.Add(x);
    this.Points.Add(y);
    this._lastX = x;
    this._lastY = y;
    this._open = true;
  }

  /// <summary>Draws a straight line to a point.</summary>
  public void LineTo(double x, double y) {
    this._EnsureOpen();
    this.Verbs.Add(GlyphVerb.LineTo);
    this.Points.Add(x);
    this.Points.Add(y);
    this._lastX = x;
    this._lastY = y;
  }

  /// <summary>Draws a cubic Bézier through two control points.</summary>
  public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3) {
    this._EnsureOpen();
    this.Verbs.Add(GlyphVerb.CurveTo);
    this.Points.Add(x1);
    this.Points.Add(y1);
    this.Points.Add(x2);
    this.Points.Add(y2);
    this.Points.Add(x3);
    this.Points.Add(y3);
    this._lastX = x3;
    this._lastY = y3;
  }

  /// <summary>Draws a quadratic Bézier, raised to the cubic that traces exactly the same curve.</summary>
  public void QuadTo(double x1, double y1, double x2, double y2) {
    this._EnsureOpen();
    var x0 = this._lastX;
    var y0 = this._lastY;
    this.CurveTo(
      x0 + 2.0 / 3.0 * (x1 - x0), y0 + 2.0 / 3.0 * (y1 - y0),
      x2 + 2.0 / 3.0 * (x1 - x2), y2 + 2.0 / 3.0 * (y1 - y2),
      x2, y2
    );
  }

  /// <summary>Closes the contour being drawn; does nothing when none is open.</summary>
  public void Close() {
    if (!this._open)
      return;

    this.Verbs.Add(GlyphVerb.Close);
    this._open = false;
  }

  /// <summary>Appends another outline's steps, which is how a composite glyph is assembled.</summary>
  public void Append(GlyphOutline other) {
    this.Close();
    this.Verbs.AddRange(other.Verbs);
    this.Points.AddRange(other.Points);
    this._open = false;
  }

  /// <summary>The same outline through an affine transform: x' = a·x + c·y + e, y' = b·x + d·y + f.</summary>
  public GlyphOutline Transformed(double a, double b, double c, double d, double e, double f) {
    var result = new GlyphOutline();
    result.Verbs.AddRange(this.Verbs);
    var points = this.Points;
    var target = result.Points;
    target.Capacity = points.Count;
    for (var i = 0; i + 1 < points.Count; i += 2) {
      var x = points[i];
      var y = points[i + 1];
      target.Add(a * x + c * y + e);
      target.Add(b * x + d * y + f);
    }

    return result;
  }

  /// <summary>The smallest box holding every point and control point, or null when there are none.</summary>
  public (double MinX, double MinY, double MaxX, double MaxY)? ControlBounds {
    get {
      if (this.Points.Count < 2)
        return null;

      double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
      for (var i = 0; i + 1 < this.Points.Count; i += 2) {
        var x = this.Points[i];
        var y = this.Points[i + 1];
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }

      return (minX, minY, maxX, maxY);
    }
  }

  // A line or curve with no contour open starts one where the pen already is, which is what a
  // charstring that draws straight after its sidebearing move expects.
  private void _EnsureOpen() {
    if (this._open)
      return;

    this.Verbs.Add(GlyphVerb.MoveTo);
    this.Points.Add(this._lastX);
    this.Points.Add(this._lastY);
    this._open = true;
  }
}
