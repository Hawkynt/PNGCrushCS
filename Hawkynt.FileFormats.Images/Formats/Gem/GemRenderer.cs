using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Gem;

/// <summary>Plays a metafile's records back onto a raster.</summary>
/// <remarks>
/// A metafile is a recording, so drawing it is replaying it: the attribute calls set the state and
/// the drawing calls use whatever state is current, exactly as they did on the workstation that
/// recorded them. Nothing is reordered and nothing is looked ahead at.
/// </remarks>
public static class GemRenderer {

  /// <summary>Tenths of a millimetre to the millimetre, which is the unit the header states a page in.</summary>
  private const double _TenthsOfMillimetrePerMillimetre = 10;

  /// <summary>Angles are stated in tenths of a degree, anticlockwise from east.</summary>
  private const double _TenthsOfDegreeToRadians = Math.PI / 1800;

  /// <summary>How much of the shorter side a rounded box's corner takes, as the interface rounds it.</summary>
  private const double _CornerFraction = 1.0 / 8;

  private sealed class State {
    public int LineColour = 1;
    public int LineType = 1;
    public double LineWidth = 1;
    public int FillColour = 1;
    public int FillInterior = GemAttributes.InteriorHollow;
    public int FillStyle = 1;
    public bool FillPerimeter = true;
  }

  /// <summary>Draws the metafile at the size its own header states.</summary>
  public static RawImage Render(GemFile file) {
    if (file.Records == null)
      throw new InvalidDataException("A GEM metafile with no records cannot be drawn.");

    var viewport = Viewport(file);
    var canvas = new VectorCanvas(viewport.Width, viewport.Height, Rgba32.White);
    var state = new State();

    // One mask bit is one device pixel on the workstation that recorded this; the picture is being
    // drawn larger than that, so the dashes are scaled by the same factor as everything else.
    var unit = Math.Max(viewport.Transform.MeanScale, 1.0 / 16);

    foreach (var record in file.Records)
      _Play(record, canvas, viewport.Transform, state, unit);

    return canvas.ToRawImage();
  }

  /// <summary>
  /// The size the file states it is, and the transform from its coordinates onto that raster.
  /// </summary>
  /// <remarks>
  /// The extent is what was drawn and the window is what the page covers, both in the same units,
  /// so the extent's share of the window is its share of the page — and the page is stated in
  /// tenths of a millimetre, which is a physical size. Rendering that at ninety-six pixels to the
  /// inch is the only step here that is a convention rather than a reading.
  /// <para/>
  /// A file that states neither a window nor a page has only the extent, and that is taken at one
  /// pixel per coordinate unit and capped.
  /// </remarks>
  public static VectorViewport Viewport(GemFile file) {
    var (ex1, ey1, ex2, ey2) = file.Extent;
    double minX = Math.Min(ex1, ex2), maxX = Math.Max(ex1, ex2);
    double minY = Math.Min(ey1, ey2), maxY = Math.Max(ey1, ey2);

    // Nothing was drawn, or the extent was never filled in; the window is the only size left.
    if (maxX <= minX || maxY <= minY) {
      var (wx1, wy1, wx2, wy2) = file.Window;
      minX = Math.Min(wx1, wx2);
      maxX = Math.Max(wx1, wx2);
      minY = Math.Min(wy1, wy2);
      maxY = Math.Max(wy1, wy2);
    }

    if (maxX <= minX || maxY <= minY) {
      minX = minY = 0;
      maxX = maxY = GemFile.NormalisedExtent;
    }

    var (windowWidth, windowHeight) = _WindowSpan(file);
    var pixelsWide = maxX - minX;
    var pixelsTall = maxY - minY;

    if (windowWidth > 0 && windowHeight > 0 && file.PageSize.Width > 0 && file.PageSize.Height > 0) {
      var millimetresWide = (maxX - minX) / windowWidth * file.PageSize.Width / _TenthsOfMillimetrePerMillimetre;
      var millimetresTall = (maxY - minY) / windowHeight * file.PageSize.Height / _TenthsOfMillimetrePerMillimetre;
      pixelsWide = VectorViewport.PixelsFromMillimetres(millimetresWide);
      pixelsTall = VectorViewport.PixelsFromMillimetres(millimetresTall);
    }

    return VectorViewport.FitCapped(minX, minY, maxX, maxY, pixelsWide, pixelsTall, _YGrowsUpwards(file));
  }

  /// <summary>How wide and tall the coordinate window is, or nothing when the file states none.</summary>
  private static (double Width, double Height) _WindowSpan(GemFile file) {
    var (x1, y1, x2, y2) = file.Window;
    if (x1 == 0 && y1 == 0 && x2 == 0 && y2 == 0)
      return (GemFile.NormalisedExtent, GemFile.NormalisedExtent);

    return (Math.Abs((double)x2 - x1), Math.Abs((double)y2 - y1));
  }

  /// <summary>
  /// Which way y runs, taken from the window the file states rather than from a convention.
  /// </summary>
  /// <remarks>
  /// The window is written as the lower-left corner then the upper-right one. If the lower-left has
  /// the larger y then y grows downwards, which is what the raster-coordinate flag means and what
  /// every one of the samples here says. A file that states no window falls back to the flag.
  /// </remarks>
  private static bool _YGrowsUpwards(GemFile file) {
    var (x1, y1, x2, y2) = file.Window;
    if (x1 == 0 && y1 == 0 && x2 == 0 && y2 == 0)
      return file.CoordinateFlag != GemFile.RasterCoordinates;

    return y1 < y2;
  }

  private static void _Play(GemRecord record, VectorCanvas canvas, Matrix2D transform, State state, double unit) {
    switch (record.Opcode) {
      case GemOpcode.SetLineType:
        state.LineType = _Integer(record, 0, 1);
        break;
      case GemOpcode.SetLineWidth:
        if (record.PointCount > 0)
          state.LineWidth = Math.Abs(record.X(0));
        break;
      case GemOpcode.SetLineColour:
        state.LineColour = _Integer(record, 0, 1);
        break;
      case GemOpcode.SetFillInterior:
        state.FillInterior = _Integer(record, 0, GemAttributes.InteriorHollow);
        break;
      case GemOpcode.SetFillStyle:
        state.FillStyle = _Integer(record, 0, 1);
        break;
      case GemOpcode.SetFillColour:
        state.FillColour = _Integer(record, 0, 1);
        break;
      case GemOpcode.SetFillPerimeter:
        state.FillPerimeter = _Integer(record, 0, 1) != 0;
        break;

      case GemOpcode.PolyLine:
        _Stroke(canvas, _PolyLine(record, transform, false), state, transform, unit);
        break;

      case GemOpcode.FilledArea:
        _FillAndOutline(canvas, _PolyLine(record, transform, true), state, transform, unit);
        break;

      case GemOpcode.GeneralisedPrimitive:
        _PlayPrimitive(record, canvas, transform, state, unit);
        break;
    }
  }

  private static void _PlayPrimitive(GemRecord record, VectorCanvas canvas, Matrix2D transform, State state, double unit) {
    if (record.PointCount < 2)
      return;

    var path = new VectorPath();
    switch (record.SubOpcode) {
      case GemPrimitive.Bar:
        _AddBox(path, record, transform);
        _FillAndOutline(canvas, path, state, transform, unit);
        return;

      case GemPrimitive.Circle:
      case GemPrimitive.Ellipse:
        _AddEllipse(path, record, transform, 0, 3600, true);
        _FillAndOutline(canvas, path, state, transform, unit);
        return;

      case GemPrimitive.PieSlice:
      case GemPrimitive.EllipticalPie:
        _AddEllipse(path, record, transform, _Integer(record, 0, 0), _Integer(record, 1, 3600), true);
        _FillAndOutline(canvas, path, state, transform, unit);
        return;

      case GemPrimitive.Arc:
      case GemPrimitive.EllipticalArc:
        _AddEllipse(path, record, transform, _Integer(record, 0, 0), _Integer(record, 1, 3600), false);
        _Stroke(canvas, path, state, transform, unit);
        return;

      case GemPrimitive.RoundedBox:
        _AddRoundedBox(path, record, transform);
        _Stroke(canvas, path, state, transform, unit);
        return;

      case GemPrimitive.FilledRoundedBox:
        _AddRoundedBox(path, record, transform);
        _FillAndOutline(canvas, path, state, transform, unit);
        return;
    }
  }

  private static VectorPath _PolyLine(GemRecord record, Matrix2D transform, bool close) {
    if (_IsCurved(record))
      return _CurvedPath(record, transform, close);

    var path = new VectorPath();
    for (var i = 0; i < record.PointCount; ++i) {
      var (x, y) = transform.Apply(record.X(i), record.Y(i));
      if (i == 0)
        path.MoveTo(x, y);
      else
        path.LineTo(x, y);
    }

    if (close)
      path.Close();

    return path;
  }

  /// <summary>
  /// Whether a polyline or filled area is the curve-carrying form of the call rather than the plain one.
  /// </summary>
  /// <remarks>
  /// The interface overloads both calls to take Bézier paths, and says so by carrying one flag byte
  /// per point in the integer array, two bytes to a word. A record whose integer count is exactly
  /// what that packing comes to is that form; the plain calls carry no integers at all, so the two
  /// cannot be mistaken for each other.
  /// </remarks>
  private static bool _IsCurved(GemRecord record)
    => record.PointCount > 0 && record.Integers.Length == (record.PointCount + 1) / 2;

  /// <summary>The flag byte belonging to one point, low byte of each word first.</summary>
  /// <remarks>
  /// Low byte first regardless of the machine, which the interface documents as a concession to the
  /// PC version and which is what makes the bytes come out in the order the points are in.
  /// </remarks>
  private static int _CurveFlag(GemRecord record, int point) {
    var word = record.Integers[point / 2];
    return point % 2 == 0 ? word & 0xFF : (word >> 8) & 0xFF;
  }

  /// <summary>
  /// Builds a path from the curve-carrying form, where each point's flag says what it begins.
  /// </summary>
  /// <remarks>
  /// Bit 0 set means the point opens a cubic Bézier and the three after it are its controls and its
  /// end; bit 0 clear means a plain vertex. Bit 1 marks a jump, which starts a new subpath and is
  /// how one call draws a shape with holes in it.
  /// </remarks>
  private static VectorPath _CurvedPath(GemRecord record, Matrix2D transform, bool close) {
    const int bezierBit = 1, jumpBit = 2;

    var path = new VectorPath();
    var started = false;

    for (var i = 0; i < record.PointCount;) {
      var flag = _CurveFlag(record, i);
      var (x, y) = transform.Apply(record.X(i), record.Y(i));

      if (!started || (flag & jumpBit) != 0) {
        if (started && close)
          path.Close();

        path.MoveTo(x, y);
        started = true;
        ++i;
        continue;
      }

      if ((flag & bezierBit) != 0 && i + 3 < record.PointCount) {
        var (cx1, cy1) = transform.Apply(record.X(i + 1), record.Y(i + 1));
        var (cx2, cy2) = transform.Apply(record.X(i + 2), record.Y(i + 2));
        var (ex, ey) = transform.Apply(record.X(i + 3), record.Y(i + 3));
        path.LineTo(x, y);
        path.CurveTo(cx1, cy1, cx2, cy2, ex, ey);
        i += 4;
        continue;
      }

      path.LineTo(x, y);
      ++i;
    }

    if (started && close)
      path.Close();

    return path;
  }

  private static void _AddBox(VectorPath path, GemRecord record, Matrix2D transform) {
    var (x0, y0) = transform.Apply(record.X(0), record.Y(0));
    var (x1, y1) = transform.Apply(record.X(1), record.Y(1));

    path.MoveTo(x0, y0);
    path.LineTo(x1, y0);
    path.LineTo(x1, y1);
    path.LineTo(x0, y1);
    path.Close();
  }

  private static void _AddRoundedBox(VectorPath path, GemRecord record, Matrix2D transform) {
    var (x0, y0) = transform.Apply(record.X(0), record.Y(0));
    var (x1, y1) = transform.Apply(record.X(1), record.Y(1));
    var left = Math.Min(x0, x1);
    var right = Math.Max(x0, x1);
    var top = Math.Min(y0, y1);
    var bottom = Math.Max(y0, y1);

    var radius = Math.Min(right - left, bottom - top) * _CornerFraction;
    if (radius <= 0) {
      path.AddRectangle(left, top, right - left, bottom - top);
      return;
    }

    path.MoveTo(left + radius, top);
    path.LineTo(right - radius, top);
    path.ArcTo(right - radius, top + radius, radius, radius, -Math.PI / 2, Math.PI / 2, false);
    path.LineTo(right, bottom - radius);
    path.ArcTo(right - radius, bottom - radius, radius, radius, 0, Math.PI / 2, false);
    path.LineTo(left + radius, bottom);
    path.ArcTo(left + radius, bottom - radius, radius, radius, Math.PI / 2, Math.PI / 2, false);
    path.LineTo(left, top + radius);
    path.ArcTo(left + radius, top + radius, radius, radius, Math.PI, Math.PI / 2, false);
    path.Close();
  }

  /// <summary>
  /// Adds an ellipse or a slice of one, which the interface states by centre, radii and two angles.
  /// </summary>
  /// <remarks>
  /// Angles run anticlockwise from east in tenths of a degree, and the end angle may be below the
  /// start, in which case the sweep goes the long way round through zero. That is the interface's
  /// rule and it is what makes a pie of more than half a turn come out the right way.
  /// </remarks>
  private static void _AddEllipse(VectorPath path, GemRecord record, Matrix2D transform, int fromTenths, int toTenths, bool closed) {
    var (centreX, centreY) = transform.Apply(record.X(0), record.Y(0));
    var (radiusX, radiusY) = transform.ApplyVector(record.X(1), record.Y(1));
    radiusX = Math.Abs(radiusX);
    radiusY = Math.Abs(radiusY);
    if (radiusX <= 0 || radiusY <= 0)
      return;

    var sweep = toTenths - fromTenths;
    if (sweep <= 0)
      sweep += 3600;

    var start = fromTenths * _TenthsOfDegreeToRadians;
    var span = sweep * _TenthsOfDegreeToRadians;

    // The transform turns y over for the raster, and the angles are stated in the drawing's frame,
    // so the sweep turns over with it.
    var flipped = transform.Determinant < 0;
    if (flipped) {
      start = -start;
      span = -span;
    }

    var whole = sweep >= 3600;
    if (!whole && closed)
      path.MoveTo(centreX, centreY);

    path.ArcTo(centreX, centreY, radiusX, radiusY, start, span, whole || !closed);

    if (closed)
      path.Close();
  }

  private static void _FillAndOutline(VectorCanvas canvas, VectorPath path, State state, Matrix2D transform, double unit) {
    if (path.IsEmpty)
      return;

    if (state.FillInterior != GemAttributes.InteriorHollow)
      canvas.Fill(path, FillRule.NonZero, GemAttributes.Colour(state.FillColour), GemAttributes.Stipple(state.FillInterior, state.FillStyle));

    if (state.FillPerimeter)
      _Stroke(canvas, path, state, transform, unit);
  }

  private static void _Stroke(VectorCanvas canvas, VectorPath path, State state, Matrix2D transform, double unit) {
    if (path.IsEmpty)
      return;

    var width = Math.Max(state.LineWidth * transform.MeanScale, 1);
    var dashes = GemAttributes.Dashes(state.LineType, unit);
    var line = dashes.Length > 0 ? path.Dashed(dashes) : path;

    canvas.Stroke(line, width, GemAttributes.Colour(state.LineColour), LineJoin.Round, LineCap.Round);
  }

  private static int _Integer(GemRecord record, int index, int fallback)
    => index < record.Integers.Length ? record.Integers[index] : fallback;
}
