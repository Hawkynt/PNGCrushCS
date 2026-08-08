using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Svg;

/// <summary>Draws an SVG document onto a raster.</summary>
/// <remarks>
/// One walk of the tree. Each element inherits its parent's paint state and transform, lays its own
/// attributes, rules and style over the state, and either draws itself or passes both to its
/// children. Shapes are built straight into device coordinates, which for an affine transform is
/// exact and saves keeping a path in user space that nothing would look at.
/// <para/>
/// When the file states no size, the same walk runs once with nothing to draw on, only to find the
/// box the drawing falls in; that box is then the picture. It is the same code either way, so the
/// measured size is the size of what actually gets drawn rather than of what might have been.
/// </remarks>
public static class SvgRenderer {

  /// <summary>How deep a <c>use</c> may point at something that points back before it is refused.</summary>
  private const int _MaxDepth = 24;

  private sealed class Context {
    public required XElement Root;
    public required SvgStyleSheet Sheet;
    public required Dictionary<string, XElement> ById;
    public VectorCanvas? Canvas;
    public double MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue;

    public bool Measuring => this.Canvas == null;

    public void Note(VectorPath path, double margin) {
      var bounds = path.Bounds;
      if (bounds == null)
        return;

      var (minX, minY, maxX, maxY) = bounds.Value;
      this.MinX = Math.Min(this.MinX, minX - margin);
      this.MinY = Math.Min(this.MinY, minY - margin);
      this.MaxX = Math.Max(this.MaxX, maxX + margin);
      this.MaxY = Math.Max(this.MaxY, maxY + margin);
    }
  }

  /// <summary>Draws the document at the size it states, or at the size of what it holds.</summary>
  public static RawImage Render(SvgFile file) {
    var root = file.Root;
    var context = new Context {
      Root = root,
      Sheet = SvgStyleSheet.From(root),
      ById = _Index(root)
    };

    var viewport = _Viewport(root, context);
    // Transparent, but remembering white: a drawing states no background, and what shows through it
    // is the page, which every renderer it could be compared against takes to be white.
    context.Canvas = new VectorCanvas(viewport.Width, viewport.Height, Rgba32.White with { A = 0 });
    _Draw(root, context, viewport.Transform, _RootStyle(root, context), null, 0);

    return context.Canvas.ToRawImage();
  }

  /// <summary>
  /// The size the drawing is rendered at, and the transform from its coordinates onto that raster.
  /// </summary>
  private static VectorViewport _Viewport(XElement root, Context context) {
    var box = SvgLength.Numbers(root.Attribute("viewBox")?.Value);
    var hasBox = box.Length >= 4 && box[2] > 0 && box[3] > 0;

    var hasWidth = SvgLength.TryPixels(root.Attribute("width")?.Value, hasBox ? box[2] : 0, out var width);
    var hasHeight = SvgLength.TryPixels(root.Attribute("height")?.Value, hasBox ? box[3] : 0, out var height);

    // Stated outright: the drawing's own width and height, and the view box maps into them.
    if (hasWidth && hasHeight && width > 0 && height > 0) {
      var (pixelWidth, pixelHeight) = VectorViewport.Cap(width, height);
      var transform = hasBox
        ? _FitBox(box, width, height, root.Attribute("preserveAspectRatio")?.Value).Then(Matrix2D.Scaling(pixelWidth / width, pixelHeight / height))
        : Matrix2D.Scaling(pixelWidth / width, pixelHeight / height);

      return new(pixelWidth, pixelHeight, transform);
    }

    // Only a view box: that is the drawing's own extent, one pixel to the unit.
    if (hasBox)
      return VectorViewport.FitCapped(box[0], box[1], box[0] + box[2], box[1] + box[3], box[2], box[3], false);

    // Nothing stated. Walk the tree once for the box the drawing falls in, which is all a renderer
    // has left — and is what the tools this is compared against fall back to as well.
    _Draw(root, context, Matrix2D.Identity, _RootStyle(root, context), null, 0);
    if (context.MinX >= context.MaxX || context.MinY >= context.MaxY)
      throw new InvalidDataException("An SVG drawing states no size and holds nothing that would give it one.");

    return VectorViewport.FitCapped(context.MinX, context.MinY, context.MaxX, context.MaxY, context.MaxX - context.MinX, context.MaxY - context.MinY, false);
  }

  /// <summary>The transform a view box needs to sit inside a viewport of the stated size.</summary>
  private static Matrix2D _FitBox(double[] box, double width, double height, string? preserve) {
    var scaleX = width / box[2];
    var scaleY = height / box[3];
    var align = (preserve ?? "xMidYMid").Trim();

    if (align.StartsWith("none", StringComparison.OrdinalIgnoreCase))
      return Matrix2D.Translation(-box[0], -box[1]).Then(Matrix2D.Scaling(scaleX, scaleY));

    // Everything else keeps the shape; slice takes the larger scale and meet the smaller, and where
    // the spare room goes is the alignment keyword.
    var slice = align.EndsWith("slice", StringComparison.OrdinalIgnoreCase);
    var scale = slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
    var spareX = width - box[2] * scale;
    var spareY = height - box[3] * scale;

    var offsetX = align.Contains("xMax", StringComparison.Ordinal) ? spareX : align.Contains("xMin", StringComparison.Ordinal) ? 0 : spareX / 2;
    var offsetY = align.Contains("YMax", StringComparison.Ordinal) ? spareY : align.Contains("YMin", StringComparison.Ordinal) ? 0 : spareY / 2;

    return Matrix2D.Translation(-box[0], -box[1]).Then(Matrix2D.Scaling(scale, scale)).Then(Matrix2D.Translation(offsetX, offsetY));
  }

  private static Dictionary<string, XElement> _Index(XElement root) {
    var index = new Dictionary<string, XElement>(StringComparer.Ordinal);
    foreach (var element in root.DescendantsAndSelf()) {
      var id = element.Attribute("id")?.Value;
      if (!string.IsNullOrEmpty(id))
        index.TryAdd(id, element);
    }

    return index;
  }

  private static SvgPresentation _RootStyle(XElement root, Context context) => new();

  /// <summary>Draws one element and everything under it.</summary>
  private static void _Draw(XElement element, Context context, Matrix2D transform, SvgPresentation inherited, VectorMask? clip, int depth) {
    if (depth > _MaxDepth)
      return;

    var name = element.Name.LocalName;

    // Definitions are drawn where they are referred to, not where they are written.
    if (name is "defs" or "symbol" or "clipPath" or "linearGradient" or "radialGradient" or "pattern" or "marker" or "mask" or "style" or "title" or "desc" or "metadata" or "filter")
      return;

    var style = _StyleOf(element, context, inherited);
    if (!style.Visible)
      return;

    var local = SvgTransform.Parse(element.Attribute("transform")?.Value).Then(transform);
    var ownClip = _ClipOf(element, context, local, style, clip, depth);

    switch (name) {
      case "svg":
      case "g":
      case "a":
      case "switch":
        foreach (var child in element.Elements())
          _Draw(child, context, local, style, ownClip, depth + 1);
        return;

      case "use":
        _DrawUse(element, context, local, style, ownClip, depth);
        return;

      case "path":
        _Paint(context, SvgPathData.Parse(element.Attribute("d")?.Value, local), element, style, local, ownClip, false);
        return;

      case "rect":
        _Paint(context, _Rectangle(element, local), element, style, local, ownClip, true);
        return;

      case "circle":
      case "ellipse":
        _Paint(context, _Ellipse(element, local, name == "circle"), element, style, local, ownClip, true);
        return;

      case "line":
        _Paint(context, _Line(element, local), element, style, local, ownClip, false);
        return;

      case "polyline":
      case "polygon":
        _Paint(context, _Points(element, local, name == "polygon"), element, style, local, ownClip, name == "polygon");
        return;

      case "image":
        _DrawImage(element, context, local, ownClip);
        return;
    }
  }

  /// <summary>Draws an <c>image</c> whose source is a data URI.</summary>
  /// <remarks>
  /// Only a data URI. An <c>image</c> that names a file or a URL is a reference to something that is
  /// not in the document, and fetching it would mean a picture opening a network connection or
  /// reading a path of somebody else's choosing; a renderer that did that would be a worse thing
  /// than one that leaves the rectangle empty.
  /// </remarks>
  private static void _DrawImage(XElement element, Context context, Matrix2D transform, VectorMask? clip) {
    var href = element.Attribute("href")?.Value ?? element.Attribute("{http://www.w3.org/1999/xlink}href")?.Value;
    var picture = SvgDataUri.Decode(href);
    if (picture == null)
      return;

    SvgLength.TryPixels(element.Attribute("x")?.Value, 0, out var x);
    SvgLength.TryPixels(element.Attribute("y")?.Value, 0, out var y);

    // With no width or height the element is the picture's own size, which is what the
    // specification says for a raster whose intrinsic size is known.
    var hasWidth = SvgLength.TryPixels(element.Attribute("width")?.Value, 0, out var width) && width > 0;
    var hasHeight = SvgLength.TryPixels(element.Attribute("height")?.Value, 0, out var height) && height > 0;
    if (!hasWidth)
      width = picture.Width;
    if (!hasHeight)
      height = picture.Height;

    if (width <= 0 || height <= 0)
      return;

    var box = new double[] { 0, 0, picture.Width, picture.Height };
    var placement = _FitBox(box, width, height, element.Attribute("preserveAspectRatio")?.Value)
      .Then(Matrix2D.Translation(x, y))
      .Then(transform);

    if (context.Measuring) {
      var area = new VectorPath();
      var corners = new[] { (0.0, 0.0), ((double)picture.Width, 0.0), ((double)picture.Width, (double)picture.Height), (0.0, (double)picture.Height) };
      var xs = new double[4];
      var ys = new double[4];
      for (var i = 0; i < 4; ++i)
        (xs[i], ys[i]) = placement.Apply(corners[i].Item1, corners[i].Item2);

      area.AddPolygon(xs, ys);
      context.Note(area, 0);
      return;
    }

    context.Canvas!.DrawImage(picture, placement, clip);
  }

  private static void _DrawUse(XElement element, Context context, Matrix2D transform, SvgPresentation style, VectorMask? clip, int depth) {
    var target = _Referenced(element, context, "href") ?? _Referenced(element, context, "{http://www.w3.org/1999/xlink}href");
    if (target == null)
      return;

    SvgLength.TryPixels(element.Attribute("x")?.Value, 0, out var x);
    SvgLength.TryPixels(element.Attribute("y")?.Value, 0, out var y);
    var placed = Matrix2D.Translation(x, y).Then(transform);

    // A use of a symbol draws the symbol's children; a use of anything else draws that thing.
    if (target.Name.LocalName is "symbol" or "svg") {
      foreach (var child in target.Elements())
        _Draw(child, context, placed, style, clip, depth + 1);

      return;
    }

    _Draw(target, context, placed, style, clip, depth + 1);
  }

  private static XElement? _Referenced(XElement element, Context context, XName attribute) {
    var value = element.Attribute(attribute)?.Value;
    if (string.IsNullOrEmpty(value) || value[0] != '#')
      return null;

    return context.ById.TryGetValue(value[1..], out var target) ? target : null;
  }

  private static SvgPresentation _StyleOf(XElement element, Context context, SvgPresentation inherited) {
    var declarations = new Dictionary<string, string>(StringComparer.Ordinal);

    // Attributes first, then the rules that match, then the element's own style attribute — which
    // is the order the specification gives them, weakest first.
    foreach (var property in SvgPresentation.Attributes) {
      var value = element.Attribute(property)?.Value;
      if (!string.IsNullOrEmpty(value))
        declarations[property] = value;
    }

    context.Sheet.Apply(element, declarations);

    foreach (var (property, value) in SvgPresentation.ParseDeclarations(element.Attribute("style")?.Value))
      declarations[property] = value;

    return inherited.With(declarations);
  }

  private static VectorMask? _ClipOf(XElement element, Context context, Matrix2D transform, SvgPresentation style, VectorMask? inherited, int depth) {
    if (context.Measuring)
      return inherited;

    var reference = element.Attribute("clip-path")?.Value;
    if (string.IsNullOrEmpty(reference))
      return inherited;

    var open = reference.IndexOf('#');
    var close = reference.LastIndexOf(')');
    if (open < 0)
      return inherited;

    var id = close > open ? reference[(open + 1)..close] : reference[(open + 1)..];
    if (!context.ById.TryGetValue(id.Trim(), out var clipPath) || clipPath.Name.LocalName != "clipPath")
      return inherited;

    var shape = new VectorPath();
    foreach (var child in clipPath.Elements()) {
      var childTransform = SvgTransform.Parse(child.Attribute("transform")?.Value).Then(transform);
      var piece = child.Name.LocalName switch {
        "path" => SvgPathData.Parse(child.Attribute("d")?.Value, childTransform),
        "rect" => _Rectangle(child, childTransform),
        "circle" => _Ellipse(child, childTransform, true),
        "ellipse" => _Ellipse(child, childTransform, false),
        "polygon" or "polyline" => _Points(child, childTransform, true),
        _ => null
      };

      if (piece == null)
        continue;

      foreach (var (xs, ys, _) in piece.SubPaths)
        shape.AddPolygon(xs.Span, ys.Span);
    }

    if (shape.IsEmpty)
      return inherited;

    var mask = context.Canvas!.MaskOf(shape, style.FillRule);
    return inherited == null ? mask : inherited.IntersectedWith(mask);
  }

  private static void _Paint(Context context, VectorPath? path, XElement element, SvgPresentation style, Matrix2D transform, VectorMask? clip, bool closed) {
    if (path == null || path.IsEmpty)
      return;

    if (closed)
      path.Close();

    var scale = transform.MeanScale;
    var stroke = _PaintFor(style.Stroke, context, style, path, transform, style.Opacity * style.StrokeOpacity);
    var strokeWidth = stroke == null || style.StrokeWidth <= 0 ? 0 : Math.Max(style.StrokeWidth * scale, 1);

    if (context.Measuring) {
      context.Note(path, strokeWidth / 2);
      return;
    }

    var canvas = context.Canvas!;

    var fill = _PaintFor(style.Fill, context, style, path, transform, style.Opacity * style.FillOpacity);
    if (fill != null)
      canvas.Fill(path, style.FillRule, fill, null, clip);

    if (strokeWidth <= 0 || stroke == null)
      return;

    var line = path;
    if (style.Dashes.Length > 0) {
      var dashes = style.Dashes.Select(d => d * scale).ToArray();
      line = path.Dashed(dashes, style.DashOffset * scale);
    }

    canvas.Stroke(line, strokeWidth, stroke, clip, style.LineJoin, style.LineCap, style.MiterLimit);
  }

  /// <summary>What a <c>fill</c> or <c>stroke</c> value paints with, or null for nothing at all.</summary>
  private static VectorPaint? _PaintFor(string? value, Context context, SvgPresentation style, VectorPath path, Matrix2D userToDevice, double alpha) {
    if (string.IsNullOrEmpty(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
      return null;

    if (alpha <= 0)
      return null;

    if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) {
      var gradient = SvgGradient.Resolve(value, context.ById, path, userToDevice, alpha);
      if (gradient != null)
        return gradient;

      // A paint server this does not read is not a colour, and painting one grey would be an
      // invention; the shape is left unpainted so what is drawn is only what was stated.
      return null;
    }

    if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
      return VectorPaint.Solid(_WithAlpha(style.CurrentColour, alpha));

    return SvgColour.TryParse(value, out var colour) ? VectorPaint.Solid(_WithAlpha(colour, alpha)) : null;
  }

  private static Rgba32 _WithAlpha(Rgba32 colour, double alpha)
    => colour with { A = (byte)Math.Clamp((int)Math.Round(colour.A * alpha), 0, 255) };

  private static VectorPath? _Rectangle(XElement element, Matrix2D transform) {
    SvgLength.TryPixels(element.Attribute("x")?.Value, 0, out var x);
    SvgLength.TryPixels(element.Attribute("y")?.Value, 0, out var y);
    if (!SvgLength.TryPixels(element.Attribute("width")?.Value, 0, out var width) || !SvgLength.TryPixels(element.Attribute("height")?.Value, 0, out var height))
      return null;

    if (width <= 0 || height <= 0)
      return null;

    var hasRx = SvgLength.TryPixels(element.Attribute("rx")?.Value, 0, out var rx);
    var hasRy = SvgLength.TryPixels(element.Attribute("ry")?.Value, 0, out var ry);
    if (hasRx && !hasRy)
      ry = rx;
    else if (hasRy && !hasRx)
      rx = ry;

    rx = Math.Clamp(rx, 0, width / 2);
    ry = Math.Clamp(ry, 0, height / 2);

    var path = new VectorPath();
    if (rx <= 0 || ry <= 0) {
      _Polygon(path, transform, [x, x + width, x + width, x], [y, y, y + height, y + height]);
      return path;
    }

    // Four straight sides and four quarter ellipses, walked in the drawing's own frame and mapped
    // out through the transform so a rounded rectangle under a rotation stays rounded.
    _MoveTo(path, transform, x + rx, y);
    _LineTo(path, transform, x + width - rx, y);
    _Corner(path, transform, x + width - rx, y + ry, rx, ry, -Math.PI / 2, Math.PI / 2);
    _LineTo(path, transform, x + width, y + height - ry);
    _Corner(path, transform, x + width - rx, y + height - ry, rx, ry, 0, Math.PI / 2);
    _LineTo(path, transform, x + rx, y + height);
    _Corner(path, transform, x + rx, y + height - ry, rx, ry, Math.PI / 2, Math.PI / 2);
    _LineTo(path, transform, x, y + ry);
    _Corner(path, transform, x + rx, y + ry, rx, ry, Math.PI, Math.PI / 2);
    path.Close();
    return path;
  }

  private static VectorPath? _Ellipse(XElement element, Matrix2D transform, bool circle) {
    SvgLength.TryPixels(element.Attribute("cx")?.Value, 0, out var cx);
    SvgLength.TryPixels(element.Attribute("cy")?.Value, 0, out var cy);

    double rx, ry;
    if (circle) {
      if (!SvgLength.TryPixels(element.Attribute("r")?.Value, 0, out var r) || r <= 0)
        return null;

      rx = ry = r;
    } else {
      if (!SvgLength.TryPixels(element.Attribute("rx")?.Value, 0, out rx) || !SvgLength.TryPixels(element.Attribute("ry")?.Value, 0, out ry) || rx <= 0 || ry <= 0)
        return null;
    }

    var path = new VectorPath();
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
    return path;
  }

  private static VectorPath? _Line(XElement element, Matrix2D transform) {
    SvgLength.TryPixels(element.Attribute("x1")?.Value, 0, out var x1);
    SvgLength.TryPixels(element.Attribute("y1")?.Value, 0, out var y1);
    SvgLength.TryPixels(element.Attribute("x2")?.Value, 0, out var x2);
    SvgLength.TryPixels(element.Attribute("y2")?.Value, 0, out var y2);

    var path = new VectorPath();
    _MoveTo(path, transform, x1, y1);
    _LineTo(path, transform, x2, y2);
    return path;
  }

  private static VectorPath? _Points(XElement element, Matrix2D transform, bool close) {
    var numbers = SvgLength.Numbers(element.Attribute("points")?.Value);
    if (numbers.Length < 4)
      return null;

    var path = new VectorPath();
    for (var i = 0; i + 1 < numbers.Length; i += 2) {
      if (i == 0)
        _MoveTo(path, transform, numbers[0], numbers[1]);
      else
        _LineTo(path, transform, numbers[i], numbers[i + 1]);
    }

    if (close)
      path.Close();

    return path;
  }

  private static void _Polygon(VectorPath path, Matrix2D transform, double[] xs, double[] ys) {
    for (var i = 0; i < xs.Length; ++i) {
      if (i == 0)
        _MoveTo(path, transform, xs[i], ys[i]);
      else
        _LineTo(path, transform, xs[i], ys[i]);
    }

    path.Close();
  }

  private static void _MoveTo(VectorPath path, Matrix2D transform, double x, double y) {
    var (px, py) = transform.Apply(x, y);
    path.MoveTo(px, py);
  }

  private static void _LineTo(VectorPath path, Matrix2D transform, double x, double y) {
    var (px, py) = transform.Apply(x, y);
    path.LineTo(px, py);
  }

  private static void _Corner(VectorPath path, Matrix2D transform, double cx, double cy, double rx, double ry, double from, double span) {
    const int steps = 12;
    for (var i = 1; i <= steps; ++i) {
      var angle = from + span * i / steps;
      var (sin, cos) = Math.SinCos(angle);
      _LineTo(path, transform, cx + rx * cos, cy + ry * sin);
    }
  }
}
