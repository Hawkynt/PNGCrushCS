using System;
using System.Collections.Generic;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>Renders one page: sizes the raster from the page boxes, sets up the device transform, runs the content and the annotations.</summary>
internal static class PdfPageRenderer {

  public static PdfPageRendering Render(PdfDocument document, PdfPageNode page, PdfRenderOptions options) {
    var crop = page.CropBox;
    var turned = page.Rotate is 90 or 270;
    var widthPoints = (turned ? crop.Height : crop.Width) * page.UserUnit;
    var heightPoints = (turned ? crop.Width : crop.Height) * page.UserUnit;

    // poppler sizes the raster as the ceiling of the scaled box; a small epsilon keeps an exact
    // product from rounding up by one through floating-point noise.
    var scale = options.Resolution / 72.0;
    var width = (int)Math.Ceiling(widthPoints * scale - 1e-6);
    var height = (int)Math.Ceiling(heightPoints * scale - 1e-6);
    if ((long)width * height > options.MaximumPixels) {
      scale *= Math.Sqrt(options.MaximumPixels / ((double)width * height));
      width = (int)Math.Ceiling(widthPoints * scale - 1e-6);
      height = (int)Math.Ceiling(heightPoints * scale - 1e-6);
    }

    width = Math.Max(1, width);
    height = Math.Max(1, height);

    var omissions = new List<string>();
    var renderer = new PdfRenderer(document, width, height, omissions);
    if (options.SimulateOverprint)
      renderer.Surface.Shadow = new PdfOverprintShadow(width, height);
    renderer.BeginPage(BaseTransform(crop, page.Rotate, scale * page.UserUnit));

    var content = _Content(document, page.Dictionary);
    renderer.Run(content, page.Resources);

    if (options.RenderAnnotations)
      _Annotations(document, page, renderer);

    return new PdfPageRendering(renderer.ToRawImage(), omissions);
  }

  /// <summary>The transform from default user space to device pixels for a crop box, rotation and scale.</summary>
  public static Matrix2D BaseTransform(PdfBox crop, int rotate, double s) => rotate switch {
    90 => new Matrix2D(0, s, s, 0, -crop.Bottom * s, -crop.Left * s),
    180 => new Matrix2D(-s, 0, 0, s, crop.Right * s, -crop.Bottom * s),
    270 => new Matrix2D(0, -s, -s, 0, crop.Top * s, crop.Right * s),
    _ => new Matrix2D(s, 0, 0, -s, -crop.Left * s, crop.Top * s),
  };

  private static byte[] _Content(PdfDocument document, PdfDict page) {
    var contents = document.Resolve(page.Raw("Contents"));
    if (contents is PdfStreamObject single)
      return _Decode(document, single);

    if (contents is not List<object?> parts)
      return [];

    // The parts of a content array are one stream split anywhere, even mid-token; join them with
    // white space between, as §7.8.2 says the split points are.
    var joined = new List<byte>();
    foreach (var part in parts)
      if (document.Stream(part) is { } stream) {
        joined.AddRange(_Decode(document, stream));
        joined.Add((byte)'\n');
      }

    return joined.ToArray();
  }

  private static byte[] _Decode(PdfDocument document, PdfStreamObject stream) {
    try {
      return document.Decode(stream);
    } catch (System.IO.InvalidDataException) {
      return [];
    }
  }

  private static void _Annotations(PdfDocument document, PdfPageNode page, PdfRenderer renderer) {
    if (document.Array(page.Dictionary.Raw("Annots")) is not { } annotations)
      return;

    foreach (var item in annotations) {
      if (document.Dict(item) is not { } annotation)
        continue;

      // Hidden (bit 2) and NoView (bit 6) annotations are not shown; popups only open on demand.
      var flags = document.Integer(annotation, "F");
      if ((flags & 0b10) != 0 || (flags & 0b10_0000) != 0)
        continue;
      if (document.Name(annotation, "Subtype") == "Popup")
        continue;

      var appearance = document.Dict(document.Get(annotation, "AP"));
      var normal = document.Resolve(appearance?.Raw("N"));
      if (normal is PdfDict states && normal is not PdfStreamObject) {
        var state = document.Name(annotation, "AS");
        normal = state == null ? null : document.Resolve(states.Raw(state));
      }

      if (normal is not PdfStreamObject form || document.Numbers(annotation.Raw("Rect")) is not { Length: 4 } rect)
        continue;

      // §12.5.5: the appearance's bounding box, through its matrix, is fitted to the rectangle.
      var bbox = document.Numbers(form.Dictionary.Raw("BBox")) ?? [0, 0, 1, 1];
      var matrix = document.Numbers(form.Dictionary.Raw("Matrix")) is { Length: 6 } m ? new Matrix2D(m[0], m[1], m[2], m[3], m[4], m[5]) : Matrix2D.Identity;
      double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
      foreach (var (cx, cy) in new[] { (bbox[0], bbox[1]), (bbox[2], bbox[1]), (bbox[2], bbox[3]), (bbox[0], bbox[3]) }) {
        var (tx, ty) = matrix.Apply(cx, cy);
        minX = Math.Min(minX, tx);
        minY = Math.Min(minY, ty);
        maxX = Math.Max(maxX, tx);
        maxY = Math.Max(maxY, ty);
      }

      double rx0 = Math.Min(rect[0], rect[2]), ry0 = Math.Min(rect[1], rect[3]), rx1 = Math.Max(rect[0], rect[2]), ry1 = Math.Max(rect[1], rect[3]);
      var sx = maxX - minX > 1e-9 ? (rx1 - rx0) / (maxX - minX) : 1;
      var sy = maxY - minY > 1e-9 ? (ry1 - ry0) / (maxY - minY) : 1;
      var fit = new Matrix2D(sx, 0, 0, sy, rx0 - minX * sx, ry0 - minY * sy);
      renderer.RunAnnotation(form, fit);
    }
  }
}
