using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Dxf;

/// <summary>Draws a drawing's entities onto a raster.</summary>
/// <remarks>
/// The entities are walked twice, for the same reason the HP-GL reader walks a plot twice: the
/// drawing's stated extents are frequently the ones AutoCAD writes when it has never computed them,
/// and then the only size the file really has is the box its geometry falls in. Both passes go
/// through the same code, so the box that is measured is the box that is drawn.
/// <para/>
/// Everything is projected onto the world xy plane. An entity's extrusion direction — group 210 —
/// would turn its own plane into the world's, and the drawings this matters for are the
/// three-dimensional ones this reader does not claim.
/// </remarks>
public static class DxfRenderer {

  /// <summary>Degrees to radians, for the groups that state an angle in degrees.</summary>
  private const double _DegreesToRadians = Math.PI / 180;

  /// <summary>
  /// The value AutoCAD writes into an extent variable it has never computed. Anything at or beyond
  /// it is not a corner of a real drawing.
  /// </summary>
  private const double _UncomputedExtent = 1e20;

  /// <summary>How wide the picture is drawn, in pixels, before the canvas cap applies.</summary>
  private const int _PreferredSide = 1024;

  /// <summary>How thick a line is drawn, in pixels. A drawing states lineweights the plot honours,
  /// not the widths a screen shows, so every line is a hairline here.</summary>
  private const double _StrokePixels = 1.0;

  /// <summary>How deep one block may be nested inside another before it is refused.</summary>
  private const int _MaxNesting = 16;

  /// <summary>How many shapes one drawing may lay down, which bounds what a wrong file can cost.</summary>
  private const int _MaxShapes = 1 << 20;

  /// <summary>How many copies an INSERT's row and column counts may place.</summary>
  private const int _MaxArray = 4096;

  /// <summary>How far a flattened curve may stray from the true one, in pixels.</summary>
  private const double _FlatteningPixels = 0.2;

  /// <summary>The fewest chords a whole turn is drawn with, which keeps a measured circle round.</summary>
  private const int _MinimumChords = 64;

  /// <summary>
  /// The AutoCAD Color Index for the nine colours the DXF Reference fixes. Index 7 is the one that
  /// takes the background's opposite, so on paper it is black.
  /// </summary>
  private static readonly Rgba32[] _Palette = [
    Rgba32.Black,
    new(255, 0, 0),
    new(255, 255, 0),
    new(0, 255, 0),
    new(0, 255, 255),
    new(0, 0, 255),
    new(255, 0, 255),
    Rgba32.Black,
    new(65, 65, 65),
    new(128, 128, 128)
  ];

  /// <summary>Draws the drawing at the size it states, or at the size its geometry has.</summary>
  public static RawImage Render(DxfFile file) {
    var drawing = DxfDrawing.From(file);

    var measured = _Measure(drawing);
    var (minX, minY, maxX, maxY) = _Extent(drawing) ?? measured
      ?? throw new InvalidDataException("A drawing exchange file whose entities draw nothing has no size.");

    // A drawing that is one straight line has no extent across it, and neither has one that is a
    // single row of text. The line's own thickness is what gives it a picture.
    var margin = Math.Max((maxX - minX + maxY - minY) * 0.01, 1e-6);
    minX -= margin;
    minY -= margin;
    maxX += margin;
    maxY += margin;

    var span = Math.Max(maxX - minX, maxY - minY);
    if (!double.IsFinite(span) || span <= 0)
      throw new InvalidDataException($"A drawing extent of {maxX - minX} by {maxY - minY} cannot be drawn.");

    var scale = _PreferredSide / span;
    var viewport = VectorViewport.FitCapped(minX, minY, maxX, maxY, (maxX - minX) * scale, (maxY - minY) * scale, true);
    var canvas = new VectorCanvas(viewport.Width, viewport.Height, Rgba32.White);

    _Play(drawing, viewport.Transform, canvas, null);

    return canvas.ToRawImage();
  }

  /// <summary>
  /// The extents the HEADER states, when they are a real box.
  /// </summary>
  /// <remarks>
  /// <c>$EXTMIN</c> and <c>$EXTMAX</c> are the lower-left and upper-right corners of the drawing
  /// extents in world coordinates. A drawing whose extents have never been computed carries
  /// <c>1.0E+20</c> and <c>-1.0E+20</c> instead, which is not a box and has to be passed over.
  /// </remarks>
  private static (double MinX, double MinY, double MaxX, double MaxY)? _Extent(DxfDrawing drawing) {
    var min = drawing.Variable("$EXTMIN");
    var max = drawing.Variable("$EXTMAX");
    if (min == null || max == null)
      return null;

    var minX = min.Number(10, double.NaN);
    var minY = min.Number(20, double.NaN);
    var maxX = max.Number(10, double.NaN);
    var maxY = max.Number(20, double.NaN);
    if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY))
      return null;

    if (Math.Abs(minX) >= _UncomputedExtent || Math.Abs(minY) >= _UncomputedExtent)
      return null;

    if (Math.Abs(maxX) >= _UncomputedExtent || Math.Abs(maxY) >= _UncomputedExtent)
      return null;

    return maxX > minX && maxY > minY ? (minX, minY, maxX, maxY) : null;
  }

  /// <summary>The box the geometry falls in, or nothing when there is none.</summary>
  private static (double MinX, double MinY, double MaxX, double MaxY)? _Measure(DxfDrawing drawing) {
    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
    var any = false;

    _Play(drawing, Matrix2D.Identity, null, (x, y) => {
      any = true;
      minX = Math.Min(minX, x);
      minY = Math.Min(minY, y);
      maxX = Math.Max(maxX, x);
      maxY = Math.Max(maxY, y);
    });

    return any ? (minX, minY, maxX, maxY) : null;
  }

  /// <summary>What one pass of the drawing needs to know about itself.</summary>
  private sealed class Pass {
    public required DxfDrawing Drawing;
    public required Matrix2D ToDevice;
    public VectorCanvas? Canvas;
    public Action<double, double>? Note;
    public int Shapes;

    /// <summary>How much bigger a device pixel is than a drawing unit, for flattening curves.</summary>
    public double DeviceScale => Math.Max(this.ToDevice.MeanScale, 1e-9);
  }

  private static void _Play(DxfDrawing drawing, Matrix2D toDevice, VectorCanvas? canvas, Action<double, double>? note) {
    var pass = new Pass { Drawing = drawing, ToDevice = toDevice, Canvas = canvas, Note = note };
    _Entities(pass, drawing.Entities, Matrix2D.Identity, 7, 0);
  }

  /// <summary>Draws a run of entities under a transform, which is a block's placement or nothing.</summary>
  private static void _Entities(Pass pass, List<DxfEntity> entities, Matrix2D local, int inherited, int depth) {
    foreach (var entity in entities)
      _Entity(pass, entity, local, inherited, depth);
  }

  private static void _Entity(Pass pass, DxfEntity entity, Matrix2D local, int inherited, int depth) {
    if (++pass.Shapes > _MaxShapes)
      throw new InvalidDataException($"A drawing of more than {_MaxShapes} entities is refused rather than drawn.");

    var colour = _Colour(pass.Drawing, entity, inherited);
    var path = new VectorPath();

    switch (entity.Type) {
      case "LINE": {
        _Move(pass, path, local, entity.Number(10, 0), entity.Number(20, 0));
        _Line(pass, path, local, entity.Number(11, 0), entity.Number(21, 0));
        _Stroke(pass, path, colour);
        return;
      }

      case "POINT": {
        // A point has no size of its own. What is drawn is the smallest mark that shows where it
        // is, and in the measuring pass it is simply the point.
        var (x, y) = (entity.Number(10, 0), entity.Number(20, 0));
        pass.Note?.Invoke(x, y);
        if (pass.Canvas != null) {
          var (dx, dy) = _Both(local, pass.ToDevice, x, y);
          path.AddEllipse(dx, dy, _StrokePixels, _StrokePixels);
          pass.Canvas.Fill(path, FillRule.NonZero, colour);
        }

        return;
      }

      case "CIRCLE": {
        _Arc(pass, path, local, entity.Number(10, 0), entity.Number(20, 0), entity.Number(40, 0), 0, 2 * Math.PI, true);
        path.Close();
        _Stroke(pass, path, colour);
        return;
      }

      case "ARC": {
        var start = entity.Number(50, 0) * _DegreesToRadians;
        var end = entity.Number(51, 0) * _DegreesToRadians;
        var sweep = end - start;

        // The angles are measured anticlockwise and the arc runs from the first to the second the
        // same way round, so a pair that reads backwards has gone the long way about.
        while (sweep <= 0)
          sweep += 2 * Math.PI;

        _Arc(pass, path, local, entity.Number(10, 0), entity.Number(20, 0), entity.Number(40, 0), start, sweep, true);
        _Stroke(pass, path, colour);
        return;
      }

      case "ELLIPSE": {
        _Ellipse(pass, path, local, entity);
        _Stroke(pass, path, colour);
        return;
      }

      case "LWPOLYLINE": {
        _LightweightPolyline(pass, path, local, entity);
        _Stroke(pass, path, colour);
        return;
      }

      case "POLYLINE": {
        var flags = entity.Integer(70, 0);

        // Bits 16 and 64 make the entity a polygon or polyface mesh, whose VERTEX records are face
        // indices rather than a path. Those are surfaces, and this reader does not draw surfaces.
        if ((flags & (16 | 64)) != 0)
          return;

        _Polyline(pass, path, local, entity, (flags & 1) != 0);
        _Stroke(pass, path, colour);
        return;
      }

      case "SOLID" or "TRACE": {
        if (!_Quadrilateral(pass, path, local, entity))
          return;

        if (pass.Canvas != null)
          pass.Canvas.Fill(path, FillRule.NonZero, colour);

        return;
      }

      case "3DFACE": {
        if (_Quadrilateral(pass, path, local, entity))
          _Stroke(pass, path, colour);

        return;
      }

      // A block's own entities may draw BYBLOCK, which means in whatever colour the INSERT that
      // placed them was drawn in, so that index goes down rather than the colour.
      case "INSERT": {
        _Insert(pass, entity, local, _Index(pass.Drawing, entity, inherited), depth);
        return;
      }
    }
  }

  /// <summary>Places a block under an INSERT's own position, scale and rotation.</summary>
  private static void _Insert(Pass pass, DxfEntity entity, Matrix2D local, int inherited, int depth) {
    if (depth >= _MaxNesting)
      throw new InvalidDataException($"A block nested more than {_MaxNesting} deep, which a drawing cannot legitimately be.");

    var name = entity.Text(2);
    if (name == null)
      throw new InvalidDataException("An INSERT with no block name.");

    // An INSERT names a definition in the BLOCKS section. One that names nothing is a drawing
    // referring to a shape it does not carry, and there is no honest way to draw that.
    if (!pass.Drawing.Blocks.TryGetValue(name, out var block))
      throw new InvalidDataException($"An INSERT places the block \"{name}\", which the BLOCKS section does not define.");

    var scaleX = entity.Number(41, 1);
    var scaleY = entity.Number(42, 1);
    if (scaleX == 0 || scaleY == 0)
      return;

    var rotation = entity.Number(50, 0) * _DegreesToRadians;
    var columns = Math.Max(1, entity.Integer(70, 1));
    var rows = Math.Max(1, entity.Integer(71, 1));
    if ((long)columns * rows > _MaxArray)
      throw new InvalidDataException($"An INSERT states {columns} by {rows} copies, which is more than a drawing places.");

    var columnStep = entity.Number(44, 0);
    var rowStep = entity.Number(45, 0);
    var x = entity.Number(10, 0);
    var y = entity.Number(20, 0);

    for (var row = 0; row < rows; ++row)
    for (var column = 0; column < columns; ++column) {
      var placement = Matrix2D
        .Translation(-block.BaseX, -block.BaseY)
        .Then(Matrix2D.Scaling(scaleX, scaleY))
        .Then(Matrix2D.Rotation(rotation))
        .Then(Matrix2D.Translation(x + column * columnStep, y + row * rowStep))
        .Then(local);

      _Entities(pass, block.Entities, placement, inherited, depth + 1);
    }
  }

  /// <summary>
  /// Reads an LWPOLYLINE's vertices, whose coordinates and bulges are repeated groups rather than
  /// one group each.
  /// </summary>
  private static void _LightweightPolyline(Pass pass, VectorPath path, Matrix2D local, DxfEntity entity) {
    var xs = new List<double>();
    var ys = new List<double>();
    var bulges = new List<double>();

    foreach (var pair in entity.Pairs)
      switch (pair.Code) {
        case 10:
          xs.Add(_Number(entity, pair));
          ys.Add(double.NaN);
          bulges.Add(0);
          break;

        case 20 when xs.Count > 0:
          ys[^1] = _Number(entity, pair);
          break;

        case 42 when xs.Count > 0:
          bulges[^1] = _Number(entity, pair);
          break;
      }

    // Group 90 states how many vertices there are. A count that does not match what follows means
    // the entity was written or truncated wrongly, and drawing whichever number happens to be there
    // would be drawing a shape the file does not describe.
    var stated = entity.Integer(90, xs.Count);
    if (stated != xs.Count)
      throw new InvalidDataException($"An LWPOLYLINE states {stated} vertices but carries {xs.Count}.");

    for (var i = 0; i < ys.Count; ++i)
      if (double.IsNaN(ys[i]))
        throw new InvalidDataException($"Vertex {i + 1} of an LWPOLYLINE has an x but no y.");

    _Walk(pass, path, local, xs, ys, bulges, (entity.Integer(70, 0) & 1) != 0);
  }

  /// <summary>Reads a POLYLINE's vertices, which are the VERTEX entities that follow it.</summary>
  private static void _Polyline(Pass pass, VectorPath path, Matrix2D local, DxfEntity entity, bool closed) {
    var xs = new List<double>();
    var ys = new List<double>();
    var bulges = new List<double>();

    foreach (var vertex in entity.Vertices) {
      // A vertex added by curve or spline fitting is a point on the fitted curve, and a spline
      // frame control point is not on the curve at all. Drawing the control points would draw the
      // frame rather than the curve.
      if ((vertex.Integer(70, 0) & 16) != 0)
        continue;

      xs.Add(vertex.Number(10, 0));
      ys.Add(vertex.Number(20, 0));
      bulges.Add(vertex.Number(42, 0));
    }

    _Walk(pass, path, local, xs, ys, bulges, closed);
  }

  /// <summary>
  /// Runs along a polyline's vertices, turning each pair into a straight segment or, where the
  /// first of the pair carries a bulge, into the arc that bulge describes.
  /// </summary>
  /// <remarks>
  /// Autodesk's VERTEX page: the bulge is the tangent of one fourth the included angle for an arc
  /// segment, made negative if the arc goes clockwise from the start point to the endpoint; a bulge
  /// of nothing is a straight segment and a bulge of one is a semicircle.
  /// </remarks>
  private static void _Walk(Pass pass, VectorPath path, Matrix2D local, List<double> xs, List<double> ys, List<double> bulges, bool closed) {
    if (xs.Count == 0)
      return;

    if (xs.Count == 1) {
      _Move(pass, path, local, xs[0], ys[0]);
      return;
    }

    _Move(pass, path, local, xs[0], ys[0]);
    var segments = closed ? xs.Count : xs.Count - 1;
    for (var i = 0; i < segments; ++i) {
      var next = (i + 1) % xs.Count;
      var bulge = bulges[i];
      if (bulge == 0) {
        _Line(pass, path, local, xs[next], ys[next]);
        continue;
      }

      var dx = xs[next] - xs[i];
      var dy = ys[next] - ys[i];
      var chord = Math.Sqrt(dx * dx + dy * dy);
      if (chord <= 0) {
        _Line(pass, path, local, xs[next], ys[next]);
        continue;
      }

      var included = 4 * Math.Atan(bulge);
      var radius = chord / (2 * Math.Sin(included / 2));
      var apothem = radius * Math.Cos(included / 2);

      // The centre sits off the chord's midpoint along the chord's left-hand normal by the apothem,
      // whose sign carries whether the arc is the short way round or the long way.
      var centreX = (xs[i] + xs[next]) / 2 - dy / chord * apothem;
      var centreY = (ys[i] + ys[next]) / 2 + dx / chord * apothem;
      var start = Math.Atan2(ys[i] - centreY, xs[i] - centreX);

      _Arc(pass, path, local, centreX, centreY, Math.Abs(radius), start, included, false);
    }

    if (closed)
      path.Close();
  }

  /// <summary>
  /// Reads an ELLIPSE, whose major axis is given as an endpoint relative to the centre and whose
  /// ends are parameters rather than angles.
  /// </summary>
  /// <remarks>
  /// Autodesk's ELLIPSE page: 11/21/31 is the endpoint of the major axis relative to the centre, 40
  /// is the ratio of the minor axis to the major, and 41 and 42 are the start and end parameters —
  /// zero and two pi for a whole ellipse. The point at parameter <c>t</c> is the centre plus
  /// <c>cos t</c> along the major axis plus <c>sin t</c> along the minor, which is the major turned
  /// a quarter turn and shortened by the ratio.
  /// </remarks>
  private static void _Ellipse(Pass pass, VectorPath path, Matrix2D local, DxfEntity entity) {
    var centreX = entity.Number(10, 0);
    var centreY = entity.Number(20, 0);
    var majorX = entity.Number(11, 0);
    var majorY = entity.Number(21, 0);
    var ratio = entity.Number(40, 1);
    var start = entity.Number(41, 0);
    var end = entity.Number(42, 2 * Math.PI);

    var major = Math.Sqrt(majorX * majorX + majorY * majorY);
    if (major <= 0)
      return;

    var sweep = end - start;
    if (sweep <= 0)
      sweep += 2 * Math.PI;

    var minorX = -majorY * ratio;
    var minorY = majorX * ratio;
    var steps = _Chords(pass, major * Math.Max(1, Math.Abs(ratio)), sweep);
    for (var i = 0; i <= steps; ++i) {
      var t = start + sweep * i / steps;
      var (sin, cos) = Math.SinCos(t);
      var x = centreX + cos * majorX + sin * minorX;
      var y = centreY + cos * majorY + sin * minorY;
      if (i == 0)
        _Move(pass, path, local, x, y);
      else
        _Line(pass, path, local, x, y);
    }

    if (sweep >= 2 * Math.PI - 1e-9)
      path.Close();
  }

  /// <summary>
  /// Reads the four corners of a SOLID, TRACE or 3DFACE.
  /// </summary>
  /// <remarks>
  /// The corners are stored 10/11/12/13, and the third and fourth are the far pair rather than the
  /// next two round: joining them in the order they are written gives a bow tie, and the shape the
  /// drawing means is first, second, fourth, third. Where only three corners were entered the
  /// fourth repeats the third, which the same order turns into a triangle by itself.
  /// </remarks>
  private static bool _Quadrilateral(Pass pass, VectorPath path, Matrix2D local, DxfEntity entity) {
    if (entity.Text(10) == null || entity.Text(11) == null || entity.Text(12) == null)
      return false;

    var x1 = entity.Number(10, 0);
    var y1 = entity.Number(20, 0);
    var x2 = entity.Number(11, 0);
    var y2 = entity.Number(21, 0);
    var x3 = entity.Number(12, 0);
    var y3 = entity.Number(22, 0);
    var x4 = entity.Number(13, x3);
    var y4 = entity.Number(23, y3);

    _Move(pass, path, local, x1, y1);
    _Line(pass, path, local, x2, y2);
    _Line(pass, path, local, x4, y4);
    _Line(pass, path, local, x3, y3);
    path.Close();

    return true;
  }

  /// <summary>Adds an arc about a centre, flattened finely enough for the size it is drawn at.</summary>
  private static void _Arc(Pass pass, VectorPath path, Matrix2D local, double centreX, double centreY, double radius, double start, double sweep, bool startsNew) {
    if (radius <= 0 || !double.IsFinite(radius))
      return;

    var steps = _Chords(pass, radius, sweep);
    for (var i = 0; i <= steps; ++i) {
      var angle = start + sweep * i / steps;
      var (sin, cos) = Math.SinCos(angle);
      var x = centreX + radius * cos;
      var y = centreY + radius * sin;
      if (i == 0 && startsNew)
        _Move(pass, path, local, x, y);
      else
        _Line(pass, path, local, x, y);
    }
  }

  /// <summary>
  /// How many chords an arc of this radius is drawn with, so that the measuring pass and the
  /// drawing pass both see a curve rather than a polygon.
  /// </summary>
  private static int _Chords(Pass pass, double radius, double sweep) {
    var turns = Math.Abs(sweep) / (2 * Math.PI);
    var device = radius * pass.DeviceScale;
    var fromTolerance = device > _FlatteningPixels
      ? Math.Abs(sweep) / (2 * Math.Acos(1 - _FlatteningPixels / device))
      : 4;

    var steps = (int)Math.Ceiling(Math.Max(fromTolerance, turns * _MinimumChords));

    return Math.Clamp(steps, 4, 4096);
  }

  private static void _Move(Pass pass, VectorPath path, Matrix2D local, double x, double y) {
    var (mx, my) = local.Apply(x, y);
    pass.Note?.Invoke(mx, my);
    if (pass.Canvas != null) {
      var (dx, dy) = pass.ToDevice.Apply(mx, my);
      path.MoveTo(dx, dy);
    }
  }

  private static void _Line(Pass pass, VectorPath path, Matrix2D local, double x, double y) {
    var (mx, my) = local.Apply(x, y);
    pass.Note?.Invoke(mx, my);
    if (pass.Canvas != null) {
      var (dx, dy) = pass.ToDevice.Apply(mx, my);
      path.LineTo(dx, dy);
    }
  }

  private static (double X, double Y) _Both(Matrix2D local, Matrix2D toDevice, double x, double y) {
    var (mx, my) = local.Apply(x, y);
    return toDevice.Apply(mx, my);
  }

  private static void _Stroke(Pass pass, VectorPath path, Rgba32 colour) {
    if (pass.Canvas != null && !path.IsEmpty)
      pass.Canvas.Stroke(path, _StrokePixels, colour, LineJoin.Round, LineCap.Round);
  }

  private static double _Number(DxfEntity entity, DxfPair pair) {
    if (!double.TryParse(pair.Value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
      throw new InvalidDataException($"Group {pair.Code} of a {entity.Type} is \"{pair.Value}\", which is not a number.");

    return value;
  }

  /// <summary>
  /// Which colour index an entity draws in.
  /// </summary>
  /// <remarks>
  /// Autodesk's common entity codes: group 62 is the colour number, absent or 256 meaning BYLAYER
  /// and zero meaning BYBLOCK, and a negative value meaning the layer is turned off. BYLAYER goes to
  /// the LAYER table's colour for the entity's layer; BYBLOCK goes to whatever the block was placed
  /// in, which is what the inherited index carries.
  /// </remarks>
  private static int _Index(DxfDrawing drawing, DxfEntity entity, int inherited) {
    var stated = entity.Integer(62, 256);
    if (stated == 0)
      return inherited;

    if (stated is > 0 and < 256)
      return stated;

    var layer = entity.Text(8);
    if (layer != null && drawing.LayerColours.TryGetValue(layer, out var byLayer))
      return byLayer;

    return 7;
  }

  private static Rgba32 _Colour(DxfDrawing drawing, DxfEntity entity, int inherited) {
    var index = _Index(drawing, entity, inherited);

    // Only the first nine indices have colours the reference itself fixes. The rest of the index is
    // a table AutoCAD ships rather than a table the format states, so anything outside is drawn in
    // ink rather than in a guess.
    return index > 0 && index < _Palette.Length ? _Palette[index] : Rgba32.Black;
  }
}
