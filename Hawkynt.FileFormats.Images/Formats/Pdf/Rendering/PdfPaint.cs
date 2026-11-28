using System;
using FileFormat.Core.Vector;

namespace FileFormat.Pdf.Rendering;

/// <summary>What a fill puts at each pixel: a colour, and how much of it.</summary>
internal abstract class PdfPaint {

  /// <summary>
  /// Fills the colour (three floats a pixel, 0..1) and multiplies the alpha (one float a pixel,
  /// already holding coverage) for a run of pixels on a row.
  /// </summary>
  public abstract void Fill(int y, int x0, int count, Span<float> rgb, Span<float> alpha);
}

/// <summary>One colour everywhere.</summary>
internal sealed class PdfSolidPaint(float r, float g, float b) : PdfPaint {

  public float R { get; } = r;

  public float G { get; } = g;

  public float B { get; } = b;

  /// <summary>What it puts on the separations when overprint is simulated, or null to go by its RGB.</summary>
  public PdfInk? Ink { get; set; }

  public override void Fill(int y, int x0, int count, Span<float> rgb, Span<float> alpha) {
    for (var i = 0; i < count; ++i) {
      rgb[i * 3] = this.R;
      rgb[i * 3 + 1] = this.G;
      rgb[i * 3 + 2] = this.B;
    }
  }
}

/// <summary>An image placed by the CTM onto the unit square, sampled per device pixel.</summary>
/// <remarks>
/// The sampling follows what poppler's rasteriser does, since that is the comparison: a picture
/// shrunk onto the page is box-filtered (here by averaging a grid of samples over each device
/// pixel's footprint), one enlarged by less than four times is interpolated bilinearly, and one
/// enlarged four times or more is replicated, which keeps a pixel-art image or a tiny mask crisp.
/// The /Interpolate flag is ignored, as it is there. A stencil mask is never interpolated, only
/// box-filtered when shrunk, and takes its colour from the paint in force.
/// </remarks>
internal sealed class PdfImagePaint : PdfPaint {

  private readonly PdfRasterImage _image;
  private readonly Matrix2D _toUnit;
  private readonly PdfPaint? _stencilPaint;
  private readonly int _samples;
  private readonly bool _bilinear;
  private float[] _scratchRgb = new float[256 * 3];
  private float[] _scratchAlpha = new float[256];

  public PdfImagePaint(PdfRasterImage image, Matrix2D imageToDevice, PdfPaint? stencilPaint) {
    this._image = image;
    this._toUnit = imageToDevice.Inverse ?? new Matrix2D(0, 0, 0, 0, -1, -1);
    this._stencilPaint = stencilPaint;

    // How many image pixels one device pixel spans along each image axis.
    var m = this._toUnit;
    var spanX = Math.Sqrt(m.A * m.A + m.C * m.C) * image.Width;
    var spanY = Math.Sqrt(m.B * m.B + m.D * m.D) * image.Height;
    var span = Math.Max(spanX, spanY);
    this._samples = span > 1.0001 ? Math.Clamp((int)Math.Ceiling(span), 1, 8) : 1;
    var enlarge = Math.Min(spanX > 0 ? 1 / spanX : 1e9, spanY > 0 ? 1 / spanY : 1e9);
    this._bilinear = this._samples == 1 && !image.IsStencil && (int)enlarge < 4 && enlarge > 1.0001;
  }

  public override void Fill(int y, int x0, int count, Span<float> rgb, Span<float> alpha) {
    var image = this._image;
    if (image.IsStencil && this._stencilPaint != null) {
      if (this._scratchAlpha.Length < count) {
        this._scratchAlpha = new float[count];
        this._scratchRgb = new float[count * 3];
      }

      // The colour is the paint's; the stencil only says where.
      this._stencilPaint.Fill(y, x0, count, rgb, alpha);
    }

    var k = this._samples;
    var inv = 1.0 / k;
    var m = this._toUnit;
    for (var i = 0; i < count; ++i) {
      if (alpha[i] <= 0)
        continue;

      double r = 0, g = 0, b = 0, a = 0;
      var px = x0 + i;
      for (var sy = 0; sy < k; ++sy)
        for (var sx = 0; sx < k; ++sx) {
          var dx = px + (sx + 0.5) * inv;
          var dy = y + (sy + 0.5) * inv;
          var u = m.A * dx + m.C * dy + m.E;
          var v = m.B * dx + m.D * dy + m.F;

          // The image's first row is at the top of the unit square.
          var ix = u * image.Width;
          var iy = (1 - v) * image.Height;
          var sampleAlpha = this._Alpha(u, v);
          if (sampleAlpha <= 0)
            continue;

          if (image.Rgb != null) {
            if (this._bilinear)
              this._Bilinear(ix, iy, out var cr, out var cg, out var cb, sampleAlpha, ref r, ref g, ref b);
            else {
              var sxp = Math.Clamp((int)Math.Floor(ix), 0, image.Width - 1);
              var syp = Math.Clamp((int)Math.Floor(iy), 0, image.Height - 1);
              var at = (syp * image.Width + sxp) * 3;
              r += image.Rgb[at] * sampleAlpha;
              g += image.Rgb[at + 1] * sampleAlpha;
              b += image.Rgb[at + 2] * sampleAlpha;
            }
          }

          a += sampleAlpha;
        }

      var samples = k * k;
      if (image.Rgb != null) {
        if (a > 0) {
          var scale = (float)(1.0 / (255 * a));
          rgb[i * 3] = (float)r * scale;
          rgb[i * 3 + 1] = (float)g * scale;
          rgb[i * 3 + 2] = (float)b * scale;
        }
      } else if (this._stencilPaint == null)
        rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = 0;

      alpha[i] *= (float)(a / samples);
    }
  }

  private void _Bilinear(double ix, double iy, out double cr, out double cg, out double cb, double weight, ref double r, ref double g, ref double b) {
    var image = this._image;
    var fx = ix - 0.5;
    var fy = iy - 0.5;
    var x0 = (int)Math.Floor(fx);
    var y0 = (int)Math.Floor(fy);
    var tx = fx - x0;
    var ty = fy - y0;
    var xa = Math.Clamp(x0, 0, image.Width - 1);
    var xb = Math.Clamp(x0 + 1, 0, image.Width - 1);
    var ya = Math.Clamp(y0, 0, image.Height - 1);
    var yb = Math.Clamp(y0 + 1, 0, image.Height - 1);
    var rgb = image.Rgb!;
    var w = image.Width;
    cr = cg = cb = 0;
    for (var c = 0; c < 3; ++c) {
      var top = rgb[(ya * w + xa) * 3 + c] * (1 - tx) + rgb[(ya * w + xb) * 3 + c] * tx;
      var bottom = rgb[(yb * w + xa) * 3 + c] * (1 - tx) + rgb[(yb * w + xb) * 3 + c] * tx;
      var v = top * (1 - ty) + bottom * ty;
      switch (c) {
        case 0: r += v * weight; break;
        case 1: g += v * weight; break;
        default: b += v * weight; break;
      }
    }
  }

  private double _Alpha(double u, double v) {
    var image = this._image;
    if (image.Alpha == null)
      return 1;

    var ax = Math.Clamp((int)Math.Floor(u * image.AlphaWidth), 0, image.AlphaWidth - 1);
    var ay = Math.Clamp((int)Math.Floor((1 - v) * image.AlphaHeight), 0, image.AlphaHeight - 1);
    return image.Alpha[ay * image.AlphaWidth + ax] / 255.0;
  }
}
