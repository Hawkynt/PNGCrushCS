using System;
using System.Collections.Generic;

namespace FileFormat.Core.Vector;

/// <summary>
/// A pixel surface that fills and strokes <see cref="VectorPath"/>s, which is all a line-and-fill
/// format needs to be drawn.
/// </summary>
/// <remarks>
/// Scanline filling with a fixed number of sample rows per pixel and exact horizontal coverage.
/// That gives edges that are smooth across a row and stepped down a column at the sample count,
/// which is enough to compare a drawing against another renderer's by shape; nothing here claims
/// to match anybody's antialiasing exactly, and the geometry is what is being checked.
/// <para/>
/// Strokes are turned into fills: each segment becomes a quadrilateral and each join and cap a
/// small polygon, all wound the same way, and the lot is filled by the non-zero rule so the
/// overlaps merge instead of cancelling. That is the standard trick and it means there is one
/// rasteriser rather than two.
/// </remarks>
public sealed class VectorCanvas {

  /// <summary>Sample rows per pixel row.</summary>
  private const int _SubSamples = 4;

  /// <summary>The most pixels a surface may have, so a file stating a silly extent cannot exhaust memory.</summary>
  public const long MaximumPixels = 64L * 1024 * 1024;

  private readonly float[] _red;
  private readonly float[] _green;
  private readonly float[] _blue;
  private readonly float[] _alpha;
  private readonly double[] _coverage;
  private readonly Rgba32 _clearedTo;

  /// <summary>How wide the surface is, in pixels.</summary>
  public int Width { get; }

  /// <summary>How tall the surface is, in pixels.</summary>
  public int Height { get; }

  /// <summary>Builds a surface of the given size, cleared to a colour.</summary>
  public VectorCanvas(int width, int height, Rgba32 background) {
    if (width < 1 || height < 1)
      throw new ArgumentOutOfRangeException(nameof(width), $"A canvas of {width}x{height} has no pixels.");
    if ((long)width * height > MaximumPixels)
      throw new ArgumentOutOfRangeException(nameof(width), $"A canvas of {width}x{height} is larger than this will draw.");

    this.Width = width;
    this.Height = height;

    var count = width * height;
    this._red = new float[count];
    this._green = new float[count];
    this._blue = new float[count];
    this._alpha = new float[count];
    this._coverage = new double[width + 2];
    this._clearedTo = background;

    var r = background.R / 255f;
    var g = background.G / 255f;
    var b = background.B / 255f;
    var a = background.A / 255f;
    for (var i = 0; i < count; ++i) {
      this._red[i] = r * a;
      this._green[i] = g * a;
      this._blue[i] = b * a;
      this._alpha[i] = a;
    }
  }

  /// <summary>Paints everything the rule calls inside the path.</summary>
  public void Fill(VectorPath path, FillRule rule, Rgba32 colour) => this.Fill(path, rule, VectorPaint.Solid(colour), null, null);

  /// <summary>Paints everything the rule calls inside the path, through a stipple.</summary>
  /// <param name="stipple">
  /// A dot pattern the paint only reaches through, or null for a solid fill. It is aligned to the
  /// pixel grid rather than to the shape, which is what the machines these patterns come from do:
  /// the pattern is a property of the screen, not of the drawing, so two shapes filled with the
  /// same one line up where they meet.
  /// </param>
  public void Fill(VectorPath path, FillRule rule, Rgba32 colour, VectorStipple? stipple)
    => this.Fill(path, rule, VectorPaint.Solid(colour), stipple, null);

  /// <summary>Paints everything the rule calls inside the path, through a stipple and a mask.</summary>
  /// <param name="mask">What the fill is allowed to reach, or null for the whole surface.</param>
  public void Fill(VectorPath path, FillRule rule, VectorPaint paint, VectorStipple? stipple, VectorMask? mask) {
    ArgumentNullException.ThrowIfNull(path);
    ArgumentNullException.ThrowIfNull(paint);
    if (stipple is { IsBlank: true })
      return;

    var edges = _EdgesOf(path);
    if (edges.Count == 0)
      return;

    this._Scan(edges, rule, y => this._Blend(y, paint, stipple, mask));
  }

  /// <summary>Paints a line of the given width along the path.</summary>
  public void Stroke(VectorPath path, double width, Rgba32 colour, LineJoin join = LineJoin.Miter, LineCap cap = LineCap.Butt, double miterLimit = 4)
    => this.Stroke(path, width, VectorPaint.Solid(colour), null, join, cap, miterLimit);

  /// <summary>Paints a line of the given width along the path, through a mask.</summary>
  public void Stroke(VectorPath path, double width, VectorPaint paint, VectorMask? mask, LineJoin join = LineJoin.Miter, LineCap cap = LineCap.Butt, double miterLimit = 4) {
    ArgumentNullException.ThrowIfNull(path);

    var outline = StrokeOutline(path, width, join, cap, miterLimit);
    if (outline.IsEmpty)
      return;

    this.Fill(outline, FillRule.NonZero, paint, null, mask);
  }

  /// <summary>Paints a raster picture onto the surface through a transform.</summary>
  /// <param name="image">The picture to place.</param>
  /// <param name="placement">
  /// Maps the picture's own pixel grid — nought to its width across and nought to its height down —
  /// onto the surface.
  /// </param>
  /// <param name="mask">What the picture is allowed to reach, or null for the whole surface.</param>
  /// <remarks>
  /// Several of these drawing formats can carry a raster inside them: SVG has an <c>image</c>
  /// element, CGM a cell array, GEM a raster opcode. Each is a rectangle of pixels placed by a
  /// transform, so one blit serves all three.
  /// <para/>
  /// The surface is walked rather than the picture, and each of its pixels asks which pixel of the
  /// picture landed there. Walking the picture instead and marking where each of its pixels goes
  /// leaves holes wherever the placement enlarges — the same picture, with a grid of the background
  /// showing through it.
  /// <para/>
  /// Nearest neighbour. What is placed is usually the whole point of the file rather than a detail
  /// of it, and at one pixel to one it comes out exactly as it went in; smoothing would soften a
  /// picture that was meant to arrive unchanged.
  /// </remarks>
  public void DrawImage(RawImage image, Matrix2D placement, VectorMask? mask = null) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      return;

    var inverse = placement.Inverse;
    if (inverse == null)
      return;

    var source = image.Format == PixelFormat.Rgba32 ? image : PixelConverter.Convert(image, PixelFormat.Rgba32);

    // The device box the placed rectangle falls in, which is where there is anything to draw.
    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
    foreach (var (cornerX, cornerY) in new[] { (0.0, 0.0), (image.Width, 0.0), (0.0, (double)image.Height), ((double)image.Width, (double)image.Height) }) {
      var (x, y) = placement.Apply(cornerX, cornerY);
      minX = Math.Min(minX, x);
      minY = Math.Min(minY, y);
      maxX = Math.Max(maxX, x);
      maxY = Math.Max(maxY, y);
    }

    var left = Math.Max(0, (int)Math.Floor(minX));
    var top = Math.Max(0, (int)Math.Floor(minY));
    var right = Math.Min(this.Width - 1, (int)Math.Ceiling(maxX));
    var bottom = Math.Min(this.Height - 1, (int)Math.Ceiling(maxY));

    for (var y = top; y <= bottom; ++y)
      for (var x = left; x <= right; ++x) {
        var (u, v) = inverse.Value.Apply(x + 0.5, y + 0.5);
        var sourceX = (int)Math.Floor(u);
        var sourceY = (int)Math.Floor(v);
        if (sourceX < 0 || sourceY < 0 || sourceX >= image.Width || sourceY >= image.Height)
          continue;

        var at = (y * this.Width + x);
        var alpha = source.PixelData[(sourceY * image.Width + sourceX) * 4 + 3] / 255f;
        if (mask != null)
          alpha *= mask.Coverage[at] / 255f;

        if (alpha <= 0)
          continue;

        var from = (sourceY * image.Width + sourceX) * 4;
        var keep = 1 - alpha;
        this._red[at] = this._red[at] * keep + source.PixelData[from] / 255f * alpha;
        this._green[at] = this._green[at] * keep + source.PixelData[from + 1] / 255f * alpha;
        this._blue[at] = this._blue[at] * keep + source.PixelData[from + 2] / 255f * alpha;
        this._alpha[at] = this._alpha[at] * keep + alpha;
      }
  }

  /// <summary>
  /// The coverage of a path, as a mask that later fills can be confined to.
  /// </summary>
  /// <remarks>
  /// A clipping path is not drawn; it says where drawing is allowed. Rasterising it into coverage
  /// once and multiplying every later fill by it gives that, including a soft edge where the clip
  /// runs diagonally, and costs one pass rather than a test per shape.
  /// </remarks>
  public VectorMask MaskOf(VectorPath path, FillRule rule) {
    ArgumentNullException.ThrowIfNull(path);

    var mask = new VectorMask(this.Width, this.Height);
    var edges = _EdgesOf(path);
    if (edges.Count == 0)
      return mask;

    this._Scan(edges, rule, y => {
      var row = y * this.Width;
      for (var x = 0; x < this.Width; ++x) {
        var coverage = this._coverage[x];
        if (coverage > 0)
          mask.Coverage[row + x] = (byte)Math.Clamp((int)(coverage * 255 + 0.5), 0, 255);
      }
    });

    return mask;
  }

  /// <summary>
  /// The area a stroke covers, as a path that can be filled by the non-zero rule.
  /// </summary>
  /// <remarks>
  /// Separate from <see cref="Stroke"/> so it can be tested on its own and so a caller that wants
  /// the stroke and the fill in one shape can have it.
  /// </remarks>
  public static VectorPath StrokeOutline(VectorPath path, double width, LineJoin join = LineJoin.Miter, LineCap cap = LineCap.Butt, double miterLimit = 4) {
    ArgumentNullException.ThrowIfNull(path);

    // A width of zero means the thinnest line the device can draw, which every one of these
    // formats says somewhere and which is one pixel here.
    var half = Math.Max(width, 1.0) / 2;
    var outline = new VectorPath();

    foreach (var (xsMemory, ysMemory, closed) in path.SubPaths) {
      var xs = xsMemory.Span;
      var ys = ysMemory.Span;
      var count = xs.Length;

      // A subpath that is one point is a dot, which only a round or square cap makes visible.
      if (count == 1) {
        if (cap == LineCap.Round)
          outline.AddEllipse(xs[0], ys[0], half, half);
        else if (cap == LineCap.Square)
          outline.AddRectangle(xs[0] - half, ys[0] - half, half * 2, half * 2);
        continue;
      }

      var segments = closed ? count : count - 1;
      for (var i = 0; i < segments; ++i) {
        var j = (i + 1) % count;
        _AddSegmentQuad(outline, xs[i], ys[i], xs[j], ys[j], half);
      }

      var joints = closed ? count : count - 2;
      for (var i = 0; i < joints; ++i) {
        var prev = i;
        var at = (i + 1) % count;
        var next = (i + 2) % count;
        _AddJoin(outline, xs[prev], ys[prev], xs[at], ys[at], xs[next], ys[next], half, join, miterLimit);
      }

      if (closed || cap == LineCap.Butt)
        continue;

      _AddCap(outline, xs[1], ys[1], xs[0], ys[0], half, cap);
      _AddCap(outline, xs[count - 2], ys[count - 2], xs[count - 1], ys[count - 1], half, cap);
    }

    return outline;
  }

  /// <summary>The surface as a picture, with the alpha divided back out.</summary>
  /// <remarks>
  /// Where nothing was drawn and the surface was cleared to something transparent, the colour is
  /// the one it was cleared to rather than nothing. Dividing by an alpha of zero would leave black
  /// there, and a picture whose transparent parts turn black the moment anything drops its alpha —
  /// which is what converting it to three channels does — is not what was drawn.
  /// </remarks>
  public RawImage ToRawImage() {
    var pixels = new byte[this.Width * this.Height * 4];
    for (var i = 0; i < this._alpha.Length; ++i) {
      var a = this._alpha[i];
      if (a <= 0) {
        pixels[i * 4 + 0] = this._clearedTo.R;
        pixels[i * 4 + 1] = this._clearedTo.G;
        pixels[i * 4 + 2] = this._clearedTo.B;
        continue;
      }

      var scale = 1 / a;
      pixels[i * 4 + 0] = _ToByte(this._red[i] * scale);
      pixels[i * 4 + 1] = _ToByte(this._green[i] * scale);
      pixels[i * 4 + 2] = _ToByte(this._blue[i] * scale);
      pixels[i * 4 + 3] = _ToByte(a);
    }

    return new() { Width = this.Width, Height = this.Height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  private static byte _ToByte(float value) => (byte)Math.Clamp((int)(value * 255 + 0.5f), 0, 255);

  private readonly record struct Edge(double X0, double Y0, double X1, double Y1, int Winding);

  private static List<Edge> _EdgesOf(VectorPath path) {
    var edges = new List<Edge>();
    foreach (var (xsMemory, ysMemory, _) in path.SubPaths) {
      var xs = xsMemory.Span;
      var ys = ysMemory.Span;

      // A fill always treats a subpath as closed, open or not; that is what every one of these
      // formats means by a filled area and what SVG, PostScript and CGM all specify.
      for (var i = 0; i < xs.Length; ++i) {
        var j = (i + 1) % xs.Length;
        var y0 = ys[i];
        var y1 = ys[j];
        if (y0 == y1 || !double.IsFinite(y0) || !double.IsFinite(y1) || !double.IsFinite(xs[i]) || !double.IsFinite(xs[j]))
          continue;

        edges.Add(y0 < y1 ? new(xs[i], y0, xs[j], y1, 1) : new(xs[j], y1, xs[i], y0, -1));
      }
    }

    return edges;
  }

  private void _Scan(List<Edge> edges, FillRule rule, Action<int> onRow) {
    double lowest = double.MaxValue, highest = double.MinValue;
    foreach (var edge in edges) {
      lowest = Math.Min(lowest, edge.Y0);
      highest = Math.Max(highest, edge.Y1);
    }

    var firstRow = Math.Max(0, (int)Math.Floor(lowest));
    var lastRow = Math.Min(this.Height - 1, (int)Math.Ceiling(highest));
    var crossings = new List<(double X, int Winding)>();

    for (var y = firstRow; y <= lastRow; ++y) {
      Array.Clear(this._coverage);
      var touched = false;

      for (var sample = 0; sample < _SubSamples; ++sample) {
        var sampleY = y + (sample + 0.5) / _SubSamples;
        crossings.Clear();

        foreach (var edge in edges) {
          if (sampleY < edge.Y0 || sampleY >= edge.Y1)
            continue;

          var t = (sampleY - edge.Y0) / (edge.Y1 - edge.Y0);
          crossings.Add((edge.X0 + t * (edge.X1 - edge.X0), edge.Winding));
        }

        if (crossings.Count < 2)
          continue;

        crossings.Sort(static (a, b) => a.X.CompareTo(b.X));

        if (rule == FillRule.EvenOdd) {
          for (var i = 0; i + 1 < crossings.Count; i += 2)
            touched |= this._AddSpan(crossings[i].X, crossings[i + 1].X);
        } else {
          var winding = 0;
          var spanStart = 0.0;
          foreach (var (x, direction) in crossings) {
            var before = winding;
            winding += direction;
            if (before == 0 && winding != 0)
              spanStart = x;
            else if (before != 0 && winding == 0)
              touched |= this._AddSpan(spanStart, x);
          }
        }
      }

      if (touched)
        onRow(y);
    }
  }

  private bool _AddSpan(double from, double to) {
    if (to <= from)
      return false;

    from = Math.Max(from, 0);
    to = Math.Min(to, this.Width);
    if (to <= from)
      return false;

    const double weight = 1.0 / _SubSamples;
    var first = (int)from;
    var last = (int)Math.Ceiling(to) - 1;

    if (first == last) {
      this._coverage[first] += (to - from) * weight;
      return true;
    }

    this._coverage[first] += (first + 1 - from) * weight;
    for (var x = first + 1; x < last; ++x)
      this._coverage[x] += weight;

    if (last < this.Width)
      this._coverage[last] += (to - last) * weight;

    return true;
  }

  private void _Blend(int y, VectorPaint paint, VectorStipple? stipple, VectorMask? mask) {
    var row = y * this.Width;
    var uniform = paint.IsUniform ? paint.At(0, 0) : default;

    for (var x = 0; x < this.Width; ++x) {
      var coverage = Math.Min(this._coverage[x], 1);
      if (coverage <= 0 || (stipple.HasValue && !stipple.Value.Covers(x, y)))
        continue;

      if (mask != null) {
        coverage *= mask.Coverage[row + x] / 255.0;
        if (coverage <= 0)
          continue;
      }

      var colour = paint.IsUniform ? uniform : paint.At(x + 0.5, y + 0.5);
      if (colour.A == 0)
        continue;

      var a = (float)coverage * (colour.A / 255f);
      var keep = 1 - a;
      var at = row + x;
      this._red[at] = this._red[at] * keep + colour.R / 255f * a;
      this._green[at] = this._green[at] * keep + colour.G / 255f * a;
      this._blue[at] = this._blue[at] * keep + colour.B / 255f * a;
      this._alpha[at] = this._alpha[at] * keep + a;
    }
  }

  private static void _AddSegmentQuad(VectorPath outline, double x0, double y0, double x1, double y1, double half) {
    var dx = x1 - x0;
    var dy = y1 - y0;
    var length = Math.Sqrt(dx * dx + dy * dy);
    if (length <= 0 || !double.IsFinite(length))
      return;

    var nx = -dy / length * half;
    var ny = dx / length * half;

    outline.MoveTo(x0 + nx, y0 + ny);
    outline.LineTo(x1 + nx, y1 + ny);
    outline.LineTo(x1 - nx, y1 - ny);
    outline.LineTo(x0 - nx, y0 - ny);
    outline.Close();
  }

  private static void _AddJoin(VectorPath outline, double px, double py, double x, double y, double nx2, double ny2, double half, LineJoin join, double miterLimit) {
    if (join == LineJoin.Round) {
      outline.AddEllipse(x, y, half, half);
      return;
    }

    var (ax, ay) = _Normal(px, py, x, y, half);
    var (bx, by) = _Normal(x, y, nx2, ny2, half);
    if (double.IsNaN(ax) || double.IsNaN(bx))
      return;

    // Which side the corner opens on decides which pair of offsets the wedge spans.
    var turn = (x - px) * (ny2 - y) - (y - py) * (nx2 - x);
    if (turn == 0)
      return;

    var sign = turn > 0 ? -1 : 1;
    var p1x = x + ax * sign;
    var p1y = y + ay * sign;
    var p2x = x + bx * sign;
    var p2y = y + by * sign;

    if (join == LineJoin.Miter) {
      var mx = (ax + bx) * sign;
      var my = (ay + by) * sign;
      var lengthSquared = mx * mx + my * my;
      if (lengthSquared > 0) {
        var scale = 2 * half * half / lengthSquared;
        var tipX = x + mx * scale;
        var tipY = y + my * scale;
        if (Math.Sqrt((tipX - x) * (tipX - x) + (tipY - y) * (tipY - y)) <= miterLimit * half) {
          _AddTriangle(outline, x, y, p1x, p1y, tipX, tipY);
          _AddTriangle(outline, x, y, tipX, tipY, p2x, p2y);
          return;
        }
      }
    }

    _AddTriangle(outline, x, y, p1x, p1y, p2x, p2y);
  }

  private static void _AddCap(VectorPath outline, double fromX, double fromY, double x, double y, double half, LineCap cap) {
    if (cap == LineCap.Round) {
      outline.AddEllipse(x, y, half, half);
      return;
    }

    var dx = x - fromX;
    var dy = y - fromY;
    var length = Math.Sqrt(dx * dx + dy * dy);
    if (length <= 0 || !double.IsFinite(length))
      return;

    var ux = dx / length * half;
    var uy = dy / length * half;
    var nx = -uy;
    var ny = ux;

    outline.MoveTo(x + nx, y + ny);
    outline.LineTo(x + nx + ux, y + ny + uy);
    outline.LineTo(x - nx + ux, y - ny + uy);
    outline.LineTo(x - nx, y - ny);
    outline.Close();
  }

  private static void _AddTriangle(VectorPath outline, double x0, double y0, double x1, double y1, double x2, double y2) {
    outline.MoveTo(x0, y0);
    outline.LineTo(x1, y1);
    outline.LineTo(x2, y2);
    outline.Close();
  }

  private static (double X, double Y) _Normal(double x0, double y0, double x1, double y1, double half) {
    var dx = x1 - x0;
    var dy = y1 - y0;
    var length = Math.Sqrt(dx * dx + dy * dy);
    return length <= 0 || !double.IsFinite(length) ? (double.NaN, double.NaN) : (-dy / length * half, dx / length * half);
  }
}
