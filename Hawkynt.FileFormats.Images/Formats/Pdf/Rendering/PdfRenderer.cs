using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// Runs content streams (PDF 32000-1 §8–§11) onto a raster surface: the graphics state, paths,
/// clipping, colour, images, form XObjects and, in the other parts of this class, shading, text and
/// transparency.
/// </summary>
/// <remarks>
/// Operators are read with the shared lexer and dispatched by name. A malformed operator — wrong
/// operand count or types — is skipped, which is what every viewer does and what keeps one bad byte
/// from losing a page. A feature that is genuinely not implemented throws
/// <see cref="NotSupportedException"/> naming it, so a page is never shown with something silently
/// missing that would be drawn elsewhere.
/// </remarks>
internal sealed partial class PdfRenderer {

  private const int _MaxFormDepth = 28;
  private const long _MaxOperators = 50_000_000;

  private readonly PdfDocument _document;
  private readonly PdfRasterizer _rasterizer;
  private readonly int _width;
  private readonly int _height;
  private readonly List<string> _omissions;
  private readonly HashSet<object> _activeForms;
  private readonly Dictionary<object, PdfRasterImage> _imageCache = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<string, Dictionary<object, PdfRasterImage>> _intentImageCaches = [];
  private readonly PdfOptionalContent _optional;

  private PdfSurface _surface;
  private PdfGraphicsState _state = new();
  private readonly Stack<PdfGraphicsState> _saved = new();
  private PdfDict? _resources;
  private VectorPath _path = new();
  private bool _pathOpen;
  private double _pathStartX, _pathStartY, _currentX, _currentY;
  private FillRule? _pendingClip;
  private int _formDepth;
  private long _operators;
  private int _hiddenDepth;
  private readonly Stack<bool> _markedHidden = new();

  // Reusable row buffers for compositing.
  private float[] _rowRgb;
  private float[] _rowAlpha;
  private float[] _rowShape;

  public PdfRenderer(PdfDocument document, int width, int height, List<string> omissions)
    : this(document, width, height, omissions, 0, new HashSet<object>(ReferenceEqualityComparer.Instance)) { }

  /// <summary>A renderer for a pattern cell or similar off-page surface, sharing the nesting guard of its parent.</summary>
  internal PdfRenderer(PdfDocument document, int width, int height, List<string> omissions, int formDepth, HashSet<object> activeForms) {
    this._formDepth = formDepth;
    this._activeForms = activeForms;
    this._document = document;
    this._width = width;
    this._height = height;
    this._omissions = omissions;
    this._rasterizer = new PdfRasterizer(width, height);
    this._surface = new PdfSurface(0, 0, width, height);
    this._rowRgb = new float[(width + 2) * 3];
    this._rowAlpha = new float[width + 2];
    this._rowShape = new float[width + 2];
    this._optional = new PdfOptionalContent(document);
  }

  /// <summary>The surface being drawn on.</summary>
  public PdfSurface Surface => this._surface;

  /// <summary>Sets the transform from default user space to device pixels and clears to white.</summary>
  public void BeginPage(Matrix2D baseTransform) {
    this._baseTransform = baseTransform;
    this._patternBase = baseTransform;
    this._state = new PdfGraphicsState { Ctm = baseTransform };
    this._surface.Clear(1, 1, 1, 1);
  }

  /// <summary>Sets the transform and leaves the surface transparent, for a pattern cell or a group.</summary>
  public void BeginTransparent(Matrix2D baseTransform) {
    this._baseTransform = baseTransform;
    this._patternBase = baseTransform;
    this._state = new PdfGraphicsState { Ctm = baseTransform };
  }

  private Matrix2D _baseTransform = Matrix2D.Identity;

  /// <summary>
  /// The transform patterns are placed by: the CTM in force when the content stream holding their
  /// resources began (§8.7.3.1) — the page's base transform, or a form's matrix when inside one.
  /// </summary>
  private Matrix2D _patternBase = Matrix2D.Identity;

  /// <summary>Draws an annotation's appearance stream from a fresh graphics state, fitted by a matrix in default user space.</summary>
  public void RunAnnotation(PdfStreamObject appearance, Matrix2D fit) {
    this._state = new PdfGraphicsState { Ctm = this._baseTransform };
    this._saved.Clear();
    this._path = new VectorPath();
    this.RunForm(appearance, fit, false);
  }

  /// <summary>Runs a content stream with the given resources.</summary>
  public void Run(byte[] content, PdfDict? resources) {
    var saveResources = this._resources;
    this._resources = resources;
    try {
      this._Interpret(content);
    } finally {
      this._resources = saveResources;
    }
  }

  /// <summary>The page as a picture: the surface over its white backdrop.</summary>
  public RawImage ToRawImage() {
    var pixels = new byte[this._width * this._height * 3];
    var src = this._surface.Pixels;
    for (var i = 0; i < this._width * this._height; ++i) {
      var a = src[i * 4 + 3];
      // The page backdrop is opaque white, so what is not covered is white.
      var r = src[i * 4] + (1 - a);
      var g = src[i * 4 + 1] + (1 - a);
      var b = src[i * 4 + 2] + (1 - a);
      pixels[i * 3] = (byte)Math.Clamp((int)(r * 255 + 0.5f), 0, 255);
      pixels[i * 3 + 1] = (byte)Math.Clamp((int)(g * 255 + 0.5f), 0, 255);
      pixels[i * 3 + 2] = (byte)Math.Clamp((int)(b * 255 + 0.5f), 0, 255);
    }

    return new RawImage { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  #region interpreter

  private void _Interpret(byte[] content) {
    var lexer = new PdfLexer(content);
    var operands = new List<object?>(16);
    var saveDepth = this._saved.Count;
    var markedDepth = this._markedHidden.Count;
    var hiddenAtStart = this._hiddenDepth;

    while (true) {
      object? token;
      try {
        token = lexer.ReadContentObject();
      } catch (InvalidDataException) {
        break;
      }

      if (token == PdfLexer.EndOfData)
        break;

      if (token is not PdfOperator op) {
        if (operands.Count < 64)
          operands.Add(token);
        continue;
      }

      if (++this._operators > _MaxOperators)
        throw new NotSupportedException("The page runs more operators than this renderer allows.");

      if (op.Name == "BI") {
        this._InlineImage(lexer);
        operands.Clear();
        continue;
      }

      try {
        this._Execute(op.Name, operands);
      } catch (Exception e) when (e is InvalidCastException or IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException or ArgumentException or OverflowException) {
        // A malformed operator is skipped, as viewers do.
      }

      operands.Clear();
    }

    // A content stream must leave the state as it found it; one that does not is corrected.
    while (this._saved.Count > saveDepth)
      this._state = this._saved.Pop();
    while (this._markedHidden.Count > markedDepth)
      this._markedHidden.Pop();
    this._hiddenDepth = hiddenAtStart;
  }

  private static double _N(List<object?> operands, int index) => PdfValues.Number(operands[index]);

  private static double[] _Numbers(List<object?> operands) {
    var values = new double[operands.Count];
    for (var i = 0; i < operands.Count; ++i)
      values[i] = PdfValues.Number(operands[i]);
    return values;
  }

  private void _Execute(string op, List<object?> o) {
    var hidden = this._hiddenDepth > 0;

    // A Type 3 glyph begun with d1 is a shape painted in the text's colour; its own colour
    // operators are ignored (§9.6.5).
    if (this._uncolouredGlyph && op is "CS" or "cs" or "SC" or "SCN" or "sc" or "scn" or "G" or "g" or "RG" or "rg" or "K" or "k")
      return;

    switch (op) {
      // Graphics state.
      case "q":
        this._saved.Push(this._state.Clone());
        if (this._saved.Count > 4096)
          throw new InvalidDataException("Graphics state stack overflow.");
        break;
      case "Q":
        if (this._saved.Count > 0)
          this._state = this._saved.Pop();
        break;
      case "cm":
        if (o.Count >= 6) {
          var m = new Matrix2D(_N(o, 0), _N(o, 1), _N(o, 2), _N(o, 3), _N(o, 4), _N(o, 5));
          this._state.Ctm = m.Then(this._state.Ctm);
        }
        break;
      case "w":
        if (o.Count >= 1)
          this._state.LineWidth = Math.Abs(_N(o, 0));
        break;
      case "J":
        if (o.Count >= 1)
          this._state.Cap = PdfValues.Integer(o[0]) switch { 1 => LineCap.Round, 2 => LineCap.Square, _ => LineCap.Butt };
        break;
      case "j":
        if (o.Count >= 1)
          this._state.Join = PdfValues.Integer(o[0]) switch { 1 => LineJoin.Round, 2 => LineJoin.Bevel, _ => LineJoin.Miter };
        break;
      case "M":
        if (o.Count >= 1)
          this._state.MiterLimit = Math.Max(1, _N(o, 0));
        break;
      case "d":
        if (o.Count >= 2) {
          this._state.Dash = this._document.Numbers(o[0]) ?? [];
          this._state.DashPhase = _N(o, 1);
        }
        break;
      case "ri":
        if (o.Count >= 1 && o[0] is string intent)
          this._state.Intent = intent;
        break;
      case "i":
        break;
      case "gs":
        if (o.Count >= 1 && o[0] is string gsName)
          this._ApplyExtGState(gsName);
        break;

      // Path construction.
      case "m":
        if (o.Count >= 2)
          this._MoveTo(_N(o, 0), _N(o, 1));
        break;
      case "l":
        if (o.Count >= 2)
          this._LineTo(_N(o, 0), _N(o, 1));
        break;
      case "c":
        if (o.Count >= 6)
          this._CurveTo(_N(o, 0), _N(o, 1), _N(o, 2), _N(o, 3), _N(o, 4), _N(o, 5));
        break;
      case "v":
        if (o.Count >= 4)
          this._CurveTo(this._currentX, this._currentY, _N(o, 0), _N(o, 1), _N(o, 2), _N(o, 3));
        break;
      case "y":
        if (o.Count >= 4)
          this._CurveTo(_N(o, 0), _N(o, 1), _N(o, 2), _N(o, 3), _N(o, 2), _N(o, 3));
        break;
      case "h":
        this._ClosePath();
        break;
      case "re":
        if (o.Count >= 4) {
          double x = _N(o, 0), y = _N(o, 1), w = _N(o, 2), h = _N(o, 3);
          this._MoveTo(x, y);
          this._LineTo(x + w, y);
          this._LineTo(x + w, y + h);
          this._LineTo(x, y + h);
          this._ClosePath();
        }
        break;

      // Painting.
      case "S":
        if (!hidden) this._Stroke();
        this._EndPath();
        break;
      case "s":
        this._ClosePath();
        if (!hidden) this._Stroke();
        this._EndPath();
        break;
      case "f" or "F":
        if (!hidden) this._Fill(FillRule.NonZero);
        this._EndPath();
        break;
      case "f*":
        if (!hidden) this._Fill(FillRule.EvenOdd);
        this._EndPath();
        break;
      case "B":
        if (!hidden) { this._Fill(FillRule.NonZero); this._Stroke(); }
        this._EndPath();
        break;
      case "B*":
        if (!hidden) { this._Fill(FillRule.EvenOdd); this._Stroke(); }
        this._EndPath();
        break;
      case "b":
        this._ClosePath();
        if (!hidden) { this._Fill(FillRule.NonZero); this._Stroke(); }
        this._EndPath();
        break;
      case "b*":
        this._ClosePath();
        if (!hidden) { this._Fill(FillRule.EvenOdd); this._Stroke(); }
        this._EndPath();
        break;
      case "n":
        this._EndPath();
        break;
      case "W":
        this._pendingClip = FillRule.NonZero;
        break;
      case "W*":
        this._pendingClip = FillRule.EvenOdd;
        break;

      // Colour.
      case "CS":
        if (o.Count >= 1)
          this._SetSpace(stroke: true, o[0]);
        break;
      case "cs":
        if (o.Count >= 1)
          this._SetSpace(stroke: false, o[0]);
        break;
      case "SC" or "SCN":
        this._SetColor(stroke: true, o);
        break;
      case "sc" or "scn":
        this._SetColor(stroke: false, o);
        break;
      case "G":
        if (o.Count >= 1) { this._state.StrokeSpace = PdfColorModel.DeviceGray; this._state.StrokeColor = [_N(o, 0)]; this._state.StrokePattern = null; }
        break;
      case "g":
        if (o.Count >= 1) { this._state.FillSpace = PdfColorModel.DeviceGray; this._state.FillColor = [_N(o, 0)]; this._state.FillPattern = null; }
        break;
      case "RG":
        if (o.Count >= 3) { this._state.StrokeSpace = PdfColorModel.DeviceRgb; this._state.StrokeColor = [_N(o, 0), _N(o, 1), _N(o, 2)]; this._state.StrokePattern = null; }
        break;
      case "rg":
        if (o.Count >= 3) { this._state.FillSpace = PdfColorModel.DeviceRgb; this._state.FillColor = [_N(o, 0), _N(o, 1), _N(o, 2)]; this._state.FillPattern = null; }
        break;
      case "K":
        if (o.Count >= 4) { this._state.StrokeSpace = PdfColorModel.DeviceCmyk; this._state.StrokeColor = [_N(o, 0), _N(o, 1), _N(o, 2), _N(o, 3)]; this._state.StrokePattern = null; }
        break;
      case "k":
        if (o.Count >= 4) { this._state.FillSpace = PdfColorModel.DeviceCmyk; this._state.FillColor = [_N(o, 0), _N(o, 1), _N(o, 2), _N(o, 3)]; this._state.FillPattern = null; }
        break;

      // XObjects, shading.
      case "Do":
        if (!hidden && o.Count >= 1 && o[0] is string xName)
          this._DoXObject(xName);
        break;
      case "sh":
        if (!hidden && o.Count >= 1 && o[0] is string shName)
          this._ShadingOperator(shName);
        break;

      // Text.
      case "BT":
        this._BeginText();
        break;
      case "ET":
        this._EndText();
        break;
      case "Tc":
        if (o.Count >= 1) this._state.CharSpacing = _N(o, 0);
        break;
      case "Tw":
        if (o.Count >= 1) this._state.WordSpacing = _N(o, 0);
        break;
      case "Tz":
        if (o.Count >= 1) this._state.HorizontalScaling = _N(o, 0) / 100;
        break;
      case "TL":
        if (o.Count >= 1) this._state.Leading = _N(o, 0);
        break;
      case "Tf":
        if (o.Count >= 2 && o[0] is string fontName)
          this._SetFont(fontName, _N(o, 1));
        break;
      case "Tr":
        if (o.Count >= 1) this._state.RenderMode = PdfValues.Integer(o[0]);
        break;
      case "Ts":
        if (o.Count >= 1) this._state.Rise = _N(o, 0);
        break;
      case "Td":
        if (o.Count >= 2) this._TextMove(_N(o, 0), _N(o, 1));
        break;
      case "TD":
        if (o.Count >= 2) {
          this._state.Leading = -_N(o, 1);
          this._TextMove(_N(o, 0), _N(o, 1));
        }
        break;
      case "Tm":
        if (o.Count >= 6) this._SetTextMatrix(new Matrix2D(_N(o, 0), _N(o, 1), _N(o, 2), _N(o, 3), _N(o, 4), _N(o, 5)));
        break;
      case "T*":
        this._TextMove(0, -this._state.Leading);
        break;
      case "Tj":
        if (o.Count >= 1 && o[0] is byte[] tj)
          this._ShowText(tj, hidden);
        break;
      case "TJ":
        if (o.Count >= 1 && o[0] is List<object?> tjArray)
          this._ShowTextArray(tjArray, hidden);
        break;
      case "'":
        this._TextMove(0, -this._state.Leading);
        if (o.Count >= 1 && o[0] is byte[] quote)
          this._ShowText(quote, hidden);
        break;
      case "\"":
        if (o.Count >= 3) {
          this._state.WordSpacing = _N(o, 0);
          this._state.CharSpacing = _N(o, 1);
          this._TextMove(0, -this._state.Leading);
          if (o[2] is byte[] dquote)
            this._ShowText(dquote, hidden);
        }
        break;
      case "d0":
        this._Type3Width(o, false);
        break;
      case "d1":
        this._Type3Width(o, true);
        break;

      // Marked content, which matters only where it hides optional content.
      case "BDC":
        if (o.Count >= 2 && o[0] is "OC")
          this._BeginOptional(o[1]);
        else
          this._markedHidden.Push(false);
        break;
      case "BMC":
        this._markedHidden.Push(false);
        break;
      case "EMC":
        if (this._markedHidden.Count > 0 && this._markedHidden.Pop())
          --this._hiddenDepth;
        break;
      case "MP" or "DP" or "BX" or "EX":
        break;
    }
  }

  private void _BeginOptional(object? property) {
    var dict = property as PdfDict ?? (property is string name ? this._document.Dict(this._document.Dict(this._document.Get(this._resources, "Properties"))?.Raw(name)) : null);
    var hide = dict != null && !this._optional.IsVisible(dict);
    this._markedHidden.Push(hide);
    if (hide)
      ++this._hiddenDepth;
  }

  #endregion

  #region graphics state

  private void _ApplyExtGState(string name) {
    var gs = this._document.Dict(this._document.Dict(this._document.Get(this._resources, "ExtGState"))?.Raw(name));
    if (gs == null)
      return;

    foreach (var (key, raw) in gs) {
      var value = this._document.Resolve(raw);
      switch (key) {
        case "RI":
          if (value is string intent)
            this._state.Intent = intent;
          break;
        case "OP":
          this._state.StrokeOverprint = value is true;
          if (!gs.ContainsKey("op"))
            this._state.FillOverprint = value is true;
          break;
        case "op":
          this._state.FillOverprint = value is true;
          break;
        case "OPM":
          this._state.OverprintMode = PdfValues.IsNumber(value) ? PdfValues.Integer(value) : 0;
          break;
        case "LW":
          this._state.LineWidth = Math.Abs(PdfValues.Number(value, this._state.LineWidth));
          break;
        case "LC":
          this._state.Cap = PdfValues.Integer(value) switch { 1 => LineCap.Round, 2 => LineCap.Square, _ => LineCap.Butt };
          break;
        case "LJ":
          this._state.Join = PdfValues.Integer(value) switch { 1 => LineJoin.Round, 2 => LineJoin.Bevel, _ => LineJoin.Miter };
          break;
        case "ML":
          this._state.MiterLimit = Math.Max(1, PdfValues.Number(value, 10));
          break;
        case "D":
          if (value is List<object?> { Count: >= 2 } dash) {
            this._state.Dash = this._document.Numbers(dash[0]) ?? [];
            this._state.DashPhase = PdfValues.Number(this._document.Resolve(dash[1]));
          }
          break;
        case "CA":
          this._state.StrokeAlpha = Math.Clamp(PdfValues.Number(value, 1), 0, 1);
          break;
        case "ca":
          this._state.FillAlpha = Math.Clamp(PdfValues.Number(value, 1), 0, 1);
          break;
        case "AIS":
          this._state.AlphaIsShape = value is true;
          break;
        case "BM":
          this._state.BlendMode = _BlendMode(value is List<object?> list && list.Count > 0 ? this._document.Resolve(list[0]) : value);
          break;
        case "SMask":
          this._state.SoftMask = value is "None" or null ? null : this._BuildSoftMask(value as PdfDict);
          break;
        case "Font":
          if (value is List<object?> { Count: >= 2 } font)
            this._SetFontFromReference(font[0], PdfValues.Number(this._document.Resolve(font[1])));
          break;
      }
    }
  }

  private static PdfBlendMode _BlendMode(object? value) => value switch {
    "Multiply" => PdfBlendMode.Multiply,
    "Screen" => PdfBlendMode.Screen,
    "Overlay" => PdfBlendMode.Overlay,
    "Darken" => PdfBlendMode.Darken,
    "Lighten" => PdfBlendMode.Lighten,
    "ColorDodge" => PdfBlendMode.ColorDodge,
    "ColorBurn" => PdfBlendMode.ColorBurn,
    "HardLight" => PdfBlendMode.HardLight,
    "SoftLight" => PdfBlendMode.SoftLight,
    "Difference" => PdfBlendMode.Difference,
    "Exclusion" => PdfBlendMode.Exclusion,
    "Hue" => PdfBlendMode.Hue,
    "Saturation" => PdfBlendMode.Saturation,
    "Color" => PdfBlendMode.Color,
    "Luminosity" => PdfBlendMode.Luminosity,
    _ => PdfBlendMode.Normal,
  };

  #endregion

  #region paths

  private (double X, double Y) _Device(double x, double y) => this._state.Ctm.Apply(x, y);

  private void _MoveTo(double x, double y) {
    var (dx, dy) = this._Device(x, y);
    this._path.MoveTo(dx, dy);
    this._pathOpen = true;
    this._pathStartX = this._currentX = x;
    this._pathStartY = this._currentY = y;
  }

  private void _LineTo(double x, double y) {
    if (!this._pathOpen)
      this._MoveTo(this._currentX, this._currentY);

    var (dx, dy) = this._Device(x, y);
    this._path.LineTo(dx, dy);
    this._currentX = x;
    this._currentY = y;
  }

  private void _CurveTo(double x1, double y1, double x2, double y2, double x3, double y3) {
    if (!this._pathOpen)
      this._MoveTo(this._currentX, this._currentY);

    var (a, b) = this._Device(x1, y1);
    var (c, d) = this._Device(x2, y2);
    var (e, f) = this._Device(x3, y3);
    this._path.CurveTo(a, b, c, d, e, f);
    this._currentX = x3;
    this._currentY = y3;
  }

  private void _ClosePath() {
    if (!this._pathOpen && this._path.IsEmpty)
      return;

    this._path.Close();
    this._pathOpen = false;
    this._currentX = this._pathStartX;
    this._currentY = this._pathStartY;
  }

  private void _EndPath() {
    if (this._pendingClip is { } rule && !this._path.IsEmpty)
      this._IntersectClip(this._path, rule);
    else if (this._pendingClip != null)
      this._IntersectClip(new VectorPath(), FillRule.NonZero);

    this._pendingClip = null;
    this._path = new VectorPath();
    this._pathOpen = false;
  }

  /// <summary>Narrows the clip to a device-space path.</summary>
  internal void _IntersectClip(VectorPath devicePath, FillRule rule) {
    var mask = new PdfClipMask(this._width, this._height);
    var old = this._state.Clip;
    var limit = old?.Bounds ?? new PixelBox(0, 0, this._width, this._height);
    this._rasterizer.Fill(devicePath, rule, limit, (y, x0, x1, coverage) => {
      var row = y * this._width;
      for (var x = x0; x < x1; ++x) {
        var c = coverage[x - x0];
        if (old != null)
          c *= old.Coverage[row + x] * (1f / 255);
        mask.Coverage[row + x] = (byte)Math.Clamp((int)(c * 255 + 0.5f), 0, 255);
      }
    });

    // The bounds of what got through, for later fills to stop at.
    var minX = int.MaxValue;
    var minY = int.MaxValue;
    var maxX = -1;
    var maxY = -1;
    for (var y = limit.Y0; y < limit.Y1; ++y) {
      var row = y * this._width;
      for (var x = limit.X0; x < limit.X1; ++x)
        if (mask.Coverage[row + x] != 0) {
          if (x < minX) minX = x;
          if (x > maxX) maxX = x;
          if (y < minY) minY = y;
          maxY = y;
        }
    }

    mask.Bounds = maxX < 0 ? default : new PixelBox(minX, minY, maxX + 1, maxY + 1);
    this._state.Clip = mask;
  }

  #endregion

  #region painting

  private void _Fill(FillRule rule) {
    if (this._path.IsEmpty)
      return;

    var paint = this._PaintFor(stroke: false, out var alpha);
    if (paint != null)
      this.PaintPath(_AdjustRectangle(this._path) ?? this._path, rule, paint, alpha);
  }

  /// <summary>
  /// A path that is one axis-aligned rectangle, with its edges rounded to whole pixels; null for any
  /// other path.
  /// </summary>
  /// <remarks>
  /// poppler extends stroke adjustment to fills of a single rectangle — Acrobat does the same — so
  /// table cells and rules drawn as thin filled boxes land on whole pixels. One that rounds to no
  /// width keeps one pixel.
  /// </remarks>
  private static VectorPath? _AdjustRectangle(VectorPath path) {
    if (path.SubPathCount != 1)
      return null;

    foreach (var (xsMemory, ysMemory, _) in path.SubPaths) {
      var xs = xsMemory.Span;
      var ys = ysMemory.Span;
      var n = xs.Length;
      if (n == 5 && xs[4] == xs[0] && ys[4] == ys[0])
        n = 4;
      if (n != 4)
        return null;

      const double eps = 1e-6;
      var alongX = Math.Abs(ys[0] - ys[1]) < eps && Math.Abs(xs[1] - xs[2]) < eps && Math.Abs(ys[2] - ys[3]) < eps && Math.Abs(xs[3] - xs[0]) < eps;
      var alongY = Math.Abs(xs[0] - xs[1]) < eps && Math.Abs(ys[1] - ys[2]) < eps && Math.Abs(xs[2] - xs[3]) < eps && Math.Abs(ys[3] - ys[0]) < eps;
      if (!alongX && !alongY)
        return null;

      double x0 = Math.Min(xs[0], xs[2]), x1 = Math.Max(xs[0], xs[2]), y0 = Math.Min(ys[0], ys[2]), y1 = Math.Max(ys[0], ys[2]);
      if (!double.IsFinite(x0) || !double.IsFinite(x1) || !double.IsFinite(y0) || !double.IsFinite(y1))
        return null;

      var left = Math.Floor(x0 + 0.5);
      var right = Math.Floor(x1 + 0.5);
      var top = Math.Floor(y0 + 0.5);
      var bottom = Math.Floor(y1 + 0.5);
      if (right <= left)
        right = left + 1;
      if (bottom <= top)
        bottom = top + 1;

      var adjusted = new VectorPath();
      adjusted.AddRectangle(left, top, right - left, bottom - top);
      return adjusted;
    }

    return null;
  }

  private void _Stroke() {
    if (this._path.IsEmpty)
      return;

    var paint = this._PaintFor(stroke: true, out var alpha);
    if (paint == null)
      return;

    var outline = PdfStroker.Outline(this._path, this._state.LineStyle, this._state.Ctm);
    this.PaintPath(outline, FillRule.NonZero, paint, alpha, hairline: this._state.LineWidth == 0);
  }

  /// <summary>The paint and constant alpha for the current fill or stroke colour, or null to paint nothing.</summary>
  private PdfPaint? _PaintFor(bool stroke, out float alpha) {
    var space = (stroke ? this._state.StrokeSpace : this._state.FillSpace).ForIntent(this._state.Intent);
    alpha = (float)(stroke ? this._state.StrokeAlpha : this._state.FillAlpha);
    if (space.PaintsNothing)
      return null;

    if (space.IsPattern)
      return this._PatternPaint(stroke);

    var paint = _SolidPaint(space, stroke ? this._state.StrokeColor : this._state.FillColor);
    if (this._surface.Shadow is { } shadow)
      paint.Ink = _InkFor(shadow, space, stroke ? this._state.StrokeColor : this._state.FillColor, stroke ? this._state.StrokeOverprint : this._state.FillOverprint, this._state.OverprintMode);
    return paint;
  }

  /// <summary>The separations a solid colour paints, or null for a space that goes through RGB.</summary>
  private static PdfInk? _InkFor(PdfOverprintShadow shadow, PdfColorModel space, double[] color, bool overprint, int mode) {
    double Component(int i) => i < color.Length ? Math.Clamp(color[i], 0, 1) : 0;
    if (ReferenceEquals(space, PdfColorModel.DeviceCmyk)) {
      var process = new float[4];
      var paints = new bool[4];
      for (var c = 0; c < 4; ++c) {
        process[c] = (float)Component(c);
        // OPM 1: a zero component of a DeviceCMYK colour leaves what is under it (§8.6.7).
        paints[c] = !overprint || mode != 1 || process[c] != 0;
      }

      return new PdfInk(process, paints, [], [], overprint);
    }

    if (space.Colorants is not { } names)
      return overprint ? PdfInk.FromRgbOverprinting : null;

    var ink = new PdfInk(new float[4], new bool[4], new int[names.Count], new float[names.Count], overprint);
    var spots = 0;
    for (var i = 0; i < names.Count; ++i) {
      var process = names[i] switch { "Cyan" => 0, "Magenta" => 1, "Yellow" => 2, "Black" => 3, _ => -1 };
      if (process >= 0) {
        ink.Process![process] = (float)Component(i);
        ink.PaintsProcess[process] = true;
        continue;
      }

      if (names[i] == "All") {
        // Every separation at the tint (§8.6.6.4).
        for (var c = 0; c < 4; ++c) {
          ink.Process![c] = (float)Component(i);
          ink.PaintsProcess[c] = true;
        }

        continue;
      }

      if (names[i] == "None")
        continue;
      var index = i;
      ink.Spots[spots] = shadow.Spot(names[i], tint => space.ColorantCmyk(index, tint));
      ink.Tints[spots++] = (float)Component(i);
    }

    return ink with { Spots = ink.Spots[..spots], Tints = ink.Tints[..spots] };
  }

  private static PdfSolidPaint _SolidPaint(PdfColorModel space, double[] color) {
    Span<double> rgb = stackalloc double[3];
    var components = color;
    if (components.Length < space.Components) {
      components = new double[space.Components];
      Array.Copy(color, components, color.Length);
    }

    space.ToRgb(components, rgb);
    return new PdfSolidPaint((float)Math.Clamp(rgb[0], 0, 1), (float)Math.Clamp(rgb[1], 0, 1), (float)Math.Clamp(rgb[2], 0, 1));
  }

  /// <summary>Fills a device-space path with a paint through the clip, soft mask and blend mode in force.</summary>
  /// <param name="hairline">Draw without antialiasing: every pixel at least half covered is fully painted.</param>
  internal void PaintPath(VectorPath devicePath, FillRule rule, PdfPaint paint, float alpha, int subSamples = 4, bool hairline = false) {
    var clip = this._state.Clip;
    var limit = this._surface.Box.Intersect(clip?.Bounds ?? new PixelBox(0, 0, this._width, this._height));
    if (limit.IsEmpty || alpha <= 0)
      return;

    var mask = this._state.SoftMask;
    var mode = this._state.BlendMode;
    var surface = this._surface;
    var knockout = surface.KnockoutBackdrop != null;
    if (surface.Shadow != null)
      surface.Shadow.Ink = paint is PdfSolidPaint solid ? solid.Ink : this._state.FillOverprint ? PdfInk.FromRgbOverprinting : null;

    this._rasterizer.Fill(devicePath, rule, limit, (y, x0, x1, coverage) => {
      var count = x1 - x0;
      var rgb = this._rowRgb;
      var a = this._rowAlpha;
      var shape = this._rowShape;
      for (var i = 0; i < count; ++i) {
        var c = coverage[i];
        if (hairline)
          c = c >= 0.5f ? 1 : 0;
        if (clip != null)
          c *= clip.Coverage[y * this._width + x0 + i] * (1f / 255);
        shape[i] = c;
        a[i] = c;
      }

      paint.Fill(y, x0, count, rgb, a);
      for (var i = 0; i < count; ++i) {
        var v = a[i] * alpha;
        if (mask != null)
          v *= mask.At(x0 + i, y);
        a[i] = v;
      }

      surface.CompositeRow(y, x0, count, rgb, a, mode, knockout ? shape.AsSpan(0, count) : default);
    }, subSamples);
  }

  #endregion

  #region colour

  private void _SetSpace(bool stroke, object? value) {
    var space = PdfColorModel.Parse(this._document, value, this._resources);
    if (stroke) {
      this._state.StrokeSpace = space;
      this._state.StrokeColor = space.InitialColor();
      this._state.StrokePattern = null;
    } else {
      this._state.FillSpace = space;
      this._state.FillColor = space.InitialColor();
      this._state.FillPattern = null;
    }
  }

  private void _SetColor(bool stroke, List<object?> o) {
    var space = stroke ? this._state.StrokeSpace : this._state.FillSpace;
    var count = o.Count;
    object? pattern = null;
    if (count > 0 && o[^1] is string patternName) {
      pattern = this._document.Dict(this._document.Get(this._resources, "Pattern"))?.Raw(patternName);
      --count;
    }

    var values = new double[Math.Max(count, 0)];
    for (var i = 0; i < count; ++i)
      values[i] = PdfValues.Number(o[i]);

    if (stroke) {
      this._state.StrokeColor = values;
      this._state.StrokePattern = space.IsPattern ? pattern : null;
    } else {
      this._state.FillColor = values;
      this._state.FillPattern = space.IsPattern ? pattern : null;
    }
  }

  #endregion

  #region XObjects and images

  private void _DoXObject(string name) {
    var reference = this._document.Dict(this._document.Get(this._resources, "XObject"))?.Raw(name);
    if (this._document.Resolve(reference) is not PdfStreamObject stream)
      return;

    var dict = stream.Dictionary;
    if (this._document.Dict(dict.Raw("OC")) is { } oc && !this._optional.IsVisible(oc))
      return;

    switch (this._document.Name(dict, "Subtype")) {
      case "Image":
        this._DrawImage(stream, dict, stream.Data, false);
        break;
      case "Form":
        this.RunForm(stream, null, false);
        break;
      case "PS":
        break; // PostScript XObjects are not rendered by viewers (§8.8.2).
    }
  }

  /// <summary>Runs a form XObject: its matrix, its bounding box as a clip, its resources, and its group if any.</summary>
  /// <param name="matrixOverride">An extra transform, as an annotation appearance needs.</param>
  internal void RunForm(PdfStreamObject stream, Matrix2D? matrixOverride, bool ignoreGroup) {
    if (this._formDepth >= _MaxFormDepth || !this._activeForms.Add(stream))
      return;

    ++this._formDepth;
    var savedPatternBase = this._patternBase;
    this._saved.Push(this._state.Clone());
    var savedPath = this._path;
    var savedOpen = this._pathOpen;
    try {
      var dict = stream.Dictionary;
      if (matrixOverride is { } extra)
        this._state.Ctm = extra.Then(this._state.Ctm);
      if (this._document.Numbers(dict.Raw("Matrix")) is { Length: 6 } m)
        this._state.Ctm = new Matrix2D(m[0], m[1], m[2], m[3], m[4], m[5]).Then(this._state.Ctm);

      this._path = new VectorPath();
      if (this._document.Numbers(dict.Raw("BBox")) is { Length: 4 } box) {
        double x0 = Math.Min(box[0], box[2]), y0 = Math.Min(box[1], box[3]), x1 = Math.Max(box[0], box[2]), y1 = Math.Max(box[1], box[3]);
        var clip = new VectorPath();
        var ctm = this._state.Ctm;
        var p = ctm.Apply(x0, y0);
        clip.MoveTo(p.X, p.Y);
        p = ctm.Apply(x1, y0);
        clip.LineTo(p.X, p.Y);
        p = ctm.Apply(x1, y1);
        clip.LineTo(p.X, p.Y);
        p = ctm.Apply(x0, y1);
        clip.LineTo(p.X, p.Y);
        clip.Close();
        this._IntersectClip(clip, FillRule.NonZero);
      }

      var resources = this._document.Dict(dict.Raw("Resources")) ?? this._resources;
      var content = this._document.Decode(stream);
      this._patternBase = this._state.Ctm;
      var group = ignoreGroup ? null : this._document.Dict(dict.Raw("Group"));
      if (group != null && this._document.Name(group, "S") == "Transparency")
        this._RunGroup(content, resources, group);
      else
        this.Run(content, resources);
    } finally {
      this._state = this._saved.Pop();
      this._patternBase = savedPatternBase;
      this._path = savedPath;
      this._pathOpen = savedOpen;
      --this._formDepth;
      this._activeForms.Remove(stream);
    }
  }

  private void _DrawImage(object cacheKey, PdfDict dict, byte[] data, bool inline) {
    var imageCache = this._imageCache;
    if (this._state.Intent is { } intent && !this._intentImageCaches.TryGetValue(intent, out imageCache))
      this._intentImageCaches[intent] = imageCache = new(ReferenceEqualityComparer.Instance);
    if (!imageCache.TryGetValue(cacheKey, out var image)) {
      image = PdfImageDecoder.Decode(this._document, dict, data, this._resources, inline, intent: this._state.Intent);
      if (!inline)
        imageCache[cacheKey] = image;
    }

    PdfPaint? stencilPaint = null;
    var alpha = (float)this._state.FillAlpha;
    if (image.IsStencil) {
      stencilPaint = this._PaintFor(stroke: false, out alpha);
      if (stencilPaint == null)
        return;
    }

    var ctm = this._state.Ctm;
    if (PdfScaledImage.TryCreate(image, ctm, stencilPaint, this._width, this._height) is { } scaled) {
      this.PaintPath(scaled.Outline(), FillRule.NonZero, scaled, alpha);
      return;
    }

    var outline = new VectorPath();
    var p = ctm.Apply(0, 0);
    outline.MoveTo(p.X, p.Y);
    p = ctm.Apply(1, 0);
    outline.LineTo(p.X, p.Y);
    p = ctm.Apply(1, 1);
    outline.LineTo(p.X, p.Y);
    p = ctm.Apply(0, 1);
    outline.LineTo(p.X, p.Y);
    outline.Close();

    this.PaintPath(outline, FillRule.NonZero, new PdfImagePaint(image, ctm, stencilPaint), alpha);
  }

  private void _InlineImage(PdfLexer lexer) {
    // BI <key value pairs> ID <data> EI
    var dict = new PdfDict();
    while (true) {
      var key = lexer.ReadContentObject();
      if (key == PdfLexer.EndOfData)
        return;
      if (key is PdfOperator { Name: "ID" })
        break;
      if (key is not string name)
        continue;
      var value = lexer.ReadContentObject();
      if (value == PdfLexer.EndOfData)
        return;
      dict[name] = value;
    }

    var data = lexer.Data;
    var start = lexer.Position;
    if (start < lexer.End && PdfLexer.IsWhitespace(data[start]))
      ++start;

    var end = _FindInlineEnd(data, start, lexer.End, dict);
    var bytes = data.AsSpan(start, Math.Max(0, end - start)).ToArray();
    lexer.Position = Math.Min(lexer.End, end + 2);

    if (this._hiddenDepth > 0)
      return;

    try {
      this._DrawImage(new object(), dict, bytes, true);
    } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException) {
      // A broken inline image is skipped.
    }
  }

  /// <summary>Where an inline image's data ends: the "EI" that is followed by something that parses as content.</summary>
  private static int _FindInlineEnd(byte[] data, int start, int end, PdfDict dict) {
    // An unfiltered image's length is known from its size; trust it when "EI" sits right after.
    if (!dict.ContainsKey("F") && !dict.ContainsKey("Filter")) {
      var w = PdfValues.Integer(dict.Raw("W") ?? dict.Raw("Width"));
      var h = PdfValues.Integer(dict.Raw("H") ?? dict.Raw("Height"));
      var mask = dict.Raw("IM") ?? dict.Raw("ImageMask");
      var bpc = mask is true ? 1 : PdfValues.Integer(dict.Raw("BPC") ?? dict.Raw("BitsPerComponent"), 8);
      var cs = dict.Raw("CS") ?? dict.Raw("ColorSpace");
      var n = mask is true ? 1 : cs switch { "G" or "DeviceGray" or "CalGray" => 1, "CMYK" or "DeviceCMYK" => 4, "RGB" or "DeviceRGB" or "CalRGB" => 3, List<object?> { Count: > 0 } l when l[0] is "I" or "Indexed" => 1, _ => -1 };
      if (n > 0 && w > 0 && h > 0) {
        var length = (long)((w * (long)n * bpc + 7) / 8) * h;
        var at = start + length;
        if (at <= end) {
          var probe = (int)at;
          while (probe < end && PdfLexer.IsWhitespace(data[probe]))
            ++probe;
          if (probe + 1 < end && data[probe] == (byte)'E' && data[probe + 1] == (byte)'I')
            return probe;
        }
      }
    }

    for (var i = start; i + 1 < end; ++i) {
      if (data[i] != (byte)'E' || data[i + 1] != (byte)'I')
        continue;
      if (i > start && !PdfLexer.IsWhitespace(data[i - 1]))
        continue;
      if (i + 2 < end && !PdfLexer.IsWhitespace(data[i + 2]) && !PdfLexer.IsDelimiter(data[i + 2]))
        continue;

      // What follows should look like content: printable bytes for a little while.
      var plausible = true;
      for (var k = i + 2; k < Math.Min(end, i + 40); ++k)
        if (data[k] > 127 || (data[k] < 32 && !PdfLexer.IsWhitespace(data[k]))) {
          plausible = false;
          break;
        }

      if (plausible)
        return i;
    }

    return end;
  }

  #endregion
}
