using System;
using FileFormat.Core;

namespace FileFormat.ChinonEs1000;

/// <summary>Synthesizes the ES-1000's four-colour CCD mosaic from a rendered RGB target.</summary>
/// <remarks>
/// The camera's forward path is not bijective: it interpolates neighbouring filtered samples,
/// applies colour gains and nonlinear saturation, clips three percent of both histogram tails and
/// then applies gamma. Consequently no encoder can recover a unique original sensor exposure from
/// an arbitrary RGB image. This class instead solves the useful inverse problem: find a legal CCD
/// field whose decode is close to the requested picture.
/// <para/>
/// The initial estimate analytically inverts the output gamma, approximately removes the camera's
/// 1.5x saturation boost and colour gains, then projects RGB onto the four complementary filter
/// responses implied by the decoder's equations. A few bounded residual-projection passes through
/// the exact forward decoder correct local error without allowing the global histogram normalizer
/// to make the iteration unstable.
/// </remarks>
internal static class ChinonEs1000Inverse {

  private const int _RefinementPasses = 3;
  private const double _SensorScale = 48.0;
  private const double _Saturation = 1.5;
  private const double _RedGain = 0.64;
  private const double _GreenGain = 0.58;
  private const double _BlueGain = 1.0;
  private const double _IntensityR = 0.476;
  private const double _IntensityG = 0.299;
  private const double _IntensityB = 0.175;
  private const double _IntensitySum = _IntensityR + _IntensityG + _IntensityB;

  public static byte[] FromRgb(RawImage source) {
    ArgumentNullException.ThrowIfNull(source);
    var target = source.SampleTo(ChinonEs1000File.Width, ChinonEs1000File.Height);
    var rgb = target.PixelData;
    var ccd = new byte[ChinonEs1000File.CcdColumns * ChinonEs1000File.CcdLines];

    _Seed(rgb, ccd);
    for (var pass = 0; pass < _RefinementPasses; ++pass) {
      var decoded = ChinonEs1000Demosaic.ToRgb24(ccd);
      _Refine(rgb, decoded, ccd);
      _ReplicateMargins(ccd);
    }

    return ccd;
  }

  private static void _Seed(byte[] rgb, byte[] ccd) {
    for (var line = 0; line < ChinonEs1000File.CcdLines; ++line)
    for (var column = 0; column < ChinonEs1000File.CcdColumns; ++column) {
      var x = Math.Clamp(column - ChinonEs1000File.LeftMargin, 0, ChinonEs1000File.Width - 1);
      var y = Math.Clamp(line - ChinonEs1000File.TopMargin, 0, ChinonEs1000File.Height - 1);
      var at = (y * ChinonEs1000File.Width + x) * 3;
      _InverseDisplay(rgb[at], rgb[at + 1], rgb[at + 2], out var r, out var g, out var b);
      var response = _FilterResponse(line, column, r, g, b);
      ccd[line * ChinonEs1000File.CcdColumns + column] = _ClampByte(response * _SensorScale);
    }
  }

  private static void _InverseDisplay(byte red, byte green, byte blue, out double r, out double g, out double b) {
    // Forward output gamma is 0.5, so square the normalized output to return to its approximately
    // linear pre-gamma domain.
    r = red / 255.0; r *= r;
    g = green / 255.0; g *= g;
    b = blue / 255.0; b *= b;

    // The camera expands chroma while preserving intensity. Pull each component back toward the
    // same weighted intensity before undoing its colour gains. The exact implementation expands the
    // sorted low/middle/high distances differently; this symmetric inverse is the stable starting
    // point and the residual passes below account for the difference.
    var intensity = (r * _IntensityR + g * _IntensityG + b * _IntensityB) / _IntensitySum;
    r = intensity + (r - intensity) / _Saturation;
    g = intensity + (g - intensity) / _Saturation;
    b = intensity + (b - intensity) / _Saturation;

    r = Math.Max(0, r / _RedGain);
    g = Math.Max(0, g / _GreenGain);
    b = Math.Max(0, b / _BlueGain);
  }

  private static double _FilterResponse(int line, int column, double r, double g, double b) {
    // These are the four complementary-filter equations obtained by algebraically inverting the
    // four parity branches in ChinonEs1000Demosaic._InterpolateVertically.
    if ((line & 1) != 0)
      return (column & 1) != 0 ? 2 * r + g + b : 2 * g + b;
    return (column & 1) != 0 ? r + 2 * g : r + g + 2 * b;
  }

  private static void _Refine(byte[] target, byte[] decoded, byte[] ccd) {
    for (var y = 0; y < ChinonEs1000File.Height; ++y)
    for (var x = 0; x < ChinonEs1000File.Width; ++x) {
      var p = (y * ChinonEs1000File.Width + x) * 3;
      var er = target[p] - decoded[p];
      var eg = target[p + 1] - decoded[p + 1];
      var eb = target[p + 2] - decoded[p + 2];
      var line = y + ChinonEs1000File.TopMargin;
      var column = x + ChinonEs1000File.LeftMargin;

      double projected;
      if ((line & 1) != 0)
        projected = (column & 1) != 0 ? (2 * er + eg + eb) / 6.0 : (2 * eg + eb) / 5.0;
      else
        projected = (column & 1) != 0 ? (er + 2 * eg) / 5.0 : (er + eg + 2 * eb) / 6.0;

      // Gamma and global normalization make the exact Jacobian image-dependent. A deliberately
      // small bounded step converges on useful residuals without oscillating when the histogram's
      // selected low/high buckets move between passes.
      var delta = Math.Clamp((int)Math.Round(projected * 0.18), -12, 12);
      var at = line * ChinonEs1000File.CcdColumns + column;
      ccd[at] = (byte)Math.Clamp(ccd[at] + delta, 0, 255);
    }
  }

  private static void _ReplicateMargins(byte[] ccd) {
    for (var line = 0; line < ChinonEs1000File.CcdLines; ++line) {
      var sourceLine = Math.Clamp(line, ChinonEs1000File.TopMargin,
        ChinonEs1000File.TopMargin + ChinonEs1000File.Height - 1);
      for (var column = 0; column < ChinonEs1000File.CcdColumns; ++column) {
        if (line >= ChinonEs1000File.TopMargin
            && line < ChinonEs1000File.TopMargin + ChinonEs1000File.Height
            && column >= ChinonEs1000File.LeftMargin
            && column < ChinonEs1000File.LeftMargin + ChinonEs1000File.Width)
          continue;

        var sourceColumn = Math.Clamp(column, ChinonEs1000File.LeftMargin,
          ChinonEs1000File.LeftMargin + ChinonEs1000File.Width - 1);
        ccd[line * ChinonEs1000File.CcdColumns + column]
          = ccd[sourceLine * ChinonEs1000File.CcdColumns + sourceColumn];
      }
    }
  }

  private static byte _ClampByte(double value)
    => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
