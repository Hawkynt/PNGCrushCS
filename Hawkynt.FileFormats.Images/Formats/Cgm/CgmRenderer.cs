using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Cgm;

/// <summary>Draws a metafile's commands onto a raster.</summary>
/// <remarks>
/// The stream is walked twice for the same reason a metafile has to be: the picture's extent is
/// stated by a command inside it, and everything before that command sets the precisions the extent
/// itself is read at. So the first pass reads the descriptor commands and stops when it knows how
/// big the picture is, and the second draws with that known.
/// </remarks>
public static class CgmRenderer {

  private const int _ClassDelimiter = 0, _ClassMetafileDescriptor = 1, _ClassPictureDescriptor = 2;
  private const int _ClassControl = 3, _ClassPrimitive = 4, _ClassAttribute = 5;

  /// <summary>How many pixels a picture stated in abstract units gets per unit at most.</summary>
  private const int _AbstractSideLimit = 2048;

  /// <summary>Draws the metafile at the extent it states.</summary>
  public static RawImage Render(CgmFile file) {
    if (file.Commands == null)
      throw new InvalidDataException("A metafile with no commands cannot be drawn.");

    var state = new CgmState();
    _ReadDescriptors(file, state);

    var (x1, y1, x2, y2) = state.VdcExtent;
    var minX = Math.Min(x1, x2);
    var maxX = Math.Max(x1, x2);
    var minY = Math.Min(y1, y2);
    var maxY = Math.Max(y1, y2);
    if (maxX <= minX || maxY <= minY)
      throw new InvalidDataException($"A metafile states an extent of {maxX - minX} by {maxY - minY}, which cannot be drawn.");

    // The extent is in the picture's own units and the standard does not say how big one is unless
    // the file states a scale, so one unit is one pixel, and a picture too large for that is brought
    // inside a sensible limit — which for a drawing keeps the shapes and only changes how finely
    // they land.
    //
    // It is never enlarged. A metafile may carry a cell array, and that is a raster: enlarging it by
    // a factor the extent happens to produce resamples the very pixels the file was written to hold,
    // and at a factor that is not a whole number they cannot be got back.
    var width = maxX - minX;
    var height = maxY - minY;
    var scale = Math.Min(_AbstractSideLimit / Math.Max(width, height), 1);

    // The picture's y axis points up, as it does everywhere the standard is used, and the first row
    // of a raster is its top.
    var viewport = VectorViewport.FitCapped(minX, minY, maxX, maxY, width * scale, height * scale, true);
    var canvas = new VectorCanvas(viewport.Width, viewport.Height, state.Background);

    _Draw(file, new CgmState { Background = state.Background }, viewport.Transform, canvas);

    return canvas.ToRawImage();
  }

  /// <summary>Reads everything up to the start of the first picture's body.</summary>
  private static void _ReadDescriptors(CgmFile file, CgmState state) {
    foreach (var command in file.Commands) {
      _Apply(command, state);

      // BEGIN PICTURE BODY: everything that says how big the picture is has been seen by now.
      if (command.ElementClass == _ClassDelimiter && command.ElementId == 4)
        return;
    }
  }

  private static void _Draw(CgmFile file, CgmState state, Matrix2D transform, VectorCanvas canvas) {
    foreach (var command in file.Commands) {
      if (command.ElementClass != _ClassPrimitive) {
        _Apply(command, state);
        continue;
      }

      var parameters = new CgmParameters(command.Parameters, state);
      switch (command.ElementId) {
        case 1:
          _Stroke(canvas, _Points(parameters, transform, false), state, transform);
          break;

        case 2:
          _StrokeDisjoint(canvas, parameters, state, transform);
          break;

        case 7:
          _FillAndEdge(canvas, _Points(parameters, transform, true), state, transform);
          break;

        case 8:
          _FillAndEdge(canvas, _PolygonSet(parameters, transform), state, transform);
          break;

        case 11: {
          var (ax, ay) = parameters.Point();
          var (bx, by) = parameters.Point();
          var path = new VectorPath();
          _AddRectangle(path, transform, ax, ay, bx, by);
          _FillAndEdge(canvas, path, state, transform);
          break;
        }

        case 12: {
          var (cx, cy) = parameters.Point();
          var radius = parameters.Vdc();
          var path = new VectorPath();
          _AddEllipse(path, transform, cx, cy, radius, radius);
          _FillAndEdge(canvas, path, state, transform);
          break;
        }

        case 17: {
          var (cx, cy) = parameters.Point();
          var (ax, ay) = parameters.Point();
          var (bx, by) = parameters.Point();
          var path = new VectorPath();
          _AddConjugateEllipse(path, transform, cx, cy, ax, ay, bx, by);
          _FillAndEdge(canvas, path, state, transform);
          break;
        }

        case 15:
        case 16: {
          var (cx, cy) = parameters.Point();
          var (sx, sy) = parameters.Point();
          var (ex, ey) = parameters.Point();
          var radius = parameters.Vdc();
          var path = new VectorPath();
          var closed = command.ElementId == 16;
          _AddCentreArc(path, transform, cx, cy, sx, sy, ex, ey, radius, closed);

          if (closed)
            _FillAndEdge(canvas, path, state, transform);
          else
            _Stroke(canvas, path, state, transform);

          break;
        }

        case 9:
          _CellArray(canvas, parameters, transform);
          break;
      }
    }
  }

  /// <summary>Draws a cell array: a rectangular grid of colours placed by three of its corners.</summary>
  /// <remarks>
  /// The standard states the array by two diagonally opposite corners <c>P</c> and <c>Q</c> and a
  /// third corner <c>R</c>. A row of cells runs from <c>P</c> towards <c>R</c> and the rows advance
  /// from <c>R</c> towards <c>Q</c>, so the first cell stored is the one at <c>P</c> and the last is
  /// the one at <c>Q</c>. That makes the grid a parallelogram rather than a rectangle where the two
  /// directions are not perpendicular, which the placement handles because it is an affine map from
  /// the cell grid onto the picture.
  /// <para/>
  /// In the binary encoding each row of cells starts on a word boundary, so a row of an odd number
  /// of bytes is followed by one the parameter list does not otherwise account for. Reading straight
  /// on through would shift every row after the first by a byte — a picture that leans, which is
  /// exactly the fault a round trip against a writer making the same mistake would not show.
  /// </remarks>
  private static void _CellArray(VectorCanvas canvas, CgmParameters parameters, Matrix2D transform) {
    var (px, py) = parameters.Point();
    var (qx, qy) = parameters.Point();
    var (rx, ry) = parameters.Point();
    var nx = parameters.Integer();
    var ny = parameters.Integer();
    var precision = parameters.Integer();

    if (nx < 1 || ny < 1 || (long)nx * ny > VectorCanvas.MaximumPixels)
      throw new InvalidDataException($"A metafile states a cell array of {nx} by {ny} cells.");

    var perCell = parameters.ColourSize(precision);
    if (perCell < 1)
      throw new InvalidDataException("A metafile states a cell array whose colours take no bytes at all.");

    var pixels = new byte[nx * ny * 4];
    for (var row = 0; row < ny; ++row) {
      for (var column = 0; column < nx; ++column) {
        if (parameters.Remaining < perCell)
          throw new InvalidDataException($"A cell array of {nx} by {ny} runs out of colours at row {row}.");

        var colour = parameters.Colour(precision);
        var at = (row * nx + column) * 4;
        pixels[at] = colour.R;
        pixels[at + 1] = colour.G;
        pixels[at + 2] = colour.B;
        pixels[at + 3] = 255;
      }

      parameters.AlignToWord();
    }

    var image = new RawImage { Width = nx, Height = ny, Format = PixelFormat.Rgba32, PixelData = pixels };

    // The cell grid's own coordinates onto the picture's: one step across is P to R over nx cells,
    // one step down is R to Q over ny rows, and the grid's origin is P.
    var placement = new Matrix2D((rx - px) / nx, (ry - py) / nx, (qx - rx) / ny, (qy - ry) / ny, px, py).Then(transform);
    canvas.DrawImage(image, placement);
  }

  private static void _Apply(CgmCommand command, CgmState state) {
    var parameters = new CgmParameters(command.Parameters, state);

    switch (command.ElementClass) {
      case _ClassMetafileDescriptor:
        switch (command.ElementId) {
          case 3:
            state.VdcIsInteger = parameters.Enumeration() == 0;
            break;
          case 4:
            state.IntegerPrecision = _Bits(parameters.Integer(), state.IntegerPrecision);
            break;
          case 5: {
            var form = parameters.Enumeration();
            var whole = parameters.Integer();
            var fraction = parameters.Integer();
            state.RealIsFloating = form == 0;
            state.RealWhole = whole;
            state.RealFraction = fraction;
            break;
          }

          case 6:
            state.IndexPrecision = _Bits(parameters.Integer(), state.IndexPrecision);
            break;
          case 7:
            state.ColourPrecision = _Bits(parameters.Integer(), state.ColourPrecision);
            break;
          case 8:
            state.ColourIndexPrecision = _Bits(parameters.Integer(), state.ColourIndexPrecision);
            break;
          case 10: {
            // Stated as the darkest colour and then the brightest, each a full set of components
            // at the current colour precision.
            var bits = state.ColourPrecision;
            state.ColourMinimum = [parameters.Unsigned(bits), parameters.Unsigned(bits), parameters.Unsigned(bits)];
            state.ColourMaximum = [parameters.Unsigned(bits), parameters.Unsigned(bits), parameters.Unsigned(bits)];
            break;
          }
        }

        return;

      case _ClassPictureDescriptor:
        switch (command.ElementId) {
          case 2:
            state.DirectColour = parameters.Enumeration() == 1;
            break;
          case 3:
            state.LineWidthIsAbsolute = parameters.Enumeration() == 0;
            break;
          case 5:
            state.EdgeWidthIsAbsolute = parameters.Enumeration() == 0;
            break;
          case 6: {
            var (x1, y1) = parameters.Point();
            var (x2, y2) = parameters.Point();
            state.VdcExtent = (x1, y1, x2, y2);
            break;
          }

          case 7:
            state.Background = parameters.DirectColour();
            break;
        }

        return;

      case _ClassControl:
        switch (command.ElementId) {
          case 1:
            state.VdcIntegerPrecision = _Bits(parameters.Integer(), state.VdcIntegerPrecision);
            break;
          case 2: {
            var form = parameters.Enumeration();
            state.VdcRealIsFloating = form == 0;
            state.VdcRealWhole = parameters.Integer();
            state.VdcRealFraction = parameters.Integer();
            break;
          }
        }

        return;

      case _ClassAttribute:
        switch (command.ElementId) {
          case 2:
            state.LineType = parameters.Index();
            break;
          case 3:
            state.LineWidth = state.LineWidthIsAbsolute ? parameters.Vdc() : parameters.Real();
            break;
          case 4:
            state.LineColour = parameters.Colour();
            break;
          case 22:
            state.InteriorStyle = parameters.Enumeration();
            break;
          case 23:
            state.FillColour = parameters.Colour();
            break;
          case 24:
            state.HatchIndex = parameters.Index();
            break;
          case 27:
            state.EdgeType = parameters.Index();
            break;
          case 28:
            state.EdgeWidth = state.EdgeWidthIsAbsolute ? parameters.Vdc() : parameters.Real();
            break;
          case 29:
            state.EdgeColour = parameters.Colour();
            break;
          case 30:
            state.EdgeVisible = parameters.Enumeration() == 1;
            break;
          case 34: {
            var index = parameters.Unsigned(state.ColourIndexPrecision);
            while (parameters.Remaining >= state.ColourPrecision / 8 * 3)
              state.ColourTable[index++] = parameters.DirectColour();

            break;
          }
        }

        return;
    }
  }

  /// <summary>A precision the standard allows, or what was already in force.</summary>
  private static int _Bits(int stated, int current) => stated is 8 or 16 or 24 or 32 ? stated : current;

  private static VectorPath _Points(CgmParameters parameters, Matrix2D transform, bool close) {
    var path = new VectorPath();
    var first = true;

    while (parameters.Remaining >= parameters.VdcSize * 2) {
      var (x, y) = parameters.Point();
      var (dx, dy) = transform.Apply(x, y);
      if (first) {
        path.MoveTo(dx, dy);
        first = false;
      } else
        path.LineTo(dx, dy);
    }

    if (close)
      path.Close();

    return path;
  }

  private static void _StrokeDisjoint(VectorCanvas canvas, CgmParameters parameters, CgmState state, Matrix2D transform) {
    var path = new VectorPath();
    while (parameters.Remaining >= parameters.VdcSize * 4) {
      var (ax, ay) = parameters.Point();
      var (bx, by) = parameters.Point();
      var (dax, day) = transform.Apply(ax, ay);
      var (dbx, dby) = transform.Apply(bx, by);
      path.MoveTo(dax, day);
      path.LineTo(dbx, dby);
    }

    _Stroke(canvas, path, state, transform);
  }

  /// <summary>
  /// A polygon set: points each carrying a flag saying whether the edge leaving it is drawn and
  /// whether it closes the contour.
  /// </summary>
  private static VectorPath _PolygonSet(CgmParameters parameters, Matrix2D transform) {
    const int closeFlagBit = 2;

    var path = new VectorPath();
    var open = false;

    while (parameters.Remaining >= parameters.VdcSize * 2 + 2) {
      var (x, y) = parameters.Point();
      var flag = parameters.Enumeration();
      var (dx, dy) = transform.Apply(x, y);

      if (!open) {
        path.MoveTo(dx, dy);
        open = true;
      } else
        path.LineTo(dx, dy);

      if (flag < closeFlagBit)
        continue;

      path.Close();
      open = false;
    }

    if (open)
      path.Close();

    return path;
  }

  private static void _AddRectangle(VectorPath path, Matrix2D transform, double ax, double ay, double bx, double by) {
    var (p0x, p0y) = transform.Apply(ax, ay);
    var (p1x, p1y) = transform.Apply(bx, ay);
    var (p2x, p2y) = transform.Apply(bx, by);
    var (p3x, p3y) = transform.Apply(ax, by);
    path.MoveTo(p0x, p0y);
    path.LineTo(p1x, p1y);
    path.LineTo(p2x, p2y);
    path.LineTo(p3x, p3y);
    path.Close();
  }

  private static void _AddEllipse(VectorPath path, Matrix2D transform, double cx, double cy, double rx, double ry) {
    const int steps = 96;
    for (var i = 0; i <= steps; ++i) {
      var angle = 2 * Math.PI * i / steps;
      var (sin, cos) = Math.SinCos(angle);
      var (px, py) = transform.Apply(cx + rx * cos, cy + ry * sin);
      if (i == 0)
        path.MoveTo(px, py);
      else
        path.LineTo(px, py);
    }

    path.Close();
  }

  /// <summary>
  /// An ellipse stated by its centre and the ends of two conjugate diameters.
  /// </summary>
  /// <remarks>
  /// The two diameters need not be the axes, so the shape is a circle mapped through the matrix
  /// those two vectors make. That is exactly what a conjugate pair means and it saves solving for
  /// the axes, which are not needed to draw it.
  /// </remarks>
  private static void _AddConjugateEllipse(VectorPath path, Matrix2D transform, double cx, double cy, double ax, double ay, double bx, double by) {
    var frame = new Matrix2D(ax - cx, ay - cy, bx - cx, by - cy, cx, cy).Then(transform);

    const int steps = 96;
    for (var i = 0; i <= steps; ++i) {
      var angle = 2 * Math.PI * i / steps;
      var (sin, cos) = Math.SinCos(angle);
      var (px, py) = frame.Apply(cos, sin);
      if (i == 0)
        path.MoveTo(px, py);
      else
        path.LineTo(px, py);
    }

    path.Close();
  }

  private static void _AddCentreArc(VectorPath path, Matrix2D transform, double cx, double cy, double sx, double sy, double ex, double ey, double radius, bool closed) {
    var start = Math.Atan2(sy, sx);
    var end = Math.Atan2(ey, ex);
    var sweep = end - start;
    if (sweep <= 0)
      sweep += 2 * Math.PI;

    var steps = Math.Clamp((int)Math.Ceiling(sweep / (Math.PI / 48)), 2, 512);

    if (closed) {
      var (centreX, centreY) = transform.Apply(cx, cy);
      path.MoveTo(centreX, centreY);
    }

    for (var i = 0; i <= steps; ++i) {
      var angle = start + sweep * i / steps;
      var (sin, cos) = Math.SinCos(angle);
      var (px, py) = transform.Apply(cx + radius * cos, cy + radius * sin);
      if (i == 0 && !closed)
        path.MoveTo(px, py);
      else
        path.LineTo(px, py);
    }

    if (closed)
      path.Close();
  }

  private static void _FillAndEdge(VectorCanvas canvas, VectorPath path, CgmState state, Matrix2D transform) {
    if (path.IsEmpty)
      return;

    switch (state.InteriorStyle) {
      case CgmState.InteriorSolid:
        canvas.Fill(path, FillRule.EvenOdd, state.FillColour);
        break;
      case CgmState.InteriorHatch:
      case CgmState.InteriorPattern:
        canvas.Fill(path, FillRule.EvenOdd, state.FillColour, CgmState.Hatch(state.HatchIndex));
        break;
    }

    if (!state.EdgeVisible)
      return;

    var width = Math.Max(_Width(state.EdgeWidth, state.EdgeWidthIsAbsolute, transform), 1);
    var dashes = CgmState.Dashes(state.EdgeType, width);
    var line = dashes.Length > 0 ? path.Dashed(dashes) : path;
    canvas.Stroke(line, width, state.EdgeColour, LineJoin.Round, LineCap.Round);
  }

  private static void _Stroke(VectorCanvas canvas, VectorPath path, CgmState state, Matrix2D transform) {
    if (path.IsEmpty)
      return;

    var width = Math.Max(_Width(state.LineWidth, state.LineWidthIsAbsolute, transform), 1);
    var dashes = CgmState.Dashes(state.LineType, width);
    var line = dashes.Length > 0 ? path.Dashed(dashes) : path;
    canvas.Stroke(line, width, state.LineColour, LineJoin.Round, LineCap.Round);
  }

  /// <summary>
  /// A width in pixels, whether the file stated it in its own units or as a multiple of the
  /// device's own nominal line.
  /// </summary>
  private static double _Width(double stated, bool absolute, Matrix2D transform)
    => absolute ? Math.Abs(stated) * transform.MeanScale : Math.Abs(stated);
}
