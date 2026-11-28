using System;
using System.Collections.Generic;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;
using FileFormat.Pdf.Fonts;

namespace FileFormat.Pdf.Rendering;

internal sealed partial class PdfRenderer {

  private const int _MaxType3Depth = 8;

  private readonly Dictionary<object, PdfFont> _fonts = new(ReferenceEqualityComparer.Instance);
  private readonly HashSet<string> _reportedFonts = [];
  private Matrix2D _textMatrix = Matrix2D.Identity;
  private Matrix2D _lineMatrix = Matrix2D.Identity;
  private VectorPath? _textClip;
  private int _type3Depth;

  /// <summary>Inside a Type 3 glyph begun with d1, which takes its colour from the text and ignores its own.</summary>
  private bool _uncolouredGlyph;

  private void _BeginText() {
    this._textMatrix = Matrix2D.Identity;
    this._lineMatrix = Matrix2D.Identity;
    this._textClip = null;
  }

  private void _EndText() {
    // Glyphs shown in a clipping mode become the clip at the end of the text object (§9.3.6).
    if (this._textClip != null)
      this._IntersectClip(this._textClip, FillRule.NonZero);
    this._textClip = null;
  }

  private void _SetFont(string name, double size) {
    var reference = this._document.Dict(this._document.Get(this._resources, "Font"))?.Raw(name);
    this._SetFontFromReference(reference, size);
  }

  private void _SetFontFromReference(object? reference, double size) {
    this._state.FontSize = size;
    if (this._document.Dict(reference) is not { } dict) {
      this._state.Font = null;
      return;
    }

    if (!this._fonts.TryGetValue(dict, out var font)) {
      font = PdfFont.Load(this._document, dict);
      this._fonts[dict] = font;
    }

    this._state.Font = font;
  }

  private void _TextMove(double tx, double ty) {
    this._lineMatrix = Matrix2D.Translation(tx, ty).Then(this._lineMatrix);
    this._textMatrix = this._lineMatrix;
  }

  private void _SetTextMatrix(Matrix2D m) {
    this._textMatrix = m;
    this._lineMatrix = m;
  }

  private void _ShowTextArray(List<object?> array, bool hidden) {
    var font = this._state.Font;
    foreach (var item in array) {
      if (item is byte[] text)
        this._ShowText(text, hidden);
      else if (PdfValues.IsNumber(item)) {
        // A number moves the next glyph back by thousandths of the font size, along the line.
        var adjust = -PdfValues.Number(item) / 1000 * this._state.FontSize;
        if (font != null && font.IsVertical)
          this._textMatrix = Matrix2D.Translation(0, adjust).Then(this._textMatrix);
        else
          this._textMatrix = Matrix2D.Translation(adjust * this._state.HorizontalScaling, 0).Then(this._textMatrix);
      }
    }
  }

  private void _ShowText(byte[] text, bool hidden) {
    var font = this._state.Font;
    if (font == null)
      return;

    var state = this._state;
    var size = state.FontSize;
    var scaling = state.HorizontalScaling;
    var mode = state.RenderMode;
    var draws = !hidden && mode != 3 && mode != 7;
    var clips = mode >= 4 && mode <= 7;

    if (font.MissingReason != null && (draws || clips) && this._reportedFonts.Add(font.Name)) {
      this._omissions.Add($"Text was not drawn: {font.MissingReason}.");
    }

    foreach (var (code, wordSpace) in font.Codes(text)) {
      double advanceX = 0, advanceY = 0;
      var spacing = state.CharSpacing + (wordSpace ? state.WordSpacing : 0);
      Matrix2D glyphToText;
      if (font.IsVertical) {
        var (w1, vx, vy) = font.VerticalMetrics(code);
        glyphToText = new Matrix2D(size * scaling, 0, 0, size, -vx * size * scaling, -vy * size + state.Rise);
        advanceY = w1 * size + spacing;
      } else {
        glyphToText = new Matrix2D(size * scaling, 0, 0, size, 0, state.Rise);
        advanceX = (font.Width(code) * size + spacing) * scaling;
      }

      if ((draws || clips) && font.MissingReason == null) {
        var textToDevice = this._textMatrix.Then(state.Ctm);
        // ISO 32000-2 §9.3.6: a Type 3 glyph is drawn by its own description in every mode but 3
        // and 7, and modes 4 to 7 add nothing to the clipping path — so a text object of Type 3
        // glyphs alone does not clip at all (ISO 32000-1: the mode has no effect on Type 3 text).
        if (font.IsType3) {
          if (draws)
            this._DrawType3Glyph(font, code, glyphToText.Then(this._textMatrix));
        }
        else
          this._DrawGlyph(font.Outline(code), glyphToText.Then(textToDevice), draws, clips);
      }

      this._textMatrix = Matrix2D.Translation(advanceX, advanceY).Then(this._textMatrix);
    }
  }

  private void _DrawGlyph(GlyphOutline? outline, Matrix2D toDevice, bool draws, bool clips) {
    if (outline == null || outline.IsEmpty)
      return;

    var path = new VectorPath();
    var points = outline.Points;
    var at = 0;
    foreach (var verb in outline.Verbs)
      switch (verb) {
        case GlyphVerb.MoveTo: {
          var (x, y) = toDevice.Apply(points[at], points[at + 1]);
          path.MoveTo(x, y);
          at += 2;
          break;
        }
        case GlyphVerb.LineTo: {
          var (x, y) = toDevice.Apply(points[at], points[at + 1]);
          path.LineTo(x, y);
          at += 2;
          break;
        }
        case GlyphVerb.CurveTo: {
          var (x1, y1) = toDevice.Apply(points[at], points[at + 1]);
          var (x2, y2) = toDevice.Apply(points[at + 2], points[at + 3]);
          var (x3, y3) = toDevice.Apply(points[at + 4], points[at + 5]);
          path.CurveTo(x1, y1, x2, y2, x3, y3);
          at += 6;
          break;
        }
        case GlyphVerb.Close:
          path.Close();
          break;
      }

    if (path.IsEmpty)
      return;

    var mode = this._state.RenderMode;
    if (draws) {
      // Small glyphs get finer vertical sampling: their stems are a pixel or two, where four
      // sub-rows show as banding against a rasteriser that computes exact coverage.
      var height = Math.Sqrt(Math.Abs(toDevice.Determinant));
      var samples = height < 48 ? 16 : 4;
      if (mode is 0 or 2 or 4 or 6) {
        var paint = this._PaintFor(stroke: false, out var alpha);
        if (paint != null)
          this.PaintPath(path, FillRule.NonZero, paint, alpha, samples);
      }

      if (mode is 1 or 2 or 5 or 6) {
        var paint = this._PaintFor(stroke: true, out var alpha);
        if (paint != null)
          this.PaintPath(PdfStroker.Outline(path, this._state.LineStyle, this._state.Ctm), FillRule.NonZero, paint, alpha);
      }
    }

    if (clips) {
      this._textClip ??= new VectorPath();
      foreach (var (xs, ys, closed) in path.SubPaths) {
        var x = xs.Span;
        var y = ys.Span;
        this._textClip.MoveTo(x[0], y[0]);
        for (var i = 1; i < x.Length; ++i)
          this._textClip.LineTo(x[i], y[i]);
        this._textClip.Close();
      }
    }
  }

  /// <summary>Runs a Type 3 glyph procedure with glyph space mapped onto the text position.</summary>
  private void _DrawType3Glyph(PdfFont font, int code, Matrix2D glyphToUserOfText) {
    if (this._type3Depth >= _MaxType3Depth)
      return;

    var proc = font.CharProc(code);
    if (proc == null || !this._activeForms.Add(proc))
      return;


    ++this._type3Depth;
    var savedState = this._state;
    var savedText = this._textMatrix;
    var savedLine = this._lineMatrix;
    var savedClipPath = this._textClip;
    var savedUncoloured = this._uncolouredGlyph;
    var savedPatternBase = this._patternBase;
    var savedPath = this._path;
    this._saved.Push(this._state.Clone());
    try {
      this._state = this._state.Clone();
      this._state.Ctm = font.FontMatrix.Then(glyphToUserOfText).Then(savedState.Ctm);
      this._path = new VectorPath();
      this._uncolouredGlyph = false;
      this._textClip = null;
      this.Run(this._document.Decode(proc), font.Resources ?? this._resources);
    } finally {
      this._state = this._saved.Pop();
      this._textMatrix = savedText;
      this._lineMatrix = savedLine;
      this._textClip = savedClipPath;
      this._uncolouredGlyph = savedUncoloured;
      this._patternBase = savedPatternBase;
      this._path = savedPath;
      --this._type3Depth;
      this._activeForms.Remove(proc);
    }
  }

  /// <summary>d0 and d1: a Type 3 glyph's width, and for d1 that its colour comes from outside.</summary>
  private void _Type3Width(List<object?> operands, bool uncoloured) {
    if (this._type3Depth > 0 && uncoloured)
      this._uncolouredGlyph = true;
  }
}
