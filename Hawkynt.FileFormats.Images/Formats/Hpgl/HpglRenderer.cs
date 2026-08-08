using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Hpgl;

/// <summary>Plays an HP-GL plot back onto a raster.</summary>
/// <remarks>
/// The plot is played twice. The first pass only records where the pen went, because a plot states
/// no page of its own beyond a frame the pen is free to leave — and every one of these files does
/// leave it. The extent of the ink is therefore the picture, and it is measured in plotter units,
/// which are a fixed physical length, so the picture has a real size before anything is drawn.
/// <para/>
/// Then the same instructions are played again with a surface to draw on.
/// </remarks>
public static class HpglRenderer {

  /// <summary>Degrees to radians, for the instructions that state an angle.</summary>
  private const double _DegreesToRadians = Math.PI / 180;

  /// <summary>How far apart an arc's chords are by default, in degrees.</summary>
  private const double _DefaultChordAngle = 5;

  /// <summary>The width a pen has before <c>PW</c> names one, in millimetres.</summary>
  private const double _DefaultPenMillimetres = 0.35;

  private sealed class State {
    public double X, Y;
    public bool Down;
    public bool Relative;
    public int Pen = 1;
    public int LineType = _SolidLineType;
    public double PatternLength;
    public double PenWidthMillimetres = _DefaultPenMillimetres;
    public double P1X = HpglFile.DefaultP1X, P1Y = HpglFile.DefaultP1Y;
    public double P2X = HpglFile.DefaultP2X, P2Y = HpglFile.DefaultP2Y;
    public bool Scaled;
    public double ScaleXMin, ScaleXMax = 1, ScaleYMin, ScaleYMax = 1;
    public bool Isotropic;
    public List<(double X, double Y, bool Down)> Polygon = [];
    public bool InPolygonMode;
  }

  /// <summary>Draws the plot at the physical size of the ink it lays down.</summary>
  public static RawImage Render(HpglFile file) {
    if (file.Instructions == null)
      throw new InvalidDataException("An HP-GL plot with no instructions cannot be drawn.");

    var extent = _Measure(file);
    if (extent == null)
      throw new InvalidDataException("An HP-GL plot that draws nothing has no size.");

    var (minX, minY, maxX, maxY) = extent.Value;

    // A plot that is one straight line has no thickness of its own; the pen gives it one, and
    // without that the extent would be degenerate and there would be nothing to fit into.
    var margin = Math.Max((maxX - minX + maxY - minY) * 0.005, 8);
    minX -= margin;
    minY -= margin;
    maxX += margin;
    maxY += margin;

    var millimetresWide = (maxX - minX) * HpglFile.MillimetresPerUnit;
    var millimetresTall = (maxY - minY) * HpglFile.MillimetresPerUnit;
    var viewport = VectorViewport.FitCapped(
      minX, minY, maxX, maxY,
      VectorViewport.PixelsFromMillimetres(millimetresWide),
      VectorViewport.PixelsFromMillimetres(millimetresTall),
      true
    );

    var canvas = new VectorCanvas(viewport.Width, viewport.Height, Rgba32.White);
    _Play(file, viewport.Transform, canvas);

    return canvas.ToRawImage();
  }

  /// <summary>Where the pen went, in plotter units, or nothing when it never went anywhere.</summary>
  private static (double MinX, double MinY, double MaxX, double MaxY)? _Measure(HpglFile file) {
    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
    var any = false;

    _Play(file, Matrix2D.Identity, null, (x, y) => {
      any = true;
      minX = Math.Min(minX, x);
      minY = Math.Min(minY, y);
      maxX = Math.Max(maxX, x);
      maxY = Math.Max(maxY, y);
    });

    // A plot that is one straight line has no extent along one axis. That is still a plot, and the
    // pen's own width gives it a thickness, so it is the margin the caller adds that makes it a
    // picture rather than this refusing it.
    return any ? (minX, minY, maxX, maxY) : null;
  }

  private static void _Play(HpglFile file, Matrix2D transform, VectorCanvas? canvas, Action<double, double>? note = null) {
    var state = new State();
    var path = new VectorPath();
    var open = false;

    void Flush() {
      if (!open || canvas == null) {
        path.Clear();
        open = false;
        return;
      }

      _StrokePath(canvas, path, state, transform);
      path.Clear();
      open = false;
    }

    // Everything the pen touches goes through here, so the measuring pass and the drawing pass
    // agree on where the ink is by construction rather than by two pieces of code being kept alike.
    void PlotTo(double x, double y, bool draw) {
      if (state.InPolygonMode) {
        state.Polygon.Add((x, y, draw));
        state.X = x;
        state.Y = y;
        return;
      }

      if (draw) {
        note?.Invoke(state.X, state.Y);
        note?.Invoke(x, y);

        if (!open) {
          var (sx, sy) = transform.Apply(state.X, state.Y);
          path.MoveTo(sx, sy);
          open = true;
        }

        var (dx, dy) = transform.Apply(x, y);
        path.LineTo(dx, dy);
      } else
        Flush();

      state.X = x;
      state.Y = y;
    }

    foreach (var instruction in file.Instructions) {
      var numbers = instruction.Numbers;

      switch (instruction.Mnemonic) {
        case "IN":
        case "DF":
          Flush();
          state.Scaled = false;
          state.LineType = _SolidLineType;
          state.PatternLength = 0;
          state.Relative = false;
          state.Down = false;
          state.InPolygonMode = false;
          state.Polygon.Clear();
          break;

        case "SP":
          Flush();
          state.Pen = numbers.Length > 0 ? (int)numbers[0] : 0;
          break;

        case "PU":
        case "PD": {
          var down = instruction.Mnemonic == "PD";
          Flush();
          state.Down = down;
          _Coordinates(numbers, state, PlotTo, down);
          break;
        }

        case "PA":
        case "PR":
          Flush();
          state.Relative = instruction.Mnemonic == "PR";
          _Coordinates(numbers, state, PlotTo, state.Down);
          break;

        case "IP":
          Flush();
          if (numbers.Length >= 4) {
            state.P1X = numbers[0];
            state.P1Y = numbers[1];
            state.P2X = numbers[2];
            state.P2Y = numbers[3];
          } else if (numbers.Length >= 2) {
            var width = state.P2X - state.P1X;
            var height = state.P2Y - state.P1Y;
            state.P1X = numbers[0];
            state.P1Y = numbers[1];
            state.P2X = state.P1X + width;
            state.P2Y = state.P1Y + height;
          } else {
            state.P1X = HpglFile.DefaultP1X;
            state.P1Y = HpglFile.DefaultP1Y;
            state.P2X = HpglFile.DefaultP2X;
            state.P2Y = HpglFile.DefaultP2Y;
          }

          break;

        case "SC":
          Flush();
          if (numbers.Length >= 4) {
            state.Scaled = true;
            state.ScaleXMin = numbers[0];
            state.ScaleXMax = numbers[1];
            state.ScaleYMin = numbers[2];
            state.ScaleYMax = numbers[3];
            state.Isotropic = numbers.Length >= 5 && (int)numbers[4] == 1;

            // Point-factor scaling states units per user unit rather than a range, so the far
            // corner has to be worked out from the frame before anything is mapped through it.
            if (numbers.Length >= 5 && (int)numbers[4] == 2) {
              state.ScaleXMax = numbers[1] == 0 ? numbers[0] + 1 : numbers[0] + (state.P2X - state.P1X) / numbers[1];
              state.ScaleYMax = numbers[3] == 0 ? numbers[2] + 1 : numbers[2] + (state.P2Y - state.P1Y) / numbers[3];
              state.Isotropic = false;
            }
          } else
            state.Scaled = false;

          break;

        case "LT":
          Flush();

          // Nothing after it means solid, and so does 99, which restores whatever was in force
          // before the last such solid. Only a stated type turns the pattern on.
          state.LineType = numbers.Length > 0 ? (int)numbers[0] : _SolidLineType;
          if (numbers.Length > 1 && numbers[1] > 0)
            state.PatternLength = numbers.Length > 2 && (int)numbers[2] == 1
              ? numbers[1] / HpglFile.MillimetresPerUnit
              : numbers[1] / 100 * Math.Sqrt(Math.Pow(state.P2X - state.P1X, 2) + Math.Pow(state.P2Y - state.P1Y, 2));

          break;

        case "PW":
          Flush();
          if (numbers.Length > 0)
            state.PenWidthMillimetres = Math.Max(numbers[0], 0);
          break;

        case "CI":
          Flush();
          if (numbers.Length > 0)
            _Circle(state, numbers[0], PlotTo);

          break;

        case "AA":
        case "AR":
          Flush();
          if (numbers.Length >= 3)
            _Arc(state, numbers, instruction.Mnemonic == "AR", PlotTo);

          break;

        case "EA":
        case "ER":
        case "RA":
        case "RR":
          Flush();
          if (numbers.Length >= 2)
            _Rectangle(state, numbers, instruction.Mnemonic is "ER" or "RR", instruction.Mnemonic is "RA" or "RR", canvas, transform, PlotTo, note);

          break;

        case "PM":
          Flush();
          if (numbers.Length == 0 || (int)numbers[0] == 0) {
            state.InPolygonMode = true;
            state.Polygon.Clear();
            state.Polygon.Add((state.X, state.Y, false));
          } else if ((int)numbers[0] == 2)
            state.InPolygonMode = false;

          break;

        case "EP":
          Flush();
          _PaintPolygon(state, canvas, transform, note, false, FillRule.EvenOdd);
          break;

        case "FP":
          Flush();
          _PaintPolygon(state, canvas, transform, note, true, numbers.Length > 0 && (int)numbers[0] == 1 ? FillRule.NonZero : FillRule.EvenOdd);
          break;
      }
    }

    Flush();
  }

  private static void _Coordinates(double[] numbers, State state, Action<double, double, bool> plot, bool draw) {
    for (var i = 0; i + 1 < numbers.Length; i += 2) {
      var (x, y) = _ToPlotter(state, numbers[i], numbers[i + 1]);
      plot(x, y, draw);
    }
  }

  /// <summary>
  /// Turns a coordinate pair into plotter units, which is the only frame anything is drawn in.
  /// </summary>
  /// <remarks>
  /// Without scaling a coordinate already is a plotter unit. With it, the scaling instruction says
  /// which user coordinates land on the two scaling points, and everything between and beyond
  /// follows from that — the mapping is not clipped to the frame, which is why a plot can and does
  /// draw outside it.
  /// </remarks>
  private static (double X, double Y) _ToPlotter(State state, double x, double y) {
    if (state.Relative) {
      if (!state.Scaled)
        return (state.X + x, state.Y + y);

      var (scaleX, scaleY) = _Scale(state);
      return (state.X + x * scaleX, state.Y + y * scaleY);
    }

    if (!state.Scaled)
      return (x, y);

    var (sx, sy) = _Scale(state);
    return (state.P1X + (x - state.ScaleXMin) * sx, state.P1Y + (y - state.ScaleYMin) * sy);
  }

  private static (double X, double Y) _Scale(State state) {
    var spanX = state.ScaleXMax - state.ScaleXMin;
    var spanY = state.ScaleYMax - state.ScaleYMin;
    var scaleX = spanX == 0 ? 1 : (state.P2X - state.P1X) / spanX;
    var scaleY = spanY == 0 ? 1 : (state.P2Y - state.P1Y) / spanY;

    if (!state.Isotropic)
      return (scaleX, scaleY);

    // Square units: the smaller of the two, applied to both, which is what leaves the drawing
    // inside the frame rather than stretched to it.
    var smaller = Math.Min(Math.Abs(scaleX), Math.Abs(scaleY));
    return (Math.Sign(scaleX) * smaller, Math.Sign(scaleY) * smaller);
  }

  private static void _Circle(State state, double radius, Action<double, double, bool> plot) {
    var (scaleX, scaleY) = state.Scaled ? _Scale(state) : (1, 1);
    var rx = Math.Abs(radius * scaleX);
    var ry = Math.Abs(radius * scaleY);
    if (rx <= 0 || ry <= 0)
      return;

    var centreX = state.X;
    var centreY = state.Y;
    const int steps = 72;

    plot(centreX + rx, centreY, false);
    for (var i = 1; i <= steps; ++i) {
      var angle = 2 * Math.PI * i / steps;
      var (sin, cos) = Math.SinCos(angle);
      plot(centreX + rx * cos, centreY + ry * sin, true);
    }

    plot(centreX, centreY, false);
  }

  private static void _Arc(State state, double[] numbers, bool relative, Action<double, double, bool> plot) {
    var (scaleX, scaleY) = state.Scaled ? _Scale(state) : (1, 1);
    double centreX, centreY;

    if (relative) {
      centreX = state.X + numbers[0] * scaleX;
      centreY = state.Y + numbers[1] * scaleY;
    } else {
      var mapped = _ToPlotterAbsolute(state, numbers[0], numbers[1]);
      centreX = mapped.X;
      centreY = mapped.Y;
    }

    var startX = state.X - centreX;
    var startY = state.Y - centreY;
    var startAngle = Math.Atan2(startY, startX);
    var radius = Math.Sqrt(startX * startX + startY * startY);
    if (radius <= 0)
      return;

    var sweep = numbers[2] * _DegreesToRadians;
    var chord = numbers.Length > 3 && numbers[3] != 0 ? Math.Abs(numbers[3]) : _DefaultChordAngle;
    var steps = Math.Clamp((int)Math.Ceiling(Math.Abs(numbers[2]) / chord), 1, 720);

    for (var i = 1; i <= steps; ++i) {
      var angle = startAngle + sweep * i / steps;
      var (sin, cos) = Math.SinCos(angle);
      plot(centreX + radius * cos, centreY + radius * sin, true);
    }
  }

  private static (double X, double Y) _ToPlotterAbsolute(State state, double x, double y) {
    var wasRelative = state.Relative;
    state.Relative = false;
    var mapped = _ToPlotter(state, x, y);
    state.Relative = wasRelative;
    return mapped;
  }

  private static void _Rectangle(State state, double[] numbers, bool relative, bool filled, VectorCanvas? canvas, Matrix2D transform, Action<double, double, bool> plot, Action<double, double>? note) {
    var (scaleX, scaleY) = state.Scaled ? _Scale(state) : (1, 1);
    double cornerX, cornerY;

    if (relative) {
      cornerX = state.X + numbers[0] * scaleX;
      cornerY = state.Y + numbers[1] * scaleY;
    } else {
      var mapped = _ToPlotterAbsolute(state, numbers[0], numbers[1]);
      cornerX = mapped.X;
      cornerY = mapped.Y;
    }

    var x0 = state.X;
    var y0 = state.Y;

    if (!filled) {
      plot(cornerX, y0, true);
      plot(cornerX, cornerY, true);
      plot(x0, cornerY, true);
      plot(x0, y0, true);
      return;
    }

    note?.Invoke(x0, y0);
    note?.Invoke(cornerX, cornerY);

    if (canvas == null)
      return;

    var path = new VectorPath();
    var (ax, ay) = transform.Apply(x0, y0);
    var (bx, by) = transform.Apply(cornerX, y0);
    var (cx, cy) = transform.Apply(cornerX, cornerY);
    var (dx, dy) = transform.Apply(x0, cornerY);
    path.MoveTo(ax, ay);
    path.LineTo(bx, by);
    path.LineTo(cx, cy);
    path.LineTo(dx, dy);
    path.Close();
    canvas.Fill(path, FillRule.NonZero, _Colour(state));
  }

  private static void _PaintPolygon(State state, VectorCanvas? canvas, Matrix2D transform, Action<double, double>? note, bool filled, FillRule rule) {
    if (state.Polygon.Count < 3)
      return;

    foreach (var (x, y, _) in state.Polygon)
      note?.Invoke(x, y);

    if (canvas == null)
      return;

    var path = new VectorPath();
    if (filled) {
      // A fill runs between every vertex whether the pen was down or up, which the language states
      // outright and which is the difference between filling a polygon and edging one.
      for (var i = 0; i < state.Polygon.Count; ++i) {
        var (x, y, _) = state.Polygon[i];
        var (dx, dy) = transform.Apply(x, y);
        if (i == 0)
          path.MoveTo(dx, dy);
        else
          path.LineTo(dx, dy);
      }

      path.Close();
      canvas.Fill(path, rule, _Colour(state));
      return;
    }

    var open = false;
    for (var i = 0; i < state.Polygon.Count; ++i) {
      var (x, y, down) = state.Polygon[i];
      var (dx, dy) = transform.Apply(x, y);
      if (!down || !open) {
        if (down && i > 0) {
          var (px, py) = transform.Apply(state.Polygon[i - 1].X, state.Polygon[i - 1].Y);
          path.MoveTo(px, py);
          path.LineTo(dx, dy);
          open = true;
          continue;
        }

        open = false;
        continue;
      }

      path.LineTo(dx, dy);
    }

    _StrokePath(canvas, path, state, transform);
  }

  private static void _StrokePath(VectorCanvas canvas, VectorPath path, State state, Matrix2D transform) {
    if (path.IsEmpty || state.Pen <= 0)
      return;

    var scale = transform.MeanScale;
    var width = Math.Max(VectorViewport.PixelsFromMillimetres(state.PenWidthMillimetres), 1);
    var dashes = _Dashes(state, state.PenWidthMillimetres / HpglFile.MillimetresPerUnit);
    var line = path;

    if (dashes.Length > 0) {
      // The pattern is stated in plotter units and the path is already on the surface, so the runs
      // are scaled the same way the geometry was.
      for (var i = 0; i < dashes.Length; ++i)
        dashes[i] = Math.Max(dashes[i] * scale, i % 2 == 0 ? width : 0);

      line = path.Dashed(dashes);
    }

    canvas.Stroke(line, width, _Colour(state), LineJoin.Round, LineCap.Round);
  }

  private static Rgba32 _Colour(State state) {
    var pens = HpglFile.Pens;
    if (state.Pen <= 0)
      return pens[0];

    return pens[(state.Pen - 1) % (pens.Length - 1) + 1];
  }

  /// <summary>The eight built-in line patterns, as percentages of the pattern length.</summary>
  /// <remarks>
  /// The language's own figure: alternating pen-down and pen-up runs, the first always pen down,
  /// adding to a hundred. Type 1 is a dot and a full gap, which is why it comes out as a dotted
  /// line and not a solid one; a plot that wants solid says <c>LT</c> with nothing after it.
  /// </remarks>
  private static readonly double[][] _LinePatterns = [
    [0, 100],                              // 1  dotted
    [50, 50],                              // 2  even dashes
    [70, 30],                              // 3  long dashes
    [80, 10, 0, 10],                       // 4  dash dot
    [70, 10, 10, 10],                      // 5  dash short dash
    [50, 10, 10, 10, 10, 10],              // 6  dash dash dash
    [70, 10, 0, 10, 0, 10],                // 7  dash dot dot
    [50, 10, 0, 10, 10, 10, 0, 10]         // 8  dash dot dash dot
  ];

  /// <summary>The line type that means solid, which is what <c>LT</c> with no parameters sets.</summary>
  private const int _SolidLineType = 99;

  /// <summary>What share of the distance between the scaling points one whole pattern takes.</summary>
  private const double _DefaultPatternFraction = 0.04;

  /// <summary>
  /// The dash runs a line type selects, in plotter units.
  /// </summary>
  /// <remarks>
  /// A run stated as nought is a dot rather than nothing, so it is given the pen's own width; a
  /// dasher told to draw a run of zero length would draw nothing at all, and the pattern that is
  /// nothing but dots would vanish.
  /// </remarks>
  private static double[] _Dashes(State state, double penWidthUnits) {
    var index = Math.Abs(state.LineType);
    if (index is < 1 or > 8)
      return [];

    var pattern = _LinePatterns[index - 1];
    var length = state.PatternLength > 0
      ? state.PatternLength
      : Math.Sqrt(Math.Pow(state.P2X - state.P1X, 2) + Math.Pow(state.P2Y - state.P1Y, 2)) * _DefaultPatternFraction;

    if (length <= 0)
      return [];

    var runs = new double[pattern.Length];
    for (var i = 0; i < pattern.Length; ++i)
      runs[i] = Math.Max(pattern[i] / 100 * length, i % 2 == 0 ? penWidthUnits : 0);

    return runs;
  }
}
