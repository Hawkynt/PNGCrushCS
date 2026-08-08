using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.PostScript;

/// <summary>The operators that set up a page and draw on it.</summary>
/// <remarks>
/// The path is built in device coordinates, because that is what the language says happens: each
/// point is transformed by the matrix in force when its segment is added, so a transform applied
/// half way through a path moves only the rest of it. Painting is then one call into the rasteriser
/// per operator, with the clip carried as coverage rather than as a shape.
/// </remarks>
public static class PostScriptGraphicsOperators {

  /// <summary>How far a flattened curve or arc may stray from the true one, in pixels.</summary>
  private const double _FlatteningPixels = 0.2;

  /// <summary>The fewest chords a whole turn is drawn with, so a small circle is still round.</summary>
  private const int _MinimumChords = 48;

  /// <summary>The most chords one arc is drawn with.</summary>
  private const int _MaximumChords = 4096;

  /// <summary>Defines the graphics operators.</summary>
  public static void Install(PsDictionary system) {
    ArgumentNullException.ThrowIfNull(system);

    _State(system);
    _Matrices(system);
    _Colour(system);
    _Paths(system);
    _Painting(system);
    _Text(system);
    _Device(system);

    PostScriptImages.Install(system);
  }

  #region graphics state

  private static void _State(PsDictionary system) {
    PostScriptOperators.Define(system, "gsave", static i => i.GraphicsSave());
    PostScriptOperators.Define(system, "grestore", static i => i.GraphicsRestore());
    PostScriptOperators.Define(system, "grestoreall", static i => i.GraphicsRestoreTo(0));

    PostScriptOperators.Define(system, "initgraphics", static i => {
      var state = i.Graphics;
      state.Ctm = i.Page.DefaultMatrix;
      state.Space = PsColourSpace.Gray;
      state.Components = [0];
      state.Colour = Rgba32.Black;
      state.LineWidth = 1;
      state.Cap = LineCap.Butt;
      state.Join = LineJoin.Miter;
      state.MiterLimit = 10;
      state.Dash = [];
      state.DashOffset = 0;
      state.Clip = null;
      state.Discards = false;
      _NewPath(i);
    });

    PostScriptOperators.Define(system, "setlinewidth", static i => i.Graphics.LineWidth = Math.Abs(i.PopNumber()));
    PostScriptOperators.Define(system, "currentlinewidth", static i => i.Push(PsObject.FromReal(i.Graphics.LineWidth)));

    PostScriptOperators.Define(system, "setlinecap", static i => i.Graphics.Cap = i.PopInteger() switch {
      0 => LineCap.Butt,
      1 => LineCap.Round,
      2 => LineCap.Square,
      var other => throw new PsErrorException("rangecheck", $"A PostScript program asked for line cap {other}.")
    });

    PostScriptOperators.Define(system, "currentlinecap", static i => i.Push(PsObject.FromInteger((int)i.Graphics.Cap)));

    PostScriptOperators.Define(system, "setlinejoin", static i => i.Graphics.Join = i.PopInteger() switch {
      0 => LineJoin.Miter,
      1 => LineJoin.Round,
      2 => LineJoin.Bevel,
      var other => throw new PsErrorException("rangecheck", $"A PostScript program asked for line join {other}.")
    });

    PostScriptOperators.Define(system, "currentlinejoin", static i => i.Push(PsObject.FromInteger((int)i.Graphics.Join)));

    PostScriptOperators.Define(system, "setmiterlimit", static i => {
      var limit = i.PopNumber();
      if (limit < 1)
        throw new PsErrorException("rangecheck", $"A PostScript program set a mitre limit of {limit}, which is shorter than the line is wide.");

      i.Graphics.MiterLimit = limit;
    });

    PostScriptOperators.Define(system, "currentmiterlimit", static i => i.Push(PsObject.FromReal(i.Graphics.MiterLimit)));

    PostScriptOperators.Define(system, "setdash", static i => {
      var offset = i.PopNumber();
      var pattern = i.PopArray();
      var lengths = new double[pattern.Length];
      var total = 0.0;
      for (var index = 0; index < lengths.Length; ++index) {
        var value = pattern[index];
        if (!value.IsNumber || value.Number < 0)
          throw new PsErrorException("rangecheck", "A PostScript dash pattern holds something that is not a length.");

        lengths[index] = value.Number;
        total += lengths[index];
      }

      if (lengths.Length > 0 && total <= 0)
        throw new PsErrorException("rangecheck", "A PostScript dash pattern whose lengths come to nothing.");

      i.Graphics.Dash = lengths;
      i.Graphics.DashOffset = offset;
    });

    PostScriptOperators.Define(system, "currentdash", static i => {
      var dash = i.Graphics.Dash;
      var pattern = new PsArray(dash.Length);
      for (var index = 0; index < dash.Length; ++index)
        pattern[index] = PsObject.FromReal(dash[index]);

      i.Push(PsObject.FromArray(pattern));
      i.Push(PsObject.FromReal(i.Graphics.DashOffset));
    });

    // Flatness is how far a curve may stray from its chords in device pixels. This rasteriser
    // flattens to a quarter of a pixel whatever a program asks for, which is finer than every value
    // a program does ask for, so the number is remembered and the curves are drawn at least as well.
    var flatness = 1.0;
    PostScriptOperators.Define(system, "setflat", i => flatness = i.PopNumber());
    PostScriptOperators.Define(system, "currentflat", i => i.Push(PsObject.FromReal(flatness)));

    var strokeAdjust = false;
    PostScriptOperators.Define(system, "setstrokeadjust", i => strokeAdjust = i.PopBoolean());
    PostScriptOperators.Define(system, "currentstrokeadjust", i => i.Push(PsObject.FromBoolean(strokeAdjust)));
  }

  #endregion

  #region matrices

  /// <summary>A transform as the six-element array every matrix operator passes about.</summary>
  private static PsObject _FromMatrix(Matrix2D matrix) {
    var array = new PsArray(6);
    array[0] = PsObject.FromReal(matrix.A);
    array[1] = PsObject.FromReal(matrix.B);
    array[2] = PsObject.FromReal(matrix.C);
    array[3] = PsObject.FromReal(matrix.D);
    array[4] = PsObject.FromReal(matrix.E);
    array[5] = PsObject.FromReal(matrix.F);
    return PsObject.FromArray(array);
  }

  /// <summary>The transform a six-element array states.</summary>
  internal static Matrix2D ToMatrix(PsArray array) {
    if (array.Length != 6)
      throw new PsErrorException("rangecheck", $"A PostScript matrix of {array.Length} numbers rather than six.");

    var values = new double[6];
    for (var index = 0; index < 6; ++index) {
      var value = array[index];
      if (!value.IsNumber)
        throw new PsErrorException("typecheck", $"A PostScript matrix holds {value.TypeName} where a number belongs.");

      values[index] = value.Number;
    }

    return new(values[0], values[1], values[2], values[3], values[4], values[5]);
  }

  private static void _StoreMatrix(PsArray array, Matrix2D matrix) {
    if (array.Length != 6)
      throw new PsErrorException("rangecheck", $"A PostScript matrix of {array.Length} numbers rather than six.");

    array[0] = PsObject.FromReal(matrix.A);
    array[1] = PsObject.FromReal(matrix.B);
    array[2] = PsObject.FromReal(matrix.C);
    array[3] = PsObject.FromReal(matrix.D);
    array[4] = PsObject.FromReal(matrix.E);
    array[5] = PsObject.FromReal(matrix.F);
  }

  /// <summary>
  /// Runs an operator that either changes the current matrix or fills one it was handed.
  /// </summary>
  /// <remarks>
  /// <c>translate</c>, <c>scale</c> and <c>rotate</c> each come in two forms, told apart by whether
  /// the topmost operand is a matrix. Both build the same transform; the one with a matrix stores it
  /// and the one without concatenates it onto the current one.
  /// </remarks>
  private static void _MatrixOrCtm(PostScriptInterpreter i, Func<PostScriptInterpreter, Matrix2D> build) {
    if (i.Peek().Type == PsType.Array) {
      var target = i.PopArray();
      _StoreMatrix(target, build(i));
      i.Push(PsObject.FromArray(target));
      return;
    }

    i.Graphics.Ctm = build(i).Then(i.Graphics.Ctm);
  }

  private static void _Matrices(PsDictionary system) {
    PostScriptOperators.Define(system, "matrix", static i => i.Push(_FromMatrix(Matrix2D.Identity)));

    PostScriptOperators.Define(system, "identmatrix", static i => {
      var target = i.PopArray();
      _StoreMatrix(target, Matrix2D.Identity);
      i.Push(PsObject.FromArray(target));
    });

    PostScriptOperators.Define(system, "currentmatrix", static i => {
      var target = i.PopArray();
      _StoreMatrix(target, i.Graphics.Ctm);
      i.Push(PsObject.FromArray(target));
    });

    PostScriptOperators.Define(system, "defaultmatrix", static i => {
      var target = i.PopArray();
      _StoreMatrix(target, i.Page.DefaultMatrix);
      i.Push(PsObject.FromArray(target));
    });

    PostScriptOperators.Define(system, "setmatrix", static i => i.Graphics.Ctm = ToMatrix(i.PopArray()));
    PostScriptOperators.Define(system, "initmatrix", static i => i.Graphics.Ctm = i.Page.DefaultMatrix);

    PostScriptOperators.Define(system, "translate", static i => _MatrixOrCtm(i, static i => {
      var y = i.PopNumber();
      var x = i.PopNumber();
      return Matrix2D.Translation(x, y);
    }));

    PostScriptOperators.Define(system, "scale", static i => _MatrixOrCtm(i, static i => {
      var y = i.PopNumber();
      var x = i.PopNumber();
      return Matrix2D.Scaling(x, y);
    }));

    PostScriptOperators.Define(system, "rotate", static i => _MatrixOrCtm(i, static i => Matrix2D.Rotation(i.PopNumber() * Math.PI / 180)));

    PostScriptOperators.Define(system, "concat", static i => i.Graphics.Ctm = ToMatrix(i.PopArray()).Then(i.Graphics.Ctm));

    PostScriptOperators.Define(system, "concatmatrix", static i => {
      var target = i.PopArray();
      var second = ToMatrix(i.PopArray());
      var first = ToMatrix(i.PopArray());
      _StoreMatrix(target, first.Then(second));
      i.Push(PsObject.FromArray(target));
    });

    PostScriptOperators.Define(system, "invertmatrix", static i => {
      var target = i.PopArray();
      _StoreMatrix(target, PsMatrix.Inverse(ToMatrix(i.PopArray())));
      i.Push(PsObject.FromArray(target));
    });

    PostScriptOperators.Define(system, "transform", static i => _Map(i, false, false));
    PostScriptOperators.Define(system, "dtransform", static i => _Map(i, false, true));
    PostScriptOperators.Define(system, "itransform", static i => _Map(i, true, false));
    PostScriptOperators.Define(system, "idtransform", static i => _Map(i, true, true));
  }

  /// <summary>Maps a point or a distance through the current matrix or a stated one, either way round.</summary>
  private static void _Map(PostScriptInterpreter i, bool inverse, bool distance) {
    var matrix = i.Peek().Type == PsType.Array ? ToMatrix(i.PopArray()) : i.Graphics.Ctm;
    if (inverse)
      matrix = PsMatrix.Inverse(matrix);

    var y = i.PopNumber();
    var x = i.PopNumber();
    var (mx, my) = distance ? matrix.ApplyVector(x, y) : matrix.Apply(x, y);
    i.Push(PsObject.FromReal(mx));
    i.Push(PsObject.FromReal(my));
  }

  #endregion

  #region colour

  private static void _Colour(PsDictionary system) {
    PostScriptOperators.Define(system, "setgray", static i => _SetColour(i, PsColourSpace.Gray, i.PopNumbers(1)));
    PostScriptOperators.Define(system, "setrgbcolor", static i => _SetColour(i, PsColourSpace.Rgb, i.PopNumbers(3)));
    PostScriptOperators.Define(system, "setcmykcolor", static i => _SetColour(i, PsColourSpace.Cmyk, i.PopNumbers(4)));

    PostScriptOperators.Define(system, "sethsbcolor", static i => {
      var components = i.PopNumbers(3);
      var (red, green, blue) = _FromHsb(components[0], components[1], components[2]);
      _SetColour(i, PsColourSpace.Rgb, [red, green, blue]);
    });

    PostScriptOperators.Define(system, "currentgray", static i => i.Push(PsObject.FromReal(_Gray(i.Graphics))));

    PostScriptOperators.Define(system, "currentrgbcolor", static i => {
      var colour = i.Graphics.Colour;
      i.Push(PsObject.FromReal(colour.R / 255.0));
      i.Push(PsObject.FromReal(colour.G / 255.0));
      i.Push(PsObject.FromReal(colour.B / 255.0));
    });

    PostScriptOperators.Define(system, "currentcmykcolor", static i => {
      var state = i.Graphics;
      if (state.Space == PsColourSpace.Cmyk) {
        for (var index = 0; index < 4; ++index)
          i.Push(PsObject.FromReal(index < state.Components.Length ? state.Components[index] : 0));

        return;
      }

      // Going the other way is the conversion in the reference: the black is whatever all three
      // additive components have in common, and each subtractive one is what is left over.
      var colour = state.Colour;
      var black = 1 - Math.Max(Math.Max(colour.R, colour.G), colour.B) / 255.0;
      i.Push(PsObject.FromReal(1 - colour.R / 255.0 - black));
      i.Push(PsObject.FromReal(1 - colour.G / 255.0 - black));
      i.Push(PsObject.FromReal(1 - colour.B / 255.0 - black));
      i.Push(PsObject.FromReal(black));
    });

    PostScriptOperators.Define(system, "currenthsbcolor", static i => {
      var colour = i.Graphics.Colour;
      var (hue, saturation, brightness) = _ToHsb(colour.R / 255.0, colour.G / 255.0, colour.B / 255.0);
      i.Push(PsObject.FromReal(hue));
      i.Push(PsObject.FromReal(saturation));
      i.Push(PsObject.FromReal(brightness));
    });

    PostScriptOperators.Define(system, "setcolorspace", static i => i.Graphics.Space = _Space(i.Pop()));
    PostScriptOperators.Define(system, "currentcolorspace", static i => {
      var array = new PsArray(1);
      array[0] = PsObject.FromName(i.Graphics.Space switch {
        PsColourSpace.Gray => "DeviceGray",
        PsColourSpace.Rgb => "DeviceRGB",
        _ => "DeviceCMYK"
      });

      i.Push(PsObject.FromArray(array));
    });

    PostScriptOperators.Define(system, "setcolor", static i => {
      var space = i.Graphics.Space;
      var count = space switch { PsColourSpace.Gray => 1, PsColourSpace.Rgb => 3, _ => 4 };
      _SetColour(i, space, i.PopNumbers(count));
    });

    PostScriptOperators.Define(system, "currentcolor", static i => {
      foreach (var component in i.Graphics.Components)
        i.Push(PsObject.FromReal(component));
    });

    // A transfer function maps a component onto what the marking engine has to be given to make it.
    // A surface with eight bits a channel and no engine has nothing to correct for, so the identity
    // is accepted and anything else is refused rather than quietly ignored — a curve that inverts
    // the page is the difference between a picture and its negative.
    PostScriptOperators.Define(system, "settransfer", static i => _RequireIdentityTransfer(i, i.Pop(), "settransfer"));
    PostScriptOperators.Define(system, "currenttransfer", static i => i.Push(PsObject.FromProcedure(new(0))));

    PostScriptOperators.Define(system, "setcolortransfer", static i => {
      var gray = i.Pop();
      var blue = i.Pop();
      var green = i.Pop();
      var red = i.Pop();
      _RequireIdentityTransfer(i, red, "setcolortransfer");
      _RequireIdentityTransfer(i, green, "setcolortransfer");
      _RequireIdentityTransfer(i, blue, "setcolortransfer");
      _RequireIdentityTransfer(i, gray, "setcolortransfer");
    });

    PostScriptOperators.Define(system, "currentcolortransfer", static i => {
      for (var index = 0; index < 4; ++index)
        i.Push(PsObject.FromProcedure(new(0)));
    });

    PostScriptOperators.Define(system, "setblackgeneration", static i => _RequireIdentityTransfer(i, i.Pop(), "setblackgeneration"));
    PostScriptOperators.Define(system, "currentblackgeneration", static i => i.Push(PsObject.FromProcedure(new(0))));
    PostScriptOperators.Define(system, "setundercolorremoval", static i => _RequireIdentityTransfer(i, i.Pop(), "setundercolorremoval"));
    PostScriptOperators.Define(system, "currentundercolorremoval", static i => i.Push(PsObject.FromProcedure(new(0))));

    // A halftone screen is a pattern of dots that turns a grey into black-and-white for a device
    // that has only those two. This surface has two hundred and fifty-six levels a channel, so a
    // screen has nothing to do here and the settings are remembered rather than applied.
    PostScriptOperators.Ignore(system, "setscreen", 3);
    PostScriptOperators.Ignore(system, "setcolorscreen", 12);
    PostScriptOperators.Ignore(system, "sethalftone", 1);

    // A pattern is a colour made of a procedure that draws a tile, so passing it over would leave
    // whatever colour was in force painting the shapes it was meant to fill.
    PostScriptOperators.Define(system, "setpattern", static _ => throw new PsUnsupportedException("A PostScript program painted with a pattern, which this reader does not tile."));
    PostScriptOperators.Define(system, "makepattern", static _ => throw new PsUnsupportedException("A PostScript program built a pattern, which this reader does not tile."));

    PostScriptOperators.Define(system, "currentscreen", static i => {
      i.Push(PsObject.FromReal(60));
      i.Push(PsObject.FromReal(0));
      i.Push(PsObject.FromProcedure(new(0)));
    });

    PostScriptOperators.Define(system, "currentcolorscreen", static i => {
      for (var index = 0; index < 4; ++index) {
        i.Push(PsObject.FromReal(60));
        i.Push(PsObject.FromReal(0));
        i.Push(PsObject.FromProcedure(new(0)));
      }
    });

    // A halftone dictionary of type 1 is the simplest the reference defines: a frequency, an angle
    // and a spot function. A program asks for it to find out how fine the screen is, which decides
    // nothing here but has to be answerable.
    PostScriptOperators.Define(system, "currenthalftone", static i => {
      var halftone = new PsDictionary(4);
      halftone.Put("HalftoneType", PsObject.FromInteger(1));
      halftone.Put("Frequency", PsObject.FromReal(60));
      halftone.Put("Angle", PsObject.FromReal(0));
      halftone.Put("SpotFunction", PsObject.FromProcedure(new(0)));
      i.Push(PsObject.FromDictionary(halftone));
    });

    // Overprint says a separation left out of a colour should not knock out what is under it, which
    // only means anything when the page is being separated onto plates. Composited onto one surface
    // there are no plates, which is also what the tool this reader is checked against does.
    var overprint = false;
    PostScriptOperators.Define(system, "setoverprint", i => overprint = i.PopBoolean());
    PostScriptOperators.Define(system, "currentoverprint", i => i.Push(PsObject.FromBoolean(overprint)));
  }

  /// <summary>How light the current colour is, which is what <c>currentgray</c> reports.</summary>
  private static double _Gray(PsGraphicsState state) {
    var colour = state.Colour;
    return (0.3 * colour.R + 0.59 * colour.G + 0.11 * colour.B) / 255.0;
  }

  private static void _SetColour(PostScriptInterpreter i, PsColourSpace space, double[] components) {
    var state = i.Graphics;
    state.Space = space;
    state.Components = components;
    state.Colour = PsColour.From(space, components);
  }

  /// <summary>Which of the three device spaces a colour space object names.</summary>
  /// <remarks>
  /// The device spaces are the ones this can paint in. A space built on top of one of them — an
  /// indexed palette, a separation, a CIE space — needs a lookup or a profile to turn a component
  /// into ink, and guessing at that would put the wrong colour on the page, so it is refused.
  /// </remarks>
  private static PsColourSpace _Space(PsObject value) {
    var name = value.Type switch {
      PsType.Name => value.Name,
      PsType.Array when value.Array.Length > 0 && value.Array[0].Type == PsType.Name => value.Array[0].Name,
      _ => throw new PsErrorException("typecheck", $"A PostScript program named a colour space with {value.TypeName}.")
    };

    return name switch {
      "DeviceGray" or "G" or "CIEBasedA" => PsColourSpace.Gray,
      "DeviceRGB" or "RGB" or "CIEBasedABC" => PsColourSpace.Rgb,
      "DeviceCMYK" or "CMYK" => PsColourSpace.Cmyk,
      _ => throw new PsUnsupportedException($"A PostScript program painted in the colour space {name}, which this reader has no way to turn into ink.")
    };
  }

  /// <summary>Checks that a transfer function leaves every component where it found it.</summary>
  private static void _RequireIdentityTransfer(PostScriptInterpreter i, PsObject procedure, string what) {
    if (procedure.Type != PsType.Array)
      throw new PsErrorException("typecheck", $"A PostScript program gave {what} {procedure.TypeName} rather than a procedure.");

    if (procedure.Array.Length == 0)
      return;

    foreach (var sample in (double[])[0, 0.25, 0.5, 0.75, 1]) {
      i.Push(PsObject.FromReal(sample));
      var before = i.Count;
      i.RunNested(procedure.WithExecutable(true));
      if (i.Count != before)
        throw new PsUnsupportedException($"A PostScript program gave {what} a procedure that does not leave one number behind.");

      var result = i.Pop();
      if (!result.IsNumber || Math.Abs(result.Number - sample) > 1e-6)
        throw new PsUnsupportedException($"A PostScript program gave {what} a curve that changes the picture, and this surface has no place to apply it.");
    }
  }

  private static (double Red, double Green, double Blue) _FromHsb(double hue, double saturation, double brightness) {
    hue = ((hue % 1) + 1) % 1;
    saturation = Math.Clamp(saturation, 0, 1);
    brightness = Math.Clamp(brightness, 0, 1);

    var sector = hue * 6;
    var index = (int)Math.Floor(sector) % 6;
    var fraction = sector - Math.Floor(sector);
    var p = brightness * (1 - saturation);
    var q = brightness * (1 - saturation * fraction);
    var t = brightness * (1 - saturation * (1 - fraction));

    return index switch {
      0 => (brightness, t, p),
      1 => (q, brightness, p),
      2 => (p, brightness, t),
      3 => (p, q, brightness),
      4 => (t, p, brightness),
      _ => (brightness, p, q)
    };
  }

  private static (double Hue, double Saturation, double Brightness) _ToHsb(double red, double green, double blue) {
    var max = Math.Max(red, Math.Max(green, blue));
    var min = Math.Min(red, Math.Min(green, blue));
    var span = max - min;
    if (span <= 0)
      return (0, 0, max);

    var hue = max == red
      ? (green - blue) / span
      : max == green
        ? 2 + (blue - red) / span
        : 4 + (red - green) / span;

    hue /= 6;
    return (hue < 0 ? hue + 1 : hue, max <= 0 ? 0 : span / max, max);
  }

  #endregion

  #region paths

  private static void _NewPath(PostScriptInterpreter i) {
    var state = i.Graphics;
    state.Path = new();
    state.Current = null;
    state.SubPathStart = null;
    state.HasPath = false;
  }

  private static (double X, double Y) _Current(PostScriptInterpreter i)
    => i.Graphics.Current ?? throw new PsErrorException("nocurrentpoint", "A PostScript program drew from a point it never went to.");

  private static void _Paths(PsDictionary system) {
    PostScriptOperators.Define(system, "newpath", _NewPath);

    PostScriptOperators.Define(system, "moveto", static i => {
      var y = i.PopNumber();
      var x = i.PopNumber();
      _MoveTo(i, i.Graphics.Ctm.Apply(x, y));
    });

    PostScriptOperators.Define(system, "rmoveto", static i => {
      var y = i.PopNumber();
      var x = i.PopNumber();
      var (cx, cy) = _Current(i);
      var (dx, dy) = i.Graphics.Ctm.ApplyVector(x, y);
      _MoveTo(i, (cx + dx, cy + dy));
    });

    PostScriptOperators.Define(system, "lineto", static i => {
      var y = i.PopNumber();
      var x = i.PopNumber();
      _Current(i);
      _LineTo(i, i.Graphics.Ctm.Apply(x, y));
    });

    PostScriptOperators.Define(system, "rlineto", static i => {
      var y = i.PopNumber();
      var x = i.PopNumber();
      var (cx, cy) = _Current(i);
      var (dx, dy) = i.Graphics.Ctm.ApplyVector(x, y);
      _LineTo(i, (cx + dx, cy + dy));
    });

    PostScriptOperators.Define(system, "curveto", static i => {
      var values = i.PopNumbers(6);
      _Current(i);
      var ctm = i.Graphics.Ctm;
      _CurveTo(i, ctm.Apply(values[0], values[1]), ctm.Apply(values[2], values[3]), ctm.Apply(values[4], values[5]));
    });

    PostScriptOperators.Define(system, "rcurveto", static i => {
      var values = i.PopNumbers(6);
      var (cx, cy) = _Current(i);
      var ctm = i.Graphics.Ctm;
      var (x1, y1) = ctm.ApplyVector(values[0], values[1]);
      var (x2, y2) = ctm.ApplyVector(values[2], values[3]);
      var (x3, y3) = ctm.ApplyVector(values[4], values[5]);
      _CurveTo(i, (cx + x1, cy + y1), (cx + x2, cy + y2), (cx + x3, cy + y3));
    });

    PostScriptOperators.Define(system, "closepath", static i => {
      var state = i.Graphics;
      if (!state.HasPath)
        return;

      state.Path.Close();
      state.Current = state.SubPathStart;
    });

    PostScriptOperators.Define(system, "arc", static i => _Arc(i, false));
    PostScriptOperators.Define(system, "arcn", static i => _Arc(i, true));
    PostScriptOperators.Define(system, "arct", static i => _ArcTangent(i, false));
    PostScriptOperators.Define(system, "arcto", static i => _ArcTangent(i, true));

    PostScriptOperators.Define(system, "currentpoint", static i => {
      var (x, y) = _Current(i);
      var (ux, uy) = PsMatrix.Inverse(i.Graphics.Ctm).Apply(x, y);
      i.Push(PsObject.FromReal(ux));
      i.Push(PsObject.FromReal(uy));
    });

    PostScriptOperators.Define(system, "pathbbox", static i => {
      var state = i.Graphics;
      if (!state.HasPath)
        throw new PsErrorException("nocurrentpoint", "A PostScript program asked for the extent of a path it has not begun.");

      var inverse = PsMatrix.Inverse(state.Ctm);
      double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
      foreach (var (xs, ys, _) in state.Path.SubPaths) {
        var x = xs.Span;
        var y = ys.Span;
        for (var index = 0; index < x.Length; ++index) {
          var (ux, uy) = inverse.Apply(x[index], y[index]);
          minX = Math.Min(minX, ux);
          minY = Math.Min(minY, uy);
          maxX = Math.Max(maxX, ux);
          maxY = Math.Max(maxY, uy);
        }
      }

      if (minX > maxX)
        throw new PsErrorException("nocurrentpoint", "A PostScript program asked for the extent of an empty path.");

      i.Push(PsObject.FromReal(minX));
      i.Push(PsObject.FromReal(minY));
      i.Push(PsObject.FromReal(maxX));
      i.Push(PsObject.FromReal(maxY));
    });

    // The path is already flat: every curve was cut into chords when it was added, so asking for it
    // to be flattened is asking for what is there. Reversing one, on the other hand, changes which
    // way a subpath winds and therefore what the non-zero rule fills, so it is not pretended at.
    PostScriptOperators.Define(system, "flattenpath", static _ => { });

    PostScriptOperators.Define(system, "clippath", static i => {
      var state = i.Graphics;
      _NewPath(i);

      // With no clip in force the clipping path is the whole page, which is a rectangle this can
      // state exactly. Once something has clipped, the region is held as coverage rather than as a
      // path and there is no honest path to hand back.
      if (state.Clip != null)
        throw new PsUnsupportedException("A PostScript program asked for the current clipping path as a path, after clipping had narrowed it.");

      state.Path.AddRectangle(0, 0, i.Page.Canvas.Width, i.Page.Canvas.Height);
      state.HasPath = true;
      state.Current = (0, 0);
      state.SubPathStart = (0, 0);
    });

    PostScriptOperators.Define(system, "initclip", static i => i.Graphics.Clip = null);
  }

  private static void _MoveTo(PostScriptInterpreter i, (double X, double Y) point) {
    var state = i.Graphics;
    state.Path.MoveTo(point.X, point.Y);
    state.Current = point;
    state.SubPathStart = point;
    state.HasPath = true;
  }

  private static void _LineTo(PostScriptInterpreter i, (double X, double Y) point) {
    var state = i.Graphics;
    state.Path.LineTo(point.X, point.Y);
    state.Current = point;
    state.HasPath = true;
  }

  private static void _CurveTo(PostScriptInterpreter i, (double X, double Y) first, (double X, double Y) second, (double X, double Y) end) {
    var state = i.Graphics;
    state.Path.CurveTo(first.X, first.Y, second.X, second.Y, end.X, end.Y);
    state.Current = end;
    state.HasPath = true;
  }

  /// <summary>
  /// An arc of a circle in user space, which under a general transform is an arc of an ellipse.
  /// </summary>
  /// <remarks>
  /// The points are worked out in user space and each one transformed, rather than the circle being
  /// transformed into an ellipse and drawn: that way a rotation or a shear in the matrix comes out
  /// right without the ellipse having to be described in device terms at all. How finely it is cut
  /// is decided from how big it comes out on the page.
  /// </remarks>
  private static void _Arc(PostScriptInterpreter i, bool clockwise) {
    var second = i.PopNumber();
    var first = i.PopNumber();
    var radius = i.PopNumber();
    var centreY = i.PopNumber();
    var centreX = i.PopNumber();

    if (radius < 0)
      throw new PsErrorException("rangecheck", $"A PostScript program drew an arc of radius {radius}.");

    var start = first * Math.PI / 180;
    var end = second * Math.PI / 180;
    var sweep = end - start;
    if (clockwise) {
      while (sweep > 0)
        sweep -= 2 * Math.PI;
    } else {
      while (sweep < 0)
        sweep += 2 * Math.PI;
    }

    var state = i.Graphics;
    var ctm = state.Ctm;
    var steps = Chords(radius * ctm.MeanScale, sweep);

    for (var step = 0; step <= steps; ++step) {
      var angle = start + sweep * step / steps;
      var (sin, cos) = Math.SinCos(angle);
      var point = ctm.Apply(centreX + radius * cos, centreY + radius * sin);
      if (step == 0 && state.Current == null)
        _MoveTo(i, point);
      else
        _LineTo(i, point);
    }
  }

  /// <summary>
  /// The arc of the given radius that runs from the line into the corner to the line out of it.
  /// </summary>
  /// <remarks>
  /// From the reference: the arc is tangent to both lines, a straight segment joins the current
  /// point to where it begins, and <c>arcto</c> additionally hands back the two tangent points.
  /// Where the three points are in a straight line there is no arc, and the corner itself is the
  /// answer.
  /// </remarks>
  private static void _ArcTangent(PostScriptInterpreter i, bool reportsTangents) {
    var radius = i.PopNumber();
    var values = i.PopNumbers(4);
    if (radius < 0)
      throw new PsErrorException("rangecheck", $"A PostScript program drew a tangent arc of radius {radius}.");

    var state = i.Graphics;
    var inverse = PsMatrix.Inverse(state.Ctm);
    var (deviceX, deviceY) = _Current(i);
    var (x0, y0) = inverse.Apply(deviceX, deviceY);
    var (x1, y1) = (values[0], values[1]);
    var (x2, y2) = (values[2], values[3]);

    var (ax, ay) = _Unit(x0 - x1, y0 - y1);
    var (bx, by) = _Unit(x2 - x1, y2 - y1);
    var cross = ax * by - ay * bx;
    var dot = ax * bx + ay * by;

    if (radius == 0 || Math.Abs(cross) < 1e-12) {
      _LineTo(i, state.Ctm.Apply(x1, y1));
      if (reportsTangents)
        _PushTangents(i, x1, y1, x1, y1);

      return;
    }

    var half = Math.Acos(Math.Clamp(dot, -1, 1)) / 2;
    var along = radius / Math.Tan(half);
    var t1 = (X: x1 + ax * along, Y: y1 + ay * along);
    var t2 = (X: x1 + bx * along, Y: y1 + by * along);

    var distance = radius / Math.Sin(half);
    var (mx, my) = _Unit(ax + bx, ay + by);
    var centre = (X: x1 + mx * distance, Y: y1 + my * distance);

    _LineTo(i, state.Ctm.Apply(t1.X, t1.Y));

    var startAngle = Math.Atan2(t1.Y - centre.Y, t1.X - centre.X);
    var endAngle = Math.Atan2(t2.Y - centre.Y, t2.X - centre.X);
    var sweep = endAngle - startAngle;
    if (cross > 0) {
      while (sweep < 0)
        sweep += 2 * Math.PI;
    } else {
      while (sweep > 0)
        sweep -= 2 * Math.PI;
    }

    var steps = Chords(radius * state.Ctm.MeanScale, sweep);
    for (var step = 1; step <= steps; ++step) {
      var angle = startAngle + sweep * step / steps;
      var (sin, cos) = Math.SinCos(angle);
      _LineTo(i, state.Ctm.Apply(centre.X + radius * cos, centre.Y + radius * sin));
    }

    if (reportsTangents)
      _PushTangents(i, t1.X, t1.Y, t2.X, t2.Y);
  }

  private static void _PushTangents(PostScriptInterpreter i, double x1, double y1, double x2, double y2) {
    i.Push(PsObject.FromReal(x1));
    i.Push(PsObject.FromReal(y1));
    i.Push(PsObject.FromReal(x2));
    i.Push(PsObject.FromReal(y2));
  }

  private static (double X, double Y) _Unit(double x, double y) {
    var length = Math.Sqrt(x * x + y * y);
    return length <= 0 || !double.IsFinite(length) ? (0, 0) : (x / length, y / length);
  }

  /// <summary>How many chords an arc of this size on the page is cut into.</summary>
  internal static int Chords(double deviceRadius, double sweep) {
    if (!double.IsFinite(deviceRadius) || !double.IsFinite(sweep))
      return 4;

    var turns = Math.Abs(sweep) / (2 * Math.PI);
    var fromTolerance = deviceRadius > _FlatteningPixels
      ? Math.Abs(sweep) / (2 * Math.Acos(1 - _FlatteningPixels / deviceRadius))
      : 4;

    return Math.Clamp((int)Math.Ceiling(Math.Max(fromTolerance, turns * _MinimumChords)), 4, _MaximumChords);
  }

  #endregion

  #region painting

  private static void _Painting(PsDictionary system) {
    PostScriptOperators.Define(system, "fill", static i => {
      i.Page.Fill(i.Graphics, FillRule.NonZero);
      _NewPath(i);
    });

    PostScriptOperators.Define(system, "eofill", static i => {
      i.Page.Fill(i.Graphics, FillRule.EvenOdd);
      _NewPath(i);
    });

    PostScriptOperators.Define(system, "stroke", static i => {
      i.Page.Stroke(i.Graphics);
      _NewPath(i);
    });

    // clip narrows the region and leaves the path alone, which is what the reference says and why
    // every program follows it with newpath.
    PostScriptOperators.Define(system, "clip", static i => i.Page.Clip(i.Graphics, FillRule.NonZero));
    PostScriptOperators.Define(system, "eoclip", static i => i.Page.Clip(i.Graphics, FillRule.EvenOdd));

    PostScriptOperators.Define(system, "showpage", static i => i.ShowPage());

    // copypage puts the page out without clearing it, so the drawing carries on afterwards onto the
    // same surface, which is what happens here anyway.
    PostScriptOperators.Define(system, "copypage", static _ => { });

    PostScriptOperators.Define(system, "erasepage", static i => {
      if (i.Graphics.Discards)
        return;

      var page = i.Page.Canvas;
      var whole = new VectorPath();
      whole.AddRectangle(0, 0, page.Width, page.Height);
      page.Fill(whole, FillRule.NonZero, Rgba32.White);
    });

    // A null device marks nothing. Programs install one to work something out without disturbing
    // the page and get out of it with grestore, which brings the real page back with the rest of
    // the state.
    PostScriptOperators.Define(system, "nulldevice", static i => {
      i.Graphics.Discards = true;
      i.Graphics.Ctm = Matrix2D.Identity;
      i.Graphics.Clip = null;
    });

    // A shading is a colour that varies over a region by a function the file carries. The function
    // types are a language of their own and guessing at the ramp would put invented colour on the
    // page, so a program that asks for one is refused.
    PostScriptOperators.Define(system, "shfill", static _ => throw new PsUnsupportedException("A PostScript program painted a shading, which this reader has no way to evaluate."));
    PostScriptOperators.Define(system, "ustroke", static _ => throw new PsUnsupportedException("A PostScript program painted a user path, which this reader does not build."));
    PostScriptOperators.Define(system, "ufill", static _ => throw new PsUnsupportedException("A PostScript program painted a user path, which this reader does not build."));
    PostScriptOperators.Define(system, "rectfill", static i => _Rectangles(i, static x => x.Page.Fill(x.Graphics, FillRule.NonZero)));
    PostScriptOperators.Define(system, "rectstroke", static i => _Rectangles(i, static x => x.Page.Stroke(x.Graphics)));
    PostScriptOperators.Define(system, "rectclip", static i => _Rectangles(i, static x => x.Page.Clip(x.Graphics, FillRule.NonZero)));
  }

  /// <summary>
  /// The rectangle operators, which are a path and a paint in one.
  /// </summary>
  /// <remarks>
  /// The operands are four numbers, a string of packed numbers, or an array of them. Only the four
  /// numbers and the array are taken: the string form encodes them in a device-dependent way that
  /// nothing here writes and that would have to be guessed at.
  /// </remarks>
  private static void _Rectangles(PostScriptInterpreter i, Action<PostScriptInterpreter> paint) {
    var numbers = new List<double>();
    var top = i.Peek();
    if (top.IsNumber) {
      var values = i.PopNumbers(4);
      numbers.AddRange(values);
    } else if (top.Type == PsType.Array) {
      var array = i.PopArray();
      for (var index = 0; index < array.Length; ++index) {
        if (!array[index].IsNumber)
          throw new PsErrorException("typecheck", "A PostScript rectangle operator was given an array holding something that is not a number.");

        numbers.Add(array[index].Number);
      }
    } else
      throw new PsUnsupportedException($"A PostScript rectangle operator was given {top.TypeName}, which encodes its numbers in a way this reader does not read.");

    if (numbers.Count % 4 != 0)
      throw new PsErrorException("rangecheck", $"A PostScript rectangle operator was given {numbers.Count} numbers.");

    _NewPath(i);
    var state = i.Graphics;
    for (var index = 0; index < numbers.Count; index += 4) {
      var (x, y, width, height) = (numbers[index], numbers[index + 1], numbers[index + 2], numbers[index + 3]);
      _MoveTo(i, state.Ctm.Apply(x, y));
      _LineTo(i, state.Ctm.Apply(x + width, y));
      _LineTo(i, state.Ctm.Apply(x + width, y + height));
      _LineTo(i, state.Ctm.Apply(x, y + height));
      state.Path.Close();
    }

    paint(i);
    _NewPath(i);
  }

  #endregion

  #region text, which is consumed and not drawn

  private static void _Text(PsDictionary system) {
    PostScriptOperators.Define(system, "findfont", static i => i.Push(_Font(i.Pop())));
    PostScriptOperators.Define(system, "definefont", static i => {
      var font = i.Pop();
      i.Pop();
      i.Push(font);
    });

    PostScriptOperators.Define(system, "undefinefont", static i => i.Pop());
    PostScriptOperators.Define(system, "scalefont", static i => {
      i.PopNumber();
    });

    PostScriptOperators.Define(system, "makefont", static i => i.Pop());
    PostScriptOperators.Define(system, "selectfont", static i => {
      i.Pop();
      i.Graphics.Font = _Font(i.Pop());
    });

    PostScriptOperators.Define(system, "setfont", static i => i.Graphics.Font = i.Pop());
    PostScriptOperators.Define(system, "currentfont", static i => i.Push(i.Graphics.Font.Type == PsType.Null ? _Font(PsObject.FromName("Courier")) : i.Graphics.Font));
    PostScriptOperators.Define(system, "rootfont", static i => i.Push(i.Graphics.Font));
    PostScriptOperators.Define(system, "setcachedevice", static i => i.Drop(6));
    PostScriptOperators.Define(system, "setcachedevice2", static i => i.Drop(10));
    PostScriptOperators.Define(system, "setcharwidth", static i => i.Drop(2));
    PostScriptOperators.Define(system, "setcachelimit", static i => i.Pop());
    PostScriptOperators.Define(system, "cachestatus", static i => {
      for (var index = 0; index < 7; ++index)
        i.Push(PsObject.FromInteger(0));
    });

    // The encodings are arrays in the system dictionary, and a program that re-encodes a font copies
    // one and writes over the slots it cares about. Which glyph name is in which slot cannot matter
    // here, because no glyph is drawn; that there is a name in every slot to copy and overwrite can.
    system.Put("StandardEncoding", PsObject.FromArray(_Encoding()));
    system.Put("ISOLatin1Encoding", PsObject.FromArray(_Encoding()));

    // Showing a string draws glyphs, and the glyphs are in a font this reader does not carry. A
    // rectangle where the words are is geometry the file never stated, so the string is consumed and
    // nothing is drawn — the same call the SVG, HP-GL and DXF readers here make.
    PostScriptOperators.Define(system, "show", static i => i.Pop());
    PostScriptOperators.Define(system, "ashow", static i => i.Drop(3));
    PostScriptOperators.Define(system, "widthshow", static i => i.Drop(4));
    PostScriptOperators.Define(system, "awidthshow", static i => i.Drop(6));
    PostScriptOperators.Define(system, "kshow", static i => i.Drop(2));
    PostScriptOperators.Define(system, "xshow", static i => i.Drop(2));
    PostScriptOperators.Define(system, "yshow", static i => i.Drop(2));
    PostScriptOperators.Define(system, "xyshow", static i => i.Drop(2));
    PostScriptOperators.Define(system, "cshow", static i => i.Drop(2));
    PostScriptOperators.Define(system, "glyphshow", static i => i.Pop());

    PostScriptOperators.Define(system, "stringwidth", static i => {
      i.Pop();
      i.Push(PsObject.FromReal(0));
      i.Push(PsObject.FromReal(0));
    });

    PostScriptOperators.Define(system, "charpath", static i => i.Drop(2));
  }

  /// <summary>An encoding vector: a name in each of the 256 slots.</summary>
  private static PsArray _Encoding() {
    var array = new PsArray(256);
    var notdef = PsObject.FromName(".notdef");
    for (var index = 0; index < 256; ++index)
      array[index] = notdef;

    return array;
  }

  /// <summary>
  /// A font object, which this carries as a name and a dictionary and never draws with.
  /// </summary>
  private static PsObject _Font(PsObject name) {
    var dictionary = new PsDictionary(8);
    dictionary.Put("FontName", name);
    dictionary.Put("FontType", PsObject.FromInteger(1));
    dictionary.Put("FontMatrix", PsObject.FromArray(new(6)));
    return PsObject.FromFont(dictionary);
  }

  #endregion

  #region the device the page is

  private static void _Device(PsDictionary system) {
    // The page size is the one the file states in its bounding box, which is decided before the
    // program runs. A program that also asks the device for a size is asking for something that has
    // already been settled, so the request is accepted and the answer is what was settled.
    PostScriptOperators.Ignore(system, "setpagedevice", 1);

    PostScriptOperators.Define(system, "currentpagedevice", static i => {
      var device = new PsDictionary(4);
      var size = new PsArray(2);
      size[0] = PsObject.FromInteger(i.Page.Canvas.Width);
      size[1] = PsObject.FromInteger(i.Page.Canvas.Height);
      device.Put("PageSize", PsObject.FromArray(size));
      i.Push(PsObject.FromDictionary(device));
    });

    PostScriptOperators.Define(system, "deviceinfo", static i => i.Push(PsObject.FromDictionary(new(4))));
  }

  #endregion
}
