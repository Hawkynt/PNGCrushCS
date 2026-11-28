using System;
using System.Collections.Generic;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

internal sealed partial class PdfRenderer {

  private const long _MaxTilePixels = 4L * 1024 * 1024;

  /// <summary>The <c>sh</c> operator: paints a shading over the whole clip region (§8.7.4.2).</summary>
  private void _ShadingOperator(string name) {
    var value = this._document.Dict(this._document.Get(this._resources, "Shading"))?.Raw(name);
    if (value == null)
      return;

    var shading = PdfShading.Parse(this._document, value, this._resources, this._state.Intent);
    var area = this._surface.Box.Intersect(this._state.Clip?.Bounds ?? new PixelBox(0, 0, this._width, this._height));
    if (shading.Extent(this._state.Ctm, this._width, this._height) is { } extent)
      area = area.Intersect(extent);
    if (area.IsEmpty)
      return;

    var paint = shading.CreatePaint(this._state.Ctm, area, false);
    var region = new VectorPath();
    region.AddRectangle(area.X0, area.Y0, area.X1 - area.X0, area.Y1 - area.Y0);
    this.PaintPath(region, FillRule.NonZero, paint, (float)this._state.FillAlpha);
  }

  /// <summary>The paint for a fill or stroke in the Pattern colour space.</summary>
  private PdfPaint? _PatternPaint(bool stroke) {
    var reference = stroke ? this._state.StrokePattern : this._state.FillPattern;
    var resolved = this._document.Resolve(reference);
    var dict = this._document.Dict(resolved);
    if (dict == null)
      return null;

    var matrix = this._document.Numbers(dict.Raw("Matrix")) is { Length: 6 } m ? new Matrix2D(m[0], m[1], m[2], m[3], m[4], m[5]) : Matrix2D.Identity;
    var toDevice = matrix.Then(this._patternBase);
    var area = this._surface.Box.Intersect(this._state.Clip?.Bounds ?? new PixelBox(0, 0, this._width, this._height));

    switch (this._document.Integer(dict, "PatternType")) {
      case 2: {
        var shading = PdfShading.Parse(this._document, dict.Raw("Shading"), this._resources, this._state.Intent);
        if (this._document.Dict(dict.Raw("ExtGState")) is { } gs)
          _ = gs; // An ExtGState in a shading pattern only carries parameters this renderer does not use.
        return shading.CreatePaint(toDevice, area, true);
      }
      case 1: {
        if (resolved is not PdfStreamObject stream)
          return null;

        PdfPaint? colour = null;
        if (this._document.Integer(dict, "PaintType", 1) == 2) {
          // An uncoloured pattern is a stencil painted in the colour given with the pattern name.
          var space = stroke ? this._state.StrokeSpace : this._state.FillSpace;
          var underlying = (space as PdfColorModel.PatternSpace)?.Underlying;
          if (underlying == null)
            return null;
          colour = _SolidPaint(underlying, stroke ? this._state.StrokeColor : this._state.FillColor);
        }

        return this._TilingPaint(stream, dict, toDevice, colour);
      }
      default:
        throw new NotSupportedException($"Pattern type {this._document.Integer(dict, "PatternType")} is not supported.");
    }
  }

  /// <summary>
  /// Renders one cell of a tiling pattern to a raster in pattern space, and returns a paint that
  /// looks each device pixel up in it through the pattern's transform, modulo the steps.
  /// </summary>
  /// <remarks>
  /// Sampling every device pixel through the exact inverse keeps the tiles where the pattern matrix
  /// puts them however the steps fall against the pixel grid, instead of blitting a whole-pixel tile
  /// that drifts. A cell whose box reaches past its steps is handled by looking in every cell that
  /// can cover the point.
  /// </remarks>
  private PdfPaint? _TilingPaint(PdfStreamObject stream, PdfDict dict, Matrix2D toDevice, PdfPaint? stencilColour) {
    var bbox = this._document.Numbers(dict.Raw("BBox"));
    if (bbox is not { Length: 4 })
      return null;

    double bx0 = Math.Min(bbox[0], bbox[2]), by0 = Math.Min(bbox[1], bbox[3]), bx1 = Math.Max(bbox[0], bbox[2]), by1 = Math.Max(bbox[1], bbox[3]);
    var xStep = Math.Abs(this._document.Number(dict, "XStep", bx1 - bx0));
    var yStep = Math.Abs(this._document.Number(dict, "YStep", by1 - by0));
    if (bx1 - bx0 <= 0 || by1 - by0 <= 0 || xStep <= 0 || yStep <= 0 || toDevice.Inverse is not { } inverse)
      return null;

    // Resolution of the cell raster: as fine as the device, at least a few pixels a cell.
    var scaleX = Math.Sqrt(toDevice.A * toDevice.A + toDevice.B * toDevice.B);
    var scaleY = Math.Sqrt(toDevice.C * toDevice.C + toDevice.D * toDevice.D);
    var cellWidth = Math.Max(1, (int)Math.Ceiling((bx1 - bx0) * scaleX));
    var cellHeight = Math.Max(1, (int)Math.Ceiling((by1 - by0) * scaleY));
    var shrink = Math.Sqrt(_MaxTilePixels / Math.Max(1.0, (double)cellWidth * cellHeight));
    if (shrink < 1) {
      cellWidth = Math.Max(1, (int)(cellWidth * shrink));
      cellHeight = Math.Max(1, (int)(cellHeight * shrink));
    }

    var cellScaleX = cellWidth / (bx1 - bx0);
    var cellScaleY = cellHeight / (by1 - by0);

    // Pattern space to cell raster: y up in pattern space, down in the raster.
    var toCell = new Matrix2D(cellScaleX, 0, 0, -cellScaleY, -bx0 * cellScaleX, by1 * cellScaleY);
    var cell = new PdfRenderer(this._document, cellWidth, cellHeight, this._omissions, this._formDepth + 1, this._activeForms) { SystemFonts = this.SystemFonts };
    cell.BeginTransparent(toCell);
    var content = this._document.Decode(stream);
    var resources = this._document.Dict(dict.Raw("Resources")) ?? this._resources;
    if (!this._activeForms.Add(stream))
      return null;
    try {
      cell.Run(content, resources);
    } finally {
      this._activeForms.Remove(stream);
    }

    return new TilingPaint(cell.Surface, inverse, bx0, by0, bx1, by1, xStep, yStep, cellScaleX, cellScaleY, stencilColour);
  }

  private sealed class TilingPaint(PdfSurface cell, Matrix2D toPattern, double bx0, double by0, double bx1, double by1, double xStep, double yStep, double scaleX, double scaleY, PdfPaint? stencil) : PdfPaint {
    public override void Fill(int y, int x0, int count, Span<float> rgb, Span<float> alpha) {
      if (stencil != null)
        stencil.Fill(y, x0, count, rgb, alpha);

      var pixels = cell.Pixels;
      var reachX = (int)Math.Ceiling((bx1 - bx0) / xStep);
      var reachY = (int)Math.Ceiling((by1 - by0) / yStep);
      for (var i = 0; i < count; ++i) {
        if (alpha[i] <= 0)
          continue;

        var (u, v) = toPattern.Apply(x0 + i + 0.5, y + 0.5);

        // The cell (i, j) whose origin is nearest below the point, then the earlier cells whose box
        // still reaches it; later cells are painted over earlier ones.
        var ci = Math.Floor((u - bx0) / xStep);
        var cj = Math.Floor((v - by0) / yStep);
        float r = 0, g = 0, b = 0, a = 0;
        for (var dj = reachY - 1; dj >= 0; --dj)
          for (var di = reachX - 1; di >= 0; --di) {
            var lu = u - (ci - di) * xStep;
            var lv = v - (cj - dj) * yStep;
            if (lu < bx0 || lu >= bx1 || lv < by0 || lv >= by1)
              continue;

            var px = Math.Clamp((int)((lu - bx0) * scaleX), 0, cell.Width - 1);
            var py = Math.Clamp((int)((by1 - lv) * scaleY), 0, cell.Height - 1);
            var at = (py * cell.Width + px) * 4;
            var sa = pixels[at + 3];
            if (sa <= 0)
              continue;

            // Source over what earlier cells left, premultiplied.
            r = pixels[at] + r * (1 - sa);
            g = pixels[at + 1] + g * (1 - sa);
            b = pixels[at + 2] + b * (1 - sa);
            a = sa + a * (1 - sa);
          }

        if (a <= 0) {
          alpha[i] = 0;
          continue;
        }

        if (stencil == null) {
          rgb[i * 3] = Math.Clamp(r / a, 0, 1);
          rgb[i * 3 + 1] = Math.Clamp(g / a, 0, 1);
          rgb[i * 3 + 2] = Math.Clamp(b / a, 0, 1);
        }

        alpha[i] *= Math.Min(1, a);
      }
    }
  }
}
