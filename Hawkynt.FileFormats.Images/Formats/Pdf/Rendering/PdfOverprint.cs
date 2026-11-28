using System;
using System.Collections.Generic;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// What a solid paint puts on the separations: the process components it paints and the spot
/// colorants with their tints. Components it does not paint are left alone when it overprints and
/// knocked out (painted with zero) when it does not.
/// </summary>
/// <remarks>A null <see cref="Process"/> means the process components come from the paint's RGB.</remarks>
internal sealed record PdfInk(float[]? Process, bool[] PaintsProcess, int[] Spots, float[] Tints, bool Overprint) {

  /// <summary>A paint taken to CMYK from its RGB that overprints: it leaves the spot separations alone.</summary>
  public static PdfInk FromRgbOverprinting { get; } = new(null, [true, true, true, true], [], [], true);
}

/// <summary>
/// A page drawn on separations as well as in RGB, so overprinting can be simulated (§8.6.7, §11.7.4):
/// four process planes and one plane per spot colorant met, each pixel's colour then being those
/// planes converted together.
/// </summary>
/// <remarks>
/// This is the model poppler's pdftoppm uses with <c>-overprint</c>, where it renders the page in
/// DeviceN8 and converts at the end: a solid DeviceCMYK, Separation or DeviceN paint writes the
/// components it paints (under OPM 1 a DeviceCMYK zero is not painted) and, when it overprints,
/// leaves the others; every other paint — RGB, grey, images, shadings, groups — is taken to CMYK as
/// poppler does (c = 1 − r and so on, black pulled out as the smallest) and paints every process
/// component and clears every spot. A pixel's colour is then the process components plus each spot's
/// own CMYK (its tint through its tint transform, as poppler converts DeviceN), clamped, through
/// DeviceCMYK. Measured against <c>pdftoppm -overprint</c>, that lands within a unit.
/// </remarks>
internal sealed class PdfOverprintShadow {
  private readonly int _width;
  private readonly float[] _process;
  private readonly List<float[]> _spots = [];
  private readonly List<float[]> _spotCmyk = [];
  private readonly Dictionary<string, int> _spotIndex = new(StringComparer.Ordinal);

  public PdfOverprintShadow(int width, int height) {
    this._width = width;
    this._process = new float[width * height * 4];
    this.Height = height;
  }

  public int Height { get; }

  /// <summary>The ink of the paint being composited, or null for one taken from its RGB.</summary>
  public PdfInk? Ink { get; set; }

  /// <summary>The plane of a spot colorant, created on first use with its CMYK at 256 tints.</summary>
  public int Spot(string name, Func<double, double[]> cmykOfTint) {
    if (this._spotIndex.TryGetValue(name, out var index))
      return index;

    var table = new float[256 * 4];
    for (var t = 0; t < 256; ++t) {
      var cmyk = cmykOfTint(t / 255.0);
      for (var c = 0; c < 4; ++c)
        table[t * 4 + c] = (float)Math.Clamp(c < cmyk.Length ? cmyk[c] : 0, 0, 1);
    }

    index = this._spots.Count;
    this._spots.Add(new float[this._process.Length / 4]);
    this._spotCmyk.Add(table);
    this._spotIndex[name] = index;
    return index;
  }

  /// <summary>Puts a pixel's paint on the separations with the given opacity and returns its colour.</summary>
  public (float R, float G, float B) Composite(int x, int y, float alpha, float r, float g, float b, bool normal) {
    var pixel = y * this._width + x;
    var at = pixel * 4;
    var ink = normal ? this.Ink : null;
    var keep = 1 - alpha;
    if (ink?.Process == null) {
      var (c, m, yy, k) = RgbToCmyk(r, g, b);
      this._process[at] = this._process[at] * keep + c * alpha;
      this._process[at + 1] = this._process[at + 1] * keep + m * alpha;
      this._process[at + 2] = this._process[at + 2] * keep + yy * alpha;
      this._process[at + 3] = this._process[at + 3] * keep + k * alpha;
      if (ink == null || !ink.Overprint)
        foreach (var spot in this._spots)
          spot[pixel] *= keep;
    } else {
      for (var c = 0; c < 4; ++c)
        if (ink.PaintsProcess[c] || !ink.Overprint)
          this._process[at + c] = this._process[at + c] * keep + (ink.PaintsProcess[c] ? ink.Process![c] : 0) * alpha;

      for (var s = 0; s < this._spots.Count; ++s) {
        var painted = Array.IndexOf(ink.Spots, s);
        if (painted >= 0)
          this._spots[s][pixel] = this._spots[s][pixel] * keep + ink.Tints[painted] * alpha;
        else if (!ink.Overprint)
          this._spots[s][pixel] *= keep;
      }
    }

    return this.ColorAt(pixel);
  }

  private (float, float, float) ColorAt(int pixel) {
    var at = pixel * 4;
    double c = this._process[at], m = this._process[at + 1], y = this._process[at + 2], k = this._process[at + 3];
    for (var s = 0; s < this._spots.Count; ++s) {
      var tint = this._spots[s][pixel];
      if (tint <= 0)
        continue;
      var t = (int)Math.Round(Math.Clamp(tint, 0, 1) * 255) * 4;
      var table = this._spotCmyk[s];
      c += table[t];
      m += table[t + 1];
      y += table[t + 2];
      k += table[t + 3];
    }

    Span<double> rgb = stackalloc double[3];
    PdfColorModel.CmykToRgb(Math.Min(c, 1), Math.Min(m, 1), Math.Min(y, 1), Math.Min(k, 1), rgb);
    return ((float)rgb[0], (float)rgb[1], (float)rgb[2]);
  }

  /// <summary>RGB to CMYK the way poppler's DeviceRGB does it: complements, with black pulled out.</summary>
  public static (float C, float M, float Y, float K) RgbToCmyk(float r, float g, float b) {
    float c = 1 - r, m = 1 - g, y = 1 - b;
    var k = Math.Min(c, Math.Min(m, y));
    return (c - k, m - k, y - k, k);
  }
}
