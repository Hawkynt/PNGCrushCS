using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.TrueType;

/// <summary>Lays a font's glyphs out as a sheet and fills their outlines.</summary>
/// <remarks>
/// The glyphs are drawn in the order the font stores them, which is glyph order and not the order of
/// any character set: glyph zero is the box a font shows for a character it does not have, and what
/// follows it is whatever the font's designer put there.
/// <para/>
/// The outlines are filled by the non-zero rule. Apple's manual has an outer contour wound one way
/// and the counter inside it the other, so the two cancel and the hole comes out; the even-odd rule
/// would give the same answer for a well-made font and the wrong one for overlapping contours, which
/// fonts do have.
/// </remarks>
public static class TrueTypeRenderer {

  /// <summary>How much of a cell the em box takes up, leaving a gap between glyphs.</summary>
  private const double _EmFraction = 0.78;

  /// <summary>Where the baseline sits in a cell, measured up from its bottom.</summary>
  private const double _BaselineFraction = 0.22;

  /// <summary>The colour a sheet is drawn on and the colour the glyphs are drawn in.</summary>
  private static readonly Rgba32 _Paper = Rgba32.White, _Ink = Rgba32.Black;

  /// <summary>Draws the font's first glyphs, sixteen to a row.</summary>
  public static RawImage Render(TrueTypeFile file) {
    if (file.Glyphs == null)
      throw new InvalidDataException("A font with no glyphs read cannot be drawn.");

    var count = Math.Min(file.Glyphs.Count, TrueTypeFile.SheetGlyphs);
    if (count < 1)
      throw new InvalidDataException("A font with no glyphs in it has no sheet.");

    var columns = Math.Min(TrueTypeFile.SheetColumns, count);
    var rows = (count + TrueTypeFile.SheetColumns - 1) / TrueTypeFile.SheetColumns;
    var cell = TrueTypeFile.SheetCell;
    var canvas = new VectorCanvas(columns * cell, rows * cell, _Paper);

    var scale = cell * _EmFraction / file.UnitsPerEm;
    for (var i = 0; i < count; ++i) {
      var column = i % TrueTypeFile.SheetColumns;
      var row = i / TrueTypeFile.SheetColumns;

      // The glyph's own origin is on the baseline at the left of the cell, and the y axis points up
      // in a font and down in a raster.
      var originX = column * cell + cell * (1 - _EmFraction) / 2;
      var originY = (row + 1) * cell - cell * _BaselineFraction;

      var path = new VectorPath();
      foreach (var contour in file.Glyphs[i].Contours)
        _Contour(path, contour, originX, originY, scale);

      if (!path.IsEmpty)
        canvas.Fill(path, FillRule.NonZero, _Ink);
    }

    return canvas.ToRawImage();
  }

  /// <summary>
  /// Turns one contour into a closed subpath.
  /// </summary>
  /// <remarks>
  /// A contour is a ring, so where it starts is a choice rather than a fact. It has to start on the
  /// curve: where the first point is not, the last one is used if it is on the curve, and otherwise
  /// the point halfway between the two, which is the on-curve point the format leaves implied.
  /// </remarks>
  private static void _Contour(VectorPath path, IReadOnlyList<TrueTypePoint> contour, double originX, double originY, double scale) {
    var count = contour.Count;
    if (count == 0)
      return;

    (double X, double Y) Place(TrueTypePoint point) => (originX + point.X * scale, originY - point.Y * scale);
    static TrueTypePoint Midpoint(TrueTypePoint a, TrueTypePoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2, true);

    var first = contour[0];
    var startIndex = 0;
    if (!first.OnCurve) {
      var last = contour[count - 1];
      first = last.OnCurve ? last : Midpoint(first, last);
      startIndex = count == 1 ? 1 : 0;
    } else
      startIndex = 1;

    var (startX, startY) = Place(first);
    path.MoveTo(startX, startY);

    TrueTypePoint? control = null;
    for (var step = 0; step < count; ++step) {
      var point = contour[(startIndex + step) % count];
      if (point.OnCurve) {
        var (x, y) = Place(point);
        if (control == null)
          path.LineTo(x, y);
        else {
          var (cx, cy) = Place(control.Value);
          path.QuadraticTo(cx, cy, x, y);
          control = null;
        }

        continue;
      }

      // Two control points in a row have an on-curve point implied exactly halfway between them.
      if (control != null) {
        var implied = Midpoint(control.Value, point);
        var (cx, cy) = Place(control.Value);
        var (mx, my) = Place(implied);
        path.QuadraticTo(cx, cy, mx, my);
      }

      control = point;
    }

    if (control != null) {
      var (cx, cy) = Place(control.Value);
      path.QuadraticTo(cx, cy, startX, startY);
    }

    path.Close();
  }
}
