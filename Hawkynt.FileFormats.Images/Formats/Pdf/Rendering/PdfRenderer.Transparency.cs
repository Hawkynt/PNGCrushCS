using System;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

internal sealed partial class PdfRenderer {

  /// <summary>
  /// Runs a transparency group (PDF 32000-1 §11.4): the form's content onto a surface of its own,
  /// then that surface onto the parent with the group's constant alpha, soft mask and blend mode.
  /// </summary>
  /// <remarks>
  /// Inside the group the alpha constants, blend mode and soft mask start again from their defaults
  /// (§11.6.6), since they were spent on the group as a whole. An isolated group starts transparent;
  /// a non-isolated one starts from what is beneath it, is composited onto that, and has the
  /// backdrop's contribution taken back out at the end by the formula of §11.4.8 so the backdrop is
  /// not counted twice when the group is laid onto it. A knockout group composites each element
  /// against the group's initial backdrop rather than against the elements before it (§11.4.6.4).
  /// <para/>
  /// A group that needs none of that — opaque, Normal blend, no soft mask, not knockout — is drawn
  /// straight onto its parent, which gives the same pixels for a fraction of the work.
  /// </remarks>
  private void _RunGroup(byte[] content, PdfDict? resources, PdfDict group) {
    var isolated = this._document.Get(group, "I") is true;
    var knockout = this._document.Get(group, "K") is true;
    var opacity = (float)this._state.FillAlpha;
    var mode = this._state.BlendMode;
    var mask = this._state.SoftMask;

    var parentKnockout = this._surface.KnockoutBackdrop;
    if (!knockout && opacity >= 1 && mask == null && mode == PdfBlendMode.Normal && !isolated && parentKnockout == null) {
      this.Run(content, resources);
      return;
    }

    var box = this._surface.Box.Intersect(this._state.Clip?.Bounds ?? new PixelBox(0, 0, this._width, this._height));
    if (box.IsEmpty)
      return;

    var parent = this._surface;
    var groupSurface = new PdfSurface(box.X0, box.Y0, box.X1 - box.X0, box.Y1 - box.Y0);
    float[]? backdrop = null;
    if (!isolated) {
      // Start from the backdrop, and keep it to take back out at the end. Inside a knockout group
      // the backdrop is that group's initial one, not what its earlier elements left (§11.4.7.4).
      backdrop = new float[groupSurface.Pixels.Length];
      var source = parentKnockout ?? parent.Pixels;
      for (var y = box.Y0; y < box.Y1; ++y) {
        var from = ((y - parent.OriginY) * parent.Width + (box.X0 - parent.OriginX)) * 4;
        var to = (y - box.Y0) * groupSurface.Width * 4;
        Array.Copy(source, from, backdrop, to, groupSurface.Width * 4);
      }

      backdrop.CopyTo(groupSurface.Pixels, 0);
      groupSurface.GroupAlpha = new float[groupSurface.Width * groupSurface.Height];
    }

    if (knockout)
      groupSurface.KnockoutBackdrop = (float[])groupSurface.Pixels.Clone();

    var savedFillAlpha = this._state.FillAlpha;
    var savedStrokeAlpha = this._state.StrokeAlpha;
    var savedMode = this._state.BlendMode;
    var savedMask = this._state.SoftMask;
    this._state.FillAlpha = 1;
    this._state.StrokeAlpha = 1;
    this._state.BlendMode = PdfBlendMode.Normal;
    this._state.SoftMask = null;
    this._surface = groupSurface;
    try {
      this.Run(content, resources);
    } finally {
      this._surface = parent;
      this._state.FillAlpha = savedFillAlpha;
      this._state.StrokeAlpha = savedStrokeAlpha;
      this._state.BlendMode = savedMode;
      this._state.SoftMask = savedMask;
    }

    if (backdrop != null)
      _RemoveBackdrop(groupSurface, backdrop);

    // The clip was already applied to everything inside the group; applying it again here would
    // square its antialiased edge.
    parent.CompositeSurface(groupSurface, opacity, mode, mask, null);
  }

  /// <summary>
  /// §11.4.8: the result of a non-isolated group with the backdrop's contribution removed, leaving
  /// colour and alpha that composite onto the backdrop as the group alone would.
  /// </summary>
  private static void _RemoveBackdrop(PdfSurface group, float[] backdrop) {
    var pixels = group.Pixels;
    var groupAlpha = group.GroupAlpha!;
    for (var i = 0; i < groupAlpha.Length; ++i) {
      var at = i * 4;
      var ag = groupAlpha[i];
      if (ag <= 0) {
        pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = 0;
        continue;
      }

      var a0 = backdrop[at + 3];
      var an = pixels[at + 3];
      for (var c = 0; c < 3; ++c) {
        var cn = an > 0 ? pixels[at + c] / an : 0;
        var c0 = a0 > 0 ? backdrop[at + c] / a0 : 0;
        var value = cn + (cn - c0) * (a0 / ag - a0);
        pixels[at + c] = Math.Clamp(value, 0, 1) * ag;
      }

      pixels[at + 3] = ag;
    }

    group.GroupAlpha = null;
  }

  /// <summary>
  /// Builds a soft mask from an ExtGState /SMask dictionary (§11.6.5.2): the group drawn onto its
  /// backdrop, then its alpha or luminosity through the transfer function.
  /// </summary>
  private PdfSoftMask? _BuildSoftMask(PdfDict? dict) {
    if (dict == null)
      return null;

    var kind = this._document.Name(dict, "S") ?? "Luminosity";
    if (this._document.Stream(dict.Raw("G")) is not { } form)
      return null;

    PdfFunction? transfer = null;
    if (this._document.Get(dict, "TR") is { } tr && tr is not "Identity")
      transfer = PdfFunction.Parse(this._document, tr);

    var luminosity = kind == "Luminosity";
    var renderer = new PdfRenderer(this._document, this._width, this._height, this._omissions, this._formDepth + 1, this._activeForms);
    renderer.BeginTransparent(this._state.Ctm);
    renderer._resources = this._resources;

    // The luminosity backdrop is /BC in the group's colour space, black by default.
    float br = 0, bg = 0, bb = 0;
    {
      var group = this._document.Dict(form.Dictionary.Raw("Group"));
      var bc = this._document.Numbers(dict.Raw("BC"));
      if (bc != null && bc.Length > 0) {
        PdfColorModel space;
        try {
          space = group?.Raw("CS") is { } cs ? PdfColorModel.Parse(this._document, cs, this._resources) : bc.Length switch { 1 => PdfColorModel.DeviceGray, 4 => PdfColorModel.DeviceCmyk, _ => PdfColorModel.DeviceRgb };
        } catch (NotSupportedException) {
          space = bc.Length switch { 1 => PdfColorModel.DeviceGray, 4 => PdfColorModel.DeviceCmyk, _ => PdfColorModel.DeviceRgb };
        }

        Span<double> rgb = stackalloc double[3];
        if (bc.Length >= space.Components) {
          space.ToRgb(bc, rgb);
          (br, bg, bb) = ((float)rgb[0], (float)rgb[1], (float)rgb[2]);
        }
      }

      if (luminosity)
        renderer._surface.Clear(br, bg, bb, 1);
    }

    renderer.RunForm(form, null, true);

    // Outside the group's bounding box poppler takes the mask straight from /BC — its luminosity,
    // with no transfer function, for either kind of mask; matched here, as measured on pdf.js's
    // smask_alpha_bc and smask_*_oob tests.
    var inside = new bool[this._width * this._height];
    if (this._document.Numbers(form.Dictionary.Raw("BBox")) is { Length: 4 } bbox) {
      var toDevice = this._document.Numbers(form.Dictionary.Raw("Matrix")) is { Length: 6 } fm
        ? new Matrix2D(fm[0], fm[1], fm[2], fm[3], fm[4], fm[5]).Then(this._state.Ctm)
        : this._state.Ctm;
      var outline = new VectorPath();
      var p0 = toDevice.Apply(bbox[0], bbox[1]);
      outline.MoveTo(p0.X, p0.Y);
      var p1 = toDevice.Apply(bbox[2], bbox[1]);
      outline.LineTo(p1.X, p1.Y);
      var p2 = toDevice.Apply(bbox[2], bbox[3]);
      outline.LineTo(p2.X, p2.Y);
      var p3 = toDevice.Apply(bbox[0], bbox[3]);
      outline.LineTo(p3.X, p3.Y);
      outline.Close();
      this._rasterizer.Fill(outline, FillRule.NonZero, new PixelBox(0, 0, this._width, this._height), (y, x0, x1, coverage) => {
        for (var x = x0; x < x1; ++x)
          inside[y * this._width + x] |= coverage[x - x0] >= 0.5f;
      });
    } else
      Array.Fill(inside, true);

    // Each pixel's mask value: the luminosity of the composite, or its alpha, through /TR.
    var values = new float[this._width * this._height];
    var pixels = renderer._surface.Pixels;
    Span<double> one = stackalloc double[1];
    Span<double> mapped = stackalloc double[Math.Max(1, transfer?.Outputs ?? 1)];
    var table = new float[256];
    for (var i = 0; i < 256; ++i) {
      if (transfer == null) {
        table[i] = i / 255f;
        continue;
      }

      one[0] = i / 255.0;
      transfer.Evaluate(one, mapped);
      table[i] = (float)Math.Clamp(mapped[0], 0, 1);
    }

    for (var i = 0; i < values.Length; ++i) {
      if (!inside[i]) {
        values[i] = Math.Clamp(0.3f * br + 0.59f * bg + 0.11f * bb, 0, 1);
        continue;
      }

      var at = i * 4;
      var a = pixels[at + 3];
      float value;
      if (luminosity) {
        // The surface started opaque in the backdrop colour, so it is opaque throughout.
        var r = a > 0 ? pixels[at] / a : br;
        var g = a > 0 ? pixels[at + 1] / a : bg;
        var b = a > 0 ? pixels[at + 2] / a : bb;
        value = 0.3f * r + 0.59f * g + 0.11f * b;
      } else
        value = a;

      values[i] = table[Math.Clamp((int)(value * 255 + 0.5f), 0, 255)];
    }

    return new PdfSoftMask(this._width, this._height, values);
  }
}
