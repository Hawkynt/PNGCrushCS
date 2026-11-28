using System;
using System.Collections.Generic;
using FileFormat.Core.Vector;

namespace FileFormat.Pdf.Rendering;

/// <summary>The line parameters of the graphics state (PDF 32000-1 §8.4.3).</summary>
internal sealed record PdfLineStyle(double Width, LineCap Cap, LineJoin Join, double MiterLimit, double[] Dash, double DashPhase);

/// <summary>
/// Turns a stroked path into the area it covers, as polygons to be filled by the non-zero rule.
/// </summary>
/// <remarks>
/// The stroke is built in user space and only then carried to the device, because that is where
/// the line width and the dash lengths are defined: under a transform that stretches one axis more
/// than the other a stroke is wider one way than the other, and building it on the device with one
/// width cannot show that. The path arrives already flattened on the device (so curves are cut
/// finely enough for the pixels) and is taken back through the inverse transform.
/// <para/>
/// Each segment becomes a quadrilateral and each join and cap a small polygon, and every polygon is
/// turned to wind the same way before it is emitted. The non-zero fill then merges the overlaps;
/// a polygon wound the other way would cancel instead and leave a hole at a sharp corner.
/// <para/>
/// A line of zero width is drawn one device pixel wide (§8.4.3.2, "the thinnest line that can be
/// rendered"); the caller draws it without antialiasing, as poppler does. Any other width is kept,
/// however thin, and antialiasing shows it as faint.
/// </remarks>
internal static class PdfStroker {

  /// <summary>The area a stroke covers, in device space.</summary>
  public static VectorPath Outline(VectorPath devicePath, PdfLineStyle style, Matrix2D userToDevice) {
    var inverse = userToDevice.Inverse;
    var scale = userToDevice.MeanScale;
    if (inverse == null || !(scale > 1e-9)) {
      // A collapsed transform: draw the thinnest line along the device path.
      return _Build(devicePath, 0.5, style with { Dash = [] }, Matrix2D.Identity, 0.25, 1, 0.5);
    }

    // Back to user space for the geometry.
    var userPath = new VectorPath();
    var inv = inverse.Value;
    foreach (var (xs, ys, closed) in devicePath.SubPaths) {
      var x = xs.Span;
      var y = ys.Span;
      for (var i = 0; i < x.Length; ++i) {
        var (ux, uy) = inv.Apply(x[i], y[i]);
        if (i == 0)
          userPath.MoveTo(ux, uy);
        else
          userPath.LineTo(ux, uy);
      }

      if (closed)
        userPath.Close();
    }

    var dashSum = 0.0;
    foreach (var d in style.Dash)
      dashSum += Math.Max(0, d);
    if (style.Dash.Length > 0 && dashSum > 0 && dashSum * scale > 0.01)
      userPath = userPath.Dashed(style.Dash, style.DashPhase);

    // A zero width is the thinnest line the device can show, one pixel (§8.4.3.2); any other width
    // is drawn as it is, however thin, as poppler does. A segment square to the device axes is
    // rounded to whole pixels by stroke adjustment, which keeps at least one.
    var half = style.Width > 0 ? style.Width / 2 : 0.5 / scale;
    return _Build(userPath, half, style, userToDevice, 0.2 / scale, scale, half);
  }

  private static VectorPath _Build(VectorPath path, double half, PdfLineStyle style, Matrix2D toDevice, double tolerance, double scale, double trueHalf) {
    var output = new VectorPath();
    var poly = new List<(double X, double Y)>(16);

    foreach (var (xsMemory, ysMemory, closed) in path.SubPaths) {
      var xs = xsMemory.Span;
      var ys = ysMemory.Span;

      // Drop repeated points so every segment has a direction.
      var px = new List<double>(xs.Length);
      var py = new List<double>(xs.Length);
      for (var i = 0; i < xs.Length; ++i) {
        if (px.Count > 0 && Math.Abs(px[^1] - xs[i]) < 1e-12 && Math.Abs(py[^1] - ys[i]) < 1e-12)
          continue;
        px.Add(xs[i]);
        py.Add(ys[i]);
      }

      var isClosed = closed;
      if (isClosed && px.Count > 1 && Math.Abs(px[0] - px[^1]) < 1e-12 && Math.Abs(py[0] - py[^1]) < 1e-12) {
        px.RemoveAt(px.Count - 1);
        py.RemoveAt(py.Count - 1);
      }

      var count = px.Count;
      if (count == 0)
        continue;

      if (count == 1) {
        // A lone point is a dot under a round or projecting cap, and nothing under a butt cap.
        if (style.Cap == LineCap.Round)
          _Emit(output, _Circle(px[0], py[0], half, tolerance), toDevice);
        else if (style.Cap == LineCap.Square) {
          poly.Clear();
          poly.Add((px[0] - half, py[0] - half));
          poly.Add((px[0] + half, py[0] - half));
          poly.Add((px[0] + half, py[0] + half));
          poly.Add((px[0] - half, py[0] + half));
          _Emit(output, poly, toDevice);
        }
        continue;
      }

      if (count == 2)
        isClosed = false;

      var segments = isClosed ? count : count - 1;
      for (var i = 0; i < segments; ++i) {
        var j = (i + 1) % count;
        var (sx0, sy0) = toDevice.Apply(px[i], py[i]);
        var (sx1, sy1) = toDevice.Apply(px[j], py[j]);
        var square = Math.Abs(sx0 - sx1) < 1e-6 || Math.Abs(sy0 - sy1) < 1e-6;
        var (nx, ny) = _Normal(px[i], py[i], px[j], py[j], square ? trueHalf : half);
        poly.Clear();
        poly.Add((px[i] + nx, py[i] + ny));
        poly.Add((px[j] + nx, py[j] + ny));
        poly.Add((px[j] - nx, py[j] - ny));
        poly.Add((px[i] - nx, py[i] - ny));
        _EmitSegment(output, poly, toDevice);
      }

      var joins = isClosed ? count : count - 2;
      for (var k = 0; k < joins; ++k) {
        var a = isClosed ? (k + count - 1) % count : k;
        var b = isClosed ? k : k + 1;
        var c = (b + 1) % count;
        _Join(output, poly, px[a], py[a], px[b], py[b], px[c], py[c], half, style, toDevice, tolerance);
      }

      if (isClosed)
        continue;

      _Cap(output, poly, px[1], py[1], px[0], py[0], half, style.Cap, toDevice, tolerance);
      _Cap(output, poly, px[count - 2], py[count - 2], px[count - 1], py[count - 1], half, style.Cap, toDevice, tolerance);
    }

    return output;
  }

  private static (double X, double Y) _Normal(double x0, double y0, double x1, double y1, double half) {
    var dx = x1 - x0;
    var dy = y1 - y0;
    var length = Math.Sqrt(dx * dx + dy * dy);
    return length <= 0 ? (0, 0) : (-dy / length * half, dx / length * half);
  }

  private static void _Join(VectorPath output, List<(double X, double Y)> poly, double ax, double ay, double bx, double by, double cx, double cy, double half, PdfLineStyle style, Matrix2D toDevice, double tolerance) {
    var cross = (bx - ax) * (cy - by) - (by - ay) * (cx - bx);
    var dot = (bx - ax) * (cx - bx) + (by - ay) * (cy - by);
    if (Math.Abs(cross) < 1e-12 && dot >= 0)
      return; // straight on: the two quads already meet

    if (style.Join == LineJoin.Round) {
      _Emit(output, _Circle(bx, by, half, tolerance), toDevice);
      return;
    }

    var (n1x, n1y) = _Normal(ax, ay, bx, by, half);
    var (n2x, n2y) = _Normal(bx, by, cx, cy, half);

    // The outer side of the corner is the one the path turns away from.
    var side = cross > 0 ? -1 : 1;
    var p1x = bx + n1x * side;
    var p1y = by + n1y * side;
    var p2x = bx + n2x * side;
    var p2y = by + n2y * side;

    if (style.Join == LineJoin.Miter) {
      // The miter tip is where the two offset lines meet; its distance from the corner over the
      // half width is 1/sin(φ/2), which is the ratio the miter limit bounds.
      var d1x = bx - ax;
      var d1y = by - ay;
      var d2x = cx - bx;
      var d2y = cy - by;
      var l1 = Math.Sqrt(d1x * d1x + d1y * d1y);
      var l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
      var cosTheta = -(d1x * d2x + d1y * d2y) / (l1 * l2); // angle between the segments at the corner
      var sinHalf = Math.Sqrt(Math.Max(0, (1 - cosTheta) / 2));
      if (sinHalf > 1e-9 && 1 / sinHalf <= style.MiterLimit) {
        var mx = (n1x + n2x) * side;
        var my = (n1y + n2y) * side;
        var ml = Math.Sqrt(mx * mx + my * my);
        if (ml > 1e-12) {
          var reach = half / sinHalf;
          poly.Clear();
          poly.Add((bx, by));
          poly.Add((p1x, p1y));
          poly.Add((bx + mx / ml * reach, by + my / ml * reach));
          poly.Add((p2x, p2y));
          _Emit(output, poly, toDevice);
          return;
        }
      }
    }

    poly.Clear();
    poly.Add((bx, by));
    poly.Add((p1x, p1y));
    poly.Add((p2x, p2y));
    _Emit(output, poly, toDevice);
  }

  private static void _Cap(VectorPath output, List<(double X, double Y)> poly, double fromX, double fromY, double x, double y, double half, LineCap cap, Matrix2D toDevice, double tolerance) {
    if (cap == LineCap.Butt)
      return;

    if (cap == LineCap.Round) {
      _Emit(output, _Circle(x, y, half, tolerance), toDevice);
      return;
    }

    var dx = x - fromX;
    var dy = y - fromY;
    var length = Math.Sqrt(dx * dx + dy * dy);
    if (length <= 0)
      return;

    var ux = dx / length * half;
    var uy = dy / length * half;
    poly.Clear();
    poly.Add((x - uy, y + ux));
    poly.Add((x - uy + ux, y + ux + uy));
    poly.Add((x + uy + ux, y - ux + uy));
    poly.Add((x + uy, y - ux));
    _Emit(output, poly, toDevice);
  }

  private static List<(double X, double Y)> _Circle(double cx, double cy, double radius, double tolerance) {
    var ratio = Math.Clamp(1 - tolerance / Math.Max(radius, tolerance), -1, 1);
    var step = 2 * Math.Acos(ratio);
    var steps = step <= 0 ? 64 : Math.Clamp((int)Math.Ceiling(2 * Math.PI / step), 8, 256);
    var points = new List<(double, double)>(steps);
    for (var i = 0; i < steps; ++i) {
      var (sin, cos) = Math.SinCos(2 * Math.PI * i / steps);
      points.Add((cx + radius * cos, cy + radius * sin));
    }

    return points;
  }

  /// <summary>
  /// Emits a segment's quadrilateral, snapping its long edges to whole pixels when the segment lies
  /// along a device axis.
  /// </summary>
  /// <remarks>
  /// This is stroke adjustment (§10.7.5), which poppler applies by default: a horizontal or vertical
  /// line's edges are rounded to the pixel grid, and a line that would round to nothing keeps one
  /// pixel, so thin rules come out as crisp one-pixel lines rather than two grey ones.
  /// </remarks>
  private static void _EmitSegment(VectorPath output, List<(double X, double Y)> poly, Matrix2D toDevice) {
    Span<double> xs = stackalloc double[4];
    Span<double> ys = stackalloc double[4];
    for (var k = 0; k < 4; ++k)
      (xs[k], ys[k]) = toDevice.Apply(poly[k].X, poly[k].Y);

    // Corners 0-1 and 3-2 are the two long edges; the segment runs from 0 to 1.
    const double eps = 1e-6;
    var horizontal = Math.Abs(ys[0] - ys[1]) < eps && Math.Abs(ys[3] - ys[2]) < eps;
    var vertical = Math.Abs(xs[0] - xs[1]) < eps && Math.Abs(xs[3] - xs[2]) < eps;
    if (horizontal || vertical) {
      var (a, b) = horizontal ? (ys[0], ys[3]) : (xs[0], xs[3]);
      var lo = Math.Floor(Math.Min(a, b) + 0.5);
      var hi = Math.Floor(Math.Max(a, b) + 0.5);
      if (hi <= lo)
        hi = lo + 1;
      var (na, nb) = a <= b ? (lo, hi) : (hi, lo);
      if (horizontal) {
        ys[0] = ys[1] = na;
        ys[3] = ys[2] = nb;
      } else {
        xs[0] = xs[1] = na;
        xs[3] = xs[2] = nb;
      }
    }

    var area = 0.0;
    for (var k = 0; k < 4; ++k) {
      var j = (k + 1) % 4;
      area += xs[k] * ys[j] - xs[j] * ys[k];
    }

    if (Math.Abs(area) < 1e-18)
      return;

    for (var k = 0; k < 4; ++k) {
      var at = area < 0 ? 3 - k : k;
      if (k == 0)
        output.MoveTo(xs[at], ys[at]);
      else
        output.LineTo(xs[at], ys[at]);
    }

    output.Close();
  }

  private static void _Emit(VectorPath output, List<(double X, double Y)> poly, Matrix2D toDevice) {
    if (poly.Count < 3)
      return;

    var area = 0.0;
    for (var i = 0; i < poly.Count; ++i) {
      var j = (i + 1) % poly.Count;
      area += poly[i].X * poly[j].Y - poly[j].X * poly[i].Y;
    }

    if (Math.Abs(area) < 1e-18)
      return;

    // Orientation is decided in device space, after the transform, which may mirror.
    var mirror = toDevice.Determinant < 0;
    var reverse = (area < 0) ^ mirror;
    for (var k = 0; k < poly.Count; ++k) {
      var p = poly[reverse ? poly.Count - 1 - k : k];
      var (dx, dy) = toDevice.Apply(p.X, p.Y);
      if (k == 0)
        output.MoveTo(dx, dy);
      else
        output.LineTo(dx, dy);
    }

    output.Close();
  }
}
