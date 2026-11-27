using System;
using System.Collections.Generic;
using FileFormat.Core.Vector;

namespace FileFormat.Pdf.Rendering;

/// <summary>A rectangle of device pixels, half-open: <c>[X0, X1) × [Y0, Y1)</c>.</summary>
internal readonly record struct PixelBox(int X0, int Y0, int X1, int Y1) {
  public bool IsEmpty => this.X1 <= this.X0 || this.Y1 <= this.Y0;
  public PixelBox Intersect(PixelBox other) => new(Math.Max(this.X0, other.X0), Math.Max(this.Y0, other.Y0), Math.Min(this.X1, other.X1), Math.Min(this.Y1, other.Y1));
  public PixelBox Union(PixelBox other) => this.IsEmpty ? other : other.IsEmpty ? this : new(Math.Min(this.X0, other.X0), Math.Min(this.Y0, other.Y0), Math.Max(this.X1, other.X1), Math.Max(this.Y1, other.Y1));
}

/// <summary>Receives one row of coverage: pixels <c>x0</c> to <c>x1</c> (exclusive), coverage indexed from <c>x0</c>.</summary>
internal delegate void CoverageRow(int y, int x0, int x1, float[] coverage);

/// <summary>
/// Turns a flattened path into per-pixel coverage, one row at a time.
/// </summary>
/// <remarks>
/// Each pixel row is sampled at a fixed number of sub-rows; along a sub-row the inside spans are
/// found from the sorted edge crossings under the fill rule and added to the row with exact
/// horizontal coverage. That is the same scheme as <see cref="VectorCanvas"/>, with an active edge
/// list so a page of text does not test every edge on every row, and with the output handed to a
/// callback so the compositor can apply clip, paint and blend mode itself. Four sub-rows match the
/// vertical resolution of poppler's 4×4 antialiasing, which is what the output is compared with.
/// </remarks>
internal sealed class PdfRasterizer(int width, int height) {

  private struct Edge {
    public double X0, Y0, Y1, Slope;
    public int Winding;
  }

  private readonly float[] _coverage = new float[width + 2];
  private readonly List<Edge> _edges = [];
  private readonly List<int> _active = [];
  private double[] _crossX = new double[64];
  private int[] _crossW = new int[64];

  public int Width { get; } = width;

  public int Height { get; } = height;

  /// <summary>The pixel box a path's edges fall in, clamped to the surface.</summary>
  public PixelBox BoundsOf(VectorPath path) {
    if (path.Bounds is not { } b || !double.IsFinite(b.MinX) || !double.IsFinite(b.MaxX) || !double.IsFinite(b.MinY) || !double.IsFinite(b.MaxY))
      return default;

    return new PixelBox(
      (int)Math.Max(0, Math.Floor(Math.Max(-1e9, b.MinX))),
      (int)Math.Max(0, Math.Floor(Math.Max(-1e9, b.MinY))),
      (int)Math.Min(this.Width, Math.Ceiling(Math.Min(1e9, b.MaxX)) + 1),
      (int)Math.Min(this.Height, Math.Ceiling(Math.Min(1e9, b.MaxY)) + 1));
  }

  /// <summary>Rasterises a path under a fill rule, within a box, calling back for each row it touches.</summary>
  public void Fill(VectorPath path, FillRule rule, PixelBox limit, CoverageRow onRow, int subSamples = 4) {
    this._edges.Clear();
    double minY = double.MaxValue, maxY = double.MinValue, minX = double.MaxValue, maxX = double.MinValue;
    foreach (var (xsMemory, ysMemory, _) in path.SubPaths) {
      var xs = xsMemory.Span;
      var ys = ysMemory.Span;
      for (var i = 0; i < xs.Length; ++i) {
        var j = i + 1 == xs.Length ? 0 : i + 1;
        double x0 = xs[i], y0 = ys[i], x1 = xs[j], y1 = ys[j];
        if (y0 == y1 || !double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1))
          continue;

        var winding = 1;
        if (y0 > y1) {
          (x0, y0, x1, y1) = (x1, y1, x0, y0);
          winding = -1;
        }

        this._edges.Add(new Edge { X0 = x0, Y0 = y0, Y1 = y1, Slope = (x1 - x0) / (y1 - y0), Winding = winding });
        minY = Math.Min(minY, y0);
        maxY = Math.Max(maxY, y1);
        minX = Math.Min(minX, Math.Min(x0, x1));
        maxX = Math.Max(maxX, Math.Max(x0, x1));
      }
    }

    if (this._edges.Count == 0)
      return;

    var box = limit.Intersect(new PixelBox(
      (int)Math.Max(0, Math.Floor(Math.Max(-1e9, minX))),
      (int)Math.Max(0, Math.Floor(Math.Max(-1e9, minY))),
      (int)Math.Min(this.Width, Math.Ceiling(Math.Min(1e9, maxX)) + 1),
      (int)Math.Min(this.Height, Math.Ceiling(Math.Min(1e9, maxY)) + 1)));
    if (box.IsEmpty)
      return;

    this._edges.Sort(static (a, b) => a.Y0.CompareTo(b.Y0));
    this._active.Clear();
    var nextEdge = 0;
    var coverage = this._coverage;
    var weight = 1f / subSamples;
    var spanLeft = (double)box.X0;
    var spanRight = (double)box.X1;

    for (var y = box.Y0; y < box.Y1; ++y) {
      var touchedLow = int.MaxValue;
      var touchedHigh = int.MinValue;

      for (var s = 0; s < subSamples; ++s) {
        var sy = y + (s + 0.5) / subSamples;

        while (nextEdge < this._edges.Count && this._edges[nextEdge].Y0 <= sy) {
          this._active.Add(nextEdge);
          ++nextEdge;
        }

        var count = 0;
        for (var a = this._active.Count - 1; a >= 0; --a) {
          var edge = this._edges[this._active[a]];
          if (edge.Y1 <= sy) {
            this._active[a] = this._active[^1];
            this._active.RemoveAt(this._active.Count - 1);
            continue;
          }

          if (sy < edge.Y0)
            continue;

          if (count == this._crossX.Length) {
            Array.Resize(ref this._crossX, count * 2);
            Array.Resize(ref this._crossW, count * 2);
          }

          this._crossX[count] = edge.X0 + (sy - edge.Y0) * edge.Slope;
          this._crossW[count] = edge.Winding;
          ++count;
        }

        if (count < 2)
          continue;

        _SortCrossings(this._crossX, this._crossW, count);

        if (rule == FillRule.EvenOdd) {
          for (var i = 0; i + 1 < count; i += 2)
            _AddSpan(coverage, this._crossX[i], this._crossX[i + 1], spanLeft, spanRight, weight, ref touchedLow, ref touchedHigh);
        } else {
          var winding = 0;
          var start = 0.0;
          for (var i = 0; i < count; ++i) {
            var before = winding;
            winding += this._crossW[i];
            if (before == 0 && winding != 0)
              start = this._crossX[i];
            else if (before != 0 && winding == 0)
              _AddSpan(coverage, start, this._crossX[i], spanLeft, spanRight, weight, ref touchedLow, ref touchedHigh);
          }
        }
      }

      if (touchedLow > touchedHigh)
        continue;

      var x0 = touchedLow;
      var x1 = touchedHigh + 1;
      var row = this._rowBuffer.Length >= x1 - x0 ? this._rowBuffer : this._rowBuffer = new float[this.Width + 2];
      for (var x = x0; x < x1; ++x) {
        var c = coverage[x];
        row[x - x0] = c > 1 ? 1 : c;
        coverage[x] = 0;
      }

      onRow(y, x0, x1, row);
    }
  }

  private float[] _rowBuffer = new float[width + 2];

  private static void _SortCrossings(double[] xs, int[] ws, int count) {
    for (var i = 1; i < count; ++i) {
      var x = xs[i];
      var w = ws[i];
      var j = i - 1;
      while (j >= 0 && xs[j] > x) {
        xs[j + 1] = xs[j];
        ws[j + 1] = ws[j];
        --j;
      }

      xs[j + 1] = x;
      ws[j + 1] = w;
    }
  }

  private static void _AddSpan(float[] coverage, double from, double to, double left, double right, float weight, ref int low, ref int high) {
    if (from < left)
      from = left;
    if (to > right)
      to = right;
    if (to <= from)
      return;

    var first = (int)from;
    var last = (int)Math.Ceiling(to) - 1;
    if (first < low)
      low = first;
    if (last > high)
      high = last;

    if (first == last) {
      coverage[first] += (float)(to - from) * weight;
      return;
    }

    coverage[first] += (float)(first + 1 - from) * weight;
    for (var x = first + 1; x < last; ++x)
      coverage[x] += weight;
    coverage[last] += (float)(to - last) * weight;
  }
}
