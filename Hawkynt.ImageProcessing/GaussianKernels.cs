using System;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// The kernel lines the blur family is built from, and the rule that sizes them.
/// </summary>
/// <remarks>
/// Converted from ImageMagick's <c>gem.c</c> and <c>morphology.c</c> (see
/// <c>THIRD-PARTY-NOTICE.ImageMagick.txt</c>). A Gaussian is not in dispute; what is, and what
/// decides whether two blurs of the same radius and sigma agree, is how wide the kernel is cut and
/// how the curve is sampled into it. Both of those are taken as they are rather than re-derived.
/// </remarks>
internal static class GaussianKernels {

  /// <summary>
  /// The smallest weight worth keeping at the kernel's edge: one step of a 16-bit component.
  /// </summary>
  /// <remarks>
  /// This is the quantum of ImageMagick's Q16 build, and it is the number that picks the kernel
  /// width when a caller gives a sigma but no radius. It is a precision threshold, not a property of
  /// the Gaussian, so a caller who wants the same width everywhere should pass a radius.
  /// </remarks>
  private const double _NEGLIGIBLE = 1.0 / 65535.0;

  private const double _EPSILON = 1.0e-12;

  /// <summary>The width of a one-dimensional kernel for a radius, or for a sigma when the radius is zero.</summary>
  public static int Width1D(double radius, double sigma) {
    if (radius > _EPSILON)
      return (int)(2.0 * Math.Ceiling(radius) + 1.0);

    var gamma = Math.Abs(sigma);
    if (gamma <= _EPSILON)
      return 3;

    var alpha = 1.0 / (2.0 * gamma * gamma);
    var beta = 1.0 / (Math.Sqrt(2.0 * Math.PI) * gamma);
    int width;
    for (width = 5; ; width += 2) {
      var j = (width - 1) / 2;
      var normalize = 0.0;
      for (var i = -j; i <= j; ++i)
        normalize += Math.Exp(-(double)(i * i) * alpha) * beta;

      var value = Math.Exp(-(double)(j * j) * alpha) * beta / normalize;
      if (value < _NEGLIGIBLE || value < _EPSILON)
        break;
    }

    return width - 2;
  }

  /// <summary>The width of a square two-dimensional kernel for a radius, or for a sigma when the radius is zero.</summary>
  public static int Width2D(double radius, double sigma) {
    if (radius > _EPSILON)
      return (int)(2.0 * Math.Ceiling(radius) + 1.0);

    var gamma = Math.Abs(sigma);
    if (gamma <= _EPSILON)
      return 3;

    var alpha = 1.0 / (2.0 * gamma * gamma);
    var beta = 1.0 / (2.0 * Math.PI * gamma * gamma);
    int width;
    for (width = 5; ; width += 2) {
      var j = (width - 1) / 2;
      var normalize = 0.0;
      for (var v = -j; v <= j; ++v)
        for (var u = -j; u <= j; ++u)
          normalize += Math.Exp(-(double)(u * u + v * v) * alpha) * beta;

      var value = Math.Exp(-(double)(j * j) * alpha) * beta / normalize;
      if (value < _NEGLIGIBLE || value < _EPSILON)
        break;
    }

    return width - 2;
  }

  /// <summary>
  /// The line <c>-blur</c> uses: a Gaussian drawn at three times the resolution and folded back
  /// into the kernel's cells, then normalised.
  /// </summary>
  /// <remarks>
  /// Supersampling rather than point-sampling because a small sigma point-sampled puts almost all of
  /// the weight in the centre cell and almost none in its neighbours, and the blur then barely blurs.
  /// Averaging three samples per cell is closer to the area under the curve, which is what a cell
  /// should hold.
  /// </remarks>
  public static double[] Blur(double radius, double sigma) {
    const int rank = 3;
    var width = radius >= 1.0 ? (int)radius * 2 + 1 : Width1D(radius, sigma);
    var weights = new double[width];
    sigma = Math.Abs(sigma);

    if (sigma <= _EPSILON) {
      weights[width / 2] = 1.0;
      return weights;
    }

    var reach = (width * rank - 1) / 2;
    var scaled = sigma * rank;
    var alpha = 1.0 / (2.0 * scaled * scaled);
    var beta = 1.0 / (Math.Sqrt(2.0 * Math.PI) * scaled);
    for (var u = -reach; u <= reach; ++u)
      weights[(u + reach) / rank] += Math.Exp(-(double)(u * u) * alpha) * beta;

    return _Normalize(weights);
  }

  /// <summary>A point-sampled Gaussian line of a given width, normalised.</summary>
  /// <remarks>
  /// The two-dimensional Gaussian <c>-gaussian-blur</c> builds is the outer product of this line
  /// with itself, and normalising the product is normalising each factor — so applying this across
  /// and then down is that kernel exactly, not an approximation of it.
  /// </remarks>
  public static double[] Sampled(int width, double sigma) {
    var weights = new double[width];
    sigma = Math.Abs(sigma);
    if (sigma <= _EPSILON) {
      weights[width / 2] = 1.0;
      return weights;
    }

    var half = width / 2;
    var alpha = 1.0 / (2.0 * sigma * sigma);
    for (var u = -half; u <= half; ++u)
      weights[u + half] = Math.Exp(-(double)(u * u) * alpha);

    return _Normalize(weights);
  }

  /// <summary>An unnormalised point-sampled Gaussian line: <c>exp(-u²/2σ²)</c>, one at the centre.</summary>
  public static double[] Raw(int width, double sigma) {
    var weights = new double[width];
    var half = width / 2;
    var alpha = 1.0 / (2.0 * sigma * sigma);
    for (var u = -half; u <= half; ++u)
      weights[u + half] = Math.Exp(-(double)(u * u) * alpha);

    return weights;
  }

  /// <summary>A flat line: every cell the same weight, summing to one.</summary>
  public static double[] Box(int width) {
    var weights = new double[width];
    Array.Fill(weights, 1.0 / width);
    return weights;
  }

  private static double[] _Normalize(double[] weights) {
    var sum = 0.0;
    foreach (var weight in weights)
      sum += weight;

    for (var i = 0; i < weights.Length; ++i)
      weights[i] /= sum;

    return weights;
  }
}
