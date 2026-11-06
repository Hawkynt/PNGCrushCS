using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// The two convolution paths every effect in this assembly goes through: a separable one for kernels
/// that are an outer product of two lines, and a general one for the few that are not.
/// </summary>
/// <remarks>
/// <para>Blur, Gaussian blur, box blur, sharpen, unsharp mask and edge detection all reduce to the
/// separable path — sharpen and edge detection because their kernels are an impulse minus a
/// separable one, so they are computed as that arithmetic on a separable result rather than as a
/// full two-dimensional grid. That turns an <c>n²</c> kernel into <c>2n</c> taps and, as much to the
/// point, means one loop to get right instead of six.</para>
/// <para>Off the edge the picture is extended by repeating its edge pixels. A blur that wrapped would
/// drag the bottom of the picture into the top; one that assumed black would darken every border.</para>
/// <para>This is convolution, not correlation: the kernel is reflected about its centre before it is
/// applied. For the symmetric kernels most effects use the two are the same thing; for a directional
/// one such as an emboss, the direction a caller writes is the direction that lights the picture.</para>
/// </remarks>
public static class Convolution {

  /// <summary>Convolves a picture's colour components with a general two-dimensional kernel.</summary>
  /// <param name="image">The picture to filter.</param>
  /// <param name="kernel">The weights; they are used as given, not normalised.</param>
  /// <param name="bias">A constant added to every result, normalised so that full scale is one.</param>
  /// <remarks>
  /// Alpha is left as it was. A kernel with negative weights applied to alpha would make a hard
  /// transparent edge ring into partial opacity, which nobody asking for an edge detector wants.
  /// </remarks>
  public static RawImage Apply(RawImage image, ConvolutionKernel kernel, double bias = 0.0) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(kernel);

    var surface = PixelSurfaces.Read(image);
    return PixelSurfaces.Write(Apply(surface, kernel, bias), image);
  }

  /// <summary>Convolves a picture with a separable kernel: one line across, then one line down.</summary>
  /// <param name="image">The picture to filter.</param>
  /// <param name="horizontal">The weights across; odd length, used as given.</param>
  /// <param name="vertical">The weights down; odd length, used as given.</param>
  /// <remarks>
  /// Alpha is filtered along with colour, and colour is weighted by alpha while it is. A blurred
  /// picture should have a soft edge, and the colour at that edge should be the colour of the pixels
  /// that are there rather than of the invisible ones beside them.
  /// </remarks>
  public static RawImage ApplySeparable(RawImage image, ReadOnlySpan<double> horizontal, ReadOnlySpan<double> vertical) {
    ArgumentNullException.ThrowIfNull(image);
    _RequireOdd(horizontal.Length, nameof(horizontal));
    _RequireOdd(vertical.Length, nameof(vertical));

    var surface = PixelSurfaces.Read(image);
    return PixelSurfaces.Write(Separable(surface, horizontal.ToArray(), vertical.ToArray()), image);
  }

  /// <summary>The separable path on a working surface.</summary>
  internal static PixelSurface Separable(PixelSurface source, double[] horizontal, double[] vertical)
    => _Line(_Line(source, horizontal, true), vertical, false);

  /// <summary>The general path on a working surface.</summary>
  internal static PixelSurface Apply(PixelSurface source, ConvolutionKernel kernel, double bias) {
    var result = source.Clone();
    var output = result.Samples;
    var colors = source.ColorChannels;
    int halfWidth = kernel.Width / 2, halfHeight = kernel.Height / 2;

    for (var y = 0; y < source.Height; ++y)
      for (var x = 0; x < source.Width; ++x) {
        var to = source.Offset(x, y);
        for (var channel = 0; channel < colors; ++channel) {
          var sum = bias;
          for (var v = 0; v < kernel.Height; ++v)
            for (var u = 0; u < kernel.Width; ++u) {
              // Reflected: the cell at the kernel's bottom right weighs the pixel at the top left.
              var weight = kernel[kernel.Width - 1 - u, kernel.Height - 1 - v];
              if (weight != 0.0)
                sum += weight * source.At(x + u - halfWidth, y + v - halfHeight, channel);
            }

          output[to + channel] = (float)sum;
        }
      }

    return result;
  }

  /// <summary>Convolves along one axis.</summary>
  private static PixelSurface _Line(PixelSurface source, double[] weights, bool horizontal) {
    var result = source.Like();
    var samples = source.Samples;
    var output = result.Samples;
    var channels = source.Channels;
    var colors = source.ColorChannels;
    var half = weights.Length / 2;
    var length = horizontal ? source.Width : source.Height;
    var lines = horizontal ? source.Height : source.Width;
    var step = horizontal ? channels : source.Width * channels;
    var lineStride = horizontal ? source.Width * channels : channels;
    Span<double> accumulator = stackalloc double[4];

    for (var line = 0; line < lines; ++line) {
      var origin = line * lineStride;
      for (var i = 0; i < length; ++i) {
        accumulator.Clear();
        double alpha = 0.0, coverage = 0.0;

        for (var n = 0; n < weights.Length; ++n) {
          // Reflected, and clamped to the edge.
          var position = i + half - n;
          position = position < 0 ? 0 : position >= length ? length - 1 : position;
          var at = origin + position * step;
          var weight = weights[n];

          if (source.HasAlpha) {
            var opacity = samples[at + colors];
            var weighted = weight * opacity;
            for (var channel = 0; channel < colors; ++channel)
              accumulator[channel] += weighted * samples[at + channel];

            coverage += weighted;
            alpha += weight * opacity;
          } else
            for (var channel = 0; channel < colors; ++channel)
              accumulator[channel] += weight * samples[at + channel];
        }

        var to = origin + i * step;
        if (source.HasAlpha) {
          var reciprocal = Math.Abs(coverage) < 1.0e-12 ? 0.0 : 1.0 / coverage;
          for (var channel = 0; channel < colors; ++channel)
            output[to + channel] = (float)(accumulator[channel] * reciprocal);

          output[to + colors] = (float)alpha;
        } else
          for (var channel = 0; channel < colors; ++channel)
            output[to + channel] = (float)accumulator[channel];
      }
    }

    return result;
  }

  private static void _RequireOdd(int length, string name) {
    if (length < 1 || length % 2 == 0)
      throw new ArgumentException("A kernel line is an odd number of weights long, so that it has a centre.", name);
  }
}
