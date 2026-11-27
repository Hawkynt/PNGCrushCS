using System;

namespace FileFormat.Pdf.Rendering;

/// <summary>The blend modes of PDF 32000-1 §11.3.5.</summary>
internal enum PdfBlendMode {
  Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion,
  Hue, Saturation, Color, Luminosity,
}

/// <summary>
/// A rectangle of pixels being painted: premultiplied RGB and alpha as floats, positioned on the
/// page so a transparency group need only be as large as what it covers.
/// </summary>
/// <remarks>
/// Compositing follows PDF 32000-1 §11.3: with source colour <c>Cs</c> and alpha <c>αs</c> over a
/// backdrop <c>Cb</c>, <c>αb</c>, the result is
/// <c>(1 − αs)·cb + (1 − αb)·cs + αs·αb·B(Cb, Cs)</c> in premultiplied terms, where <c>B</c> is the
/// blend function; for Normal that is ordinary source-over. The blend functions themselves are the
/// formulas of §11.3.5 transcribed, separable ones per channel and the four non-separable ones
/// through the luminosity, saturation and clipping helpers the specification defines.
/// </remarks>
internal sealed class PdfSurface {

  /// <summary>Where the surface's first column sits on the page.</summary>
  public int OriginX { get; }

  /// <summary>Where the surface's first row sits on the page.</summary>
  public int OriginY { get; }

  public int Width { get; }

  public int Height { get; }

  /// <summary>Four floats a pixel: premultiplied red, green, blue, and alpha.</summary>
  public float[] Pixels { get; }

  /// <summary>
  /// For a non-isolated group, the alpha of the group's own elements alone, without the backdrop it
  /// started from (§11.4.8), or null.
  /// </summary>
  public float[]? GroupAlpha { get; set; }

  /// <summary>For a knockout group, what each element is composited against instead of what is there.</summary>
  public float[]? KnockoutBackdrop { get; set; }

  public PdfSurface(int originX, int originY, int width, int height) {
    this.OriginX = originX;
    this.OriginY = originY;
    this.Width = Math.Max(1, width);
    this.Height = Math.Max(1, height);
    this.Pixels = new float[this.Width * this.Height * 4];
  }

  /// <summary>The page box this surface covers.</summary>
  public PixelBox Box => new(this.OriginX, this.OriginY, this.OriginX + this.Width, this.OriginY + this.Height);

  /// <summary>Paints the whole surface one opaque colour.</summary>
  public void Clear(float r, float g, float b, float a) {
    var p = this.Pixels;
    for (var i = 0; i < p.Length; i += 4) {
      p[i] = r * a;
      p[i + 1] = g * a;
      p[i + 2] = b * a;
      p[i + 3] = a;
    }
  }

  /// <summary>
  /// Composites one row: for each pixel, a colour and an alpha (already multiplied by coverage,
  /// clip, constant alpha and soft mask) under a blend mode.
  /// </summary>
  /// <param name="y">The page row.</param>
  /// <param name="x0">The first page column.</param>
  /// <param name="count">How many pixels.</param>
  /// <param name="rgb">Three floats a pixel, not premultiplied.</param>
  /// <param name="alpha">One float a pixel.</param>
  /// <param name="shape">
  /// For a knockout group, how much of each pixel the element covers, with its constant alpha and
  /// soft mask left out; ignored otherwise.
  /// </param>
  public void CompositeRow(int y, int x0, int count, ReadOnlySpan<float> rgb, ReadOnlySpan<float> alpha, PdfBlendMode mode, ReadOnlySpan<float> shape = default) {
    var row = y - this.OriginY;
    if ((uint)row >= (uint)this.Height)
      return;

    var start = Math.Max(x0, this.OriginX);
    var end = Math.Min(x0 + count, this.OriginX + this.Width);
    var p = this.Pixels;
    var knockout = this.KnockoutBackdrop;

    for (var x = start; x < end; ++x) {
      var i = x - x0;
      var a = alpha[i];
      if (a <= 0)
        continue;
      if (a > 1)
        a = 1;

      // In a knockout group the element is composited against the group's initial backdrop with
      // its opacity alone, and the result replaces what is there in proportion to its shape
      // (§11.4.6.4), so earlier elements of the group do not show through it.
      var cover = 1f;
      if (knockout != null && !shape.IsEmpty) {
        cover = Math.Clamp(shape[i], 0, 1);
        if (cover <= 0)
          continue;
        a = Math.Min(1, a / cover);
      }

      var at = (row * this.Width + (x - this.OriginX)) * 4;
      var sr = rgb[i * 3];
      var sg = rgb[i * 3 + 1];
      var sb = rgb[i * 3 + 2];

      float br, bg, bb, ba;
      if (knockout != null) {
        br = knockout[at];
        bg = knockout[at + 1];
        bb = knockout[at + 2];
        ba = knockout[at + 3];
      } else {
        br = p[at];
        bg = p[at + 1];
        bb = p[at + 2];
        ba = p[at + 3];
      }

      float rr, rg, rb, ra;
      if (mode == PdfBlendMode.Normal || ba <= 0) {
        var keep = 1 - a;
        rr = sr * a + br * keep;
        rg = sg * a + bg * keep;
        rb = sb * a + bb * keep;
        ra = a + ba * keep;
      } else {
        var inv = 1 / ba;
        var cbr = Math.Clamp(br * inv, 0, 1);
        var cbg = Math.Clamp(bg * inv, 0, 1);
        var cbb = Math.Clamp(bb * inv, 0, 1);
        float mr, mg, mb;
        if (mode >= PdfBlendMode.Hue)
          (mr, mg, mb) = _NonSeparable(mode, cbr, cbg, cbb, sr, sg, sb);
        else {
          mr = _Separable(mode, cbr, sr);
          mg = _Separable(mode, cbg, sg);
          mb = _Separable(mode, cbb, sb);
        }

        var keep = 1 - a;
        var fresh = a * (1 - ba);
        var both = a * ba;
        rr = br * keep + sr * fresh + mr * both;
        rg = bg * keep + sg * fresh + mg * both;
        rb = bb * keep + sb * fresh + mb * both;
        ra = a + ba * keep;
      }

      if (knockout != null && cover < 1) {
        var keep = 1 - cover;
        rr = p[at] * keep + rr * cover;
        rg = p[at + 1] * keep + rg * cover;
        rb = p[at + 2] * keep + rb * cover;
        ra = p[at + 3] * keep + ra * cover;
      }

      p[at] = rr;
      p[at + 1] = rg;
      p[at + 2] = rb;
      p[at + 3] = ra;

      if (this.GroupAlpha is { } groupAlpha) {
        var gi = at >> 2;
        var effective = knockout != null ? a * cover : a;
        groupAlpha[gi] = knockout != null
          ? groupAlpha[gi] * (1 - cover) + a * cover
          : groupAlpha[gi] + effective * (1 - groupAlpha[gi]);
      }
    }
  }

  private static float _Separable(PdfBlendMode mode, float b, float s) => mode switch {
    PdfBlendMode.Multiply => b * s,
    PdfBlendMode.Screen => b + s - b * s,
    PdfBlendMode.Overlay => _HardLight(s, b),
    PdfBlendMode.Darken => Math.Min(b, s),
    PdfBlendMode.Lighten => Math.Max(b, s),
    PdfBlendMode.ColorDodge => b <= 0 ? 0 : s >= 1 ? 1 : Math.Min(1, b / (1 - s)),
    PdfBlendMode.ColorBurn => b >= 1 ? 1 : s <= 0 ? 0 : 1 - Math.Min(1, (1 - b) / s),
    PdfBlendMode.HardLight => _HardLight(b, s),
    PdfBlendMode.SoftLight => _SoftLight(b, s),
    PdfBlendMode.Difference => Math.Abs(b - s),
    PdfBlendMode.Exclusion => b + s - 2 * b * s,
    _ => s,
  };

  private static float _HardLight(float b, float s) => s <= 0.5f ? b * 2 * s : _Screen(b, 2 * s - 1);

  private static float _Screen(float b, float s) => b + s - b * s;

  private static float _SoftLight(float b, float s) {
    if (s <= 0.5f)
      return b - (1 - 2 * s) * b * (1 - b);

    var d = b <= 0.25f ? ((16 * b - 12) * b + 4) * b : MathF.Sqrt(b);
    return b + (2 * s - 1) * (d - b);
  }

  private static float _Lum(float r, float g, float b) => 0.3f * r + 0.59f * g + 0.11f * b;

  private static (float, float, float) _ClipColor(float r, float g, float b) {
    var l = _Lum(r, g, b);
    var n = Math.Min(r, Math.Min(g, b));
    var x = Math.Max(r, Math.Max(g, b));
    if (n < 0) {
      var d = l - n;
      if (d > 0) {
        r = l + (r - l) * l / d;
        g = l + (g - l) * l / d;
        b = l + (b - l) * l / d;
      }
    }

    if (x > 1) {
      var d = x - l;
      if (d > 0) {
        r = l + (r - l) * (1 - l) / d;
        g = l + (g - l) * (1 - l) / d;
        b = l + (b - l) * (1 - l) / d;
      }
    }

    return (r, g, b);
  }

  private static (float, float, float) _SetLum(float r, float g, float b, float l) {
    var d = l - _Lum(r, g, b);
    return _ClipColor(r + d, g + d, b + d);
  }

  private static float _Sat(float r, float g, float b) => Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

  private static (float, float, float) _SetSat(float r, float g, float b, float s) {
    Span<float> c = [r, g, b];
    int max = 0, min = 0;
    for (var i = 1; i < 3; ++i) {
      if (c[i] > c[max])
        max = i;
      if (c[i] < c[min])
        min = i;
    }

    if (max == min)
      return (0, 0, 0);

    var mid = 3 - max - min;
    c[mid] = (c[mid] - c[min]) * s / (c[max] - c[min]);
    c[max] = s;
    c[min] = 0;
    return (c[0], c[1], c[2]);
  }

  private static (float, float, float) _NonSeparable(PdfBlendMode mode, float br, float bg, float bb, float sr, float sg, float sb) {
    switch (mode) {
      case PdfBlendMode.Hue: {
        var (r, g, b) = _SetSat(sr, sg, sb, _Sat(br, bg, bb));
        return _SetLum(r, g, b, _Lum(br, bg, bb));
      }
      case PdfBlendMode.Saturation: {
        var (r, g, b) = _SetSat(br, bg, bb, _Sat(sr, sg, sb));
        return _SetLum(r, g, b, _Lum(br, bg, bb));
      }
      case PdfBlendMode.Color:
        return _SetLum(sr, sg, sb, _Lum(br, bg, bb));
      default:
        return _SetLum(br, bg, bb, _Lum(sr, sg, sb));
    }
  }

  /// <summary>Composites another surface (a finished transparency group) onto this one.</summary>
  /// <param name="group">The group, premultiplied.</param>
  /// <param name="opacity">The constant alpha in force where the group is painted.</param>
  /// <param name="mask">A page-sized soft mask, or null.</param>
  /// <param name="clip">A page-sized clip coverage, or null.</param>
  public void CompositeSurface(PdfSurface group, float opacity, PdfBlendMode mode, PdfSoftMask? mask, PdfClipMask? clip) {
    var box = group.Box.Intersect(this.Box);
    if (box.IsEmpty)
      return;

    var count = box.X1 - box.X0;
    var rgb = new float[count * 3];
    var alpha = new float[count];
    for (var y = box.Y0; y < box.Y1; ++y) {
      var any = false;
      for (var x = box.X0; x < box.X1; ++x) {
        var i = x - box.X0;
        var at = ((y - group.OriginY) * group.Width + (x - group.OriginX)) * 4;
        var a = group.Pixels[at + 3];
        if (a <= 0) {
          alpha[i] = 0;
          continue;
        }

        var inv = 1 / a;
        rgb[i * 3] = Math.Clamp(group.Pixels[at] * inv, 0, 1);
        rgb[i * 3 + 1] = Math.Clamp(group.Pixels[at + 1] * inv, 0, 1);
        rgb[i * 3 + 2] = Math.Clamp(group.Pixels[at + 2] * inv, 0, 1);
        var value = a * opacity;
        if (mask != null)
          value *= mask.At(x, y);
        if (clip != null)
          value *= clip.At(x, y);
        alpha[i] = value;
        any |= value > 0;
      }

      if (any)
        this.CompositeRow(y, box.X0, count, rgb, alpha, mode);
    }
  }
}

/// <summary>A clipping path rasterised: how much of each page pixel drawing may reach.</summary>
internal sealed class PdfClipMask {

  public int Width { get; }

  public int Height { get; }

  /// <summary>One byte a pixel, page-sized.</summary>
  public byte[] Coverage { get; }

  /// <summary>The box outside which nothing gets through.</summary>
  public PixelBox Bounds { get; set; }

  public PdfClipMask(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.Coverage = new byte[width * height];
  }

  public float At(int x, int y) => (uint)x < (uint)this.Width && (uint)y < (uint)this.Height ? this.Coverage[y * this.Width + x] * (1f / 255) : 0;
}

/// <summary>A soft mask in force: a page-sized array of how much each pixel lets through.</summary>
internal sealed class PdfSoftMask(int width, int height, float[] values) {

  public int Width { get; } = width;

  public int Height { get; } = height;

  public float[] Values { get; } = values;

  public float At(int x, int y) => (uint)x < (uint)this.Width && (uint)y < (uint)this.Height ? this.Values[y * this.Width + x] : 0;
}
