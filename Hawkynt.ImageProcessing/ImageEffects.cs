using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// Blurs, sharpens, edge detection, emboss and median: the effects that look at a pixel's
/// neighbours.
/// </summary>
/// <remarks>
/// <para>Every result that mixes neighbouring pixels needs colours a palette does not have, so a
/// palette picture comes back as RGB (or RGBA when it had transparency). A grey picture stays grey
/// and a 16-bit one stays 16-bit.</para>
/// <para>Radius and sigma mean what they mean to ImageMagick, and so do the kernels they build: the
/// width a sigma alone selects, the supersampled line <c>-blur</c> uses, the impulse-minus-Gaussian
/// that is <c>-sharpen</c>. These were converted from its <c>effect.c</c> rather than re-derived,
/// because the numbers a user types into a blur box are only portable if they mean the same thing
/// on both sides.</para>
/// <para>Like the colour corrections, these work on the stored, gamma-encoded components — again
/// ImageMagick's default. A blur in linear light darkens less at high-contrast edges; it is also not
/// what anybody comparing against the usual tools will get, so it is not what this does.</para>
/// </remarks>
public static class ImageEffects {

  /// <summary>Averages each pixel with the square of neighbours around it, all weighted equally.</summary>
  /// <param name="image">The picture to blur.</param>
  /// <param name="radius">How many pixels the square reaches on each side; the square is <c>2r+1</c> across.</param>
  public static RawImage BoxBlur(RawImage image, int radius) {
    ArgumentNullException.ThrowIfNull(image);
    if (radius < 0)
      throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius cannot be negative.");
    if (radius == 0)
      return image;

    var line = GaussianKernels.Box(2 * radius + 1);
    return _Filter(image, surface => Convolution.Separable(surface, line, line));
  }

  /// <summary>Blurs with a Gaussian, the way <c>-blur radius x sigma</c> does.</summary>
  /// <param name="image">The picture to blur.</param>
  /// <param name="radius">Half the kernel width in pixels, or 0 to size the kernel from sigma.</param>
  /// <param name="sigma">The Gaussian's standard deviation in pixels.</param>
  public static RawImage Blur(RawImage image, double radius, double sigma) {
    ArgumentNullException.ThrowIfNull(image);
    var line = GaussianKernels.Blur(radius, sigma);
    return _Filter(image, surface => Convolution.Separable(surface, line, line));
  }

  /// <summary>Blurs with a point-sampled Gaussian, the way <c>-gaussian-blur radius x sigma</c> does.</summary>
  /// <param name="image">The picture to blur.</param>
  /// <param name="radius">Half the kernel width in pixels, or 0 to size the kernel from sigma.</param>
  /// <param name="sigma">The Gaussian's standard deviation in pixels.</param>
  /// <remarks>
  /// This and <see cref="Blur"/> differ only in how the curve is put into the kernel's cells —
  /// point samples here, a supersampled average there — and agree closely for any sigma above about
  /// one pixel. Below that, point sampling puts nearly all the weight on the centre and this blurs
  /// noticeably less.
  /// </remarks>
  public static RawImage GaussianBlur(RawImage image, double radius, double sigma) {
    ArgumentNullException.ThrowIfNull(image);
    var width = radius >= 1.0 ? (int)radius * 2 + 1 : GaussianKernels.Width2D(radius, sigma);
    var line = GaussianKernels.Sampled(width, sigma);
    return _Filter(image, surface => Convolution.Separable(surface, line, line));
  }

  /// <summary>Sharpens, the way <c>-sharpen radius x sigma</c> does.</summary>
  /// <param name="image">The picture to sharpen.</param>
  /// <param name="radius">Half the kernel width in pixels, or 0 to size the kernel from sigma.</param>
  /// <param name="sigma">The Gaussian's standard deviation in pixels.</param>
  /// <remarks>
  /// The kernel is a negated Gaussian <c>G</c> whose centre cell is replaced by twice the magnitude
  /// of the whole Gaussian, normalised to sum to one. That is an impulse minus a separable kernel,
  /// so it is computed as one: with <c>S</c> the sum of <c>G</c> and <c>G₀ = 1</c> its centre,
  /// <c>((2S + G₀)·x − S·blur(x)) / (S + G₀)</c>. The result is the grid ImageMagick builds, to
  /// rounding, at the cost of a separable blur instead of a full two-dimensional pass.
  /// <para/>
  /// Alpha is not sharpened.
  /// </remarks>
  public static RawImage Sharpen(RawImage image, double radius, double sigma) {
    ArgumentNullException.ThrowIfNull(image);

    var effectiveSigma = Math.Abs(sigma) < 1.0e-12 ? 1.0e-12 : sigma;
    var width = GaussianKernels.Width2D(radius, sigma);
    var raw = GaussianKernels.Raw(width, effectiveSigma);
    var lineSum = 0.0;
    foreach (var weight in raw)
      lineSum += weight;

    var total = lineSum * lineSum;
    var centre = 2.0 * total + 1.0;
    var normaliser = 1.0 / (total + 1.0);
    var line = GaussianKernels.Sampled(width, effectiveSigma);

    return _Filter(image, surface => {
      var blurred = Convolution.Separable(_Opaque(surface), line, line);
      return _Combine(surface, blurred, (original, blur) => (centre * original - total * blur) * normaliser);
    });
  }

  /// <summary>
  /// Sharpens by adding back the difference between the picture and a blur of it, the way
  /// <c>-unsharp radius x sigma + gain + threshold</c> does.
  /// </summary>
  /// <param name="image">The picture to sharpen.</param>
  /// <param name="radius">Half the blur kernel width, or 0 to size it from sigma.</param>
  /// <param name="sigma">The blur's standard deviation in pixels.</param>
  /// <param name="gain">How much of the difference to add back; 1 doubles the local contrast.</param>
  /// <param name="threshold">
  /// The difference, as a fraction of full scale, below which a pixel is left alone. It is compared
  /// against twice the difference, as ImageMagick does, so a threshold of 0.05 spares differences
  /// under 2.5 percent. Use it to keep noise in smooth areas from being sharpened along with edges.
  /// </param>
  public static RawImage UnsharpMask(RawImage image, double radius, double sigma, double gain = 1.0, double threshold = 0.05) {
    ArgumentNullException.ThrowIfNull(image);

    var line = GaussianKernels.Blur(radius, sigma);
    return _Filter(image, surface => {
      var blurred = Convolution.Separable(_Opaque(surface), line, line);
      return _Combine(surface, blurred, (original, blur) => {
        var difference = original - blur;
        return Math.Abs(2.0 * difference) < threshold ? original : original + gain * difference;
      });
    });
  }

  /// <summary>Marks edges, the way <c>-edge radius</c> does.</summary>
  /// <param name="image">The picture to examine.</param>
  /// <param name="radius">How far the neighbourhood reaches; the square is <c>2⌈r⌉+1</c> across.</param>
  /// <remarks>
  /// Each pixel is compared with the mean of the square around it and the difference multiplied by
  /// the square's area — the kernel of all minus-ones with the centre set so that the whole sums to
  /// zero. Flat regions go black; anything that differs from its surroundings lights up, in its own
  /// colour. Alpha is left as it was.
  /// </remarks>
  public static RawImage DetectEdges(RawImage image, double radius = 1.0) {
    ArgumentNullException.ThrowIfNull(image);

    var width = GaussianKernels.Width1D(radius, 0.5);
    var area = (double)width * width;
    var line = GaussianKernels.Box(width);
    return _Filter(image, surface => {
      var mean = Convolution.Separable(_Opaque(surface), line, line);
      return _Combine(surface, mean, (original, average) => area * (original - average));
    });
  }

  /// <summary>Lights the picture from one side so it looks raised, the way <c>-emboss radius x sigma</c> does.</summary>
  /// <param name="image">The picture to emboss.</param>
  /// <param name="radius">Half the kernel width, or 0 to size it from sigma.</param>
  /// <param name="sigma">The Gaussian's standard deviation in pixels.</param>
  /// <remarks>
  /// The kernel is a single diagonal of Gaussian weights — the only kernel here that is neither
  /// separable nor an impulse minus a separable one, so it is the one that takes the general path —
  /// and its raw result is mostly near black, which is why the emboss ends by equalising the
  /// histogram (one map from luma, applied to every component). That step is not decoration:
  /// without it the result is a picture nobody would call embossed, and it is what ImageMagick's
  /// <c>-emboss</c> does, so it is here too.
  /// Because it equalises, the result depends on the whole picture, not only on each neighbourhood.
  /// </remarks>
  public static RawImage Emboss(RawImage image, double radius, double sigma) {
    ArgumentNullException.ThrowIfNull(image);

    var width = GaussianKernels.Width1D(radius, sigma);
    var effectiveSigma = Math.Abs(sigma) < 1.0e-12 ? 1.0e-12 : sigma;
    var half = (width - 1) / 2;
    var weights = new double[width * width];
    var sum = 0.0;

    for (var v = -half; v <= half; ++v) {
      // Only the anti-diagonal u = -v carries weight; everything else in the grid is zero.
      var u = -v;
      var sign = u < 0 || v < 0 ? -8.0 : 8.0;
      var weight = sign * Math.Exp(-(double)(u * u + v * v) / (2.0 * effectiveSigma * effectiveSigma))
                   / (2.0 * Math.PI * effectiveSigma * effectiveSigma);
      weights[(v + half) * width + u + half] = weight;
      sum += weight;
    }

    var scale = Math.Abs(sum) < 1.0e-12 ? 1.0e12 : 1.0 / sum;
    for (var i = 0; i < weights.Length; ++i)
      weights[i] *= scale;

    var kernel = new ConvolutionKernel(width, width, weights);
    return _Filter(image, surface => {
      var embossed = Convolution.Apply(surface, kernel, 0.0);
      Equalization.Apply(embossed, true);
      return embossed;
    });
  }

  /// <summary>Replaces each component with the median of its neighbourhood, the way <c>-median</c> does.</summary>
  /// <param name="image">The picture to filter.</param>
  /// <param name="width">Neighbourhood width in pixels.</param>
  /// <param name="height">Neighbourhood height in pixels; defaults to the width.</param>
  /// <remarks>
  /// Each component is ranked on its own, so the result can hold a colour none of the neighbours
  /// had — a vector median would avoid that, and would also be neither what the usual tools do nor
  /// cheap. For an even size the neighbourhood extends one further up and to the left than down
  /// and to the right, and the median is the upper of the two middle values.
  /// </remarks>
  public static RawImage Median(RawImage image, int width, int height = 0) {
    ArgumentNullException.ThrowIfNull(image);
    if (height == 0)
      height = width;
    if (width < 1)
      throw new ArgumentOutOfRangeException(nameof(width), width, "A neighbourhood is at least one pixel across.");
    if (height < 1)
      throw new ArgumentOutOfRangeException(nameof(height), height, "A neighbourhood is at least one pixel high.");

    return _Filter(image, surface => _Median(surface, width, height));
  }

  private static PixelSurface _Median(PixelSurface source, int width, int height) {
    var result = source.Clone();
    var output = result.Samples;
    var colors = source.ColorChannels;
    var window = new float[width * height];
    int left = width / 2, top = height / 2;

    for (var y = 0; y < source.Height; ++y)
      for (var x = 0; x < source.Width; ++x) {
        var to = source.Offset(x, y);
        for (var channel = 0; channel < colors; ++channel) {
          var n = 0;
          for (var v = 0; v < height; ++v)
            for (var u = 0; u < width; ++u)
              window[n++] = source.At(x + u - left, y + v - top, channel);

          Array.Sort(window);
          output[to + channel] = window[window.Length / 2];
        }
      }

    return result;
  }

  /// <summary>Combines a picture with a filtered copy of it, component by component, leaving alpha as it was.</summary>
  private static PixelSurface _Combine(PixelSurface original, PixelSurface filtered, Func<double, double, double> combine) {
    var result = original.Clone();
    var output = result.Samples;
    var source = original.Samples;
    var other = filtered.Samples;
    var stride = original.Channels;
    var colors = original.ColorChannels;

    for (var at = 0; at < output.Length; at += stride)
      for (var channel = 0; channel < colors; ++channel)
        output[at + channel] = (float)combine(source[at + channel], other[at + channel]);

    return result;
  }

  /// <summary>
  /// The colour components of a surface without its alpha, for the filters whose arithmetic is on
  /// colour alone.
  /// </summary>
  /// <remarks>
  /// Sharpen, unsharp and edge detection subtract a blur from the original. If that blur were
  /// weighted by alpha, as a plain blur is, the difference at a transparent edge would be the
  /// difference between two different colour averages rather than between a pixel and its
  /// surroundings, and a halo would appear exactly where the picture has no business having one.
  /// </remarks>
  private static PixelSurface _Opaque(PixelSurface surface) {
    if (!surface.HasAlpha)
      return surface;

    // Same layout, but the last component is no longer marked as alpha, so the blur weights every
    // component plainly. The blurred alpha that results is never read.
    var opaque = new PixelSurface(
      surface.Width, surface.Height, surface.Channels, false, surface.Wide, surface.Writeback);
    Array.Copy(surface.Samples, opaque.Samples, surface.Samples.Length);
    return opaque;
  }

  private static RawImage _Filter(RawImage image, Func<PixelSurface, PixelSurface> filter) {
    var surface = PixelSurfaces.Read(image);
    return PixelSurfaces.Write(filter(surface), image);
  }
}
